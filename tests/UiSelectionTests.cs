using System;
using System.Collections.Generic;

static class UiSelectionTests {
    public static void Run(){
        var offline=new Device{Serial="old",State="offline"};var first=new Device{Serial="one",State="device"};var second=new Device{Serial="two",State="device"};
        var devices=new List<Device>{offline,first,second};
        if(UiSelection.PreferredPhone(devices,"old")!=first||UiSelection.PreferredPhone(devices,"two")!=second)throw new Exception("Phone selection must prefer a ready device, retaining the previous ready phone.");
        if(UiSelection.PreferredPhone(new List<Device>{offline},"old")!=null)throw new Exception("Offline phone treated as ready.");
        var cable=new WaveDevice{Id=3,Name="CABLE Input"};
        if(UiSelection.UniqueMicrophone(new List<WaveDevice>{cable})!=cable||UiSelection.UniqueMicrophone(new List<WaveDevice>())!=null||UiSelection.UniqueMicrophone(new List<WaveDevice>{cable,new WaveDevice{Id=4,Name="CABLE Input"}})!=null)throw new Exception("Automatic microphone selection must reject missing and ambiguous routes.");
        Console.WriteLine("PASS: automatic ready-phone selection and unique microphone route policy");
    }
}
