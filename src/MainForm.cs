using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

enum PlaybackState { Idle, Connecting, Playing, Stopping, Failed }

sealed class MainForm : Form {
    readonly Adb adb;
    readonly TabControl modes=new TabControl();
    readonly ComboBox device=new ComboBox(),left=new ComboBox(),right=new ComboBox();
    readonly NumericUpDown delayL=new NumericUpDown(),delayR=new NumericUpDown();
    readonly Button singleStart=new Button(),dualStart=new Button(),refresh=new Button(),repair=new Button(),swap=new Button(),test=new Button();
    readonly Label heading=new Label(),hint=new Label(),deviceCount=new Label(),statusTitle=new Label(),statusHint=new Label();
    readonly TextBox details=new TextBox();
    readonly Panel logPanel=new Panel();
    readonly CheckBox reconnect=new CheckBox();
    readonly System.Windows.Forms.Timer retry=new System.Windows.Forms.Timer();
    List<Device> devices=new List<Device>();
    Relay relay; PlaybackState state; bool scanning,closingRequested,wasSingle,stopRequested,sessionFailed,retryPending;
    string lastSingle,lastLeft,lastRight;
    readonly string settingsFile;
    System.Media.SoundPlayer player; MemoryStream tone;
    static readonly Color Ink=Color.FromArgb(29,43,64),Muted=Color.FromArgb(91,105,124),Blue=Color.FromArgb(35,91,216);

