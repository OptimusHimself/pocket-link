using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

// One phone capture generation. Output host and OBS belong to CameraBridge.
sealed class CameraVideoInput:IDisposable {
    readonly Adb adb;readonly Action<string> log;readonly CameraFrameHost frames;
    Process server,decoder;TcpListener listener;TcpClient input;Thread pump,decode;
    volatile bool stopping,hasFrame;string serial,socket;bool mapped;
    public CameraVideoInput(Adb adb,Action<string> log,CameraFrameHost frames){this.adb=adb;this.log=log;this.frames=frames;}
    internal const string DecoderArguments="-hide_banner -loglevel warning -fflags +genpts -flags low_delay -analyzeduration 100000 -probesize 32768 -threads 2 -r 30 -f h264 -i pipe:0 -an -vf fps=20 -c:v mjpeg -threads 2 -q:v 5 -flush_packets 1 -f image2pipe pipe:1";
    public void Start(string device,bool front,string ffmpeg){
        serial=device;string prefix="-s "+Adb.Q(device)+" ";string id=(0x10000000|Process.GetCurrentProcess().Id).ToString("x8");socket="scrcpy_"+id;
        CheckCanceled();listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        adb.Run(prefix+"push "+Adb.Q(Path.Combine(adb.Root,"tools/scrcpy-win64-v5.0.1/scrcpy-server"))+" /data/local/tmp/pocketlink-camera-server");CheckCanceled();
        adb.Run(prefix+"reverse localabstract:"+socket+" tcp:"+((IPEndPoint)listener.LocalEndpoint).Port);mapped=true;CheckCanceled();
        server=adb.Spawn(prefix+"shell CLASSPATH=/data/local/tmp/pocketlink-camera-server app_process / com.genymobile.scrcpy.Server 5.0.1 scid="+id+" video=true video_source=camera camera_facing="+(front?"front":"back")+" camera_size=1280x720 max_fps=30 video_codec=h264 audio=false control=false tunnel_forward=false raw_stream=true cleanup=true");
        server.OutputDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};server.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};server.BeginOutputReadLine();server.BeginErrorReadLine();
        var pending=listener.BeginAcceptTcpClient(null,null);using(var wait=pending.AsyncWaitHandle){int elapsed=0;while(!wait.WaitOne(100)){CheckCanceled();if(server.HasExited||++elapsed>=150)throw new IOException("相机视频流连接超时");}}
        CheckCanceled();input=listener.EndAcceptTcpClient(pending);input.ReceiveTimeout=15000;
        decoder=Process.Start(new ProcessStartInfo(ffmpeg,DecoderArguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true});
        decoder.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};decoder.BeginErrorReadLine();
        pump=new Thread(()=>{try{input.GetStream().CopyTo(decoder.StandardInput.BaseStream);}catch(Exception e){if(!stopping)log("相机流："+e.Message);}finally{try{decoder.StandardInput.Close();}catch{}}});pump.IsBackground=true;pump.Start();
        decode=new Thread(()=>{try{JpegFrames.Read(decoder.StandardOutput.BaseStream,jpeg=>{if(!stopping){frames.SetFrame(jpeg);hasFrame=true;}},()=>stopping);}catch(Exception e){if(!stopping)log("相机解码："+e.Message);}});decode.IsBackground=true;decode.Start();
        for(int i=0;!hasFrame;i++){CheckCanceled();if(decoder.HasExited||server.HasExited||i>=200)throw new IOException("相机解码未得到视频帧，请查看日志。");Thread.Sleep(100);}
        CheckCanceled();
    }
    void CheckCanceled(){if(stopping)throw new OperationCanceledException();}
    public bool Alive{get{return !stopping&&server!=null&&!server.HasExited&&decoder!=null&&!decoder.HasExited;}}
    public void RequestStop(){stopping=true;try{if(input!=null)input.Close();if(listener!=null)listener.Stop();}catch{}}
    public void Dispose(){
        RequestStop();Kill(server);Kill(decoder);
        if(pump!=null&&!pump.Join(3000))log("相机输入线程退出超时");
        if(decode!=null&&!decode.Join(3000))log("相机解码线程退出超时");
        if(server!=null)server.Dispose();if(decoder!=null)decoder.Dispose();
        if(mapped){mapped=false;try{adb.Run("-s "+Adb.Q(serial)+" reverse --remove localabstract:"+socket);}catch{}}
    }
    static void Kill(Process p){if(p==null)return;try{if(!p.HasExited){p.Kill();p.WaitForExit(3000);}}catch(InvalidOperationException){}}
}

static class JpegFrames {
    // Read pipe blocks, rather than making an OS pipe read for each byte.
    public static void Read(Stream source,Action<byte[]> publish,Func<bool> canceled){
        byte[] buffer=new byte[32768];int previous=-1,count;bool inside=false;
        using(var jpeg=new MemoryStream(65536)){
            while(!canceled()&&(count=source.Read(buffer,0,buffer.Length))>0){
                for(int i=0;i<count;i++){
                    int current=buffer[i];
                    if(!inside){if(previous==255&&current==216){inside=true;jpeg.SetLength(0);jpeg.WriteByte(255);jpeg.WriteByte(216);}}
                    else{jpeg.WriteByte((byte)current);if(jpeg.Length>4*1024*1024)throw new IOException("相机帧过大");if(previous==255&&current==217){publish(jpeg.ToArray());inside=false;}}
                    previous=current;
                }
            }
        }
    }
}
