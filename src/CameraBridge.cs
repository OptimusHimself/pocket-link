using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

sealed class CameraBridge:IDisposable {
    readonly Adb adb;readonly Action<string> log;Process obs;CameraFrameHost frames;CameraVideoInput video;volatile bool stopping;string serial,ffmpeg,obsConfig;bool facing;
    public CameraBridge(Adb adb,Action<string> log){this.adb=adb;this.log=log;}
    public string PreviewUrl{get{return frames==null?null:frames.Url;}}
    internal int OutputProcessId{get{return obs==null?0:obs.Id;}}
    public static string FindObs(){foreach(string p in new[]{@"D:\obs-studio\bin\64bit\obs64.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"obs-studio/bin/64bit/obs64.exe")})if(File.Exists(p))return p;return null;}
    static string FindDecoder(){string local=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"../tools/ffmpeg/ffmpeg.exe");if(File.Exists(local))return local;foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){try{string p=Path.Combine(folder,"ffmpeg.exe");if(File.Exists(p))return p;}catch(ArgumentException){}}throw new IOException("未找到 FFmpeg，请将其放入 tools/ffmpeg/ffmpeg.exe。");}
    internal static string OrientationArguments(int rotation){CameraFrameHost.ValidateRotation(rotation);return "output-rotation="+rotation;}
    public void SetTransform(int rotation,bool mirror){CameraFrameHost.ValidateRotation(rotation);if(frames!=null)frames.Transform(rotation,mirror);}
    public void Start(string device,bool front,int rotation=270,bool mirror=false){
        serial=device;string obsExe=FindObs();if(obsExe==null)throw new IOException("未找到 OBS Studio。");ffmpeg=FindDecoder();
        var runtime=ObsRuntime.Prepare(adb.Root,obsExe);obsExe=runtime.Exe;obsConfig=runtime.Config;
        frames=new CameraFrameHost();frames.Transform(rotation,mirror);StartVideo(front);
        WriteObsCollection(frames.Url);DateTime started=DateTime.Now;
        obs=Process.Start(new ProcessStartInfo(obsExe,"--portable --multi --collection SoundLink --profile SoundLink --scene SoundLink --startvirtualcam --minimize-to-tray --disable-updater --disable-missing-files-check --disable-shutdown-check"){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(obsExe)});
        bool ready=false;for(int i=0;i<150;i++){if(stopping)throw new OperationCanceledException();if(obs.HasExited)throw new IOException("OBS 启动失败");string logs=Path.Combine(obsConfig,"logs");if(Directory.Exists(logs))foreach(string file in Directory.GetFiles(logs,"*.txt")){try{if(File.GetLastWriteTime(file)>=started&&ReadSharedLog(file).Contains("==== Virtual Camera Start")){ready=true;break;}}catch(IOException){}}if(ready)break;Thread.Sleep(100);}
        if(!ready)throw new IOException("OBS 虚拟摄像头启动超时");log("后台摄像头已就绪；旋转与镜像实时作用于 OBS 输出，关闭预览不会停止采集。");
    }
    void StartVideo(bool front){
        if(stopping)throw new OperationCanceledException();log("正在启用"+(front?"前置":"后置")+"摄像头…");
        var next=new CameraVideoInput(adb,log,frames);video=next;
        if(stopping){next.RequestStop();throw new OperationCanceledException();}
        next.Start(serial,front,ffmpeg);facing=front;
    }
    // Runs on the resource worker. The virtual camera and preview URL stay alive.
    public void SwitchFacing(bool front){
        if(front==facing)return;if(stopping)throw new OperationCanceledException();
        var watch=Stopwatch.StartNew();if(video!=null){video.Dispose();video=null;}
        frames.ClearFrame();StartVideo(front);log("镜头切换完成："+watch.ElapsedMilliseconds+" ms，保留 OBS 与虚拟摄像头输出。");
    }
    internal static string SceneJson(string url=null){return new JavaScriptSerializer().Serialize(new{name="SoundLink",current_scene="SoundLink",current_program_scene="SoundLink",scene_order=new[]{new{name="SoundLink"}},sources=new object[]{new{name="Phone Camera",id="browser_source",versioned_id="browser_source",enabled=true,muted=true,mixers=0,settings=new{url=url??"http://127.0.0.1:1/",width=1280,height=720,fps=20,fps_custom=true,shutdown=false,restart_when_active=false,reroute_audio=false}},new{name="SoundLink",id="scene",versioned_id="scene",settings=new{id_counter=1,items=new[]{new{name="Phone Camera",id=1,visible=true,locked=true,rot=0.0,pos=new{x=0.0,y=0.0},scale=new{x=1.0,y=1.0},bounds_type=2,bounds_align=0,bounds=new{x=1280.0,y=720.0},align=5}}},mixers=0}}});}
    internal static string ReadSharedLog(string file){using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}
    void WriteObsCollection(string url){string root=Path.Combine(obsConfig,"basic");Directory.CreateDirectory(Path.Combine(root,"scenes"));File.WriteAllText(Path.Combine(root,"scenes/SoundLink.json"),SceneJson(url));string profile=Path.Combine(root,"profiles/SoundLink");Directory.CreateDirectory(profile);File.WriteAllText(Path.Combine(profile,"basic.ini"),"[General]\nName=SoundLink\n[Video]\nBaseCX=1280\nBaseCY=720\nOutputCX=1280\nOutputCY=720\nFPSType=0\nFPSCommon=30\n[Audio]\nSampleRate=48000\nChannelSetup=Stereo\n");}
    public bool Alive{get{return !stopping&&video!=null&&video.Alive&&obs!=null&&!obs.HasExited;}}
    public void RequestStop(){stopping=true;var current=video;if(current!=null)current.RequestStop();}
    public void Dispose(){RequestStop();try{if(video!=null)video.Dispose();}finally{try{if(obs!=null){OwnedProcessExit.CloseObs(obs,log);obs.Dispose();}}finally{if(frames!=null)frames.Dispose();}}}
}
