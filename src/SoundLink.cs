using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Native {
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int AudioCapture_Initialize();
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int AudioCapture_Listen(int port, IntPtr callback);
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int AudioCapture_IsConnected(IntPtr text, int size);
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int AudioCapture_Start();
    [DllImport("audio_capture.dll", CallingConvention=CallingConvention.Cdecl)] public static extern void AudioCapture_Cleanup();
}

// One input frame produces one sample per phone. Delay never changes sample count.
public sealed class ChannelDelay {
    readonly byte[] ring; int pos;
    public ChannelDelay(int samples) { ring = new byte[samples * 2]; }
    public void Apply(byte[] data) {
        if (ring.Length == 0) return;
        for (int i=0;i<data.Length;i++) { byte old=ring[pos]; ring[pos]=data[i]; data[i]=old; pos=(pos+1)%ring.Length; }
    }
}
static class Pcm {
    public static void Split(byte[] stereo, int count, byte[] left, byte[] right) {
        if (count%4!=0) throw new ArgumentException("Incomplete stereo frame");
        for(int i=0,j=0;i<count;i+=4,j+=2) { left[j]=stereo[i]; left[j+1]=stereo[i+1]; right[j]=stereo[i+2]; right[j+1]=stereo[i+3]; }
    }
    public static byte[] Header(int rate) {
        byte[] h=new byte[8]; Array.Copy(BitConverter.GetBytes(rate),h,4); h[4]=1; h[6]=16; return h;
    }
    public static void ReadExact(Stream stream, byte[] data) {
        int pos=0; while(pos<data.Length) { int n=stream.Read(data,pos,data.Length-pos); if(n==0) throw new EndOfStreamException("Audio connection closed"); pos+=n; }
    }
}

sealed class Device {
    public string Serial, Description;
    public override string ToString() { return Description+" ["+Serial+"]"; }
}
sealed class Adb {
    public readonly string Root, Exe;
    public Adb(string root) { Root=root; Exe=Path.Combine(root,"tools/platform-tools/adb.exe"); }
    public Process Spawn(string args) {
        ProcessStartInfo p=new ProcessStartInfo(Exe,args); p.UseShellExecute=false; p.CreateNoWindow=true;
        p.RedirectStandardOutput=true; p.RedirectStandardError=true; p.EnvironmentVariables["ADB_LIBUSB"]="1";
        Process proc=Process.Start(p); return proc;
    }
    public string Run(string args) {
        using(Process p=Spawn(args)) {
            string output="",error="";
            p.OutputDataReceived+=(s,e)=> { if(e.Data!=null) output+=e.Data+"\n"; };
            p.ErrorDataReceived+=(s,e)=> { if(e.Data!=null) error+=e.Data+"\n"; };
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            if(!p.WaitForExit(15000)) { p.Kill(); throw new Exception("ADB timeout: "+args); } p.WaitForExit();
            if(p.ExitCode!=0) throw new Exception(error+output); return output;
        }
    }
    public static string Q(string value) { if(value.IndexOf('"')>=0) throw new ArgumentException("Invalid argument"); return "\""+value+"\""; }
    public List<Device> Devices() {
        List<Device> result=new List<Device>();
        foreach(string line in Run("devices -l").Split('\n')) {
            string[] parts=line.Split(new char[]{' ','\t','\r'},StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length<2 || parts[1]!="device") continue;
            string name=parts[0]; foreach(string part in parts) if(part.StartsWith("model:")) name=part.Substring(6);
            result.Add(new Device{Serial=parts[0],Description=name});
        } return result;
    }
}

