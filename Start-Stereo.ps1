$ErrorActionPreference = 'Stop'
$env:ADB_LIBUSB = '1'
$exe = "$PSScriptRoot/build/SoundLink.exe"
$source = "$PSScriptRoot/src/SoundLink.cs"
if (!(Test-Path $exe) -or (Get-Item $source).LastWriteTimeUtc -gt (Get-Item $exe).LastWriteTimeUtc -or !(Test-Path "$PSScriptRoot/build/audio_capture.dll")) { & "$PSScriptRoot/Build.ps1" }
Start-Process -FilePath "$PSScriptRoot/build/SoundLink.exe" -WorkingDirectory $PSScriptRoot
