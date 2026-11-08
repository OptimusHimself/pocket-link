using System;
using System.IO;
using System.Threading;

sealed class ResourceOptions {
    public string Serial,MicrophoneOutputName;public bool Speaker,Camera,Microphone,FrontCamera,CameraMirror;public int MicrophoneOutput=-1,CameraRotation=270;
    public void Validate(){if(String.IsNullOrEmpty(Serial))throw new InvalidOperationException("请选择已授权的手机。");if(!Speaker&&!Camera&&!Microphone)throw new InvalidOperationException("请至少选择一项手机资源。");if(Microphone&&MicrophoneOutput<0)throw new InvalidOperationException("会议麦克风需要 VB-CABLE。请先配置虚拟麦克风组件，或暂时取消麦克风。");}
}
sealed class ResourceSession : IResourceJob,ICameraControls,ICameraFacingControls {
    readonly object facingLock=new object();bool desiredFacing;Action facingReady;
    int stopQueued;
    readonly Func<ISpeakerRelay> createSpeaker;readonly Action<ResourceOptions> validate;readonly Func<string> playbackIdentity;readonly int speakerCleanupTimeout;
    readonly Adb adb;readonly Action<string> log;volatile bool stopping,speakerEnded,micEnded;ISpeakerRelay speaker;CameraBridge camera;MicrophoneBridge mic;
    public ResourceSession(Adb adb,Action<string> log,Func<ISpeakerRelay> createSpeaker=null,Action<ResourceOptions> validate=null,Func<string> playbackIdentity=null,int speakerCleanupTimeout=5000){this.adb=adb;this.log=log;this.createSpeaker=createSpeaker??(()=>new Relay(adb,log));this.validate=validate??(options=>ComponentHealth.Validate(adb.Root,options));this.playbackIdentity=playbackIdentity??AudioRouting.DefaultPlaybackId;if(speakerCleanupTimeout<=0)throw new ArgumentOutOfRangeException("speakerCleanupTimeout");this.speakerCleanupTimeout=speakerCleanupTimeout;}
    public void Start(ResourceOptions options,Action ready,Action<string> ended){
        lock(facingLock)desiredFacing=options.FrontCamera;
        Thread thread=new Thread(()=>{
            string failure=null,stage="组件检查";Thread micThread=null;ManualResetEvent speakerReady=new ManualResetEvent(false),microphoneReady=new ManualResetEvent(false);Exception micFailure=null;
            try{
                options.Validate();validate(options);if(stopping)return;
                if(options.Camera||options.Microphone){
                    if(!File.Exists(Path.Combine(adb.Root,"tools/scrcpy-win64-v5.0.1/scrcpy-server")))throw new IOException("缺少手机采集组件，请先运行 Setup-Resources.ps1。");
                    int sdk;if(!Int32.TryParse(adb.Run("-s "+Adb.Q(options.Serial)+" shell getprop ro.build.version.sdk").Trim(),out sdk))throw new IOException("无法读取手机 Android 版本");
                    if(options.Camera&&sdk<31)throw new IOException("摄像头传输需要 Android 12 或更新版本。");
                    if(options.Microphone&&sdk<30)throw new IOException("麦克风传输需要 Android 11 或更新版本。");
                }
                if(options.Speaker){stage="扬声器启动";speaker=createSpeaker();speaker.Connected+=()=>speakerReady.Set();speaker.Start(options.Serial,()=>{speakerReady.Set();speakerEnded=true;});Wait(speakerReady,20000);if(speakerEnded)throw new IOException(speaker.Failure??"手机扬声器启动失败，详情见诊断日志。");}
                if(stopping)return;
                bool currentFacing=options.FrontCamera;
                if(options.Camera){stage="摄像头启动";camera=new CameraBridge(adb,log);camera.Start(options.Serial,currentFacing,options.CameraRotation,options.CameraMirror);}
                if(stopping)return;
                if(options.Microphone){stage="麦克风启动";mic=new MicrophoneBridge(adb,log);micThread=new Thread(()=>{try{mic.Run(options.Serial,options.MicrophoneOutput,()=>microphoneReady.Set(),options.MicrophoneOutputName);}catch(Exception e){micFailure=e;}finally{mic.Dispose();micEnded=true;microphoneReady.Set();}});micThread.IsBackground=true;micThread.Start();Wait(microphoneReady,20000);if(micEnded)throw new IOException(micFailure==null?"麦克风已断开":micFailure.Message);}
                string playback=options.Speaker?playbackIdentity():null;var routeCheck=System.Diagnostics.Stopwatch.StartNew();
                if(stopping)return;stage="运行中";ready();
                while(!stopping){
                    if(options.Camera){bool target;lock(facingLock)target=desiredFacing;if(target!=currentFacing){camera.SwitchFacing(target);currentFacing=target;}Action done=null;lock(facingLock){if(desiredFacing==currentFacing){done=facingReady;facingReady=null;}}if(done!=null&&!stopping)done();}
                    if(options.Speaker&&(speakerEnded||!speaker.PhoneAlive))throw new IOException("扬声器连接已断开");if(options.Camera){string error=camera.HealthError;if(error!=null)throw new IOException(error);}if(options.Microphone&&micEnded)throw new IOException(micFailure==null?"麦克风连接已断开":micFailure.Message);
                    if((options.Speaker||options.Microphone)&&routeCheck.ElapsedMilliseconds>=3000){AudioRouting.EnsureSpeakerIsolation();if(options.Speaker&&playback!=playbackIdentity())throw new IOException("Windows 默认播放设备已改变，请确认播放出口后重新勾选扬声器。");routeCheck.Restart();}Thread.Sleep(200);
                }
            }catch(OperationCanceledException){}catch(Exception e){if(!stopping)failure=stage+"："+e.Message;}
            finally{
                Stop();QueueStopIo();
                try{if(camera!=null)camera.Dispose();}catch(Exception e){failure="摄像头清理失败："+e.Message;}
                bool micJoined=micThread==null||micThread.Join(20000);
                if(!micJoined){failure="麦克风清理超时，请退出并重新启动 PocketLink";ResourceSafety.Block(PhoneResource.Microphone,failure);}
                if(speaker!=null){var cleanup=System.Diagnostics.Stopwatch.StartNew();while(!speakerEnded&&cleanup.ElapsedMilliseconds<speakerCleanupTimeout)Thread.Sleep(20);if(!speakerEnded){failure="扬声器引擎结束超时，请退出并重新启动 PocketLink";ResourceSafety.Block(PhoneResource.Speaker,failure);}}
                if(speaker==null||speakerEnded)speakerReady.Dispose();if(micJoined)microphoneReady.Dispose();ended(failure);
            }
        });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.MTA);thread.Start();
    }
    void Wait(WaitHandle signal,int timeout){int elapsed=0;while(!signal.WaitOne(100)){if(stopping)throw new OperationCanceledException();elapsed+=100;if(elapsed>timeout)throw new IOException("手机资源启动超时");}if(stopping)throw new OperationCanceledException();}
    public void Stop(){stopping=true;if(Interlocked.Exchange(ref stopQueued,1)==0)QueueStopIo();}
    void QueueStopIo(){ThreadPool.QueueUserWorkItem(_=>{try{if(speaker!=null)speaker.Stop();}catch(Exception e){log("扬声器停止异常："+e.Message);}try{if(mic!=null)mic.Stop();}catch(Exception e){log("麦克风停止异常："+e.Message);}try{if(camera!=null)camera.RequestStop();}catch(Exception e){log("摄像头停止异常："+e.Message);}});}
    public void SetTransform(int rotation,bool mirror){if(camera!=null)camera.SetTransform(rotation,mirror);}
    public void SetFacing(bool front,Action ready){lock(facingLock){desiredFacing=front;facingReady=ready;}}
    public string PreviewUrl{get{return camera==null?null:camera.PreviewUrl;}}
}
