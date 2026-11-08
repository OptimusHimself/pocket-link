param([string]$OutputDirectory = 'build')
$ErrorActionPreference = 'Stop'
$outputPath = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Force $outputPath | Out-Null
Push-Location $PSScriptRoot
try {
    $compiler = "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    $artwork = @('/win32icon:assets/icons/pocketlink.ico', '/resource:assets/icons/pocketlink.ico,PocketLink.ico', '/resource:assets/fonts/Orbitron-Variable.ttf,PocketLink.Orbitron.ttf')
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll @artwork "/out:$outputPath/PocketLink.exe" src\*.cs tests\*.cs
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll @artwork "/out:$outputPath/PocketLink.Tests.exe" src\*.cs tests\*.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    Copy-Item tools/AudioShare/audio_capture.dll "$outputPath/audio_capture.dll" -Force
    Copy-Item assets/fonts/Orbitron-OFL.txt "$outputPath/Orbitron-OFL.txt" -Force
} finally { Pop-Location }
