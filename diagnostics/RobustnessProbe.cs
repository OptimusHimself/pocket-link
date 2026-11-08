using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

static class RobustnessProbe {
    static bool cameraOnly;
    static readonly ConcurrentQueue<Action> queue=new ConcurrentQueue<Action>();static readonly List<IntPtr> frozen=new List<IntPtr>();
    [DllImport("kernel32.dll")]static extern IntPtr OpenThread(uint access,bool inherit,uint id);
    [DllImport("kernel32.dll")]static extern uint SuspendThread(IntPtr thread);
    [DllImport("kernel32.dll")]static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    static void Log(string value){Console.WriteLine(DateTime.Now.ToString("HH:mm:ss")+" "+value);}
    static void Wait(Func<bool> condition){var watch=Stopwatch.StartNew();while(true){Action action;while(queue.TryDequeue(out action))action();if(condition())return;if(watch.ElapsedMilliseconds>45000)throw new Exception("Fault recovery timed out");Thread.Sleep(30);}}
    static List<int> Children(int parent,string name){var ids=new List<int>();using(var query=new ManagementObjectSearcher("SELECT ProcessId,Name FROM Win32_Process WHERE ParentProcessId="+parent)){using(var rows=query.Get()){foreach(ManagementObject row in rows){using(row){if(String.Equals((string)row["Name"],name,StringComparison.OrdinalIgnoreCase))ids.Add(Convert.ToInt32(row["ProcessId"]));}}}}return ids;}
    static int Owned(string name){var ids=Children(Process.GetCurrentProcess().Id,name);if(ids.Count!=1)throw new Exception("Expected one owned "+name);return ids[0];}
    static void FreezeDecoder(){int id=Owned("ffmpeg.exe");var children=Children(id,"ffmpeg.exe");if(children.Count==1)id=children[0];using(var process=Process.GetProcessById(id)){foreach(ProcessThread thread in process.Threads){using(thread){IntPtr handle=OpenThread(2,false,(uint)thread.Id);if(handle==IntPtr.Zero)continue;if(SuspendThread(handle)==UInt32.MaxValue){CloseHandle(handle);continue;}frozen.Add(handle);}}}if(frozen.Count==0)throw new Exception("Could not freeze owned decoder");Log("INJECT decoder alive but suspended PID "+id);}
    static void Thaw(){foreach(IntPtr thread in frozen){ResumeThread(thread);CloseHandle(thread);}frozen.Clear();}
    static void AudioRunning(ResourceController controller){if(!cameraOnly&&(controller.Status(PhoneResource.Speaker)!="运行中"||controller.Status(PhoneResource.Microphone)!="运行中"))throw new Exception("Camera failure stopped an audio resource");}
    static ResourceOptions Camera(string serial){return new ResourceOptions{Serial=serial,Camera=true,FrontCamera=true,CameraRotation=90};}
    static void NoVideoProcesses(){Thread.Sleep(500);foreach(string name in new[]{"obs64","obs-browser-page","ffmpeg"})if(Process.GetProcessesByName(name).Length!=0)throw new Exception("Leftover video process: "+name);}
    static int Main(string[] args){
        cameraOnly=args.Length==1&&args[0]=="--camera-only";
        string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));var adb=new Adb(root);var phone=adb.Devices().Find(x=>x.Ready);if(phone==null)throw new Exception("No authorized phone");
        if(!cameraOnly)AudioRouting.EnsureSpeakerIsolation();
        var controller=new ResourceController(()=>new ResourceSession(adb,Log),action=>queue.Enqueue(action));string lastError=null;controller.Error+=message=>{lastError=message;Log("EXPECTED_RESOURCE_ERROR "+message);};
        using(var watcher=new DeviceWatcher(adb)){
            try{
                watcher.Warning+=Log;watcher.Start();Wait(()=>watcher.SubscriptionProcessId!=0);int old=watcher.SubscriptionProcessId;using(var process=Process.GetProcessById(old))process.Kill();Wait(()=>watcher.SubscriptionProcessId!=0&&watcher.SubscriptionProcessId!=old);Log("PASS subscription recovered after process exit");
                if(!cameraOnly){var cable=WaveOutput.ResolveDevice(WaveOutput.VirtualDevices(),-1,null);controller.Set(PhoneResource.Speaker,true,new ResourceOptions{Serial=phone.Serial,Speaker=true});controller.Set(PhoneResource.Microphone,true,new ResourceOptions{Serial=phone.Serial,Microphone=true,MicrophoneOutput=cable.Id,MicrophoneOutputName=cable.Name});}controller.Set(PhoneResource.Camera,true,Camera(phone.Serial));
                Wait(()=>controller.Status(PhoneResource.Camera)=="运行中"&&(cameraOnly||controller.Status(PhoneResource.Speaker)=="运行中"&&controller.Status(PhoneResource.Microphone)=="运行中"));
                FreezeDecoder();Wait(()=>!controller.Active(PhoneResource.Camera));Thaw();AudioRunning(controller);NoVideoProcesses();if(lastError==null||!lastError.Contains("没有新视频帧"))throw new Exception("Stall was not diagnosed precisely");Log("PASS stalled decoder detected; owned tree cleaned; "+(cameraOnly?"camera-only run":"audio preserved"));
                controller.Set(PhoneResource.Camera,true,Camera(phone.Serial));Wait(()=>controller.Status(PhoneResource.Camera)=="运行中");using(var obs=Process.GetProcessById(Owned("obs64.exe")))obs.Kill();Wait(()=>!controller.Active(PhoneResource.Camera));AudioRunning(controller);NoVideoProcesses();Thread.Sleep(500);if(controller.Active(PhoneResource.Camera))throw new Exception("Camera restarted without user request");Log("PASS OBS crash isolated; camera stayed off");
                controller.Set(PhoneResource.Camera,true,Camera(phone.Serial));Wait(()=>controller.Status(PhoneResource.Camera)=="运行中");adb.Run("kill-server");Wait(()=>!controller.Busy);Wait(()=>watcher.SubscriptionProcessId!=0);Thread.Sleep(1000);Wait(()=>true);if(controller.Busy)throw new Exception("Capture restarted after transport loss");NoVideoProcesses();Log("PASS project ADB transport loss cleaned all capture; no automatic media restart");
                var state=new Dictionary<string,string>();foreach(PhoneResource key in Enum.GetValues(typeof(PhoneResource)))state[key.ToString()]=controller.Status(key);string report=DiagnosticReport.Save(root,state,lastError);Log("REPORT "+report);Log("COMPLETE");return 0;
            }catch(Exception e){Log("FAIL "+e);return 1;}
            finally{Thaw();controller.StopAll();Wait(()=>!controller.Busy);watcher.Stop();try{adb.Run("kill-server");}catch(Exception e){Log("Test ADB cleanup: "+e.Message);}}
        }
    }
}
