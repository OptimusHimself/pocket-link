using System;
using System.Security.Principal;
using System.Threading;

sealed class SingleInstance:IDisposable {
    static string Prefix{get{return "Local\\PocketLink-"+WindowsIdentity.GetCurrent().User.Value;}}
    readonly Mutex mutex;readonly EventWaitHandle show,hide,exit;readonly RegisteredWaitHandle[] waits;
    public bool Owner{get;private set;}
    public static bool Signal(string action){try{using(var e=EventWaitHandle.OpenExisting(Prefix+"-"+action)){e.Set();return true;}}catch(WaitHandleCannotBeOpenedException){return false;}}
    public SingleInstance(Action reveal,Action conceal,Action quit){
        bool created;mutex=new Mutex(true,Prefix,out created);Owner=created;if(!Owner)return;
        show=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+"-show");hide=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+"-hide");exit=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+"-exit");
        waits=new[]{Watch(show,reveal),Watch(hide,conceal),Watch(exit,quit)};
    }
    static RegisteredWaitHandle Watch(WaitHandle handle,Action callback){return ThreadPool.RegisterWaitForSingleObject(handle,(s,t)=>callback(),null,-1,false);}
    public void Dispose(){if(Owner){foreach(var w in waits)w.Unregister(null);show.Dispose();hide.Dispose();exit.Dispose();mutex.ReleaseMutex();}mutex.Dispose();}
}
