using System;
using System.Diagnostics;

// Blocking ADB subscription: no periodic adb-devices process while idle.
sealed class DeviceWatcher:IDisposable {
    readonly Adb adb;Process process;bool disposed;
    public event Action Changed;
    public DeviceWatcher(Adb adb){this.adb=adb;}
    public void Start(){
        if(disposed)return;if(process!=null&&!process.HasExited)return;
        process=adb.Spawn("track-devices");process.OutputDataReceived+=(s,e)=>{if(e.Data!=null&&Changed!=null)Changed();};process.ErrorDataReceived+=(s,e)=>{};process.BeginOutputReadLine();process.BeginErrorReadLine();
    }
    public void Stop(){if(process!=null){try{if(!process.HasExited)process.Kill();process.Dispose();}catch{}process=null;}}
    public void Dispose(){disposed=true;Stop();}
}
