using System;
using System.Diagnostics;
using System.Threading;

sealed class RetryBudget {
    int attempts;
    public int NextDelay(){return attempts>=3?-1:2000*++attempts;}
    public void Reset(){attempts=0;}
}

// Idle uses one blocking subscription. A timer exists only after subscription failure.
sealed class DeviceWatcher:IDisposable {
    readonly Adb adb;readonly object gate=new object();readonly RetryBudget budget=new RetryBudget();
    Process process;Timer retry;bool wanted,disposed;
    public event Action Changed;public event Action<string> Warning;
    internal int SubscriptionProcessId{get{lock(gate)return process==null?0:process.Id;}}
    public DeviceWatcher(Adb adb){this.adb=adb;}
    public void Start(){lock(gate){if(disposed)return;wanted=true;budget.Reset();if(process!=null)return;CancelRetry();StartCore();}}
    void StartCore(){
        if(!wanted||disposed)return;
        Process child=null;
        try{child=adb.Spawn("track-devices");process=child;var lifetime=Stopwatch.StartNew();
            child.OutputDataReceived+=(s,e)=>{if(e.Data!=null){lock(gate){if(process!=child)return;}if(Changed!=null)Changed();}};
            child.ErrorDataReceived+=(s,e)=>{};
            child.Exited+=(s,e)=>{lock(gate){if(process!=child)return;process=null;child.Dispose();if(lifetime.ElapsedMilliseconds>=30000)budget.Reset();ScheduleRetry();}if(Changed!=null)Changed();};
            child.EnableRaisingEvents=true;child.BeginOutputReadLine();child.BeginErrorReadLine();
        }catch(Exception e){if(child!=null){process=null;try{if(!child.HasExited)child.Kill();child.Dispose();}catch{}}Tell("设备订阅启动失败："+e.Message);ScheduleRetry();}
    }
    void ScheduleRetry(){
        if(!wanted||disposed)return;CancelRetry();int delay=budget.NextDelay();
        if(delay<0){Tell("设备订阅连续失败，已停止自动重试。请点击“重新连接”。");return;}
        Tell("设备订阅中断，"+(delay/1000)+" 秒后重试；不自动恢复摄像头或麦克风。");
        retry=new Timer(_=>{lock(gate){CancelRetry();StartCore();}},null,delay,Timeout.Infinite);
    }
    void Tell(string text){if(Warning!=null)Warning(text);}
    void CancelRetry(){if(retry!=null){retry.Dispose();retry=null;}}
    public void Stop(){Process child;lock(gate){wanted=false;CancelRetry();child=process;process=null;}if(child!=null){try{if(!child.HasExited)child.Kill();}catch(InvalidOperationException){}finally{child.Dispose();}}}
    public void Dispose(){lock(gate)disposed=true;Stop();}
}
