using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

enum PlaybackState { Idle, Connecting, Playing, Stopping, Failed }

sealed class MainForm : Form {
    readonly Adb adb;
    readonly CheckBox mirrorChoice;bool savedMirror;
    readonly CheckBox speakerChoice,cameraChoice,micChoice;
    readonly ComboBox facing,orientation;WaveDevice microphoneOutput;
    static readonly int[] Rotations={270,90,0,180};int savedRotation=270,savedFacing;

    readonly ComboBox device;readonly UiTheme theme;readonly PocketLinkView view;

    readonly Button reconnectButton;readonly ToolStripMenuItem diagnosticButton=new ToolStripMenuItem();
    bool reporting;string lastFailure;readonly System.Diagnostics.Stopwatch autoReportClock=new System.Diagnostics.Stopwatch();

    readonly TextBox details=new TextBox();


    readonly System.Windows.Forms.Timer retry=new System.Windows.Forms.Timer();
    List<Device> devices=new List<Device>();
    readonly ResourceController controller;readonly DeviceWatcher watcher;NotifyIcon tray;ToolStripMenuItem traySpeaker,trayCamera,trayMic;bool scanning,exitRequested,suppress,scanAgain,firstHide=true,exiting,reconnecting,reconnectScanStarted;
    string lastDevice;
    readonly string settingsFile;


