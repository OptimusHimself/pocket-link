$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force "$PSScriptRoot/build" | Out-Null
Push-Location $PSScriptRoot
try {
    $compiler = "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:build\PocketLink.exe src\*.cs tests\*.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /out:build\PocketLink.Tests.exe src\*.cs tests\*.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    Copy-Item tools/AudioShare/audio_capture.dll build/audio_capture.dll -Force
} finally { Pop-Location }
