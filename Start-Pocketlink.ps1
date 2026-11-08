$ErrorActionPreference = 'Stop'
$env:ADB_LIBUSB = '1'
$exe = "$PSScriptRoot/build/PocketLink.exe"
if (Test-Path $exe) {
    $running = Get-Process -Name PocketLink -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq [IO.Path]::GetFullPath($exe) }
    if ($running) { Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot; return }
}
$latestSource = (Get-Item "$PSScriptRoot/src/*.cs", "$PSScriptRoot/assets/icons/pocketlink.ico", "$PSScriptRoot/assets/fonts/Orbitron-Variable.ttf", "$PSScriptRoot/Build.ps1" | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
if (!(Test-Path $exe) -or $latestSource -gt (Get-Item $exe).LastWriteTimeUtc -or !(Test-Path "$PSScriptRoot/build/audio_capture.dll")) { & "$PSScriptRoot/Build.ps1" }
Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot
