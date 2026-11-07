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
        Application.Run(new MainForm(root,args.Length>=1?args[0]:null,args.Length==2?args[1]:null));return 0;
    }
    static void SelfTest() {
        byte[] input={1,2,3,4,5,6,7,8},l=new byte[4],r=new byte[4];Pcm.Split(input,8,l,r);
        if(BitConverter.ToString(l)!="01-02-05-06" || BitConverter.ToString(r)!="03-04-07-08")throw new Exception("Channel leakage");
        ChannelDelay d=new ChannelDelay(1);d.Apply(l);if(BitConverter.ToString(l)!="00-00-01-02")throw new Exception("Delay initial block");d.Apply(r);if(BitConverter.ToString(r)!="05-06-03-04")throw new Exception("Delay continuity");
        byte[] h=Pcm.Header(48000);if(BitConverter.ToString(h)!="80-BB-00-00-01-00-10-00")throw new Exception("Mono header");
        byte[] stimulus=new byte[48000*4],a=new byte[stimulus.Length/2],b=new byte[stimulus.Length/2];
        // Alternate hard-panned impulses: the other device must receive exact zeros.
        stimulus[0]=0x34;stimulus[1]=0x12;Pcm.Split(stimulus,stimulus.Length,a,b);
        if(a[0]!=0x34 || a[1]!=0x12 || Array.Exists(b,x=>x!=0))throw new Exception("Left-only stimulus leaked right");
        Array.Clear(stimulus,0,stimulus.Length);stimulus[2]=0x78;stimulus[3]=0x56;Pcm.Split(stimulus,stimulus.Length,a,b);
        if(b[0]!=0x78 || b[1]!=0x56 || Array.Exists(a,x=>x!=0))throw new Exception("Right-only stimulus leaked left");
        Array.Clear(stimulus,0,stimulus.Length);stimulus[0]=stimulus[2]=0x12;Pcm.Split(stimulus,stimulus.Length,a,b);
        if(BitConverter.ToString(a)!=BitConverter.ToString(b))throw new Exception("Dual-mono source must match on both outputs");
        try { Pcm.ReadExact(new MemoryStream(new byte[3]),new byte[4]);throw new Exception("Truncated stream accepted"); }catch(EndOfStreamException){}
        var devices=Adb.ParseDevices("List of devices attached\r\nleft device model:Honor\r\nright unauthorized\r\nthird offline\r\n");
        if(devices.Count!=3||!devices[0].Ready||devices[1].Ready||devices[2].Ready)throw new Exception("Device authorization states lost");
        using(MemoryStream wave=TestAudio.CreateWave()) {
            byte[] bytes=wave.ToArray();
            if(bytes.Length!=44+48000*6*4||BitConverter.ToInt16(bytes,22)!=2)throw new Exception("Invalid test WAV");
            for(int second=0;second<6;second++) {
                bool heard=false;int active=(second/2)%2;
                for(int frame=0;frame<48000;frame++)for(int ch=0;ch<2;ch++) {
                    short sample=BitConverter.ToInt16(bytes,44+(second*48000+frame)*4+ch*2);
                    if((second%2==1||ch!=active)&&sample!=0)throw new Exception("Test WAV leaks into silent channel or gap");
                    if(sample!=0)heard=true;
                }
                if(second%2==0&&!heard)throw new Exception("Test WAV has no signal");
            }
        }
        Console.WriteLine("PASS: channel isolation, dual-mono, delay continuity, protocol, disconnect, authorization states, audible/silent test WAV channels");
    }
}

