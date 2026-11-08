using System;
using System.IO;

static class ComponentHealth {
    public static string FindDecoder(string root){
        string local=Path.Combine(root,"tools/ffmpeg/ffmpeg.exe");if(File.Exists(local))return local;
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')){try{string path=Path.Combine(folder.Trim('"'),"ffmpeg.exe");if(File.Exists(path))return path;}catch(ArgumentException){}}
        throw new IOException("缺少 FFmpeg：请恢复 tools/ffmpeg/ffmpeg.exe，或检查其安装路径。");
    }
    static void Require(string root,string relative,string message){if(!File.Exists(Path.Combine(root,relative)))throw new IOException(message);}
    public static void ValidateFiles(string root,ResourceOptions options){
        Require(root,"tools/platform-tools/adb.exe","缺少项目内 ADB，请恢复 tools/platform-tools。");
        if(options.Speaker){Require(root,"tools/AudioShare/server","缺少手机播放组件，请恢复 tools/AudioShare/server。");if(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audio_capture.dll")))throw new IOException("缺少 audio_capture.dll，请重新运行 Build.ps1。");}
        if(options.Camera||options.Microphone)Require(root,"tools/scrcpy-win64-v5.0.1/scrcpy-server","缺少配套的 scrcpy 5.0.1 服务端，请运行 Setup-Resources.ps1。");
        if(options.Camera){FindDecoder(root);if(CameraBridge.FindObs()==null)throw new IOException("未找到 OBS Studio，请检查更新后的安装路径。");Require(root,"scripts/Prepare-ObsRuntime.ps1","缺少专用 OBS 配置脚本，请恢复 scripts/Prepare-ObsRuntime.ps1。");}
    }
    public static void Validate(string root,ResourceOptions options){
        ResourceSafety.Check(options);
        ValidateFiles(root,options);
        if(options.Camera&&!CameraDevices.HasObs())throw new IOException("Windows 未列出 OBS Virtual Camera；请修复 OBS 虚拟摄像头组件后重试。");
        if(options.Microphone)WaveOutput.ResolveDevice(WaveOutput.VirtualDevices(),options.MicrophoneOutput,options.MicrophoneOutputName);
    }
}