    public MainForm(string root,string autoDevice,bool autoMeeting=false,bool cameraOnly=false) {
        adb=new Adb(root);controller=new ResourceController(()=>new ResourceSession(adb,Log),Post);watcher=new DeviceWatcher(adb);settingsFile=Path.Combine(root,".runtime","devices.txt");ReadSettings();
        theme=new UiTheme();view=new PocketLinkView(theme);device=view.Devices;facing=view.Facing;orientation=view.Orientation;mirrorChoice=view.Mirror;speakerChoice=view.Speaker;cameraChoice=view.Camera;micChoice=view.Microphone;reconnectButton=view.Reconnect;
        Text="PocketLink";Font=theme.Body;ForeColor=theme.Text;BackColor=theme.Background;Icon=theme.Icon;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(660,470);MinimumSize=new Size(600,500);StartPosition=FormStartPosition.CenterScreen;Controls.Add(view);
        facing.SelectedIndex=savedFacing;orientation.SelectedIndex=Array.IndexOf(Rotations,savedRotation);mirrorChoice.Checked=savedMirror;
        reconnectButton.Click+=(s,e)=>ReconnectDevices();device.SelectedIndexChanged+=(s,e)=>UpdateControls();
        facing.SelectedIndexChanged+=(s,e)=>UpdateCameraFacing();orientation.SelectedIndexChanged+=(s,e)=>UpdateCameraTransform();mirrorChoice.CheckedChanged+=(s,e)=>UpdateCameraTransform();
        speakerChoice.CheckedChanged+=(s,e)=>ResourceChoiceChanged(PhoneResource.Speaker,speakerChoice);
        cameraChoice.CheckedChanged+=(s,e)=>ResourceChoiceChanged(PhoneResource.Camera,cameraChoice);
        micChoice.CheckedChanged+=(s,e)=>ResourceChoiceChanged(PhoneResource.Microphone,micChoice);
        diagnosticButton.Text="Save diagnostic report";diagnosticButton.Click+=(s,e)=>SaveDiagnostic(false);
        Disposed+=(s,e)=>{retry.Dispose();watcher.Dispose();if(tray!=null)tray.Dispose();details.Dispose();theme.Dispose();};
        retry.Interval=1200;retry.Tick+=(s,e)=>{retry.Stop();Discover(false,null);};
        watcher.Changed+=()=>Post(ScheduleDeviceScan);
        watcher.Warning+=message=>Post(()=>Log(message));
        controller.Changed+=()=>{ClearFailedChoices();UpdateControls();UpdateTray();FinishReconnect();FinishExit();};
        controller.Error+=message=>{lastFailure=message;Log(message);UpdateControls();SaveDiagnostic(true);};
        InitializeTray();
        Shown+=(s,e)=>{Discover(false,()=>{try{watcher.Start();}catch(Exception error){Log(error.Message);}if(autoDevice!=null){Select(device,autoDevice);suppress=true;cameraChoice.Checked=autoMeeting;micChoice.Checked=autoMeeting&&!cameraOnly;speakerChoice.Checked=!autoMeeting;suppress=false;StartPlayback();}});};
        FormClosing+=(s,e)=>{if(e.CloseReason==CloseReason.UserClosing&&!exitRequested){e.Cancel=true;HideToTray();return;}if(controller.Busy){e.Cancel=true;RequestExit();return;}retry.Stop();watcher.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}};
        UpdateControls();
    }
    bool PrepareMicrophone(){
        var outputs=WaveOutput.VirtualDevices();
        microphoneOutput=UiSelection.UniqueMicrophone(outputs);
        if(microphoneOutput!=null)return true;
        if(outputs.Count==0){
            if(MessageBox.Show(this,"VB-CABLE is not available. Open microphone setup?","Microphone",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes)ConfigureMicrophone();
            lastFailure="CABLE Input is unavailable.";Log(lastFailure);view.ShowError(lastFailure);return false;
        }
        using(var dialog=new Form{Text="Microphone output",Font=theme.Body,BackColor=theme.Background,ForeColor=theme.Text,ClientSize=new Size(460,130),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false,Icon=theme.Icon}){
            var choices=new ComboBox{Left=18,Top=20,Width=424,DropDownStyle=ComboBoxStyle.DropDownList};foreach(var output in outputs)choices.Items.Add(output);choices.SelectedIndex=0;
            var confirm=new Button{Text="Use output",Left=224,Top=76,Width=106,DialogResult=DialogResult.OK};var cancel=new Button{Text="Cancel",Left=338,Top=76,Width=104,DialogResult=DialogResult.Cancel};dialog.Controls.AddRange(new Control[]{choices,confirm,cancel});dialog.AcceptButton=confirm;dialog.CancelButton=cancel;
            if(dialog.ShowDialog(this)!=DialogResult.OK)return false;microphoneOutput=choices.SelectedItem as WaveDevice;return microphoneOutput!=null;
        }
    }
    void ConfigureMicrophone(){
        if(controller.Busy){MessageBox.Show(this,"Stop all resources before installing VB-CABLE.");return;}
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
    void SetState(PlaybackState value,string message){if(value==PlaybackState.Failed){lastFailure=message;Log(message);}UpdateControls();}
    void UpdateControls(){
        bool busy=controller.Busy;Device selected=device.SelectedItem as Device;
        bool ready=selected!=null&&selected.Ready&&!scanning&&!reconnecting&&!exitRequested;
        device.Enabled=!busy&&!scanning&&!reconnecting;reconnectButton.Enabled=!scanning&&!reconnecting&&!exitRequested;reconnectButton.Text=reconnecting?"Reconnecting…":"Reconnect";
        speakerChoice.Enabled=ready||(!reconnecting&&!exitRequested&&controller.Active(PhoneResource.Speaker));cameraChoice.Enabled=ready||(!reconnecting&&!exitRequested&&controller.Active(PhoneResource.Camera));micChoice.Enabled=ready||(!reconnecting&&!exitRequested&&controller.Active(PhoneResource.Microphone));
        facing.Enabled=orientation.Enabled=mirrorChoice.Enabled=!reconnecting&&!exitRequested;
        view.ShowDevice(selected,device.Items.Count,scanning,reconnecting);
        foreach(PhoneResource key in Enum.GetValues(typeof(PhoneResource)))view.ShowState(key,controller.Status(key));view.ShowError(lastFailure);
    }
    void ClearFailedChoices(){bool previous=suppress;suppress=true;try{if(controller.Status(PhoneResource.Speaker)=="失败")speakerChoice.Checked=false;if(controller.Status(PhoneResource.Camera)=="失败")cameraChoice.Checked=false;if(controller.Status(PhoneResource.Microphone)=="失败")micChoice.Checked=false;}finally{suppress=previous;}}
    void SaveDiagnostic(bool automatic){
        if(reporting||automatic&&autoReportClock.IsRunning&&autoReportClock.ElapsedMilliseconds<60000)return;
        if(automatic)autoReportClock.Restart();reporting=true;diagnosticButton.Enabled=false;diagnosticButton.Text="Saving report…";
        var state=new Dictionary<string,string>();foreach(PhoneResource key in Enum.GetValues(typeof(PhoneResource)))state[key.ToString()]=controller.Status(key);string failure=lastFailure;
        ThreadPool.QueueUserWorkItem(_=>{string message;try{message="诊断报告已保存："+DiagnosticReport.Save(adb.Root,state,failure,()=>Volatile.Read(ref exitRequested));}catch(Exception error){message="无法保存诊断报告："+error.Message;}Post(()=>{reporting=false;diagnosticButton.Enabled=true;diagnosticButton.Text="Save diagnostic report";Log(message);if(!automatic)MessageBox.Show(this,message,"Diagnostics",MessageBoxButtons.OK,MessageBoxIcon.Information);});});
    }
    void ReconnectDevices(){
        if(exitRequested||scanning||reconnecting)return;
        reconnecting=true;reconnectScanStarted=false;retry.Stop();scanAgain=false;lastFailure=null;
        Log("重新连接：关闭当前资源，清理后重新检查 USB 设备；资源保持待命。");
        StopPlayback();FinishReconnect();UpdateControls();
    }
    void FinishReconnect(){
        if(!reconnecting||exitRequested||controller.Busy||scanning||reconnectScanStarted)return;
        reconnectScanStarted=true;Discover(true,null);
    }
    void Discover(bool fix,Action finished) {
        if(exitRequested||reconnecting&&!fix)return;if(scanning){scanAgain=true;return;}scanning=true;UpdateControls();
        string selected=Serial(device)??lastDevice;bool previouslyReady=devices.Exists(x=>x.Serial==selected&&x.Ready);
        ThreadPool.QueueUserWorkItem(_=>{try{
            if(fix){if(controller.Busy)throw new InvalidOperationException("请先关闭资源再修复设备服务。");watcher.Stop();adb.Run("kill-server");adb.Run("start-server");watcher.Start();}
            List<Device> found=adb.Devices();Post(()=>{scanning=false;if(fix){reconnecting=false;reconnectScanStarted=false;}devices=found;Fill(device,selected,0);
                int ready=found.FindAll(x=>x.Ready).Count;
                Log("设备列表已更新："+found.Count+" 台，已授权 "+ready+" 台。");CheckDevicePresence(selected,previouslyReady);UpdateControls();if(finished!=null)finished();if(scanAgain){scanAgain=false;ScheduleDeviceScan();}});
        }catch(Exception e){Post(()=>{scanning=false;if(fix){reconnecting=false;reconnectScanStarted=false;}Log(e.Message);SetState(PlaybackState.Failed,"设备查找失败，请点击“重新连接”重试。详情见诊断日志。");});}});
    }
    static string Serial(ComboBox box) {Device d=box.SelectedItem as Device;return d==null?null:d.Serial;}
    static void Select(ComboBox box,string id) {foreach(Device d in box.Items)if(d.Serial==id){box.SelectedItem=d;return;}}
    void Fill(ComboBox box,string previous,int fallback) {box.Items.Clear();foreach(Device d in devices)box.Items.Add(d);Select(box,previous);if(box.SelectedItem==null||!((Device)box.SelectedItem).Ready){Device ready=UiSelection.PreferredPhone(devices,previous);if(ready!=null)box.SelectedItem=ready;}if(box.SelectedIndex<0&&devices.Count>fallback)box.SelectedIndex=fallback;}
    ResourceOptions Options(Device selected,PhoneResource resource){WaveDevice target=microphoneOutput;return new ResourceOptions{Serial=selected.Serial,Speaker=resource==PhoneResource.Speaker,Camera=resource==PhoneResource.Camera,Microphone=resource==PhoneResource.Microphone,FrontCamera=facing.SelectedIndex==0,CameraRotation=Rotations[orientation.SelectedIndex],CameraMirror=mirrorChoice.Checked,MicrophoneOutput=target==null?-1:target.Id,MicrophoneOutputName=target==null?null:target.Name};}
    CheckBox Choice(PhoneResource key){return key==PhoneResource.Speaker?speakerChoice:key==PhoneResource.Camera?cameraChoice:micChoice;}
    void StartPlayback(){foreach(PhoneResource key in Enum.GetValues(typeof(PhoneResource)))ApplyResource(key,Choice(key));}
    void ResourceChoiceChanged(PhoneResource key,CheckBox box){if(!suppress)ApplyResource(key,box);}
    void ResetChoice(CheckBox box){bool previous=suppress;suppress=true;box.Checked=false;suppress=previous;}
    void ApplyResource(PhoneResource key,CheckBox box){
        if(exitRequested||reconnecting)return;
        if(!box.Checked){controller.Set(key,false,null);UpdateControls();UpdateTray();return;}
        Device selected=device.SelectedItem as Device;
        if(scanning||selected==null||!selected.Ready){ResetChoice(box);SetState(PlaybackState.Failed,"Connect and authorize a USB phone first.");return;}
        if(key==PhoneResource.Microphone&&!controller.Active(key)&&!PrepareMicrophone()){ResetChoice(box);UpdateControls();return;}
        var options=Options(selected,key);try{options.Validate();}catch(Exception error){ResetChoice(box);SetState(PlaybackState.Failed,error.Message);return;}
        lastFailure=null;lastDevice=selected.Serial;SaveSettings();controller.Set(key,true,options);UpdateControls();UpdateTray();
    }
    void UpdateCameraFacing(){
        if(facing.SelectedIndex<0)return;savedFacing=facing.SelectedIndex;SaveSettings();
        if(suppress||exitRequested||scanning||reconnecting||!cameraChoice.Checked)return;
        Device selected=device.SelectedItem as Device;if(selected==null||!selected.Ready)return;
        Log("切换摄像头："+(savedFacing==0?"前置":"后置"));
        controller.Set(PhoneResource.Camera,true,Options(selected,PhoneResource.Camera));
    }

    void UpdateCameraTransform(){if(orientation.SelectedIndex<0)return;savedRotation=Rotations[orientation.SelectedIndex];savedMirror=mirrorChoice.Checked;controller.SetCameraTransform(savedRotation,savedMirror);SaveSettings();}
    void StopPlayback(){suppress=true;speakerChoice.Checked=cameraChoice.Checked=micChoice.Checked=false;suppress=false;controller.StopAll();UpdateControls();UpdateTray();}
    void CheckDevicePresence(string previous,bool previouslyReady){
        Device present=devices.Find(x=>x.Serial==previous&&x.Ready);
        if(DevicePresence.MustRelease(previouslyReady,present!=null,controller.Busy)){suppress=true;speakerChoice.Checked=cameraChoice.Checked=micChoice.Checked=false;suppress=false;controller.StopAll();Log("手机已断开，释放采集资源并进入低耗待机。");}

    }
    void ScheduleDeviceScan(){if(exitRequested||reconnecting)return;retry.Stop();retry.Start();}
    protected override void WndProc(ref Message message){base.WndProc(ref message);if(message.Msg==0x219&&retry!=null)ScheduleDeviceScan();}
    void InitializeTray(){
        ContextMenuStrip menu=new ContextMenuStrip();menu.Items.Add("Open PocketLink",null,(s,e)=>RevealWindow());menu.Items.Add(new ToolStripSeparator());
        traySpeaker=new ToolStripMenuItem("Speaker",null,(s,e)=>TrayToggle(PhoneResource.Speaker));trayCamera=new ToolStripMenuItem("Camera",null,(s,e)=>TrayToggle(PhoneResource.Camera));trayMic=new ToolStripMenuItem("Microphone",null,(s,e)=>TrayToggle(PhoneResource.Microphone));menu.Items.AddRange(new ToolStripItem[]{traySpeaker,trayCamera,trayMic});menu.Items.Add("Stop all",null,(s,e)=>StopPlayback());menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Diagnostics",null,(s,e)=>ShowDiagnostics());menu.Items.Add(diagnosticButton);menu.Items.Add("Microphone setup",null,(s,e)=>ConfigureMicrophone());menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Exit PocketLink",null,(s,e)=>RequestExit());
        tray=new NotifyIcon{Icon=theme.Icon,Text="PocketLink · Standby",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>RevealWindow();
    }
    void ShowDiagnostics(){using(var dialog=new Form{Text="PocketLink · Diagnostics",ClientSize=new Size(760,420),StartPosition=FormStartPosition.CenterParent,Icon=theme.Icon}){var log=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,BackColor=theme.Background,ForeColor=theme.Text,Font=new Font("Consolas",10),Text=details.Text};dialog.Controls.Add(log);dialog.ShowDialog(this);}}
    void TrayToggle(PhoneResource key){if(reconnecting||exitRequested)return;var box=Choice(key);suppress=true;box.Checked=!controller.Active(key);suppress=false;RevealWindow();ApplyResource(key,box);}
    void UpdateTray(){if(tray==null)return;traySpeaker.Checked=controller.Active(PhoneResource.Speaker);trayCamera.Checked=controller.Active(PhoneResource.Camera);trayMic.Checked=controller.Active(PhoneResource.Microphone);tray.Text=controller.Busy?"PocketLink · Resources active":"PocketLink · Standby";}
    public void RevealWindow(){if(InvokeRequired){Post(RevealWindow);return;}Show();WindowState=FormWindowState.Normal;Activate();}
    public void CloseWindow(){if(InvokeRequired){Post(CloseWindow);return;}Close();}
    public void HideToTray(){if(InvokeRequired){Post(HideToTray);return;}Hide();if(firstHide&&tray!=null){firstHide=false;tray.ShowBalloonTip(4000,"PocketLink is in the tray",controller.Busy?"Resources keep running. Right-click the tray icon to stop or exit.":"Right-click the tray icon to open or exit PocketLink.",ToolTipIcon.Info);}}
    public void RequestExit(){if(InvokeRequired){Post(RequestExit);return;}if(exitRequested)return;exitRequested=true;retry.Stop();controller.StopAll();FinishExit();}
    void FinishExit(){if(!exitRequested||controller.Busy||exiting)return;exiting=true;watcher.Dispose();ThreadPool.QueueUserWorkItem(_=>{try{adb.Run("kill-server");}catch{}Post(()=>Close());});}
    void ReadSettings() {try{string[] values=File.ReadAllLines(settingsFile);if(values.Length>0)lastDevice=values[0];int rotation;if(values.Length>1&&Int32.TryParse(values[1],out rotation)&&Array.IndexOf(Rotations,rotation)>=0)savedRotation=rotation;int lens;if(values.Length>2&&Int32.TryParse(values[2],out lens)&&(lens==0||lens==1))savedFacing=lens;if(values.Length>3)Boolean.TryParse(values[3],out savedMirror);}catch(IOException){}catch(UnauthorizedAccessException){}}
    void SaveSettings() {try{Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));File.WriteAllLines(settingsFile,new string[]{lastDevice??"",savedRotation.ToString(),savedFacing.ToString(),savedMirror.ToString()});}catch(Exception e){Log("无法保存设备选择："+e.Message);}}
}
