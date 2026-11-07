using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Web.Script.Serialization;

sealed class CameraBridge:IDisposable {
    readonly Adb adb;readonly Action<string> log;Process server,decoder,obs;TcpListener listener;TcpClient input;CameraFrameHost frames;Thread pump,decode;volatile bool stopping;string serial,socket;bool mapped;
    public CameraBridge(Adb adb,Action<string> log){this.adb=adb;this.log=log;}
    public string PreviewUrl{get{return frames==null?null:frames.Url;}}
    public static string FindObs(){foreach(string p in new[]{@"D:\obs-studio\bin\64bit\obs64.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"obs-studio/bin/64bit/obs64.exe")})if(File.Exists(p))return p;return null;}
    static string FindDecoder(){string local=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../tools/ffmpeg/ffmpeg.exe");if(File.Exists(local))return local;foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){try{string p=Path.Combine(folder,"ffmpeg.exe");if(File.Exists(p))return p;}catch(ArgumentException){}}throw new IOException("未找到 FFmpeg，请将其放入 tools/ffmpeg/ffmpeg.exe。");}
    internal static string OrientationArguments(int rotation){CameraFrameHost.ValidateRotation(rotation);return "output-rotation="+rotation;}
    public void SetTransform(int rotation,bool mirror){CameraFrameHost.ValidateRotation(rotation);if(frames!=null)frames.Transform(rotation,mirror);}
    public void Start(string device,bool front,int rotation=270,bool mirror=false){
        log("正在启用"+(front?"前置":"后置")+"摄像头…");
        serial=device;string obsExe=FindObs();if(obsExe==null)throw new IOException("未找到 OBS Studio。");string ffmpeg=FindDecoder();frames=new CameraFrameHost();frames.Transform(rotation,mirror);
        listener=new TcpListener(IPAddress.Loopback,0);listener.Start();string id=(0x10000000|Process.GetCurrentProcess().Id).ToString("x8");socket="scrcpy_"+id;string prefix="-s "+Adb.Q(device)+" ";
        adb.Run(prefix+"push "+Adb.Q(Path.Combine(adb.Root,"tools/scrcpy-win64-v5.0.1/scrcpy-server"))+" /data/local/tmp/pocketlink-camera-server");if(stopping)throw new OperationCanceledException();
        adb.Run(prefix+"reverse localabstract:"+socket+" tcp:"+((IPEndPoint)listener.LocalEndpoint).Port);mapped=true;
        server=adb.Spawn(prefix+"shell CLASSPATH=/data/local/tmp/pocketlink-camera-server app_process / com.genymobile.scrcpy.Server 5.0.1 scid="+id+" video=true video_source=camera camera_facing="+(front?"front":"back")+" camera_size=1280x720 max_fps=30 video_codec=h264 audio=false control=false tunnel_forward=false raw_stream=true cleanup=true");
        server.OutputDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};server.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};server.BeginOutputReadLine();server.BeginErrorReadLine();
        var pending=listener.BeginAcceptTcpClient(null,null);using(var wait=pending.AsyncWaitHandle){if(!wait.WaitOne(15000))throw new IOException("相机视频流连接超时");}input=listener.EndAcceptTcpClient(pending);input.ReceiveTimeout=15000;
        var info=new ProcessStartInfo(ffmpeg,"-hide_banner -loglevel warning -fflags +genpts -flags low_delay -analyzeduration 100000 -probesize 32768 -r 30 -f h264 -i pipe:0 -an -vf fps=20 -c:v mjpeg -q:v 5 -flush_packets 1 -f image2pipe pipe:1"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        decoder=Process.Start(info);decoder.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};decoder.BeginErrorReadLine();
        pump=new Thread(()=>{try{input.GetStream().CopyTo(decoder.StandardInput.BaseStream);}catch(Exception e){if(!stopping)log("相机流："+e.Message);}finally{try{decoder.StandardInput.Close();}catch{}}});pump.IsBackground=true;pump.Start();decode=new Thread(ReadFrames);decode.IsBackground=true;decode.Start();
        for(int i=0;!frames.HasFrame;i++){if(stopping)throw new OperationCanceledException();if(decoder.HasExited||server.HasExited||i>200)throw new IOException("相机解码未得到视频帧，请查看日志。");Thread.Sleep(100);}
        WriteObsCollection(frames.Url);DateTime started=DateTime.Now;
        obs=Process.Start(new ProcessStartInfo(obsExe,"--multi --collection SoundLink --profile SoundLink --scene SoundLink --startvirtualcam --minimize-to-tray"){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(obsExe)});
        bool ready=false;for(int i=0;i<150;i++){if(stopping)throw new OperationCanceledException();if(obs.HasExited)throw new IOException("OBS 启动失败");string logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"obs-studio/logs");if(Directory.Exists(logs))foreach(string file in Directory.GetFiles(logs,"*.txt")){try{if(File.GetLastWriteTime(file)>=started&&ReadSharedLog(file).Contains("==== Virtual Camera Start")){ready=true;break;}}catch(IOException){}}if(ready)break;Thread.Sleep(100);}
        if(!ready)throw new IOException("OBS 虚拟摄像头启动超时");log("后台摄像头已就绪；旋转与镜像实时作用于 OBS 输出，关闭预览不会停止采集。");
    }
    void ReadFrames(){try{Stream stream=decoder.StandardOutput.BaseStream;using(var jpeg=new MemoryStream()){int previous=-1,current;bool inside=false;while(!stopping&&(current=stream.ReadByte())>=0){if(!inside){if(previous==255&&current==216){inside=true;jpeg.SetLength(0);jpeg.WriteByte(255);jpeg.WriteByte(216);}}else{jpeg.WriteByte((byte)current);if(jpeg.Length>4*1024*1024)throw new IOException("相机帧过大");if(previous==255&&current==217){frames.SetFrame(jpeg.ToArray());inside=false;}}previous=current;}}}catch(Exception e){if(!stopping)log("相机解码："+e.Message);}}
    internal static string SceneJson(string url=null){return new JavaScriptSerializer().Serialize(new{name="SoundLink",current_scene="SoundLink",current_program_scene="SoundLink",scene_order=new[]{new{name="SoundLink"}},sources=new object[]{new{name="Phone Camera",id="browser_source",versioned_id="browser_source",enabled=true,muted=true,mixers=0,settings=new{url=url??"http://127.0.0.1:1/",width=1280,height=720,fps=20,fps_custom=true,shutdown=false,restart_when_active=false,reroute_audio=false}},new{name="SoundLink",id="scene",versioned_id="scene",settings=new{id_counter=1,items=new[]{new{name="Phone Camera",id=1,visible=true,locked=true,rot=0.0,pos=new{x=0.0,y=0.0},scale=new{x=1.0,y=1.0},bounds_type=2,bounds_align=0,bounds=new{x=1280.0,y=720.0},align=5}}},mixers=0}}});}
    internal static string ReadSharedLog(string file){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}
    void WriteObsCollection(string url){string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"obs-studio/basic");Directory.CreateDirectory(Path.Combine(root,"scenes"));File.WriteAllText(Path.Combine(root,"scenes/SoundLink.json"),SceneJson(url));string profile=Path.Combine(root,"profiles/SoundLink");Directory.CreateDirectory(profile);File.WriteAllText(Path.Combine(profile,"basic.ini"),"[General]\nName=SoundLink\n[Video]\nBaseCX=1280\nBaseCY=720\nOutputCX=1280\nOutputCY=720\nFPSType=0\nFPSCommon=30\n[Audio]\nSampleRate=48000\nChannelSetup=Stereo\n");}
    public bool Alive{get{return !stopping&&server!=null&&!server.HasExited&&decoder!=null&&!decoder.HasExited&&obs!=null&&!obs.HasExited;}}
    public void RequestStop(){stopping=true;try{if(input!=null)input.Close();if(listener!=null)listener.Stop();}catch{}}
    public void Dispose(){RequestStop();foreach(Process process in new[]{server,decoder,obs}){if(process==null)continue;try{if(!process.HasExited){if(process==obs){process.CloseMainWindow();if(!process.WaitForExit(3000))process.Kill();}else process.Kill();}process.Dispose();}catch{}}if(pump!=null)pump.Join(2000);if(decode!=null)decode.Join(2000);if(frames!=null)frames.Dispose();if(mapped){try{adb.Run("-s "+Adb.Q(serial)+" reverse --remove localabstract:"+socket);}catch{}}}
}
