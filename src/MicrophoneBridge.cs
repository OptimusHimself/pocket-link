using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

sealed class MicrophoneBridge:IDisposable {
    readonly Adb adb;readonly Action<string> log;readonly string socket="scrcpy_"+Process.GetCurrentProcess().Id.ToString("x8");
    TcpListener listener;TcpClient input;Process server;volatile bool stopped;string serial;bool mapped;
    public MicrophoneBridge(Adb adb,Action<string> log){this.adb=adb;this.log=log;}
    public void Run(string device,int outputId,Action ready,string outputName=null){
        AudioRouting.EnsureSpeakerIsolation();
        using(WaveOutput output=new WaveOutput(outputId,outputName)){
        log("麦克风输出："+output.Device.Name+"（当前编号 "+output.Device.Id+"）");
        serial=device;string prefix="-s "+Adb.Q(device)+" ";
        int sdk;if(!Int32.TryParse(adb.Run(prefix+"shell getprop ro.build.version.sdk").Trim(),out sdk)||sdk<30)throw new IOException("手机麦克风转发需要 Android 11 或更新版本。");
        listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        adb.Run(prefix+"push "+Adb.Q(Path.Combine(adb.Root,"tools/scrcpy-win64-v5.0.1/scrcpy-server"))+" /data/local/tmp/soundlink-mic-server");
        adb.Run(prefix+"reverse localabstract:"+socket+" tcp:"+((IPEndPoint)listener.LocalEndpoint).Port);mapped=true;if(stopped)return;
        string id=Process.GetCurrentProcess().Id.ToString("x8");
        server=adb.Spawn(prefix+"shell CLASSPATH=/data/local/tmp/soundlink-mic-server app_process / com.genymobile.scrcpy.Server 5.0.1 scid="+id+" video=false control=false audio=true audio_source=mic audio_codec=raw tunnel_forward=false raw_stream=true cleanup=true");
        server.OutputDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};server.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};server.BeginOutputReadLine();server.BeginErrorReadLine();
        var pending=listener.BeginAcceptTcpClient(null,null);using(var wait=pending.AsyncWaitHandle){if(!wait.WaitOne(15000))throw new IOException("手机麦克风连接超时，请解锁手机后重试。");}
        input=listener.EndAcceptTcpClient(pending);input.ReceiveTimeout=15000;
            byte[] pcm=new byte[WaveOutput.BlockBytes];Pcm.ReadExact(input.GetStream(),pcm);ready();
            do{output.Write(pcm,()=>stopped);Pcm.ReadExact(input.GetStream(),pcm);}while(!stopped);
        }
    }
    public void Stop(){stopped=true;try{if(input!=null)input.Close();if(listener!=null)listener.Stop();}catch{}}
    public void Dispose(){Stop();if(server!=null){try{if(!server.HasExited)server.Kill();server.Dispose();}catch{}}if(mapped){try{adb.Run("-s "+Adb.Q(serial)+" reverse --remove localabstract:"+socket);}catch{}}}
}
