using System;
using System.Diagnostics;
using System.IO;

sealed class ObsRuntime {
    public string Exe,Config;
    public static ObsRuntime Prepare(string project,string installedExe){
        string source=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(installedExe),"../.."));
        string root=Path.Combine(project,".runtime/obs-runtime");string marker=Path.Combine(root,"source.txt");
        string exe=Path.Combine(root,"bin/64bit/obs64.exe");
        if(!File.Exists(marker)||!File.Exists(exe)||!String.Equals(File.ReadAllText(marker),source,StringComparison.OrdinalIgnoreCase)){
            var info=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -File "+Adb.Q(Path.Combine(project,"scripts/Prepare-ObsRuntime.ps1"))+" -ObsRoot "+Adb.Q(source)+" -RuntimeRoot "+Adb.Q(root)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(info)){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(15000)){process.Kill();throw new IOException("初始化专用 OBS 配置超时。");}if(process.ExitCode!=0)throw new IOException("无法准备专用 OBS 运行目录："+error.Result);}
        }
        string config=Path.Combine(root,"config/obs-studio");Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config,"user.ini"),"[General]\nFirstRun=true\nConfirmOnExit=false\n[BasicWindow]\nPreviewEnabled=false\nPreviewProgramMode=false\nSysTrayEnabled=true\nSysTrayWhenStarted=true\n[Basic]\nProfile=SoundLink\nProfileDir=SoundLink\nSceneCollection=SoundLink\nSceneCollectionFile=SoundLink\n");
        return new ObsRuntime{Exe=exe,Config=config};
    }
}
