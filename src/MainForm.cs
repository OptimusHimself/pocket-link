using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

enum PlaybackState { Idle, Connecting, Playing, Stopping, Failed }

sealed class MainForm : Form {
    readonly Adb adb;
    readonly RadioButton musicPreset=new RadioButton(),meetingPreset=new RadioButton();
    readonly CheckBox speakerChoice=new CheckBox(),cameraChoice=new CheckBox(),micChoice=new CheckBox();
    readonly ComboBox facing=new ComboBox(),micOutput=new ComboBox();

    readonly ComboBox device=new ComboBox();

    readonly Button start=new Button(),refresh=new Button(),repair=new Button();
    readonly Label heading=new Label(),hint=new Label(),deviceCount=new Label(),statusTitle=new Label(),statusHint=new Label();
    readonly TextBox details=new TextBox();
    readonly Panel logPanel=new Panel();
    readonly CheckBox reconnect=new CheckBox();
    readonly System.Windows.Forms.Timer retry=new System.Windows.Forms.Timer();
    List<Device> devices=new List<Device>();
    ResourceSession session; PlaybackState state; bool scanning,closingRequested,stopRequested,retryPending;
    string lastDevice;
    readonly string settingsFile;

    static readonly Color Ink=Color.FromArgb(29,43,64),Muted=Color.FromArgb(91,105,124),Blue=Color.FromArgb(35,91,216);

