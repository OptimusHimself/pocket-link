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

sealed class Device {
    public string Serial, Description, State;
    public bool Ready { get { return State=="device"; } }
    public override string ToString() { return Description+"  ·  "+Serial+(Ready?"":State=="unauthorized"?"  ·  等待手机授权":"  ·  离线"); }
}
sealed class Adb {
    public readonly string Root, Exe;
    public Adb(string root) { Root=root; Exe=Path.Combine(root,"tools/platform-tools/adb.exe"); }
    public Process Spawn(string args) {
        ProcessStartInfo p=new ProcessStartInfo(Exe,"-P 15037 "+args); p.UseShellExecute=false; p.CreateNoWindow=true;
        p.RedirectStandardOutput=true; p.RedirectStandardError=true; p.EnvironmentVariables["ADB_LIBUSB"]="1";
        Process proc=Process.Start(p); return proc;
    }
    public string Run(string args,Func<bool> canceled=null,int timeout=15000) {
        if(canceled!=null&&canceled())throw new OperationCanceledException();
        using(Process p=Spawn(args)) {
            ProcessOutput result=ProcessRunner.Run(p,timeout,canceled);
            if(result.ExitCode!=0)throw new IOException("ADB 执行失败："+result.Error+result.Output);return result.Output;
        }
    }
    public static string Q(string value) { if(value.IndexOf('"')>=0) throw new ArgumentException("Invalid argument"); return "\""+value+"\""; }
    public List<Device> Devices(Func<bool> canceled=null) { return ParseDevices(Run("devices -l",canceled)); }
    internal static List<Device> ParseDevices(string output) {
        List<Device> result=new List<Device>();
        foreach(string line in output.Split('\n')) {
            string[] parts=line.Split(new char[]{' ','\t','\r'},StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length<2 || (parts[1]!="device" && parts[1]!="unauthorized" && parts[1]!="offline")) continue;
            string name=parts[0]; foreach(string part in parts) if(part.StartsWith("model:")) name=part.Substring(6);
            result.Add(new Device{Serial=parts[0],Description=name,State=parts[1]});
        } return result;
    }
}
