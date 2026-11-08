using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

static class RobustnessTests {
    sealed class HungSpeaker:ISpeakerRelay {
        public event Action Connected;public bool PhoneAlive{get{return true;}}public string Failure{get{return null;}}public readonly ManualResetEvent Release=new ManualResetEvent(false);public Action End;
        public void Start(string device,Action ended){End=ended;if(Connected!=null)Connected();}public void Stop(){Release.WaitOne();}
    }
    static int SessionTimeout(){
        var speaker=new HungSpeaker();var ready=new ManualResetEvent(false);var ended=new ManualResetEvent(false);string error=null;
        var session=new ResourceSession(new Adb("unused"),message=>{},()=>speaker,options=>{},()=>"test-endpoint",100);
        session.Start(new ResourceOptions{Serial="fake",Speaker=true},()=>ready.Set(),failure=>{error=failure;ended.Set();});
        if(!ready.WaitOne(2000))return 10;var watch=Stopwatch.StartNew();session.Stop();if(watch.ElapsedMilliseconds>100)return 11;
        if(!ended.WaitOne(2000)||error==null||!error.Contains("结束超时"))return 12;
        bool blocked=false;try{ResourceSafety.Check(new ResourceOptions{Serial="fake",Speaker=true});}catch(InvalidOperationException){blocked=true;}
        speaker.Release.Set();speaker.End();return blocked?0:13;
    }
    sealed class Job:IResourceJob {
        public bool ThrowStart,ThrowStop;public int Stops;public Action Ready;public Action<string> End;
        public void Start(ResourceOptions options,Action ready,Action<string> ended){Ready=ready;End=ended;if(ThrowStart)throw new IOException("injected start failure");ready();}
        public void Stop(){Stops++;if(ThrowStop){ThrowStop=false;throw new IOException("injected stop failure");}}
    }
    public static int Child(string mode){if(mode=="session-timeout")return SessionTimeout();if(mode=="hang"){Thread.Sleep(10000);return 0;}if(mode=="flood"){Console.Write(new string('x',50000));Console.Error.Write(new string('y',50000));return 7;}if(mode=="blocked"){ResourceSafety.Block(PhoneResource.Speaker,"injected native timeout");ResourceSafety.Check(new ResourceOptions{Serial="p",Camera=true});try{ResourceSafety.Check(new ResourceOptions{Serial="p",Speaker=true});}catch(InvalidOperationException e){return e.Message.Contains("重新启动")?0:9;}return 8;}return 2;}
    public static void Run(){
        long ticks=0;var progress=new ProgressHealth(()=>ticks,1000);ticks=9999;if(progress.Expired(10000))throw new Exception("Progress deadline fired early");ticks=10000;if(!progress.Expired(10000))throw new Exception("Stalled producer remained healthy");progress.Pulse();if(progress.Expired(10000))throw new Exception("Fresh progress rejected");
        var budget=new RetryBudget();if(budget.NextDelay()!=2000||budget.NextDelay()!=4000||budget.NextDelay()!=6000||budget.NextDelay()!=-1||budget.NextDelay()!=-1)throw new Exception("Subscription retry budget is unbounded");budget.Reset();if(budget.NextDelay()!=2000)throw new Exception("Manual reconnect did not reset budget");
        var jobs=new List<Job>();bool fail=false;var controller=new ResourceController(()=>{var job=new Job{ThrowStart=fail};jobs.Add(job);return job;},action=>action());int errors=0;controller.Error+=message=>errors++;
        controller.Set(PhoneResource.Speaker,true,new ResourceOptions{Serial="p",Speaker=true});fail=true;
        controller.Set(PhoneResource.Camera,true,new ResourceOptions{Serial="p",Camera=true});
        if(controller.Active(PhoneResource.Camera)||!controller.Active(PhoneResource.Speaker)||errors!=1)throw new Exception("Synchronous startup failure escaped or stopped another resource");
        jobs[1].Ready();jobs[1].End(null);if(controller.Status(PhoneResource.Camera)!="失败")throw new Exception("Late callback revived failed job");
        fail=false;controller.Set(PhoneResource.Camera,true,new ResourceOptions{Serial="p",Camera=true});jobs[0].ThrowStop=true;controller.StopAll();
        if(jobs[2].Stops!=1||controller.Status(PhoneResource.Speaker)!="停止失败")throw new Exception("Stop failure prevented cleanup of other resources");
        controller.StopAll();jobs[0].End(null);jobs[2].End(null);if(controller.Busy)throw new Exception("Manual cleanup retry remained busy");
        var factoryFailure=new ResourceController(()=>{throw new IOException("missing component");},action=>action());factoryFailure.Set(PhoneResource.Camera,true,new ResourceOptions{Serial="p",Camera=true});if(factoryFailure.Busy||factoryFailure.Status(PhoneResource.Camera)!="失败")throw new Exception("Factory failure wedged controller");
        string missing=Path.Combine(Path.GetTempPath(),"PocketLink-missing-"+Guid.NewGuid().ToString("N"));bool rejected=false;try{ComponentHealth.ValidateFiles(missing,new ResourceOptions{Serial="p",Camera=true});}catch(IOException e){rejected=e.Message.Contains("ADB");}if(!rejected)throw new Exception("Missing dependency accepted");
        string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"PocketLink.Tests.exe");var flood=ProcessRunner.Capture(exe,"--test-child flood",5000);if(flood.ExitCode!=7||flood.Output.Length!=16384||flood.Error.Length!=16384)throw new Exception("Process output was unbounded or deadlocked");
        if(ProcessRunner.Capture(exe,"--test-child blocked",5000).ExitCode!=0)throw new Exception("Timed-out native engine was reused or blocked unrelated resources");
        if(ProcessRunner.Capture(exe,"--test-child session-timeout",5000).ExitCode!=0)throw new Exception("Blocked native cleanup froze session stop or was reused");
        rejected=false;try{ProcessRunner.Capture(exe,"--test-child hang",200);}catch(TimeoutException){rejected=true;}if(!rejected)throw new Exception("Hung process was not bounded");
        using(var child=Process.Start(new ProcessStartInfo(exe,"--test-child hang"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){rejected=false;try{ProcessRunner.Run(child,5000,()=>true);}catch(OperationCanceledException){rejected=true;}if(!rejected||!child.HasExited)throw new Exception("Canceled process was left alive");}
        Console.WriteLine("PASS: stall deadline, bounded retry, startup/factory/stop faults, stale callbacks, missing files, process timeout/cancel/output bounds");
    }
}
