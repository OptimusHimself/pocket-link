using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

static class OwnedProcessTree {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Entry{public uint size,usage,id;public UIntPtr heap;public uint module,threads,parent;public int priority;public uint flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string exe;}
    [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Process32First(IntPtr snapshot,ref Entry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Process32Next(IntPtr snapshot,ref Entry entry);
    [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    public static void Kill(Process root){
        if(root==null)return;DateTime captured=DateTime.UtcNow;var children=new Dictionary<int,List<int>>();IntPtr snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot!=new IntPtr(-1)){try{var item=new Entry{size=(uint)Marshal.SizeOf(typeof(Entry))};if(Process32First(snapshot,ref item))do{List<int> list;if(!children.TryGetValue((int)item.parent,out list)){list=new List<int>();children[(int)item.parent]=list;}list.Add((int)item.id);}while(Process32Next(snapshot,ref item));}finally{CloseHandle(snapshot);}}
        var visited=new HashSet<int>();KillNode(root,children,visited,captured);
    }
    static void KillNode(Process process,Dictionary<int,List<int>> children,HashSet<int> visited,DateTime captured){
        try{if(!visited.Add(process.Id))return;DateTime start=process.StartTime.ToUniversalTime(),end=process.HasExited?process.ExitTime.ToUniversalTime():captured;List<int> ids;
            if(children.TryGetValue(process.Id,out ids))foreach(int id in ids){try{using(var child=Process.GetProcessById(id)){DateTime created=child.StartTime.ToUniversalTime();if(created>=start&&created<=end&&created<=captured)KillNode(child,children,visited,captured);}}catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}
            if(!process.HasExited){process.Kill();process.WaitForExit(1500);}
        }catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
    }
}