    public MainForm(string root,string autoLeft,string autoRight) {
        adb=new Adb(root);settingsFile=Path.Combine(root,".runtime","devices.txt");ReadSettings();
        Text="SoundLink · 手机扬声器";Font=new Font("Microsoft YaHei UI",10);ForeColor=Ink;BackColor=Color.FromArgb(245,247,251);
        ClientSize=new Size(920,680);MinimumSize=new Size(870,660);StartPosition=FormStartPosition.CenterScreen;
        AutoScaleMode=AutoScaleMode.Dpi;
        TableLayoutPanel layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=6};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,78));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        Panel header=new Panel{Dock=DockStyle.Fill};layout.Controls.Add(header,0,0);
        heading.Text="SoundLink";heading.Font=new Font(Font.FontFamily,23,FontStyle.Bold);heading.SetBounds(0,0,350,40);header.Controls.Add(heading);
        Label intro=TextLabel("把电脑的声音，通过 USB 送到手机。",0,45,760,25);intro.ForeColor=Muted;header.Controls.Add(intro);
        TableLayoutPanel toolbar=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));layout.Controls.Add(toolbar,0,1);
        deviceCount.Text="尚未查找设备";deviceCount.Dock=DockStyle.Fill;deviceCount.TextAlign=ContentAlignment.MiddleLeft;toolbar.Controls.Add(deviceCount,0,0);
        ConfigureButton(refresh,"刷新设备",false);ConfigureButton(repair,"修复连接",false);toolbar.Controls.Add(refresh,1,0);toolbar.Controls.Add(repair,2,0);
        refresh.Click+=(s,e)=>Discover(false,null);repair.Click+=(s,e)=>Discover(true,null);
        modes.Dock=DockStyle.Fill;modes.Font=new Font(Font.FontFamily,11);modes.Padding=new Point(22,10);layout.Controls.Add(modes,0,2);
        TabPage one=new TabPage("单设备"),two=new TabPage("双设备 · 开发中");one.BackColor=two.BackColor=Color.White;modes.TabPages.Add(one);modes.TabPages.Add(two);
        BuildSingle(one);BuildDual(two);
        modes.Selecting+=(s,e)=>{if(relay!=null)e.Cancel=true;};modes.SelectedIndexChanged+=(s,e)=>UpdateControls();
        Panel banner=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(233,239,249),Padding=new Padding(12)};layout.Controls.Add(banner,0,3);
        statusTitle.SetBounds(12,9,800,24);statusTitle.Font=new Font(Font.FontFamily,11,FontStyle.Bold);banner.Controls.Add(statusTitle);
        statusHint.SetBounds(12,35,800,30);statusHint.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;statusHint.ForeColor=Muted;banner.Controls.Add(statusHint);
        Button logToggle=new Button{Text="显示诊断日志",FlatStyle=FlatStyle.Flat,Dock=DockStyle.Left,Width=130};logToggle.FlatAppearance.BorderSize=0;layout.Controls.Add(logToggle,0,4);
        logPanel.Dock=DockStyle.Fill;logPanel.Height=130;logPanel.Visible=false;layout.Controls.Add(logPanel,0,5);
        details.Dock=DockStyle.Fill;details.ReadOnly=true;details.Multiline=true;details.ScrollBars=ScrollBars.Vertical;details.Font=new Font("Consolas",9);logPanel.Controls.Add(details);
        logToggle.Click+=(s,e)=>{logPanel.Visible=!logPanel.Visible;logToggle.Text=logPanel.Visible?"隐藏诊断日志":"显示诊断日志";};
        retry.Interval=5000;retry.Tick+=(s,e)=>{if(retryPending && relay==null && !scanning)Discover(false,()=>{Device d=device.SelectedItem as Device;if(d!=null&&d.Ready&&reconnect.Checked)StartPlayback(true);});};retry.Start();
        reconnect.CheckedChanged+=(s,e)=>{if(!reconnect.Checked)retryPending=false;};
        Shown+=(s,e)=>Discover(false,()=>{if(autoLeft!=null){modes.SelectedIndex=autoRight==null?0:1;Select(device,autoLeft);Select(left,autoLeft);Select(right,autoRight);StartPlayback(autoRight==null);}});
        FormClosing+=(s,e)=>{if(relay!=null){e.Cancel=true;closingRequested=true;StopPlayback();return;}retry.Stop();StopTone();};
        SetState(PlaybackState.Idle,"连接手机后，选择设备并开始播放。");
    }
    static Label TextLabel(string text,int x,int y,int w,int h) {return new Label{Text=text,Location=new Point(x,y),Size=new Size(w,h)};}
    static void ConfigureButton(Button b,string text,bool primary) {b.Text=text;b.Dock=DockStyle.Fill;b.Margin=new Padding(4,5,4,5);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=primary?0:1;b.BackColor=primary?Blue:Color.White;b.ForeColor=primary?Color.White:Ink;}
    static void ConfigureChoice(ComboBox box) {box.DropDownStyle=ComboBoxStyle.DropDownList;box.Dock=DockStyle.Fill;box.Margin=new Padding(0,6,0,10);}
    TableLayoutPanel PageLayout(TabPage page,int rows) {TableLayoutPanel p=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,18,20,12),ColumnCount=1,RowCount=rows};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));page.Controls.Add(p);return p;}
    void BuildSingle(TabPage page) {
        TableLayoutPanel p=PageLayout(page,6);
        p.RowStyles.Add(new RowStyle(SizeType.Absolute,47));p.RowStyles.Add(new RowStyle(SizeType.Absolute,24));p.RowStyles.Add(new RowStyle(SizeType.Absolute,47));p.RowStyles.Add(new RowStyle(SizeType.Absolute,34));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));p.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        p.Controls.Add(new Label{Text="完整播放电脑音频\n基于 AudioShare；一台手机接收完整的左右声道。",Dock=DockStyle.Fill,ForeColor=Muted},0,0);
        p.Controls.Add(new Label{Text="播放设备",Dock=DockStyle.Fill},0,1);ConfigureChoice(device);p.Controls.Add(device,0,2);device.SelectedIndexChanged+=(s,e)=>UpdateControls();
        reconnect.Text="意外断开后，自动重新连接所选设备";reconnect.Dock=DockStyle.Fill;p.Controls.Add(reconnect,0,3);
        hint.Text="解锁手机并允许 USB 调试。连接成功后，在电脑播放网易云等应用的音频。\n音量使用手机的媒体音量调节。";hint.Dock=DockStyle.Fill;hint.ForeColor=Muted;hint.Padding=new Padding(0,10,0,0);p.Controls.Add(hint,0,4);
        ConfigureButton(singleStart,"连接并播放",true);p.Controls.Add(singleStart,0,5);singleStart.Click+=(s,e)=>{if(relay!=null)StopPlayback();else StartPlayback(true);};
    }
    void BuildDual(TabPage page) {
        TableLayoutPanel p=PageLayout(page,4);p.RowStyles.Add(new RowStyle(SizeType.Absolute,55));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));p.RowStyles.Add(new RowStyle(SizeType.Absolute,35));p.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        p.Controls.Add(new Label{Text="开发中 · 两台手机分别播放左、右声道\n同步效果及声道隔离仍需实机验收；建议先验证单设备播放。",ForeColor=Color.FromArgb(150,92,20),Dock=DockStyle.Fill},0,0);
        TableLayoutPanel pair=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));p.Controls.Add(pair,0,1);
        pair.Controls.Add(ChannelPanel("L  左声道",left,delayL),0,0);pair.Controls.Add(ChannelPanel("R  右声道",right,delayR),1,0);
        p.Controls.Add(new Label{Text="延迟补偿：给较早发声的手机增加延迟。调整前请先停止连接。",ForeColor=Muted,Dock=DockStyle.Fill},0,2);
        TableLayoutPanel buttons=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,30));p.Controls.Add(buttons,0,3);
        ConfigureButton(dualStart,"连接两台设备",true);ConfigureButton(swap,"交换左右",false);ConfigureButton(test,"测试左右声道",false);buttons.Controls.Add(dualStart,0,0);buttons.Controls.Add(swap,1,0);buttons.Controls.Add(test,2,0);
        dualStart.Click+=(s,e)=>{if(relay!=null)StopPlayback();else StartPlayback(false);};swap.Click+=(s,e)=>{object temp=left.SelectedItem;left.SelectedItem=right.SelectedItem;right.SelectedItem=temp;UpdateControls();};test.Click+=(s,e)=>TestTone();
        left.SelectedIndexChanged+=(s,e)=>UpdateControls();right.SelectedIndexChanged+=(s,e)=>UpdateControls();
    }
    Control ChannelPanel(string name,ComboBox box,NumericUpDown delay) {
        TableLayoutPanel panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,0,12,0),ColumnCount=1,RowCount=4};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,48));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,26));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        panel.Controls.Add(new Label{Text=name,Font=new Font(Font.FontFamily,12,FontStyle.Bold),Dock=DockStyle.Fill},0,0);ConfigureChoice(box);panel.Controls.Add(box,0,1);
        panel.Controls.Add(new Label{Text="延迟补偿（毫秒）",ForeColor=Muted,Dock=DockStyle.Fill},0,2);delay.Minimum=0;delay.Maximum=500;delay.Increment=5;delay.Dock=DockStyle.Left;delay.Width=120;panel.Controls.Add(delay,0,3);return panel;
    }
    void Post(Action action) {if(IsDisposed||Disposing||!IsHandleCreated)return;try{BeginInvoke(new Action(()=>{if(!IsDisposed&&!Disposing)action();}));}catch(InvalidOperationException){}}
    void Log(string message) {
        if(InvokeRequired){Post(()=>Log(message));return;}
        if(IsDisposed)return;
        if(details.TextLength>48000)details.Text=details.Text.Substring(details.TextLength-24000);
        details.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+message+Environment.NewLine);
        if(message.StartsWith("停止：")){sessionFailed=true;SetState(PlaybackState.Failed,message.Substring(3));}
    }
    void SetState(PlaybackState value,string message) {state=value;statusTitle.Text=value==PlaybackState.Idle?"未连接":value==PlaybackState.Connecting?"正在连接":value==PlaybackState.Playing?"已连接 · 音频传输已启动":value==PlaybackState.Stopping?"正在断开连接":"连接失败";statusTitle.ForeColor=value==PlaybackState.Failed?Color.Firebrick:value==PlaybackState.Playing?Color.FromArgb(20,112,72):Ink;statusHint.Text=message;UpdateControls();}
    void UpdateControls() {
        bool active=relay!=null,busy=active||scanning;
        device.Enabled=left.Enabled=right.Enabled=delayL.Enabled=delayR.Enabled=swap.Enabled=!busy;
        refresh.Enabled=repair.Enabled=!busy;
        singleStart.Text=active&&wasSingle?(state==PlaybackState.Stopping?"正在停止…":"断开连接"):"连接并播放";
        dualStart.Text=active&&!wasSingle?(state==PlaybackState.Stopping?"正在停止…":"断开连接"):"连接两台设备";
        Device d=device.SelectedItem as Device,l=left.SelectedItem as Device,r=right.SelectedItem as Device;
        singleStart.Enabled=active?wasSingle&&state!=PlaybackState.Stopping:!scanning&&d!=null&&d.Ready;
        dualStart.Enabled=active?!wasSingle&&state!=PlaybackState.Stopping:!scanning&&l!=null&&r!=null&&l.Ready&&r.Ready&&l.Serial!=r.Serial;
        test.Enabled=active&&!wasSingle&&state==PlaybackState.Playing;
    }
    void Discover(bool fix,Action finished) {
        if(scanning||relay!=null)return;scanning=true;deviceCount.Text=fix?"正在修复连接…":"正在查找设备…";UpdateControls();
        string selected=Serial(device)??lastSingle,selectedL=Serial(left)??lastLeft,selectedR=Serial(right)??lastRight;
        ThreadPool.QueueUserWorkItem(_=>{try{
            if(fix){adb.Run("kill-server");adb.Run("start-server");}
            List<Device> found=adb.Devices();Post(()=>{scanning=false;devices=found;Fill(device,selected,0);Fill(left,selectedL,0);Fill(right,selectedR,1);
                int ready=found.FindAll(x=>x.Ready).Count;deviceCount.Text=ready+" 台可连接"+(found.Count>ready?" · "+(found.Count-ready)+" 台待授权或离线":"");
                hint.Text=found.Count==0?"未发现手机。检查 USB 数据线，并在手机开启 USB 调试；也可点击上方“修复连接”。":ready==0?"手机已发现。请解锁手机，允许 USB 调试；若显示离线，请重新插拔后刷新。":"单设备播放完整立体声。连接成功后，在电脑播放网易云等应用；使用手机媒体音量调节响度。";
                Log("设备列表已更新："+found.Count+" 台，已授权 "+ready+" 台。");UpdateControls();if(finished!=null)finished();});
        }catch(Exception e){Post(()=>{scanning=false;deviceCount.Text="设备查找失败";Log(e.Message);SetState(PlaybackState.Failed,"设备查找失败，请点击“修复连接”重试。详情见诊断日志。");});}});
    }
    static string Serial(ComboBox box) {Device d=box.SelectedItem as Device;return d==null?null:d.Serial;}
    static void Select(ComboBox box,string id) {foreach(Device d in box.Items)if(d.Serial==id){box.SelectedItem=d;return;}}
    void Fill(ComboBox box,string previous,int fallback) {box.Items.Clear();foreach(Device d in devices)box.Items.Add(d);Select(box,previous);if(box.SelectedIndex<0&&devices.Count>fallback)box.SelectedIndex=fallback;}
    void StartPlayback(bool single) {
        if(relay!=null||scanning)return;Device d=(single?device:left).SelectedItem as Device,r=right.SelectedItem as Device;
        if(d==null||!d.Ready||(!single&&(r==null||!r.Ready||d.Serial==r.Serial))){SetState(PlaybackState.Failed,single?"请选择一台已授权的手机。":"请选择两台不同且已授权的手机。");return;}
        wasSingle=single;stopRequested=sessionFailed=retryPending=false;
        lastSingle=Serial(device);lastLeft=Serial(left);lastRight=Serial(right);SaveSettings();
        Relay current=new Relay(adb,Log);relay=current;
        current.Connected+=()=>Post(()=>{if(relay==current&&state!=PlaybackState.Stopping)SetState(PlaybackState.Playing,single?"手机接收完整立体声。请在电脑播放音乐。":"左右声道已分别发送。请用专门测试音验证声道隔离。");});
        SetState(PlaybackState.Connecting,single?"正在准备手机播放。可点击“断开连接”取消。":"正在准备左右两台手机。此模式仍在开发中。");
        current.Start(d.Serial,single?null:r.Serial,single?0:(int)delayL.Value,single?0:(int)delayR.Value,()=>Post(()=>{
            if(relay!=current)return;relay=null;StopTone();if(!sessionFailed||stopRequested)SetState(PlaybackState.Idle,"连接已断开，可以重新选择设备。");else UpdateControls();
            retryPending=wasSingle&&reconnect.Checked&&!stopRequested;
            if(closingRequested)Close();
        }));
    }
    void StopPlayback() {retryPending=false;stopRequested=true;StopTone();if(relay==null)return;SetState(PlaybackState.Stopping,"正在关闭音频通道，请稍候。");relay.Stop();}
    void ReadSettings() {try{string[] values=File.ReadAllLines(settingsFile);if(values.Length>=3){lastSingle=values[0];lastLeft=values[1];lastRight=values[2];}}catch(IOException){}catch(UnauthorizedAccessException){}}
    void SaveSettings() {try{Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));File.WriteAllLines(settingsFile,new string[]{lastSingle??"",lastLeft??"",lastRight??""});}catch(Exception e){Log("无法保存设备选择："+e.Message);}}
    void StopTone() {if(player!=null){player.Stop();player.Dispose();player=null;}if(tone!=null){tone.Dispose();tone=null;}}
    void TestTone() {
        if(state!=PlaybackState.Playing||wasSingle)return;StopTone();tone=TestAudio.CreateWave();player=new System.Media.SoundPlayer(tone);player.Play();
        statusHint.Text="测试顺序：左低音 → 静音 → 右高音 → 静音 → 左低音。请暂停其他电脑音频，检查另一台是否静音。";Log("播放明确左右分离的测试信号；普通歌曲不能代替声道隔离验收。");
    }
}

static class TestAudio {
    public static MemoryStream CreateWave() {
        const int rate=48000;byte[] data=new byte[rate*6*4];
        for(int i=0;i<rate*6;i++){int second=i/rate,channel=(second/2)%2;double phase=(i%rate)/(double)rate;double fade=Math.Min(1,Math.Min(phase*100,(1-phase)*100));short sample=second%2==0?(short)(Math.Sin(2*Math.PI*(channel==0?440:880)*i/rate)*6000*fade):(short)0;int at=i*4+channel*2;data[at]=(byte)sample;data[at+1]=(byte)(sample>>8);}
        MemoryStream stream=new MemoryStream();BinaryWriter writer=new BinaryWriter(stream);writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+data.Length);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)2);writer.Write(rate);writer.Write(rate*4);writer.Write((short)4);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(data.Length);writer.Write(data);stream.Position=0;return stream;
    }
}
