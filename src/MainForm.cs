using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

enum PlaybackState { Idle, Connecting, Playing, Stopping, Failed }

sealed class MainForm : Form {
    readonly Adb adb;
    readonly CheckBox mirrorChoice=new CheckBox();bool savedMirror;
    readonly CheckBox speakerChoice=new CheckBox(),cameraChoice=new CheckBox(),micChoice=new CheckBox();
    readonly ComboBox facing=new ComboBox(),orientation=new ComboBox(),micOutput=new ComboBox();
    static readonly int[] Rotations={270,90,0,180};int savedRotation=270,savedFacing;

    readonly ComboBox device=new ComboBox();

    readonly Button start=new Button(),refresh=new Button(),repair=new Button();
    readonly Label heading=new Label(),hint=new Label(),deviceCount=new Label(),statusTitle=new Label(),statusHint=new Label();
    readonly TextBox details=new TextBox();
    readonly Panel logPanel=new Panel();
    readonly CheckBox reconnect=new CheckBox();
    readonly System.Windows.Forms.Timer retry=new System.Windows.Forms.Timer();
    List<Device> devices=new List<Device>();
    readonly ResourceController controller;readonly DeviceWatcher watcher;NotifyIcon tray;ToolStripMenuItem traySpeaker,trayCamera,trayMic;bool scanning,exitRequested,suppress,started,scanAgain,firstHide=true,exiting;string resumeSerial;
    string lastDevice;
    readonly string settingsFile;

    static readonly Color Ink=Color.FromArgb(29,43,64),Muted=Color.FromArgb(91,105,124),Blue=Color.FromArgb(35,91,216);

