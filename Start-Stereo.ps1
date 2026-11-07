$ErrorActionPreference = 'Stop'
$env:ADB_LIBUSB = '1'
if (!(Test-Path "$PSScriptRoot/build/SoundLink.exe")) { & "$PSScriptRoot/Build.ps1" }
Start-Process -FilePath "$PSScriptRoot/build/SoundLink.exe" -WorkingDirectory $PSScriptRoot
