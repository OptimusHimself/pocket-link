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
        string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(root,args.Length==1?args[0]:null));return 0;
    }
    static void SelfTest() {
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
        string scene=CameraBridge.SceneJson();if(!scene.Contains("SoundLinkCamera")||!scene.Contains("\"capture_audio\":false"))throw new Exception("Camera scene must not capture audio");
        Console.WriteLine("PASS: unchanged speaker PCM, disconnect, device authorization, independent resource selection, microphone dependency, camera audio isolation");
    }
}
