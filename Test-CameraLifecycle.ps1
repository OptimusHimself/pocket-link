param([switch]$Baseline,[switch]$Smoke,[string]$BaselineRef='c36adc6')
$ErrorActionPreference='Stop'
if($Baseline -and $Smoke){throw 'Choose baseline measurement or integration smoke test.'}
Push-Location $PSScriptRoot
try {
    if(Get-Process CameraLifecycleProbe,CameraResourceSmoke,PerformanceProbe -ErrorAction SilentlyContinue){throw 'A diagnostic is already running.'}
    & .\build\PocketLink.exe --exit
    Get-Process PocketLink -ErrorAction SilentlyContinue | ForEach-Object {if(-not $_.WaitForExit(30000)){throw 'PocketLink has not finished releasing resources.'}}
    if(Get-Process obs64 -ErrorAction SilentlyContinue){throw 'Another OBS instance is running. Close it before an isolated comparison.'}
    $sources=@('src\*.cs','tests\*.cs')
    $defines=@()
    $label='final'
    if($Baseline){
        New-Item -ItemType Directory -Force .runtime/camera-baseline | Out-Null
        git archive --format=zip -o .runtime/camera-baseline.zip $BaselineRef src tests
        if($LASTEXITCODE -ne 0){throw 'Cannot export baseline sources.'}
        Expand-Archive -LiteralPath .runtime/camera-baseline.zip -DestinationPath .runtime/camera-baseline -Force
        $sources=@('.runtime\camera-baseline\src\*.cs','.runtime\camera-baseline\tests\*.cs')
        $defines=@('/define:BASELINE');$label='before'
    }
    $entry=if($Smoke){'CameraResourceSmoke'}else{'CameraLifecycleProbe'}
    $compiler="$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/main:$entry" "/out:build\$entry.exe" @defines @sources "diagnostics\$entry.cs"
    if($LASTEXITCODE -ne 0){throw 'Camera diagnostic compilation failed.'}
    & ".\build\$entry.exe" $label | Tee-Object -FilePath ".runtime/camera-$entry-$label.log"
    if($LASTEXITCODE -ne 0){throw 'Camera diagnostic failed. See .runtime logs.'}
} finally {Pop-Location}
