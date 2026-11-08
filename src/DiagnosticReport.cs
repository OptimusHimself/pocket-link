using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;

static class DiagnosticReport {
    static object Attempt(Func<object> read){try{return read();}catch(Exception e){return new{error=e.Message};}}
    static object FileInfo(string name,string path){if(path==null||!File.Exists(path))return new{name=name,path=path,exists=false};return new{name=name,path=Path.GetFullPath(path),exists=true,version=FileVersionInfo.GetVersionInfo(path).FileVersion,modifiedUtc=File.GetLastWriteTimeUtc(path).ToString("o")};}
    static object Version(string exe,string args,Func<bool> canceled=null){return Attempt(()=>{var output=ProcessRunner.Capture(exe,args,5000,canceled);return new{exitCode=output.ExitCode,output=output.Output,error=output.Error};});}
    public static string Save(string root,Dictionary<string,string> state,string failure,Func<bool> canceled=null){
        if(canceled!=null&&canceled())throw new OperationCanceledException();
        var adb=new Adb(root);var report=new Dictionary<string,object>();
        report["schema"]=1;report["createdUtc"]=DateTime.UtcNow.ToString("o");report["resources"]=state;report["lastFailure"]=failure;report["mediaRecorded"]=false;
        report["blockedResources"]=ResourceSafety.Snapshot();
        report["windows"]=Attempt(()=>{using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")){return new{build=key.GetValue("CurrentBuild"),revision=key.GetValue("UBR"),release=key.GetValue("DisplayVersion"),logicalProcessors=Environment.ProcessorCount};}});
        report["projectCommit"]=Version("git","-C "+Adb.Q(root)+" rev-parse --short HEAD",canceled);
        report["adb"]=Version(adb.Exe,"-P 15037 version",canceled);report["phones"]=Attempt(()=>adb.Devices(canceled).Select(x=>new{model=x.Description,state=x.State,serialSuffix=x.Serial.Length>4?x.Serial.Substring(x.Serial.Length-4):x.Serial}).ToArray());
        report["defaultPlayback"]=Attempt(()=>new{name=AudioRouting.DefaultPlayback(),id=AudioRouting.DefaultPlaybackId()});report["virtualAudioOutputs"]=Attempt(()=>WaveOutput.VirtualDevices().Select(x=>new{id=x.Id,name=x.Name}).ToArray());report["cameraDevices"]=Attempt(()=>CameraDevices.Inspect());
        report["obs"]=Attempt(()=>FileInfo("OBS",CameraBridge.FindObs()));
        report["ffmpeg"]=Attempt(()=>{string exe=ComponentHealth.FindDecoder(root);return new{file=FileInfo("FFmpeg",exe),version=Version(exe,"-version",canceled)};});
        report["projectFiles"]=new[]{"tools/platform-tools/adb.exe","tools/scrcpy-win64-v5.0.1/scrcpy-server","tools/AudioShare/server","scripts/Prepare-ObsRuntime.ps1"}.Select(x=>Attempt(()=>FileInfo(x,Path.Combine(root,x)))).ToArray();
        report["captureDll"]=Attempt(()=>FileInfo("audio_capture.dll",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio_capture.dll")));
        string directory=Path.Combine(root,".runtime/diagnostics");Directory.CreateDirectory(directory);string path=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".json");
        File.WriteAllText(path,new JavaScriptSerializer().Serialize(report));return path;
    }
}
