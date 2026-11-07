using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

sealed class Relay {
    readonly Adb adb; readonly Action<string> log;
    Process player;string serial;bool mapped;
    readonly List<TcpClient> sockets=new List<TcpClient>();
    readonly List<TcpListener> listeners=new List<TcpListener>();
    readonly string tunnel="soundlink_"+Process.GetCurrentProcess().Id;
    volatile bool stopping; bool nativeInitialized;
    public event Action Connected;
    public Relay(Adb a, Action<string> l) { adb=a; log=l; }
    public void Start(string device,Action ended) {
        Thread worker=new Thread(()=> {
            try { Run(device); }
            catch(Exception e) { if(!stopping) log("停止："+e.Message); }
            finally { try { Cleanup(); } finally { ended(); } }
        }); worker.IsBackground=true; worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    TcpListener Listen() { TcpListener l=new TcpListener(IPAddress.Loopback,0); l.Start(); lock(listeners) listeners.Add(l); return l; }
    TcpClient Accept(TcpListener listener) {
        var pending=listener.BeginAcceptTcpClient(null,null);
        using(var wait=pending.AsyncWaitHandle){if(!wait.WaitOne(15000)) throw new Exception("手机连接超时，请检查 USB 调试授权");}
        TcpClient c=listener.EndAcceptTcpClient(pending); c.NoDelay=true; c.SendTimeout=1500; c.ReceiveTimeout=15000;
        lock(sockets) sockets.Add(c); return c;
    }
    void Run(string device) {
        AudioRouting.EnsureSpeakerIsolation();
        log("扬声器采集输出："+AudioRouting.DefaultPlayback()+"；麦克风虚拟通道不作为扬声器来源。");
        log("正在准备手机播放组件…");
        serial=device;TcpListener output=Listen();
        if(stopping) return;
        string prefix="-s "+Adb.Q(device)+" ";
        adb.Run(prefix+"push "+Adb.Q(Path.Combine(adb.Root,"tools/AudioShare/server"))+" /data/local/tmp/soundlink-server");if(stopping)return;
        adb.Run(prefix+"reverse localabstract:"+tunnel+" tcp:"+((IPEndPoint)output.LocalEndpoint).Port);mapped=true;if(stopping)return;
        player=adb.Spawn(prefix+"shell app_process -Djava.class.path=/data/local/tmp/soundlink-server /data/local/tmp com.ysbing.audioshare.Main socketName="+tunnel+" connectCode="+device);
        player.OutputDataReceived+=(s,e)=>{};player.ErrorDataReceived+=(s,e)=>{if(!String.IsNullOrEmpty(e.Data))log(e.Data);};player.BeginOutputReadLine();player.BeginErrorReadLine();
        TcpClient phone=Accept(output);
        // Consume the phone's handshake before starting audio.
        byte[] hello=Encoding.UTF8.GetBytes(device); Pcm.ReadExact(phone.GetStream(),hello);

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
        phone.GetStream().Write(header,0,8);
        byte[] source=new byte[rate/100*4];
        input.ReceiveTimeout=0;log("已连接："+device+"；"+rate+" Hz，完整立体声。");
        if(Connected!=null) Connected();
        long frames=0; Stopwatch stats=Stopwatch.StartNew();
        while(!stopping) {
            Pcm.ReadExact(input.GetStream(),source);
            phone.GetStream().Write(source,0,source.Length);
            frames+=source.Length/4;
            if(stats.ElapsedMilliseconds>=5000) { log("传输中："+frames+" 帧"); stats.Restart(); }
        }
    }
    void CloseIO() { lock(sockets) foreach(TcpClient c in sockets) { try { c.Close(); } catch {} } lock(listeners) foreach(TcpListener l in listeners) { try { l.Stop(); } catch {} } }
    void Cleanup() {
        CloseIO(); if(nativeInitialized) { Native.AudioCapture_Cleanup(); nativeInitialized=false; }
        if(player!=null){try{if(!player.HasExited)player.Kill();player.Dispose();}catch{}}
        if(mapped){try{adb.Run("-s "+Adb.Q(serial)+" reverse --remove localabstract:"+tunnel);}catch{}}
    }
    public void Stop() { stopping=true;CloseIO(); }

}
