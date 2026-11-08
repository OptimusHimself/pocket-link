using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

static class AudioRouting {
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class Enumerator{}
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IEnum {
        [PreserveSig]int EnumAudioEndpoints(int flow,uint state,out IntPtr devices);
        [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDevice {
        [PreserveSig]int Activate(ref Guid iid,uint context,IntPtr parameters,out IntPtr instance);
        [PreserveSig]int OpenPropertyStore(uint access,out IntPtr properties);
        [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetState(out uint state);
    }
    internal static bool IsMicrophoneBus(string name){return name!=null&&(name.IndexOf("VB-Audio Virtual Cable",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("CABLE Input",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("CABLE In 16",StringComparison.OrdinalIgnoreCase)>=0);}
    public static string DefaultPlayback(){
        var enumerator=(IEnum)new Enumerator();IDevice device=null;
        try{Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,0,out device));string id;Marshal.ThrowExceptionForHR(device.GetId(out id));string guid=id.Substring(id.LastIndexOf('{'));using(var properties=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\"+guid+@"\Properties")){return properties==null?id:(properties.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},14") as string)??(properties.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string)??id;}}
        finally{if(device!=null)Marshal.ReleaseComObject(device);Marshal.ReleaseComObject(enumerator);}
    }
    public static string DefaultPlaybackId(){var enumerator=(IEnum)new Enumerator();IDevice device=null;try{Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,0,out device));string id;Marshal.ThrowExceptionForHR(device.GetId(out id));return id;}finally{if(device!=null)Marshal.ReleaseComObject(device);Marshal.ReleaseComObject(enumerator);}}
    public static void EnsureSpeakerIsolation(){string output=DefaultPlayback();if(IsMicrophoneBus(output))throw new InvalidOperationException("当前 Windows 默认输出是 VB-CABLE 麦克风通道，会造成回声。请在 Windows 声音设置中把输出改为显示器或耳机；会议麦克风保留 CABLE Output，会议扬声器也选择显示器或耳机。");}
}

