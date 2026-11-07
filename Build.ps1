$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force "$PSScriptRoot/build" | Out-Null
Push-Location $PSScriptRoot
try {
    & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:build\SoundLink.exe src\*.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
    Copy-Item tools/AudioShare/audio_capture.dll build/audio_capture.dll -Force
} finally { Pop-Location }
