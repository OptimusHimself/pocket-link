using System;
using System.Collections.Generic;
using System.IO;

static class CameraPipelineTests {
    sealed class Fragments:MemoryStream {public Fragments(byte[] bytes):base(bytes){}public override int Read(byte[] buffer,int offset,int count){return base.Read(buffer,offset,Math.Min(3,count));}}
    sealed class CameraJob:IResourceJob,ICameraFacingControls {
        public int Stops,Switches;public bool Front;public Action Ready;Action<string> ended;
        public void Start(ResourceOptions options,Action ready,Action<string> end){Front=options.FrontCamera;ended=end;ready();}
        public void SetFacing(bool front,Action ready){Front=front;Switches++;Ready=ready;}
        public void Stop(){Stops++;}public void Finish(){ended(null);}
    }
    public static void Run(){
        byte[] bytes={0,255,216,1,2,255,217,8,255,216,3,255,217,255,216,9};var result=new List<byte[]>();
        using(var source=new Fragments(bytes))JpegFrames.Read(source,result.Add,()=>false);
        if(result.Count!=2||BitConverter.ToString(result[0])!="FF-D8-01-02-FF-D9"||BitConverter.ToString(result[1])!="FF-D8-03-FF-D9")throw new Exception("JPEG pipe fragments changed or incomplete frame published");
        byte[] huge=new byte[4*1024*1024+2];huge[0]=255;huge[1]=216;bool rejected=false;
        try{JpegFrames.Read(new MemoryStream(huge),x=>{},()=>false);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("Oversized frame accepted");
        using(var host=new CameraFrameHost()){host.SetFrame(result[0]);host.Transform(180,true);using(var client=new System.Net.WebClient()){byte[] received=client.DownloadData(host.Url+"frame");if(client.ResponseHeaders["X-PocketLink-Rotation"]!="180"||client.ResponseHeaders["X-PocketLink-Mirror"]!="1"||BitConverter.ToString(received)!=BitConverter.ToString(result[0]))throw new Exception("Frame response lost transform metadata or changed JPEG");}host.ClearFrame();if(host.HasFrame)throw new Exception("Camera switch retained old frame");}
        var jobs=new List<CameraJob>();var controller=new ResourceController(()=>{var job=new CameraJob();jobs.Add(job);return job;},action=>action());
        controller.Set(PhoneResource.Camera,true,new ResourceOptions{Serial="p",Camera=true,FrontCamera=false});
        controller.Set(PhoneResource.Camera,true,new ResourceOptions{Serial="p",Camera=true,FrontCamera=true});
        if(jobs.Count!=1||jobs[0].Stops!=0||!jobs[0].Front||controller.Status(PhoneResource.Camera)!="切换镜头中")throw new Exception("Camera switch recreated output session");
        controller.Set(PhoneResource.Camera,true,new ResourceOptions{Serial="p",Camera=true,FrontCamera=false});jobs[0].Ready();
        if(jobs[0].Front||jobs[0].Switches!=2||controller.Status(PhoneResource.Camera)!="运行中")throw new Exception("Rapid lens selection lost");
        controller.Set(PhoneResource.Camera,false,null);jobs[0].Ready();
        if(controller.Status(PhoneResource.Camera)!="停止中")throw new Exception("Switch callback revived canceled camera");jobs[0].Finish();
        Console.WriteLine("PASS: bounded fragmented JPEG parsing, old-frame clearing, output reuse and switch cancellation");
    }
}
