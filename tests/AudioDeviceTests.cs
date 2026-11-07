using System;
using System.Collections.Generic;

static class AudioDeviceTests {
    public static void Run(){
        var devices=new List<WaveDevice>{new WaveDevice{Id=0,Name="Speakers"},new WaveDevice{Id=4,Name="CABLE Input (VB-Audio Virtual C"}};
        if(WaveOutput.ResolveDevice(devices,0,"CABLE Input (VB-Audio Virtual C").Id!=4)throw new Exception("Stale device ID routed microphone to physical speakers");
        if(WaveOutput.ResolveDevice(devices,1,null).Id!=4)throw new Exception("Unique cable did not recover after re-enumeration");
        devices.Add(new WaveDevice{Id=5,Name="CABLE Input B"});
        if(WaveOutput.ResolveDevice(devices,5,"CABLE Input (VB-Audio Virtual C").Id!=4)throw new Exception("Device name lost to stale numeric ID");
        bool rejected=false;try{WaveOutput.ResolveDevice(devices,0,"Removed cable");}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("Ambiguous cable selection accepted");
        devices.RemoveAll(x=>WaveOutput.IsCableOutput(x.Name));
        rejected=false;try{WaveOutput.ResolveDevice(devices,0,null);}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("Physical speaker accepted as microphone bus");
        Console.WriteLine("PASS: microphone route recovers from device renumbering and rejects physical/ambiguous outputs");
    }
}
