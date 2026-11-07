using System;
using System.Collections.Generic;

enum PhoneResource { Speaker, Camera, Microphone }
interface IResourceJob {void Start(ResourceOptions options,Action ready,Action<string> ended);void Stop();}
sealed class ResourceController {
    sealed class Slot {public IResourceJob Job;public ResourceOptions Options;public bool Wanted,Stopping;public string State="关闭";}
    readonly Dictionary<PhoneResource,Slot> slots=new Dictionary<PhoneResource,Slot>();readonly Func<IResourceJob> factory;readonly Action<Action> dispatch;
    public event Action Changed;public event Action<string> Error;
    public ResourceController(Func<IResourceJob> factory,Action<Action> dispatch){this.factory=factory;this.dispatch=dispatch;foreach(PhoneResource key in Enum.GetValues(typeof(PhoneResource)))slots[key]=new Slot();}
    public bool Busy{get{foreach(var s in slots.Values)if(s.Job!=null)return true;return false;}}
    public bool Active(PhoneResource key){return slots[key].Job!=null;}
    public string Status(PhoneResource key){return slots[key].State;}
    public void Set(PhoneResource key,bool enabled,ResourceOptions options){var s=slots[key];s.Wanted=enabled;if(options!=null)s.Options=options;if(!enabled){if(s.Job!=null&&!s.Stopping){s.Stopping=true;s.State="停止中";s.Job.Stop();Notify();}return;}if(s.Job==null)Start(key,s);}
    void Start(PhoneResource key,Slot s){
        var job=factory();s.Job=job;s.Stopping=false;s.State="连接中";Notify();
        job.Start(s.Options,()=>dispatch(()=>{if(s.Job==job&&!s.Stopping){s.State="运行中";Notify();}}),error=>dispatch(()=>{
            if(s.Job!=job)return;bool wasStopping=s.Stopping;s.Job=null;s.Stopping=false;s.State=error==null?"关闭":"失败";
            if(error!=null&&!wasStopping){s.Wanted=false;if(Error!=null)Error(key+"："+error);}
            bool restart=s.Wanted&&wasStopping;Notify();if(restart)Start(key,s);
        }));
    }
    public void StopAll(){foreach(PhoneResource key in Enum.GetValues(typeof(PhoneResource)))Set(key,false,null);}
    void Notify(){if(Changed!=null)Changed();}
}