sealed class Relay : IDisposable {
    readonly Adb adb; readonly Action<string> log;
    readonly List<Process> children=new List<Process>();
    readonly List<TcpClient> sockets=new List<TcpClient>();
    readonly List<TcpListener> listeners=new List<TcpListener>();
    readonly List<string> serials=new List<string>();
    readonly string tunnel="soundlink_"+Process.GetCurrentProcess().Id;
    volatile bool stopping; Thread worker; bool nativeInitialized;
    public Relay(Adb a, Action<string> l) { adb=a; log=l; }
    public void Start(string left,string right,int leftDelay,int rightDelay,Action ended) {
        worker=new Thread(()=> {
            try { Run(left,right,leftDelay,rightDelay); }
            catch(Exception e) { if(!stopping) log("停止："+e.Message); }
            finally { Cleanup(); ended(); }
        }); worker.IsBackground=true; worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    TcpListener Listen() { TcpListener l=new TcpListener(IPAddress.Loopback,0); l.Start(); lock(listeners) listeners.Add(l); return l; }
    TcpClient Accept(TcpListener listener) {
        var pending=listener.BeginAcceptTcpClient(null,null);
        if(!pending.AsyncWaitHandle.WaitOne(15000)) throw new Exception("手机连接超时，请检查 USB 调试授权");
        TcpClient c=listener.EndAcceptTcpClient(pending); c.NoDelay=true; c.SendTimeout=1500; c.ReceiveTimeout=15000;
        lock(sockets) sockets.Add(c); return c;
    }
    void Run(string left,string right,int delayL,int delayR) {
        if(left==right) throw new Exception("左右声道必须选择不同手机");
        log("准备左右设备…");
        TcpListener[] outputs={Listen(),Listen()}; string[] ids={left,right};
        for(int i=0;i<2;i++) {
            string prefix="-s "+Adb.Q(ids[i])+" ";
            adb.Run(prefix+"push "+Adb.Q(Path.Combine(adb.Root,"tools/AudioShare/server"))+" /data/local/tmp/soundlink-server");
            adb.Run(prefix+"reverse localabstract:"+tunnel+" tcp:"+((IPEndPoint)outputs[i].LocalEndpoint).Port);
            serials.Add(ids[i]);
            Process p=adb.Spawn(prefix+"shell app_process -Djava.class.path=/data/local/tmp/soundlink-server /data/local/tmp com.ysbing.audioshare.Main socketName="+tunnel+" connectCode="+ids[i]);
            p.OutputDataReceived+=(s,e)=>{}; p.ErrorDataReceived+=(s,e)=>{if(!String.IsNullOrEmpty(e.Data)) log(e.Data);};
            p.BeginOutputReadLine();p.BeginErrorReadLine(); children.Add(p);
        }
        TcpClient lc=Accept(outputs[0]), rc=Accept(outputs[1]);
        // Consume each phone's handshake before starting the shared audio timeline.
        byte[] hello=new byte[256]; lc.GetStream().Read(hello,0,hello.Length); rc.GetStream().Read(hello,0,hello.Length);
        if(stopping) return;
        if(Native.AudioCapture_Initialize()!=1) throw new Exception("无法采集 Windows 默认输出设备"); nativeInitialized=true;
        // Reserve a local ephemeral port, then let the upstream capture DLL listen on it.
        TcpListener reserve=Listen(); int port=((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
        if(Native.AudioCapture_Listen(port,IntPtr.Zero)!=1) throw new Exception("采集端口启动失败");
        TcpClient input=new TcpClient(); lock(sockets) sockets.Add(input); input.Connect(IPAddress.Loopback,port);
        input.ReceiveTimeout=3000; input.GetStream().WriteByte(83);
        Stopwatch wait=Stopwatch.StartNew();
        while(Native.AudioCapture_IsConnected(IntPtr.Zero,0)!=1) { if(stopping) return; if(wait.ElapsedMilliseconds>3000) throw new Exception("采集握手超时"); Thread.Sleep(10); }
        if(Native.AudioCapture_Start()!=1) throw new Exception("音频采集启动失败");
        byte[] header=new byte[8]; Pcm.ReadExact(input.GetStream(),header);
        int rate=BitConverter.ToInt32(header,0);
        if(BitConverter.ToUInt16(header,4)!=2 || BitConverter.ToUInt16(header,6)!=16 || rate<8000 || rate>192000) throw new Exception("不支持的采集格式");
        byte[] mono=Pcm.Header(rate); lc.GetStream().Write(mono,0,8);rc.GetStream().Write(mono,0,8);
        ChannelDelay dl=new ChannelDelay(rate*delayL/1000), dr=new ChannelDelay(rate*delayR/1000);
        byte[] source=new byte[rate/100*4], ldata=new byte[source.Length/2],rdata=new byte[source.Length/2];
        input.ReceiveTimeout=0; log("已连接：左="+left+"，右="+right+"；"+rate+" Hz。请播放网易云音乐。");
        long frames=0; Stopwatch stats=Stopwatch.StartNew();
        while(!stopping) {
            Pcm.ReadExact(input.GetStream(),source); Pcm.Split(source,source.Length,ldata,rdata); dl.Apply(ldata);dr.Apply(rdata);
            lc.GetStream().Write(ldata,0,ldata.Length); rc.GetStream().Write(rdata,0,rdata.Length); frames+=source.Length/4;
            if(stats.ElapsedMilliseconds>=5000) { log("传输中：每声道 "+frames+" 帧；延迟补偿 L="+delayL+" ms / R="+delayR+" ms"); stats.Restart(); }
        }
    }
    void CloseIO() { lock(sockets) foreach(TcpClient c in sockets) { try { c.Close(); } catch {} } lock(listeners) foreach(TcpListener l in listeners) { try { l.Stop(); } catch {} } }
    void Cleanup() {
        CloseIO(); if(nativeInitialized) { Native.AudioCapture_Cleanup(); nativeInitialized=false; }
        foreach(Process p in children) { try { if(!p.HasExited) p.Kill(); p.Dispose(); } catch {} }
        foreach(string id in serials) { try { adb.Run("-s "+Adb.Q(id)+" reverse --remove localabstract:"+tunnel); } catch {} }
    }
    public void Stop() { stopping=true;CloseIO(); }
    public void Dispose() { Stop(); if(worker!=null && !worker.Join(20000)) log("连接仍在退出，请稍候"); }
}

sealed class MainForm : Form {
    readonly ComboBox left=new ComboBox(),right=new ComboBox();
    readonly NumericUpDown delayL=new NumericUpDown(),delayR=new NumericUpDown();
    readonly TextBox status=new TextBox(); readonly Button start=new Button(), refresh=new Button();
    readonly Adb adb; Relay relay; bool closing;
    public MainForm(string root,string autoLeft,string autoRight) {
        adb=new Adb(root); Text="SoundLink · 双手机立体声"; ClientSize=new Size(740,440); MinimumSize=new Size(756,479);
        Font=new Font("Microsoft YaHei UI",10); StartPosition=FormStartPosition.CenterScreen;
        Label title=new Label{Text="两台手机，分别播放左 / 右声道",AutoSize=true,Location=new Point(20,18)};Controls.Add(title);
        AddLabel("左声道手机",20,65); AddLabel("右声道手机",385,65);
        left.SetBounds(20,90,335,30);right.SetBounds(385,90,335,30);left.DropDownStyle=right.DropDownStyle=ComboBoxStyle.DropDownList;
        Controls.Add(left);Controls.Add(right);
        AddLabel("左延迟补偿 (ms)",20,135);AddLabel("右延迟补偿 (ms)",385,135);
        delayL.SetBounds(180,132,90,30);delayR.SetBounds(545,132,90,30);delayL.Maximum=delayR.Maximum=500;
        Controls.Add(delayL);Controls.Add(delayR);
        refresh.Text="刷新设备";refresh.SetBounds(20,185,110,36);refresh.Click+=(s,e)=>RefreshDevices();Controls.Add(refresh);
        Button swap=new Button{Text="交换左右",Bounds=new Rectangle(145,185,110,36)};swap.Click+=(s,e)=>{if(relay!=null)return;object tmp=left.SelectedItem;left.SelectedItem=right.SelectedItem;right.SelectedItem=tmp;};Controls.Add(swap);
        start.Text="开始双声道";start.SetBounds(270,185,145,36);start.Click+=(s,e)=>Toggle();Controls.Add(start);
        Button test=new Button{Text="左右声道测试",Bounds=new Rectangle(430,185,160,36)};test.Click+=(s,e)=>TestTone();Controls.Add(test);
        status.SetBounds(20,240,700,175);status.Multiline=true;status.ReadOnly=true;status.ScrollBars=ScrollBars.Vertical;status.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;Controls.Add(status);
        FormClosing+=(s,e)=>{closing=true;if(relay!=null)relay.Dispose();};
        Shown+=(s,e)=>{RefreshDevices();if(autoLeft!=null && autoRight!=null) { Select(left,autoLeft);Select(right,autoRight);Toggle(); }};
    }
    static void Select(ComboBox box,string id) { foreach(Device d in box.Items) if(d.Serial==id)box.SelectedItem=d; }
    void AddLabel(string text,int x,int y) { Controls.Add(new Label{Text=text,AutoSize=true,Location=new Point(x,y)}); }
    void Log(string text) { if(closing || IsDisposed)return; if(InvokeRequired) {BeginInvoke(new Action<string>(Log),text);return;}status.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+text+Environment.NewLine); }
    void RefreshDevices() { try { var devices=adb.Devices();left.Items.Clear();right.Items.Clear();foreach(Device d in devices){left.Items.Add(d);right.Items.Add(d);}if(devices.Count>0)left.SelectedIndex=0;if(devices.Count>1)right.SelectedIndex=1;Log("已授权设备："+devices.Count+" 台。需要两台不同手机。"); }catch(Exception e){Log(e.Message);} }
    void Toggle() {
        if(relay!=null) {start.Enabled=false;relay.Stop();Log("正在停止…");return;}
        Device l=left.SelectedItem as Device,r=right.SelectedItem as Device;
        if(l==null || r==null || l.Serial==r.Serial) {Log("请选择两台不同且已授权的手机");return;}
        SetRunning(true);Relay current=new Relay(adb,Log);relay=current;
        current.Start(l.Serial,r.Serial,(int)delayL.Value,(int)delayR.Value,()=>{if(!closing)BeginInvoke(new Action(()=>{if(relay==current){relay=null;SetRunning(false);}}));});
    }
    void SetRunning(bool running) {start.Enabled=true;start.Text=running?"停止":"开始双声道";left.Enabled=right.Enabled=delayL.Enabled=delayR.Enabled=refresh.Enabled=!running;}
    MemoryStream tone; System.Media.SoundPlayer player;
    void TestTone() {
        if(relay==null){Log("请先开始双声道连接");return;}
        int rate=48000,frames=rate*6;byte[] data=new byte[frames*4];
        for(int i=0;i<frames;i++) {int second=i/rate;int channel=(second/2)%2;double phase=(i%rate)/(double)rate;double fade=Math.Min(1,Math.Min(phase*100,(1-phase)*100)); short sample=second%2==0?(short)(Math.Sin(2*Math.PI*(channel==0?440:880)*i/rate)*6000*fade):(short)0;int at=i*4+channel*2;data[at]=(byte)sample;data[at+1]=(byte)(sample>>8);}
        if(player!=null){player.Stop();player.Dispose();tone.Dispose();}
        tone=new MemoryStream();BinaryWriter b=new BinaryWriter(tone);b.Write(Encoding.ASCII.GetBytes("RIFF"));b.Write(36+data.Length);b.Write(Encoding.ASCII.GetBytes("WAVEfmt "));b.Write(16);b.Write((short)1);b.Write((short)2);b.Write(rate);b.Write(rate*4);b.Write((short)4);b.Write((short)16);b.Write(Encoding.ASCII.GetBytes("data"));b.Write(data.Length);b.Write(data);tone.Position=0;
        player=new System.Media.SoundPlayer(tone);player.Play();Log("测试：左手机低音 → 静音 → 右手机高音 → 静音 → 左手机低音。测试时可暂停网易云。");
    }
}

static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length>0 && args[0]=="--self-test") {SelfTest();return 0;}
        string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(root,args.Length==2?args[0]:null,args.Length==2?args[1]:null));return 0;
    }
    static void SelfTest() {
        byte[] input={1,2,3,4,5,6,7,8},l=new byte[4],r=new byte[4];Pcm.Split(input,8,l,r);
        if(BitConverter.ToString(l)!="01-02-05-06" || BitConverter.ToString(r)!="03-04-07-08")throw new Exception("Channel leakage");
        ChannelDelay d=new ChannelDelay(1);d.Apply(l);if(BitConverter.ToString(l)!="00-00-01-02")throw new Exception("Delay initial block");d.Apply(r);if(BitConverter.ToString(r)!="05-06-03-04")throw new Exception("Delay continuity");
        byte[] h=Pcm.Header(48000);if(BitConverter.ToString(h)!="80-BB-00-00-01-00-10-00")throw new Exception("Mono header");
        Console.WriteLine("PASS: left/right isolation, sample-preserving delay across blocks, mono protocol header");
    }
}
