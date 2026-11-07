$ErrorActionPreference = 'Stop'
$env:ADB_LIBUSB = '1'
$exe = "$PSScriptRoot/build/SoundLink.exe"
$latestSource = (Get-ChildItem "$PSScriptRoot/src/*.cs" | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
if (!(Test-Path $exe) -or $latestSource -gt (Get-Item $exe).LastWriteTimeUtc -or !(Test-Path "$PSScriptRoot/build/audio_capture.dll")) { & "$PSScriptRoot/Build.ps1" }
Start-Process -FilePath "$PSScriptRoot/build/SoundLink.exe" -WorkingDirectory $PSScriptRoot