    public MainForm(string root,string autoDevice,bool autoMeeting=false) {
        adb=new Adb(root);settingsFile=Path.Combine(root,".runtime","devices.txt");ReadSettings();
        Text="SoundLink · 手机资源控制台";Font=new Font("Microsoft YaHei UI",10);ForeColor=Ink;BackColor=Color.FromArgb(245,247,251);
        ClientSize=new Size(960,840);MinimumSize=new Size(920,800);StartPosition=FormStartPosition.CenterScreen;
        AutoScaleMode=AutoScaleMode.Dpi;
        TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=6};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,78));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        Panel header=new Panel{Dock=DockStyle.Fill};layout.Controls.Add(header,0,0);
        heading.Text="SoundLink";heading.Font=new Font(Font.FontFamily,23,FontStyle.Bold);heading.SetBounds(0,0,350,40);header.Controls.Add(heading);
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
        retry.Interval=5000;retry.Tick+=(s,e)=>{if(retryPending && session==null && !scanning)Discover(false,()=>{Device d=device.SelectedItem as Device;if(d!=null&&d.Ready&&reconnect.Checked)StartPlayback();});};retry.Start();
        reconnect.CheckedChanged+=(s,e)=>{if(!reconnect.Checked)retryPending=false;};
        Shown+=(s,e)=>Discover(false,()=>{if(autoDevice!=null){Select(device,autoDevice);if(autoMeeting)meetingPreset.Checked=true;StartPlayback();}});
        FormClosing+=(s,e)=>{if(session!=null){e.Cancel=true;closingRequested=true;StopPlayback();return;}retry.Stop();};
        SetState(PlaybackState.Idle,"连接手机后，选择设备并开始播放。");
    }
    static Label TextLabel(string text,int x,int y,int w,int h) {return new Label{Text=text,Location=new Point(x,y),Size=new Size(w,h)};}
    static void ConfigureButton(Button b,string text,bool primary) {b.Text=text;b.Dock=DockStyle.Fill;b.Margin=new Padding(4,5,4,5);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=primary?0:1;b.BackColor=primary?Blue:Color.White;b.ForeColor=primary?Color.White:Ink;}
    static void ConfigureChoice(ComboBox box) {box.DropDownStyle=ComboBoxStyle.DropDownList;box.Dock=DockStyle.Fill;box.Margin=new Padding(0,6,0,10);}
    TableLayoutPanel PageLayout(Panel page,int rows) {TableLayoutPanel p=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,18,20,12),ColumnCount=1,RowCount=rows};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));page.Controls.Add(p);return p;}
    void BuildPlayback(Panel page) {
        TableLayoutPanel p=PageLayout(page,9);
        foreach(int h in new[]{38,24,46,58,58,58,48,64,52})p.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        FlowLayoutPanel presets=new FlowLayoutPanel{Dock=DockStyle.Fill};musicPreset.Text="听音乐";musicPreset.Checked=true;meetingPreset.Text="视频会议";musicPreset.AutoSize=meetingPreset.AutoSize=true;presets.Controls.Add(musicPreset);presets.Controls.Add(meetingPreset);p.Controls.Add(presets,0,0);
        musicPreset.CheckedChanged+=(s,e)=>{if(musicPreset.Checked){speakerChoice.Checked=true;cameraChoice.Checked=micChoice.Checked=false;}};
        meetingPreset.CheckedChanged+=(s,e)=>{if(meetingPreset.Checked){speakerChoice.Checked=false;cameraChoice.Checked=true;micChoice.Checked=HasMicOutput();}};
        p.Controls.Add(new Label{Text="USB 手机",Dock=DockStyle.Fill},0,1);ConfigureChoice(device);p.Controls.Add(device,0,2);device.SelectedIndexChanged+=(s,e)=>UpdateControls();
        speakerChoice.Checked=true;
        p.Controls.Add(ResourceRow(speakerChoice,"扬声器","电脑 → 手机，完整立体声播放",null),0,3);
        facing.DropDownStyle=ComboBoxStyle.DropDownList;facing.Items.AddRange(new object[]{"前置摄像头","后置摄像头"});facing.SelectedIndex=0;facing.Width=200;
        p.Controls.Add(ResourceRow(cameraChoice,"摄像头","手机 → 电脑，会议中选择 OBS Virtual Camera",facing),0,4);
        micOutput.DropDownStyle=ComboBoxStyle.DropDownList;micOutput.Width=250;RefreshVirtualOutputs();
        p.Controls.Add(ResourceRow(micChoice,"麦克风","手机 → 电脑，会议中选择 CABLE Output",micOutput),0,5);
        FlowLayoutPanel extras=new FlowLayoutPanel{Dock=DockStyle.Fill};Button setup=new Button{Text="配置会议麦克风",AutoSize=true};setup.Click+=(s,e)=>ConfigureMicrophone();extras.Controls.Add(setup);reconnect.Text="听歌意外断开后自动重连";reconnect.AutoSize=true;extras.Controls.Add(reconnect);p.Controls.Add(extras,0,6);
        hint.Text="听音乐仅开启扬声器；会议可独立勾选摄像头和麦克风。摄像头、麦克风仅在点击启动后采集。";hint.Dock=DockStyle.Fill;hint.ForeColor=Muted;p.Controls.Add(hint,0,7);
        ConfigureButton(start,"启动所选资源",true);p.Controls.Add(start,0,8);start.Click+=(s,e)=>{if(session!=null)StopPlayback();else StartPlayback();};
    }
    Control ResourceRow(CheckBox enabled,string title,string description,Control extra) {
        TableLayoutPanel row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,BackColor=Color.FromArgb(247,249,252),Margin=new Padding(0,4,0,4),Padding=new Padding(8)};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));
        enabled.Text=title;enabled.Dock=DockStyle.Fill;row.Controls.Add(enabled,0,0);row.Controls.Add(new Label{Text=description,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Muted},1,0);if(extra!=null){extra.Dock=DockStyle.Fill;row.Controls.Add(extra,2,0);}return row;
    }
    bool HasMicOutput(){return micOutput.Items.Count>0&&micOutput.Items[0] is WaveDevice;}
    void RefreshVirtualOutputs(){micOutput.Items.Clear();foreach(WaveDevice output in WaveOutput.VirtualDevices())micOutput.Items.Add(output);if(micOutput.Items.Count==0){micOutput.Items.Add("需安装 VB-CABLE，点击下方配置");micChoice.Checked=false;}micOutput.SelectedIndex=0;UpdateControls();}
    void ConfigureMicrophone(){
        if(session!=null){MessageBox.Show(this,"请先停止连接，再配置会议麦克风。");return;}
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
    void SetState(PlaybackState value,string message) {state=value;statusTitle.Text=value==PlaybackState.Idle?"未连接":value==PlaybackState.Connecting?"正在连接":value==PlaybackState.Playing?"所选资源已启动":value==PlaybackState.Stopping?"正在断开连接":"连接失败";statusTitle.ForeColor=value==PlaybackState.Failed?Color.Firebrick:value==PlaybackState.Playing?Color.FromArgb(20,112,72):Ink;statusHint.Text=message;UpdateControls();}
    void UpdateControls() {
        bool active=session!=null,busy=active||scanning;
        device.Enabled=musicPreset.Enabled=meetingPreset.Enabled=speakerChoice.Enabled=cameraChoice.Enabled=micChoice.Enabled=facing.Enabled=micOutput.Enabled=!busy;
        micChoice.Enabled=micOutput.Enabled=!busy&&HasMicOutput();
        refresh.Enabled=repair.Enabled=!busy;
        start.Text=active?(state==PlaybackState.Stopping?"正在停止…":"断开连接"):"启动所选资源";
        Device d=device.SelectedItem as Device;
        start.Enabled=active?state!=PlaybackState.Stopping:!scanning&&d!=null&&d.Ready;
    }
    void Discover(bool fix,Action finished) {
        if(scanning||session!=null)return;scanning=true;deviceCount.Text=fix?"正在修复连接…":"正在查找设备…";UpdateControls();
        string selected=Serial(device)??lastDevice;RefreshVirtualOutputs();
        ThreadPool.QueueUserWorkItem(_=>{try{
            if(fix){adb.Run("kill-server");adb.Run("start-server");}
            List<Device> found=adb.Devices();Post(()=>{scanning=false;devices=found;Fill(device,selected,0);
                int ready=found.FindAll(x=>x.Ready).Count;deviceCount.Text=ready+" 台可连接"+(found.Count>ready?" · "+(found.Count-ready)+" 台待授权或离线":"");
                hint.Text=found.Count==0?"未发现手机。检查 USB 数据线，并在手机开启 USB 调试；也可点击上方“修复连接”。":ready==0?"手机已发现。请解锁手机，允许 USB 调试；若显示离线，请重新插拔后刷新。":"听歌只开启扬声器；会议请在腾讯会议或钉钉中选择 OBS Virtual Camera 和 CABLE Output。";
                Log("设备列表已更新："+found.Count+" 台，已授权 "+ready+" 台。");UpdateControls();if(finished!=null)finished();});
        }catch(Exception e){Post(()=>{scanning=false;deviceCount.Text="设备查找失败";Log(e.Message);SetState(PlaybackState.Failed,"设备查找失败，请点击“修复连接”重试。详情见诊断日志。");});}});
    }
    static string Serial(ComboBox box) {Device d=box.SelectedItem as Device;return d==null?null:d.Serial;}
    static void Select(ComboBox box,string id) {foreach(Device d in box.Items)if(d.Serial==id){box.SelectedItem=d;return;}}
    void Fill(ComboBox box,string previous,int fallback) {box.Items.Clear();foreach(Device d in devices)box.Items.Add(d);Select(box,previous);if(box.SelectedIndex<0&&devices.Count>fallback)box.SelectedIndex=fallback;}
    void StartPlayback() {
        if(session!=null||scanning)return;Device selected=device.SelectedItem as Device;
        if(selected==null||!selected.Ready){SetState(PlaybackState.Failed,"请选择一台已授权的手机。");return;}
        WaveDevice target=micOutput.SelectedItem as WaveDevice;
        ResourceOptions options=new ResourceOptions{Serial=selected.Serial,Speaker=speakerChoice.Checked,Camera=cameraChoice.Checked,Microphone=micChoice.Checked,FrontCamera=facing.SelectedIndex==0,MicrophoneOutput=target==null?-1:target.Id};
        try{options.Validate();}catch(Exception e){SetState(PlaybackState.Failed,e.Message);return;}
        stopRequested=retryPending=false;lastDevice=selected.Serial;SaveSettings();
        ResourceSession current=new ResourceSession(adb,Log);session=current;
        Log("启动资源：扬声器="+options.Speaker+"，摄像头="+options.Camera+"，麦克风="+options.Microphone);
        SetState(PlaybackState.Connecting,"正在启动勾选的手机资源，未勾选的资源保持关闭。");
        current.Start(options,()=>Post(()=>{if(session==current&&state!=PlaybackState.Stopping)SetState(PlaybackState.Playing,options.Camera||options.Microphone?"会议软件请选择 OBS Virtual Camera / CABLE Output，并检查会议预览和麦克风音量条。":"请在电脑播放网易云音乐，手机音量控制响度。");}),error=>Post(()=>{
            if(session!=current)return;session=null;if(error!=null){Log(error);SetState(PlaybackState.Failed,error);}else SetState(PlaybackState.Idle,"所选资源已关闭。");
            retryPending=options.Speaker&&!options.Camera&&!options.Microphone&&reconnect.Checked&&!stopRequested;if(closingRequested)Close();
        }));
    }
    void StopPlayback(){retryPending=false;stopRequested=true;if(session==null)return;SetState(PlaybackState.Stopping,"正在关闭扬声器、摄像头及麦克风通道…");session.Stop();}
    void ReadSettings() {try{string[] values=File.ReadAllLines(settingsFile);if(values.Length>0)lastDevice=values[0];}catch(IOException){}catch(UnauthorizedAccessException){}}
    void SaveSettings() {try{Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));File.WriteAllLines(settingsFile,new string[]{lastDevice??""});}catch(Exception e){Log("无法保存设备选择："+e.Message);}}
}
