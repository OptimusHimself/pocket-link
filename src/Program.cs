using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Program {
    [STAThread] static int Main(string[] args) {
        if(args.Length>0 && args[0]=="--self-test") {SelfTest();return 0;}
        if(args.Length==1&&(args[0]=="--exit"||args[0]=="--hide"||args[0]=="--show"))return SingleInstance.Signal(args[0].Substring(2))?0:1;
        string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        bool meeting=args.Length==2&&args[0]=="--meeting";
        MainForm form=null;
        using(var instance=new SingleInstance(()=>{if(form!=null)form.RevealWindow();},()=>{if(form!=null)form.CloseWindow();},()=>{if(form!=null)form.RequestExit();})){
            if(!instance.Owner){for(int i=0;i<20;i++){if(SingleInstance.Signal("show"))return 0;Thread.Sleep(50);}return 1;}
            form=new MainForm(root,meeting?args[1]:args.Length==1?args[0]:null,meeting);var handle=form.Handle;Application.Run(form);
        }return 0;
    }
    static void SelfTest() {
        ControllerTests.Run();
        try { Pcm.ReadExact(new MemoryStream(new byte[3]),new byte[4]);throw new Exception("Truncated stream accepted"); }catch(EndOfStreamException){}
        var devices=Adb.ParseDevices("List of devices attached\r\nleft device model:Honor\r\nright unauthorized\r\nthird offline\r\n");
        if(devices.Count!=3||!devices[0].Ready||devices[1].Ready||devices[2].Ready)throw new Exception("Device authorization states lost");
        byte[] original={1,2,3,4,5,6,7,8},copy=new byte[8];Pcm.ReadExact(new MemoryStream(original),copy);
        if(BitConverter.ToString(original)!=BitConverter.ToString(copy))throw new Exception("Speaker PCM changed");
        new ResourceOptions{Serial="phone",Speaker=true}.Validate();
        new ResourceOptions{Serial="phone",Camera=true}.Validate();
        new ResourceOptions{Serial="phone",Microphone=true,MicrophoneOutput=1}.Validate();
        bool rejected=false;try{new ResourceOptions{Serial="phone",Microphone=true}.Validate();}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("Missing virtual microphone accepted");
        rejected=false;try{new ResourceOptions{Serial="phone"}.Validate();}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("Empty resource selection accepted");
        if(!CameraBridge.OrientationArguments(90).Contains("--window-width=360")||!CameraBridge.OrientationArguments(270).Contains("--display-orientation=270")||!CameraBridge.OrientationArguments(0).Contains("--window-width=640"))throw new Exception("Camera orientation dimensions do not match");
        if(!WaveOutput.IsCableOutput("扬声器 (VB-Audio Virtual Cable)")||!WaveOutput.IsCableOutput("CABLE Input (VB-Audio Virtual Cab")||WaveOutput.IsCableOutput("CABLE In 16 Ch (VB-Audio Virtual)"))throw new Exception("Localized VB-CABLE 46 endpoint detection failed");
        string logfile=Path.GetTempFileName();try{using(var writer=new FileStream(logfile,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){byte[] marker=Encoding.UTF8.GetBytes("==== Virtual Camera Start");writer.Write(marker,0,marker.Length);writer.Flush();if(!CameraBridge.ReadSharedLog(logfile).Contains("Virtual Camera Start"))throw new Exception("Active OBS log could not be read");}}finally{File.Delete(logfile);}
        string scene=CameraBridge.SceneJson();if(!scene.Contains("SoundLinkCamera")||!scene.Contains("\"capture_audio\":false"))throw new Exception("Camera scene must not capture audio");
        Console.WriteLine("PASS: unchanged speaker PCM, disconnect, device authorization, independent resource selection, microphone dependency, camera audio isolation");
    }
}
