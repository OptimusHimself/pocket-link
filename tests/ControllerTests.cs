using System;
using System.Collections.Generic;

static class ControllerTests {
    sealed class Job:IResourceJob {public ResourceOptions Options;public int Stops;Action<string> ended;public void Start(ResourceOptions options,Action ready,Action<string> finished){Options=options;ended=finished;ready();}public void Stop(){Stops++;}public void Finish(string error=null){ended(error);}}
    public static void Run(){
        CameraSwitchTests();
        if(!AudioRouting.IsMicrophoneBus("扬声器 (VB-Audio Virtual Cable)")||!AudioRouting.IsMicrophoneBus("CABLE Input")||AudioRouting.IsMicrophoneBus("1 - X71QN (AMD High Definition Audio Device)"))throw new Exception("Microphone bus mistaken for program audio output");
        var jobs=new List<Job>();var controller=new ResourceController(()=>{var job=new Job();jobs.Add(job);return job;},action=>action());
        var speaker=new ResourceOptions{Serial="phone",Speaker=true};var camera=new ResourceOptions{Serial="phone",Camera=true};var mic=new ResourceOptions{Serial="phone",Microphone=true,MicrophoneOutput=1};
        controller.Set(PhoneResource.Speaker,true,speaker);controller.Set(PhoneResource.Camera,true,camera);
        if(jobs.Count!=2||jobs[0].Stops!=0)throw new Exception("Adding camera restarted speaker");
        controller.Set(PhoneResource.Microphone,true,mic);jobs[2].Finish("test mic failure");
        if(!controller.Active(PhoneResource.Speaker)||!controller.Active(PhoneResource.Camera))throw new Exception("Mic failure stopped other resources");
        controller.Set(PhoneResource.Camera,false,null);if(jobs[0].Stops!=0||jobs[1].Stops!=1)throw new Exception("Stopping camera affected speaker");
        controller.Set(PhoneResource.Camera,true,camera);jobs[1].Finish();if(jobs.Count!=4||!controller.Active(PhoneResource.Camera))throw new Exception("Enable during shutdown was lost");
        controller.StopAll();jobs[0].Finish();jobs[3].Finish();if(controller.Busy||jobs.Count!=4)throw new Exception("Stop all restarted resources");
        // Repeated applies must never start duplicate capture processes.
        controller.Set(PhoneResource.Speaker,true,speaker);controller.Set(PhoneResource.Speaker,true,speaker);if(jobs.Count!=5)throw new Exception("Repeated enable spawned duplicate capture");
        controller.StopAll();jobs[4].Finish();
        if(!DevicePresence.MustRelease(true,false,false)||DevicePresence.MustRelease(true,true,true))throw new Exception("Disconnect race did not clear capture selection");
        if(!DevicePresence.CanResumeSpeaker("same","same",true,true)||DevicePresence.CanResumeSpeaker("same","other",true,true)||DevicePresence.CanResumeSpeaker("same","same",true,false)||DevicePresence.CanResumeSpeaker("same","same",false,true))throw new Exception("Reconnect must require same authorized phone and opt-in");
        Console.WriteLine("PASS: independent resource toggles, failure isolation, queued restart, stop-all cleanup, duplicate prevention");
    }
    static void CameraSwitchTests(){
        var jobs=new List<Job>();var controller=new ResourceController(()=>{var job=new Job();jobs.Add(job);return job;},action=>action());
        var back=new ResourceOptions{Serial="phone",Camera=true,FrontCamera=false};
        var front=new ResourceOptions{Serial="phone",Camera=true,FrontCamera=true};
        controller.Set(PhoneResource.Speaker,true,new ResourceOptions{Serial="phone",Speaker=true});
        controller.Set(PhoneResource.Microphone,true,new ResourceOptions{Serial="phone",Microphone=true,MicrophoneOutput=1});
        controller.Set(PhoneResource.Camera,true,back);controller.Set(PhoneResource.Camera,true,front);
        if(jobs.Count!=3||jobs[2].Stops!=1||jobs[0].Stops!=0||jobs[1].Stops!=0)throw new Exception("Camera switch must stop only old camera before restart");
        jobs[2].Finish();if(jobs.Count!=4||!jobs[3].Options.FrontCamera)throw new Exception("Front camera selection not applied");
        controller.Set(PhoneResource.Camera,true,back);controller.Set(PhoneResource.Camera,true,front);jobs[3].Finish();
        if(jobs.Count!=5||!jobs[4].Options.FrontCamera)throw new Exception("Rapid lens changes did not keep latest selection");
        controller.Set(PhoneResource.Camera,true,back);controller.Set(PhoneResource.Camera,false,null);jobs[4].Finish();
        if(jobs.Count!=5||controller.Active(PhoneResource.Camera))throw new Exception("Camera switch restarted after user disabled capture");
        Console.WriteLine("PASS: live front/back camera switch, audio isolation, rapid selection and cancel");
    }
}
