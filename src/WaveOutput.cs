using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

sealed class WaveDevice {
    public int Id;public string Name;
    public override string ToString(){return Name;}
}
sealed class WaveOutput:IDisposable {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Caps{public ushort mid,pid;public uint version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;public uint formats;public ushort channels,reserved;public uint support;}
    [StructLayout(LayoutKind.Sequential,Pack=2)]struct Format{public ushort tag,channels;public uint rate,bytes;public ushort align,bits,extra;}
    [StructLayout(LayoutKind.Sequential)]struct Header{public IntPtr data;public uint length,recorded;public UIntPtr user;public uint flags,loops;public IntPtr next;public UIntPtr reserved;}
    [DllImport("winmm.dll")]static extern uint waveOutGetNumDevs();
    [DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern uint waveOutGetDevCapsW(UIntPtr id,out Caps caps,uint size);
    [DllImport("winmm.dll")]static extern uint waveOutOpen(out IntPtr handle,uint id,ref Format format,IntPtr callback,IntPtr instance,uint flags);
    [DllImport("winmm.dll")]static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")]static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")]static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
    [DllImport("winmm.dll")]static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")]static extern uint waveOutClose(IntPtr handle);
    readonly IntPtr[] data=new IntPtr[4],headers=new IntPtr[4];readonly bool[] submitted=new bool[4];IntPtr handle;int index;readonly uint size=(uint)Marshal.SizeOf(typeof(Header));
    public const int BlockBytes=3840;
    internal static bool IsCableOutput(string name){return name.IndexOf("16 Ch",StringComparison.OrdinalIgnoreCase)<0&&(name.IndexOf("CABLE Input",StringComparison.OrdinalIgnoreCase)>=0||name.IndexOf("VB-Audio Virtual Cable",StringComparison.OrdinalIgnoreCase)>=0);}
    public static List<WaveDevice> VirtualDevices(){var result=new List<WaveDevice>();for(uint i=0;i<waveOutGetNumDevs();i++){Caps c;if(waveOutGetDevCapsW(new UIntPtr(i),out c,(uint)Marshal.SizeOf(typeof(Caps)))!=0)continue;if(IsCableOutput(c.name))result.Add(new WaveDevice{Id=(int)i,Name=c.name});}return result;}
    internal static WaveDevice ResolveDevice(List<WaveDevice> devices,int previousId,string name){
        var cables=devices.FindAll(x=>IsCableOutput(x.Name));
        var matches=cables.FindAll(x=>!String.IsNullOrEmpty(name)&&String.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase));
        if(matches.Count==1)return matches[0];
        if(String.IsNullOrEmpty(name)){var previous=cables.Find(x=>x.Id==previousId);if(previous!=null)return previous;}
        if(cables.Count==1)return cables[0];
        throw new InvalidOperationException(cables.Count==0?"未找到 CABLE Input，请检查 VB-CABLE 是否启用，再刷新设备。":"虚拟音频线已变化，请刷新设备并重新选择麦克风输出。");
    }
    public WaveDevice Device{get;private set;}
    public WaveOutput(int id,string name=null){Device=ResolveDevice(VirtualDevices(),id,name);Format f=new Format{tag=1,channels=2,rate=48000,bytes=192000,align=4,bits=16};Check(waveOutOpen(out handle,(uint)Device.Id,ref f,IntPtr.Zero,IntPtr.Zero,0));try{for(int i=0;i<4;i++){data[i]=Marshal.AllocHGlobal(BlockBytes);headers[i]=Marshal.AllocHGlobal((int)size);Marshal.StructureToPtr(new Header{data=data[i],length=BlockBytes},headers[i],false);Check(waveOutPrepareHeader(handle,headers[i],size));}}catch{Dispose();throw;}}
    static void Check(uint result){if(result!=0)throw new InvalidOperationException("虚拟音频输出不可用，错误码 "+result);}
    public void Write(byte[] pcm,Func<bool> canceled){int slot=index++%4;while(submitted[slot]&&(((Header)Marshal.PtrToStructure(headers[slot],typeof(Header))).flags&1)==0){if(canceled())throw new OperationCanceledException();Thread.Sleep(2);}Marshal.Copy(pcm,0,data[slot],pcm.Length);Check(waveOutWrite(handle,headers[slot],size));submitted[slot]=true;}
    public void Dispose(){if(handle!=IntPtr.Zero)waveOutReset(handle);for(int i=0;i<4;i++){if(headers[i]!=IntPtr.Zero){if(handle!=IntPtr.Zero)waveOutUnprepareHeader(handle,headers[i],size);Marshal.FreeHGlobal(headers[i]);headers[i]=IntPtr.Zero;}if(data[i]!=IntPtr.Zero){Marshal.FreeHGlobal(data[i]);data[i]=IntPtr.Zero;}}if(handle!=IntPtr.Zero){waveOutClose(handle);handle=IntPtr.Zero;}}
}
