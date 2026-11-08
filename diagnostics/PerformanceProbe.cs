using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

// Build separately from the application. Uses production resource sessions.
static class PerformanceProbe {
    static string root;static StreamWriter samples;static readonly List<ResourceSession> jobs=new List<ResourceSession>();static readonly List<ManualResetEvent> ended=new List<ManualResetEvent>();
    static void Log(string text){Console.WriteLine(DateTime.Now.ToString("HH:mm:ss")+" "+text);}
    static int Main(string[] args){
        root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
        Directory.CreateDirectory(Path.Combine(root,".runtime"));
        using(samples=new StreamWriter(Path.Combine(root,".runtime/performance-samples.csv"),false)){
            samples.WriteLine("phase,pid,process,seconds,cpu_ms,working_mb,private_mb,logical_cpus");samples.AutoFlush=true;
            try{
                var adb=new Adb(root);var devices=adb.Devices();var phone=devices.Find(x=>x.Ready);if(phone==null)throw new Exception("No authorized phone");
                var cable=WaveOutput.ResolveDevice(WaveOutput.VirtualDevices(),-1,null);AudioRouting.EnsureSpeakerIsolation();
                Log("PHONE "+phone.Serial+"; logical CPUs="+Environment.ProcessorCount);
                Measure("Idle",30);
                foreach(string phase in new[]{"Speaker","Microphone","CameraFront","CameraBack","All"}){
                    try{
                        if(phase=="Speaker"||phase=="All")Start(adb,new ResourceOptions{Serial=phone.Serial,Speaker=true});
                        if(phase=="Microphone"||phase=="All")Start(adb,new ResourceOptions{Serial=phone.Serial,Microphone=true,MicrophoneOutput=cable.Id,MicrophoneOutputName=cable.Name});
                        if(phase.StartsWith("Camera")||phase=="All")Start(adb,new ResourceOptions{Serial=phone.Serial,Camera=true,FrontCamera=phase!="CameraBack",CameraRotation=90});
                        Measure(phase,30);
                    }finally{Stop();}
                }
                Measure("AfterStop",30);Log("COMPLETE");return 0;
            }catch(Exception e){Log("FAIL "+e);return 1;}finally{Stop();}
        }
    }
    static void Start(Adb adb,ResourceOptions options){
        var job=new ResourceSession(adb,Log);var done=new ManualResetEvent(false);var ready=new ManualResetEvent(false);string failure=null;bool connected=false;
        jobs.Add(job);ended.Add(done);Stopwatch watch=Stopwatch.StartNew();
        job.Start(options,()=>{connected=true;ready.Set();},error=>{failure=error;if(!connected)ready.Set();done.Set();});
        if(!ready.WaitOne(45000)||!connected)throw new Exception("Startup failed: "+failure);
        Log("READY "+(options.Camera?(options.FrontCamera?"front":"back"):options.Speaker?"speaker":"microphone")+" "+watch.ElapsedMilliseconds+"ms");ready.Dispose();
    }
    static void Stop(){
        if(jobs.Count==0)return;Stopwatch watch=Stopwatch.StartNew();foreach(var job in jobs)job.Stop();
        foreach(var done in ended){if(!done.WaitOne(30000))throw new Exception("Resource cleanup timeout");done.Dispose();}
        jobs.Clear();ended.Clear();Log("STOPPED "+watch.ElapsedMilliseconds+"ms");
    }
    static bool Included(Process p){string n=p.ProcessName;return n=="PerformanceProbe"||n=="PocketLink"||n=="adb"||n=="ffmpeg"||n=="obs64"||n.StartsWith("obs-browser");}
    static Dictionary<int,double> Snapshot(){var result=new Dictionary<int,double>();foreach(var p in Process.GetProcesses()){using(p){try{if(Included(p))result[p.Id]=p.TotalProcessorTime.TotalMilliseconds;}catch(InvalidOperationException){}}}return result;}
    static void Measure(string phase,int seconds){
        Log("WARMUP "+phase);Thread.Sleep(5000);Log("MEASURE "+phase);var previous=Snapshot();var watch=Stopwatch.StartNew();double last=0;
        while(watch.Elapsed.TotalSeconds<seconds){Thread.Sleep(2000);double now=watch.Elapsed.TotalSeconds;var next=new Dictionary<int,double>();foreach(var p in Process.GetProcesses()){using(p){try{if(!Included(p))continue;double cpu=p.TotalProcessorTime.TotalMilliseconds;next[p.Id]=cpu;double old;if(!previous.TryGetValue(p.Id,out old))continue;
                    samples.WriteLine(String.Join(",",phase,p.Id,p.ProcessName,(now-last).ToString("F3",CultureInfo.InvariantCulture),(cpu-old).ToString("F3",CultureInfo.InvariantCulture),(p.WorkingSet64/1048576.0).ToString("F2",CultureInfo.InvariantCulture),(p.PrivateMemorySize64/1048576.0).ToString("F2",CultureInfo.InvariantCulture),Environment.ProcessorCount));
                }catch(InvalidOperationException){}}}previous=next;last=now;}
        foreach(var done in ended)if(done.WaitOne(0))throw new Exception("Resource ended during "+phase);
        Log("MEASURED "+phase);
    }
}
