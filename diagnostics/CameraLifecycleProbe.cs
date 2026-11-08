using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;

static class CameraLifecycleProbe {
    static StreamWriter csv;
    static void Log(string message){Console.WriteLine(DateTime.Now.ToString("HH:mm:ss")+" "+message);}
    static int ObsId(){var processes=Process.GetProcessesByName("obs64");try{if(processes.Length!=1)throw new Exception("Expected exactly one owned OBS instance");return processes[0].Id;}finally{foreach(var p in processes)p.Dispose();}}
    static bool Included(Process p){string n=p.ProcessName;return n=="CameraLifecycleProbe"||n=="adb"||n=="ffmpeg"||n=="obs64"||n.StartsWith("obs-browser");}
    static Dictionary<int,double> Cpu(){var result=new Dictionary<int,double>();foreach(var p in Process.GetProcesses()){using(p){try{if(Included(p))result[p.Id]=p.TotalProcessorTime.TotalMilliseconds;}catch(InvalidOperationException){}}}return result;}
    static void Measure(string phase){
        Thread.Sleep(5000);var previous=Cpu();var watch=Stopwatch.StartNew();double last=0;
        while(watch.Elapsed.TotalSeconds<30){Thread.Sleep(2000);double now=watch.Elapsed.TotalSeconds;var next=new Dictionary<int,double>();foreach(var p in Process.GetProcesses()){using(p){try{if(!Included(p))continue;double cpu=p.TotalProcessorTime.TotalMilliseconds,old;next[p.Id]=cpu;if(!previous.TryGetValue(p.Id,out old))continue;
            csv.WriteLine(String.Join(",",phase,p.Id,p.ProcessName,(now-last).ToString("F3",CultureInfo.InvariantCulture),(cpu-old).ToString("F3",CultureInfo.InvariantCulture),(p.WorkingSet64/1048576.0).ToString("F2",CultureInfo.InvariantCulture),(p.PrivateMemorySize64/1048576.0).ToString("F2",CultureInfo.InvariantCulture),Environment.ProcessorCount));
        }catch(InvalidOperationException){}}}previous=next;last=now;}
        Log("MEASURED "+phase);
    }
    static void CheckFrame(CameraBridge camera){using(var client=new WebClient()){byte[] bytes=client.DownloadData(camera.PreviewUrl+"frame");using(var image=System.Drawing.Image.FromStream(new MemoryStream(bytes))){if(image.Width!=1280||image.Height!=720)throw new Exception("Camera dimensions changed");}camera.SetTransform(270,true);string state=client.DownloadString(camera.PreviewUrl+"state");if(!state.Contains("270")||!state.Contains("true"))throw new Exception("Live transform lost");camera.SetTransform(90,false);}Log("FRAME_AND_TRANSFORM_OK");}
    static int Main(string[] args){
        string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));string label=args.Length>0?args[0]:"after";CameraBridge camera=null;
        if(Process.GetProcessesByName("obs64").Length!=0)throw new Exception("Stop existing OBS before running isolated comparison");
        using(csv=new StreamWriter(Path.Combine(root,".runtime/camera-"+label+".csv"))){csv.AutoFlush=true;csv.WriteLine("phase,pid,process,seconds,cpu_ms,working_mb,private_mb,logical_cpus");
            try{var adb=new Adb(root);var phone=adb.Devices().Find(x=>x.Ready);if(phone==null)throw new Exception("No phone");Log("PHONE "+phone.Description);
                camera=new CameraBridge(adb,Log);var watch=Stopwatch.StartNew();camera.Start(phone.Serial,true,90,false);Log("START_MS "+watch.ElapsedMilliseconds);int id=ObsId();string url=camera.PreviewUrl;CheckFrame(camera);Measure("Front");if(!camera.Alive)throw new Exception("Front capture ended");
                watch.Restart();
#if BASELINE
                camera.Dispose();camera=new CameraBridge(adb,Log);camera.Start(phone.Serial,false,90,false);
#else
                camera.SwitchFacing(false);
#endif
                Log("SWITCH_MS "+watch.ElapsedMilliseconds+" OUTPUT_REUSED "+(id==ObsId())+" URL_REUSED "+(url==camera.PreviewUrl));CheckFrame(camera);Measure("Back");if(!camera.Alive)throw new Exception("Back capture ended");
                watch.Restart();camera.Dispose();camera=null;Log("STOP_MS "+watch.ElapsedMilliseconds);Thread.Sleep(1500);
                foreach(string name in new[]{"obs64","obs-browser-page","ffmpeg"}){if(Process.GetProcessesByName(name).Length!=0)throw new Exception("Leftover process: "+name);}Log("CLEANUP_OK");return 0;
            }catch(Exception e){Log("FAIL "+e);return 1;}finally{if(camera!=null)camera.Dispose();}
        }
    }
}
