using System;
using System.IO;
using System.Threading;

sealed class ResourceOptions {
    public string Serial;public bool Speaker,Camera,Microphone,FrontCamera;public int MicrophoneOutput=-1,CameraRotation=90;
    public void Validate(){if(String.IsNullOrEmpty(Serial))throw new InvalidOperationException("请选择已授权的手机。");if(!Speaker&&!Camera&&!Microphone)throw new InvalidOperationException("请至少选择一项手机资源。");if(Microphone&&MicrophoneOutput<0)throw new InvalidOperationException("会议麦克风需要 VB-CABLE。请先配置虚拟麦克风组件，或暂时取消麦克风。");}
}
sealed class ResourceSession {
    readonly Adb adb;readonly Action<string> log;volatile bool stopping,speakerEnded,micEnded;Relay speaker;CameraBridge camera;MicrophoneBridge mic;
    public ResourceSession(Adb adb,Action<string> log){this.adb=adb;this.log=log;}
    public void Start(ResourceOptions options,Action ready,Action<string> ended){
        Thread thread=new Thread(()=>{
            string failure=null;Thread micThread=null;ManualResetEvent speakerReady=new ManualResetEvent(false),microphoneReady=new ManualResetEvent(false);Exception micFailure=null;
            try{
                options.Validate();
                if(options.Camera||options.Microphone){
                    if(!File.Exists(Path.Combine(adb.Root,"tools/scrcpy-win64-v5.0.1/scrcpy-server")))throw new IOException("缺少手机采集组件，请先运行 Setup-Resources.ps1。");
                    int sdk;if(!Int32.TryParse(adb.Run("-s "+Adb.Q(options.Serial)+" shell getprop ro.build.version.sdk").Trim(),out sdk))throw new IOException("无法读取手机 Android 版本");
                    if(options.Camera&&sdk<31)throw new IOException("摄像头传输需要 Android 12 或更新版本。");
                    if(options.Microphone&&sdk<30)throw new IOException("麦克风传输需要 Android 11 或更新版本。");
                }
                if(options.Speaker){speaker=new Relay(adb,log);speaker.Connected+=()=>speakerReady.Set();speaker.Start(options.Serial,()=>{speakerEnded=true;speakerReady.Set();});Wait(speakerReady,20000);if(speakerEnded)throw new IOException("手机扬声器启动失败，详情见诊断日志。");}
                if(stopping)return;
                if(options.Camera){camera=new CameraBridge(adb,log);camera.Start(options.Serial,options.FrontCamera,options.CameraRotation);}
                if(stopping)return;
                if(options.Microphone){mic=new MicrophoneBridge(adb,log);micThread=new Thread(()=>{try{mic.Run(options.Serial,options.MicrophoneOutput,()=>microphoneReady.Set());}catch(Exception e){micFailure=e;}finally{mic.Dispose();micEnded=true;microphoneReady.Set();}});micThread.IsBackground=true;micThread.Start();Wait(microphoneReady,20000);if(micEnded)throw new IOException(micFailure==null?"麦克风已断开":micFailure.Message);}
                if(stopping)return;ready();
                while(!stopping){if(options.Speaker&&speakerEnded)throw new IOException("扬声器连接已断开");if(options.Camera&&!camera.Alive)throw new IOException("摄像头预览或 OBS 已关闭，请重新连接并保留预览窗口。");if(options.Microphone&&micEnded)throw new IOException(micFailure==null?"麦克风连接已断开":micFailure.Message);Thread.Sleep(200);}
            }catch(OperationCanceledException){}catch(Exception e){if(!stopping)failure=e.Message;}
            finally{Stop();if(camera!=null)camera.Dispose();if(micThread!=null)micThread.Join(20000);if(speaker!=null)while(!speakerEnded)Thread.Sleep(20);speakerReady.Dispose();microphoneReady.Dispose();ended(failure);}
        });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.MTA);thread.Start();
    }
    void Wait(WaitHandle signal,int timeout){int elapsed=0;while(!signal.WaitOne(100)){if(stopping)throw new OperationCanceledException();elapsed+=100;if(elapsed>timeout)throw new IOException("手机资源启动超时");}if(stopping)throw new OperationCanceledException();}
    public void Stop(){stopping=true;if(speaker!=null)speaker.Stop();if(mic!=null)mic.Stop();if(camera!=null)camera.RequestStop();}
}