    public MainForm(string root,string autoDevice,bool autoMeeting=false,bool cameraOnly=false) {
        adb=new Adb(root);controller=new ResourceController(()=>new ResourceSession(adb,Log),Post);watcher=new DeviceWatcher(adb);settingsFile=Path.Combine(root,".runtime","devices.txt");ReadSettings();
        Text="PocketLink · 手机资源控制台";Font=new Font("Microsoft YaHei UI",10);ForeColor=Ink;BackColor=Color.FromArgb(245,247,251);
        ClientSize=new Size(960,840);MinimumSize=new Size(920,800);StartPosition=FormStartPosition.CenterScreen;
        AutoScaleMode=AutoScaleMode.Dpi;
        TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=6};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,78));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        Panel header=new Panel{Dock=DockStyle.Fill};layout.Controls.Add(header,0,0);
        heading.Text="PocketLink";heading.Font=new Font(Font.FontFamily,23,FontStyle.Bold);heading.SetBounds(0,0,350,40);header.Controls.Add(heading);
        Label intro=TextLabel("一根 USB 数据线，按需使用手机的扬声器、摄像头和麦克风。",0,45,760,25);intro.ForeColor=Muted;header.Controls.Add(intro);
        TableLayoutPanel toolbar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));layout.Controls.Add(toolbar,0,1);
        deviceCount.Text="尚未查找设备";deviceCount.Dock=DockStyle.Fill;deviceCount.TextAlign=ContentAlignment.MiddleLeft;toolbar.Controls.Add(deviceCount,0,0);
        ConfigureButton(refresh,"刷新设备",false);ConfigureButton(repair,"修复连接",false);toolbar.Controls.Add(refresh,1,0);toolbar.Controls.Add(repair,2,0);
        refresh.Click+=(s,e)=>Discover(false,null);repair.Click+=(s,e)=>Discover(true,null);
        Panel playback=new Panel{Dock=DockStyle.Fill,BackColor=Color.White};layout.Controls.Add(playback,0,2);BuildPlayback(playback);
        Panel banner=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(233,239,249),Padding=new Padding(12)};layout.Controls.Add(banner,0,3);
        statusTitle.SetBounds(12,9,800,24);statusTitle.Font=new Font(Font.FontFamily,11,FontStyle.Bold);banner.Controls.Add(statusTitle);
        statusHint.SetBounds(12,35,800,30);statusHint.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;statusHint.ForeColor=Muted;banner.Controls.Add(statusHint);
        Button logToggle=new Button{Text="显示诊断日志",FlatStyle=FlatStyle.Flat,Dock=DockStyle.Left,Width=130};logToggle.FlatAppearance.BorderSize=0;layout.Controls.Add(logToggle,0,4);
        logPanel.Dock=DockStyle.Fill;logPanel.Height=130;logPanel.Visible=false;layout.Controls.Add(logPanel,0,5);
        details.Dock=DockStyle.Fill;details.ReadOnly=true;details.Multiline=true;details.ScrollBars=ScrollBars.Vertical;details.Font=new Font("Consolas",9);logPanel.Controls.Add(details);
        logToggle.Click+=(s,e)=>{logPanel.Visible=!logPanel.Visible;logToggle.Text=logPanel.Visible?"隐藏诊断日志":"显示诊断日志";};
        retry.Interval=1200;retry.Tick+=(s,e)=>{retry.Stop();Discover(false,null);};
        watcher.Changed+=()=>Post(ScheduleDeviceScan);
        controller.Changed+=()=>{UpdateControls();UpdateTray();TryResumeSpeaker();FinishExit();};
        controller.Error+=message=>{Log(message);statusTitle.Text="资源连接失败";statusHint.Text=message;};
        InitializeTray();
        Shown+=(s,e)=>{Discover(false,()=>{try{watcher.Start();}catch(Exception error){Log(error.Message);}if(autoDevice!=null){Select(device,autoDevice);suppress=true;cameraChoice.Checked=autoMeeting;micChoice.Checked=autoMeeting&&!cameraOnly;speakerChoice.Checked=!autoMeeting;suppress=false;StartPlayback();}});};
        FormClosing+=(s,e)=>{if(e.CloseReason==CloseReason.UserClosing&&!exitRequested){e.Cancel=true;HideToTray();return;}if(controller.Busy){e.Cancel=true;RequestExit();return;}retry.Stop();watcher.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}};
        SetState(PlaybackState.Idle,"连接手机后，选择设备并开始播放。");
    }
    static Label TextLabel(string text,int x,int y,int w,int h) {return new Label{Text=text,Location=new Point(x,y),Size=new Size(w,h)};}
    static void ConfigureButton(Button b,string text,bool primary) {b.Text=text;b.Dock=DockStyle.Fill;b.Margin=new Padding(4,5,4,5);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=primary?0:1;b.BackColor=primary?Blue:Color.White;b.ForeColor=primary?Color.White:Ink;}
    static void ConfigureChoice(ComboBox box) {box.DropDownStyle=ComboBoxStyle.DropDownList;box.Dock=DockStyle.Fill;box.Margin=new Padding(0,6,0,10);}
    TableLayoutPanel PageLayout(Panel page,int rows) {TableLayoutPanel p=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,18,20,12),ColumnCount=1,RowCount=rows};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));page.Controls.Add(p);return p;}
    void BuildPlayback(Panel page) {
        TableLayoutPanel p=PageLayout(page,9);
        foreach(int h in new[]{38,24,46,58,58,58,48,64,52})p.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        p.Controls.Add(new Label{Text="独立控制手机资源；摄像头方向和镜像可在运行中调整。",Dock=DockStyle.Fill},0,0);
        p.Controls.Add(new Label{Text="USB 手机",Dock=DockStyle.Fill},0,1);ConfigureChoice(device);p.Controls.Add(device,0,2);device.SelectedIndexChanged+=(s,e)=>UpdateControls();
        speakerChoice.Checked=false;
        p.Controls.Add(ResourceRow(speakerChoice,"扬声器","电脑 → 手机，完整立体声播放",null),0,3);
        facing.DropDownStyle=ComboBoxStyle.DropDownList;facing.Items.AddRange(new object[]{"前置摄像头","后置摄像头"});facing.SelectedIndex=savedFacing;facing.Width=200;
        orientation.DropDownStyle=ComboBoxStyle.DropDownList;orientation.Items.AddRange(new object[]{"竖持 · 270°","竖持反向 · 90°","横持 · 0°","横持反向 · 180°"});orientation.SelectedIndex=Array.IndexOf(Rotations,savedRotation);
        TableLayoutPanel cameraControls=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};cameraControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));cameraControls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));facing.Dock=orientation.Dock=DockStyle.Fill;cameraControls.Controls.Add(facing,0,0);cameraControls.Controls.Add(orientation,1,0);
        p.Controls.Add(ResourceRow(cameraChoice,"摄像头","手机 → 电脑，默认竖持；会议选 OBS Virtual Camera",cameraControls),0,4);
        micOutput.DropDownStyle=ComboBoxStyle.DropDownList;micOutput.Width=250;RefreshVirtualOutputs();
        p.Controls.Add(ResourceRow(micChoice,"麦克风","手机 → 电脑，会议中选择 CABLE Output",micOutput),0,5);
        FlowLayoutPanel extras=new FlowLayoutPanel{Dock=DockStyle.Fill};Button setup=new Button{Text="配置会议麦克风",AutoSize=true};setup.Click+=(s,e)=>ConfigureMicrophone();extras.Controls.Add(setup);mirrorChoice.Text="水平镜像";mirrorChoice.Checked=savedMirror;mirrorChoice.AutoSize=true;extras.Controls.Add(mirrorChoice);Button preview=new Button{Text="打开摄像头预览",AutoSize=true};preview.Click+=(s,e)=>{string url=controller.CameraPreviewUrl;if(url!=null)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});};extras.Controls.Add(preview);orientation.SelectedIndexChanged+=(s,e)=>UpdateCameraTransform();mirrorChoice.CheckedChanged+=(s,e)=>UpdateCameraTransform();reconnect.Text="扬声器断开后自动恢复";reconnect.AutoSize=true;extras.Controls.Add(reconnect);p.Controls.Add(extras,0,6);
        hint.Text="独立开启所需资源。预览可随时关闭，旋转与镜像实时作用于会议输出。";hint.Dock=DockStyle.Fill;hint.ForeColor=Muted;p.Controls.Add(hint,0,7);
                ConfigureButton(start,"应用资源选择",true);p.Controls.Add(start,0,8);start.Click+=(s,e)=>StartPlayback();
        speakerChoice.CheckedChanged+=(s,e)=>ResourceChoiceChanged();cameraChoice.CheckedChanged+=(s,e)=>ResourceChoiceChanged();micChoice.CheckedChanged+=(s,e)=>ResourceChoiceChanged();
        Button stop=new Button{Text="关闭所有资源",AutoSize=true};stop.Click+=(s,e)=>StopPlayback();extras.Controls.Add(stop);
    }
    Control ResourceRow(CheckBox enabled,string title,string description,Control extra) {
        TableLayoutPanel row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,BackColor=Color.FromArgb(247,249,252),Margin=new Padding(0,4,0,4),Padding=new Padding(8)};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));
        enabled.Text=title;enabled.Dock=DockStyle.Fill;row.Controls.Add(enabled,0,0);row.Controls.Add(new Label{Text=description,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Muted},1,0);if(extra!=null){extra.Dock=DockStyle.Fill;row.Controls.Add(extra,2,0);}return row;
    }
    bool HasMicOutput(){return micOutput.Items.Count>0&&micOutput.Items[0] is WaveDevice;}
    void RefreshVirtualOutputs(){micOutput.Items.Clear();foreach(WaveDevice output in WaveOutput.VirtualDevices())micOutput.Items.Add(output);if(micOutput.Items.Count==0){micOutput.Items.Add("需安装 VB-CABLE，点击下方配置");micChoice.Checked=false;}micOutput.SelectedIndex=0;UpdateControls();}
    void ConfigureMicrophone(){
        if(controller.Busy){MessageBox.Show(this,"请先停止连接，再配置会议麦克风。");return;}
        var info=new System.Diagnostics.ProcessStartInfo("powershell.exe","-NoProfile -File "+Adb.Q(Path.Combine(adb.Root,"Setup-Resources.ps1"))+" -InstallMicrophone"){UseShellExecute=true};System.Diagnostics.Process.Start(info);
    }
    void Post(Action action) {if(IsDisposed||Disposing||!IsHandleCreated)return;try{BeginInvoke(new Action(()=>{if(!IsDisposed&&!Disposing)action();}));}catch(InvalidOperationException){}}
    void Log(string message) {
        if(InvokeRequired){Post(()=>Log(message));return;}
        if(IsDisposed)return;
        if(details.TextLength>48000)details.Text=details.Text.Substring(details.TextLength-24000);
        details.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+message+Environment.NewLine);
        try{Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));File.AppendAllText(Path.Combine(Path.GetDirectoryName(settingsFile),"session.log"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ")+message+Environment.NewLine);}catch(IOException){}catch(UnauthorizedAccessException){}

    }
    void SetState(PlaybackState value,string message){statusTitle.Text=value==PlaybackState.Failed?"连接失败":"设备待命";statusHint.Text=message;UpdateControls();}
    void UpdateControls(){
        bool busy=controller!=null&&controller.Busy;
        device.Enabled=!busy&&!scanning;refresh.Enabled=repair.Enabled=!scanning;
        speakerChoice.Enabled=cameraChoice.Enabled=true;
        micChoice.Enabled=HasMicOutput()||(controller!=null&&controller.Active(PhoneResource.Microphone));
        facing.Enabled=controller==null||!controller.Active(PhoneResource.Camera);orientation.Enabled=mirrorChoice.Enabled=true;
        micOutput.Enabled=HasMicOutput()&&(controller==null||!controller.Active(PhoneResource.Microphone));
        Device selected=device.SelectedItem as Device;start.Enabled=!scanning&&selected!=null&&selected.Ready;
        start.Text="应用资源选择";
        if(controller!=null){statusTitle.Text=busy?"手机资源正在使用":"待机 · 采集已停止";statusHint.Text="扬声器："+controller.Status(PhoneResource.Speaker)+"   摄像头："+controller.Status(PhoneResource.Camera)+"   麦克风："+controller.Status(PhoneResource.Microphone);}
    }
    void Discover(bool fix,Action finished) {
        if(exitRequested)return;if(scanning){scanAgain=true;return;}scanning=true;deviceCount.Text=fix?"正在修复连接…":"正在查找设备…";UpdateControls();
        string selected=Serial(device)??lastDevice;bool previouslyReady=devices.Exists(x=>x.Serial==selected&&x.Ready);if(!controller.Busy)RefreshVirtualOutputs();
        ThreadPool.QueueUserWorkItem(_=>{try{
            if(fix){if(controller.Busy)throw new InvalidOperationException("请先关闭资源再修复设备服务。");watcher.Stop();adb.Run("kill-server");adb.Run("start-server");watcher.Start();}
            List<Device> found=adb.Devices();Post(()=>{scanning=false;devices=found;Fill(device,selected,0);
                int ready=found.FindAll(x=>x.Ready).Count;deviceCount.Text=ready+" 台可连接"+(found.Count>ready?" · "+(found.Count-ready)+" 台待授权或离线":"");
                hint.Text=found.Count==0?"未发现手机。检查 USB 数据线，并在手机开启 USB 调试；也可点击上方“修复连接”。":ready==0?"手机已发现。请解锁手机，允许 USB 调试；若显示离线，请重新插拔后刷新。":"听歌只开启扬声器；会议请在腾讯会议或钉钉中选择 OBS Virtual Camera 和 CABLE Output。";
                Log("设备列表已更新："+found.Count+" 台，已授权 "+ready+" 台。");CheckDevicePresence(selected,previouslyReady);UpdateControls();if(finished!=null)finished();if(scanAgain){scanAgain=false;ScheduleDeviceScan();}});
        }catch(Exception e){Post(()=>{scanning=false;deviceCount.Text="设备查找失败";Log(e.Message);SetState(PlaybackState.Failed,"设备查找失败，请点击“修复连接”重试。详情见诊断日志。");});}});
    }
    static string Serial(ComboBox box) {Device d=box.SelectedItem as Device;return d==null?null:d.Serial;}
    static void Select(ComboBox box,string id) {foreach(Device d in box.Items)if(d.Serial==id){box.SelectedItem=d;return;}}
    void Fill(ComboBox box,string previous,int fallback) {box.Items.Clear();foreach(Device d in devices)box.Items.Add(d);Select(box,previous);if(box.SelectedIndex<0&&devices.Count>fallback)box.SelectedIndex=fallback;}
    ResourceOptions Options(Device selected,PhoneResource resource){WaveDevice target=micOutput.SelectedItem as WaveDevice;return new ResourceOptions{Serial=selected.Serial,Speaker=resource==PhoneResource.Speaker,Camera=resource==PhoneResource.Camera,Microphone=resource==PhoneResource.Microphone,FrontCamera=facing.SelectedIndex==0,CameraRotation=Rotations[orientation.SelectedIndex],CameraMirror=mirrorChoice.Checked,MicrophoneOutput=target==null?-1:target.Id,MicrophoneOutputName=target==null?null:target.Name};}
    void StartPlayback(){
        if(exitRequested||scanning)return;Device selected=device.SelectedItem as Device;
        if(selected==null||!selected.Ready){SetState(PlaybackState.Failed,"请选择已授权的手机。");return;}
        started=true;lastDevice=selected.Serial;savedRotation=Rotations[orientation.SelectedIndex];savedFacing=facing.SelectedIndex;savedMirror=mirrorChoice.Checked;SaveSettings();
        foreach(PhoneResource key in Enum.GetValues(typeof(PhoneResource))){bool enabled=key==PhoneResource.Speaker?speakerChoice.Checked:key==PhoneResource.Camera?cameraChoice.Checked:micChoice.Checked;ResourceOptions options=Options(selected,key);if(enabled){try{options.Validate();}catch(Exception error){Log(error.Message);continue;}}controller.Set(key,enabled,options);}
        UpdateControls();UpdateTray();
    }
    void ResourceChoiceChanged(){if(!suppress)StartPlayback();}

    void UpdateCameraTransform(){if(orientation.SelectedIndex<0)return;savedRotation=Rotations[orientation.SelectedIndex];savedMirror=mirrorChoice.Checked;controller.SetCameraTransform(savedRotation,savedMirror);SaveSettings();}
    void StopPlayback(){resumeSerial=null;suppress=true;speakerChoice.Checked=cameraChoice.Checked=micChoice.Checked=false;suppress=false;controller.StopAll();UpdateControls();UpdateTray();}
    void CheckDevicePresence(string previous,bool previouslyReady){
        Device present=devices.Find(x=>x.Serial==previous&&x.Ready);
        if(DevicePresence.MustRelease(previouslyReady,present!=null,controller.Busy)){if(started&&speakerChoice.Checked&&reconnect.Checked)resumeSerial=previous;suppress=true;speakerChoice.Checked=cameraChoice.Checked=micChoice.Checked=false;suppress=false;controller.StopAll();Log("手机已断开，释放采集资源并进入低耗待机。");}
        TryResumeSpeaker();
    }
    void TryResumeSpeaker(){if(exitRequested||resumeSerial==null||!reconnect.Checked||controller.Active(PhoneResource.Speaker))return;Device found=devices.Find(x=>DevicePresence.CanResumeSpeaker(resumeSerial,x.Serial,x.Ready,reconnect.Checked));if(found==null)return;resumeSerial=null;Select(device,found.Serial);suppress=true;speakerChoice.Checked=false;suppress=false;controller.Set(PhoneResource.Speaker,true,Options(found,PhoneResource.Speaker));Log("原手机已接入，按设置恢复扬声器。摄像头和麦克风保持关闭。");}
    void ScheduleDeviceScan(){if(exitRequested)return;retry.Stop();retry.Start();}
    protected override void WndProc(ref Message message){base.WndProc(ref message);if(message.Msg==0x219&&retry!=null)ScheduleDeviceScan();}
    void InitializeTray(){
        ContextMenuStrip menu=new ContextMenuStrip();menu.Items.Add("打开控制台",null,(s,e)=>RevealWindow());menu.Items.Add(new ToolStripSeparator());
        traySpeaker=new ToolStripMenuItem("扬声器",null,(s,e)=>TrayToggle(PhoneResource.Speaker));trayCamera=new ToolStripMenuItem("摄像头",null,(s,e)=>TrayToggle(PhoneResource.Camera));trayMic=new ToolStripMenuItem("麦克风",null,(s,e)=>TrayToggle(PhoneResource.Microphone));menu.Items.AddRange(new ToolStripItem[]{traySpeaker,trayCamera,trayMic});menu.Items.Add("关闭所有资源",null,(s,e)=>StopPlayback());menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出 PocketLink",null,(s,e)=>RequestExit());
        tray=new NotifyIcon{Icon=SystemIcons.Application,Text="PocketLink · 待机",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>RevealWindow();
    }
    void TrayToggle(PhoneResource key){CheckBox box=key==PhoneResource.Speaker?speakerChoice:key==PhoneResource.Camera?cameraChoice:micChoice;suppress=true;box.Checked=!controller.Active(key);suppress=false;if(!controller.Active(key)){Device selected=device.SelectedItem as Device;if(selected==null||!selected.Ready){RevealWindow();SetState(PlaybackState.Failed,"请连接并选择手机。");return;}var options=Options(selected,key);try{options.Validate();}catch(Exception error){RevealWindow();SetState(PlaybackState.Failed,error.Message);return;}started=true;controller.Set(key,true,options);}else controller.Set(key,false,null);}
    void UpdateTray(){if(tray==null)return;traySpeaker.Checked=controller.Active(PhoneResource.Speaker);trayCamera.Checked=controller.Active(PhoneResource.Camera);trayMic.Checked=controller.Active(PhoneResource.Microphone);tray.Text=controller.Busy?"PocketLink · 手机资源使用中":"PocketLink · 待机";}
    public void RevealWindow(){if(InvokeRequired){Post(RevealWindow);return;}Show();WindowState=FormWindowState.Normal;Activate();}
    public void CloseWindow(){if(InvokeRequired){Post(CloseWindow);return;}Close();}
    public void HideToTray(){if(InvokeRequired){Post(HideToTray);return;}Hide();if(firstHide&&tray!=null){firstHide=false;tray.ShowBalloonTip(4000,"PocketLink 已隐藏到托盘",controller.Busy?"当前资源继续运行，右键托盘图标可关闭资源或退出。":"程序待命中，右键托盘图标可退出。",ToolTipIcon.Info);}}
    public void RequestExit(){if(InvokeRequired){Post(RequestExit);return;}if(exitRequested)return;exitRequested=true;retry.Stop();resumeSerial=null;controller.StopAll();FinishExit();}
    void FinishExit(){if(!exitRequested||controller.Busy||exiting)return;exiting=true;watcher.Dispose();ThreadPool.QueueUserWorkItem(_=>{try{adb.Run("kill-server");}catch{}Post(()=>Close());});}
    void ReadSettings() {try{string[] values=File.ReadAllLines(settingsFile);if(values.Length>0)lastDevice=values[0];int rotation;if(values.Length>1&&Int32.TryParse(values[1],out rotation)&&Array.IndexOf(Rotations,rotation)>=0)savedRotation=rotation;int lens;if(values.Length>2&&Int32.TryParse(values[2],out lens)&&(lens==0||lens==1))savedFacing=lens;if(values.Length>3)Boolean.TryParse(values[3],out savedMirror);}catch(IOException){}catch(UnauthorizedAccessException){}}
    void SaveSettings() {try{Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));File.WriteAllLines(settingsFile,new string[]{lastDevice??"",savedRotation.ToString(),savedFacing.ToString(),savedMirror.ToString()});}catch(Exception e){Log("无法保存设备选择："+e.Message);}}
}
