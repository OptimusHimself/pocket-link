using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

static class OwnedProcessExit {
    delegate bool WindowCallback(IntPtr window,IntPtr state);
    [DllImport("user32.dll")]static extern bool EnumWindows(WindowCallback callback,IntPtr state);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder name,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
    // Process.MainWindowHandle omits hidden tray windows. Target only this owned OBS PID.
    public static void CloseObs(Process process,Action<string> log){
        if(process==null||process.HasExited)return;int id=process.Id;bool posted=false;
        EnumWindows((window,state)=>{uint owner;GetWindowThreadProcessId(window,out owner);if(owner!=(uint)id)return true;
            var kind=new StringBuilder(256);var title=new StringBuilder(512);GetClassName(window,kind,kind.Capacity);GetWindowText(window,title,title.Capacity);
            if(title.ToString().IndexOf("OBS",StringComparison.OrdinalIgnoreCase)>=0){posted=PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);log("请求关闭专用 OBS 窗口："+kind+" / "+title);return !posted;}return true;
        },IntPtr.Zero);
        if(posted&&process.WaitForExit(3000))return;
        if(!process.HasExited){log("专用 OBS 未正常退出，结束本程序启动的实例。");process.Kill();process.WaitForExit(3000);}
    }
}
