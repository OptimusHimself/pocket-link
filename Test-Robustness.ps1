param([switch]$CameraOnly)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try{
    if(Get-Process RobustnessProbe,PerformanceProbe,CameraLifecycleProbe,CameraResourceSmoke -ErrorAction SilentlyContinue){throw 'Another diagnostic is running.'}
    & .\build\PocketLink.exe --exit
    Get-Process PocketLink -ErrorAction SilentlyContinue|ForEach-Object {if(-not $_.WaitForExit(30000)){throw 'PocketLink is still releasing resources.'}}
    if(Get-Process obs64,ffmpeg -ErrorAction SilentlyContinue){throw 'Close other OBS/FFmpeg workloads before fault injection.'}
    & .\Build.ps1
    & .\build\PocketLink.Tests.exe --self-test
    if($LASTEXITCODE -ne 0){throw 'Unit tests failed.'}
    & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll /main:RobustnessProbe /out:build\RobustnessProbe.exe src\*.cs tests\*.cs diagnostics\RobustnessProbe.cs
    if($LASTEXITCODE -ne 0){throw 'Fault probe compilation failed.'}
    if($CameraOnly){& .\build\RobustnessProbe.exe --camera-only | Tee-Object -FilePath .runtime/robustness-probe.log}
    else{& .\build\RobustnessProbe.exe | Tee-Object -FilePath .runtime/robustness-probe.log}
    if($LASTEXITCODE -ne 0){throw 'Fault probe failed; inspect .runtime/robustness-probe.log.'}
}finally{Pop-Location}
