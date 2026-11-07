using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

sealed class CameraBridge : IDisposable {
    public const string WindowTitle="SoundLinkCamera";
    readonly Adb adb;readonly Action<string> log;Process camera,obs;volatile bool stopping;
    public CameraBridge(Adb adb,Action<string> log){this.adb=adb;this.log=log;}
    public static string FindObs(){foreach(string path in new[]{@"D:\obs-studio\bin\64bit\obs64.exe",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"obs-studio/bin/64bit/obs64.exe")})if(File.Exists(path))return path;return null;}
    public void Start(string serial,bool front){
        string exe=Path.Combine(adb.Root,"tools/scrcpy-win64-v5.0.1/scrcpy.exe"),obsExe=FindObs();
        if(!File.Exists(exe))throw new IOException("缺少摄像头组件，请运行 Setup-Resources.ps1。");
        if(obsExe==null)throw new IOException("未找到 OBS Studio。请先安装 OBS 及其 Virtual Camera。");
        var p=new ProcessStartInfo(exe,"--serial "+Adb.Q(serial)+" --video-source=camera --camera-facing="+(front?"front":"back")+" --camera-size=1280x720 --max-fps=30 --no-audio --no-control --window-title="+WindowTitle+" --window-width=640 --window-height=360");
        p.UseShellExecute=false;p.CreateNoWindow=true;p.WorkingDirectory=Path.GetDirectoryName(exe);p.RedirectStandardError=true;p.RedirectStandardOutput=true;
        p.EnvironmentVariables["ADB"]=adb.Exe;p.EnvironmentVariables["ADB_LIBUSB"]="1";p.EnvironmentVariables["ADB_SERVER_SOCKET"]="tcp:localhost:15037";
        camera=Process.Start(p);camera.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};camera.OutputDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};camera.BeginErrorReadLine();camera.BeginOutputReadLine();
        for(int i=0;i<100;i++){if(stopping)throw new OperationCanceledException();if(camera.HasExited)throw new IOException("手机摄像头启动失败，请解锁手机并检查诊断日志。");camera.Refresh();if(camera.MainWindowHandle!=IntPtr.Zero)break;Thread.Sleep(100);}
        if(camera.MainWindowHandle==IntPtr.Zero)throw new IOException("手机摄像头预览未就绪。");
        WriteObsCollection();
        obs=Process.Start(new ProcessStartInfo(obsExe,"--multi --collection SoundLink --profile SoundLink --scene SoundLink --startvirtualcam --minimize-to-tray"){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(obsExe)});
        Thread.Sleep(2000);if(obs.HasExited)throw new IOException("OBS 虚拟摄像头启动失败。请检查 OBS 安装。");
        log("摄像头桥接已启动。会议软件请选择 OBS Virtual Camera；仍需检查会议预览画面。");
    }
    internal static string SceneJson(){
        return new JavaScriptSerializer().Serialize(new {
            name="SoundLink",current_scene="SoundLink",current_program_scene="SoundLink",scene_order=new[]{new{name="SoundLink"}},
            sources=new object[]{
                new {name="Phone Camera",id="window_capture",versioned_id="window_capture",enabled=true,muted=true,mixers=0,settings=new{window=WindowTitle+":SDL_app:scrcpy.exe",priority=0,method=2,cursor=false,client_area=true,capture_audio=false}},
                new {name="SoundLink",id="scene",versioned_id="scene",settings=new{id_counter=1,items=new[]{new{name="Phone Camera",id=1,visible=true,locked=true,rot=0.0,pos=new{x=0.0,y=0.0},scale=new{x=1.0,y=1.0},bounds_type=2,bounds_align=0,bounds=new{x=1280.0,y=720.0},align=5}}},mixers=0}
            }
        });
    }
    void WriteObsCollection(){
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"obs-studio/basic");
        Directory.CreateDirectory(Path.Combine(root,"scenes"));File.WriteAllText(Path.Combine(root,"scenes/SoundLink.json"),SceneJson());
        string profile=Path.Combine(root,"profiles/SoundLink");Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile,"basic.ini"),"[General]\nName=SoundLink\n[Video]\nBaseCX=1280\nBaseCY=720\nOutputCX=1280\nOutputCY=720\nFPSType=0\nFPSCommon=30\n[Audio]\nSampleRate=48000\nChannelSetup=Stereo\n");
    }
    public bool Alive{get{return camera!=null&&!camera.HasExited&&obs!=null&&!obs.HasExited;}}
    public void RequestStop(){stopping=true;}
    public void Dispose(){stopping=true;foreach(Process process in new[]{camera,obs}){if(process==null)continue;try{if(!process.HasExited){process.CloseMainWindow();if(!process.WaitForExit(3000))process.Kill();}process.Dispose();}catch{}}}
}
