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

sealed class Relay : IDisposable {
    readonly Adb adb; readonly Action<string> log;
    readonly List<Process> children=new List<Process>();
    readonly List<TcpClient> sockets=new List<TcpClient>();
    readonly List<TcpListener> listeners=new List<TcpListener>();
    readonly List<string> serials=new List<string>();
    readonly string tunnel="soundlink_"+Process.GetCurrentProcess().Id;
    volatile bool stopping; Thread worker; bool nativeInitialized;
    public event Action Connected;
    public Relay(Adb a, Action<string> l) { adb=a; log=l; }
    public void Start(string left,string right,int leftDelay,int rightDelay,Action ended) {
        worker=new Thread(()=> {
            try { Run(left,right,leftDelay,rightDelay); }
            catch(Exception e) { if(!stopping) log("停止："+e.Message); }
            finally { try { Cleanup(); } finally { ended(); } }
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
        bool single=right==null;
        if(left==right) throw new Exception("左右声道必须选择不同手机");
        log(single?"准备单手机完整立体声…":"准备左右设备…");
        TcpListener[] outputs=single?new TcpListener[]{Listen()}:new TcpListener[]{Listen(),Listen()}; string[] ids=single?new string[]{left}:new string[]{left,right};
        for(int i=0;i<ids.Length;i++) {
            if(stopping) return;
            string prefix="-s "+Adb.Q(ids[i])+" ";
            adb.Run(prefix+"push "+Adb.Q(Path.Combine(adb.Root,"tools/AudioShare/server"))+" /data/local/tmp/soundlink-server");
            adb.Run(prefix+"reverse localabstract:"+tunnel+" tcp:"+((IPEndPoint)outputs[i].LocalEndpoint).Port);
            serials.Add(ids[i]);
            Process p=adb.Spawn(prefix+"shell app_process -Djava.class.path=/data/local/tmp/soundlink-server /data/local/tmp com.ysbing.audioshare.Main socketName="+tunnel+" connectCode="+ids[i]);
            p.OutputDataReceived+=(s,e)=>{}; p.ErrorDataReceived+=(s,e)=>{if(!String.IsNullOrEmpty(e.Data)) log(e.Data);};
            p.BeginOutputReadLine();p.BeginErrorReadLine(); children.Add(p);
        }
        TcpClient lc=Accept(outputs[0]), rc=single?null:Accept(outputs[1]);
        // Consume each phone's handshake before starting the shared audio timeline.
        byte[] hello=Encoding.UTF8.GetBytes(left); Pcm.ReadExact(lc.GetStream(),hello);
        if(rc!=null) { hello=Encoding.UTF8.GetBytes(right); Pcm.ReadExact(rc.GetStream(),hello); }
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
        byte[] mono=Pcm.Header(rate); lc.GetStream().Write(single?header:mono,0,8);if(rc!=null)rc.GetStream().Write(mono,0,8);
        ChannelDelay dl=new ChannelDelay(rate*delayL/1000*(single?2:1)), dr=new ChannelDelay(rate*delayR/1000);
        byte[] source=new byte[rate/100*4], ldata=new byte[source.Length/2],rdata=new byte[source.Length/2];
        input.ReceiveTimeout=0; log(single?"已连接：单手机完整立体声="+left+"；"+rate+" Hz。请播放网易云音乐。":"已连接：左="+left+"，右="+right+"；"+rate+" Hz。请播放网易云音乐。");
        if(Connected!=null) Connected();
        long frames=0; Stopwatch stats=Stopwatch.StartNew();
        while(!stopping) {
            Pcm.ReadExact(input.GetStream(),source);
            if(single) {dl.Apply(source);lc.GetStream().Write(source,0,source.Length);}
            else {Pcm.Split(source,source.Length,ldata,rdata); dl.Apply(ldata);dr.Apply(rdata);lc.GetStream().Write(ldata,0,ldata.Length);rc.GetStream().Write(rdata,0,rdata.Length);}
            frames+=source.Length/4;
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


