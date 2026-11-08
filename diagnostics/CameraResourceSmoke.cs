using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

// Integration exercise: actual controller/session callbacks, not UI automation.
static class CameraResourceSmoke {
    static readonly ConcurrentQueue<Action> queue=new ConcurrentQueue<Action>();static string error;
    static void Wait(Func<bool> condition){var watch=Stopwatch.StartNew();while(true){Action action;while(queue.TryDequeue(out action))action();if(error!=null)throw new Exception(error);if(condition())return;if(watch.ElapsedMilliseconds>45000)throw new Exception("State transition timed out");Thread.Sleep(20);}}
    static void Log(string value){Console.WriteLine(value);}
    static void CheckVirtualOutput(string root){
        var info=new ProcessStartInfo("ffmpeg.exe","-hide_banner -loglevel info -f dshow -i \"video=OBS Virtual Camera\" -t 2 -vf signalstats,metadata=print -f null -"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        using(var p=Process.Start(info)){var stderr=p.StandardError.ReadToEndAsync();var stdout=p.StandardOutput.ReadToEndAsync();if(!p.WaitForExit(15000)){p.Kill();throw new Exception("Virtual camera capture timeout");}string output=stderr.Result;File.WriteAllText(Path.Combine(root,".runtime/camera-virtual-output-check.log"),output);
            bool changing=false;foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(output,@"lavfi.signalstats.YDIF=([0-9.eE+-]+)")){double value;if(Double.TryParse(match.Groups[1].Value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)&&value>0)changing=true;}
            if(p.ExitCode!=0||!changing)throw new Exception("Virtual output did not yield changing video frames; inspect capture log");
        }Log("VIRTUAL_OUTPUT_DYNAMIC_FRAMES_OK");
    }
    static ResourceOptions Camera(string serial,bool front){return new ResourceOptions{Serial=serial,Camera=true,FrontCamera=front,CameraRotation=90};}
    static int Main(){
        string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));var adb=new Adb(root);var phone=adb.Devices().Find(x=>x.Ready);if(phone==null)throw new Exception("No phone");
        var controller=new ResourceController(()=>new ResourceSession(adb,Log),action=>queue.Enqueue(action));controller.Error+=message=>error=message;
        try{
            var cable=WaveOutput.ResolveDevice(WaveOutput.VirtualDevices(),-1,null);
            controller.Set(PhoneResource.Speaker,true,new ResourceOptions{Serial=phone.Serial,Speaker=true});
            controller.Set(PhoneResource.Microphone,true,new ResourceOptions{Serial=phone.Serial,Microphone=true,MicrophoneOutput=cable.Id,MicrophoneOutputName=cable.Name});
            controller.Set(PhoneResource.Camera,true,Camera(phone.Serial,true));
            Wait(()=>controller.Status(PhoneResource.Speaker)=="运行中"&&controller.Status(PhoneResource.Microphone)=="运行中"&&controller.Status(PhoneResource.Camera)=="运行中");
            string url=controller.CameraPreviewUrl;int obs=Process.GetProcessesByName("obs64")[0].Id;
            using(var client=new WebClient()){if(client.DownloadData(url+"frame").Length<100)throw new Exception("No actual camera frame");}
            Log("ALL_RESOURCES_READY");
            controller.Set(PhoneResource.Camera,true,Camera(phone.Serial,false));Wait(()=>controller.Status(PhoneResource.Camera)=="运行中");
            if(controller.CameraPreviewUrl!=url||Process.GetProcessesByName("obs64")[0].Id!=obs)throw new Exception("Output recreated on switch");
            if(controller.Status(PhoneResource.Speaker)!="运行中"||controller.Status(PhoneResource.Microphone)!="运行中")throw new Exception("Switch interrupted audio");
            Log("SWITCH_REUSE_AND_AUDIO_OK");
            CheckVirtualOutput(root);
            controller.Set(PhoneResource.Camera,true,Camera(phone.Serial,true));Thread.Sleep(350);controller.Set(PhoneResource.Camera,false,null);
            Wait(()=>!controller.Active(PhoneResource.Camera));
            if(!controller.Active(PhoneResource.Speaker)||!controller.Active(PhoneResource.Microphone))throw new Exception("Camera cancellation stopped audio");
            Log("SWITCH_CANCEL_OK");controller.StopAll();Wait(()=>!controller.Busy);Log("ALL_STOPPED");return 0;
        }catch(Exception e){Log("FAIL "+e);return 1;}
        finally{error=null;controller.StopAll();Wait(()=>!controller.Busy);}
    }
}
