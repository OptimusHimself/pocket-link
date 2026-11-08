param([switch]$SummarizeOnly)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $SummarizeOnly) {
        if (Get-Process PerformanceProbe -ErrorAction SilentlyContinue) { throw 'A performance probe is already running.' }
        if (Test-Path build/PocketLink.exe) {
            & .\build\PocketLink.exe --exit
            Get-Process PocketLink -ErrorAction SilentlyContinue | ForEach-Object {
                if (-not $_.WaitForExit(30000)) { throw 'PocketLink is still releasing resources.' }
            }
        }
        $compiler = "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
        & $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /main:PerformanceProbe /out:build\PerformanceProbe.exe src\*.cs tests\*.cs diagnostics\PerformanceProbe.cs
        if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
        Start-Process -FilePath "$PSScriptRoot/build/PocketLink.exe" -WindowStyle Hidden
        & .\build\PerformanceProbe.exe | Tee-Object -FilePath .runtime/performance-run.log
        if ($LASTEXITCODE -ne 0) { throw 'Performance probe failed. See .runtime/performance-run.log.' }
    }
    $rows = Import-Csv .runtime/performance-samples.csv
    $summary = foreach ($phase in ($rows | Group-Object phase)) {
        $processes = $phase.Group | Group-Object pid
        $duration = ($processes | ForEach-Object { ($_.Group | Measure-Object seconds -Sum).Sum } | Measure-Object -Maximum).Maximum
        $cores = [int]$phase.Group[0].logical_cpus
        $cpu = ($phase.Group | Measure-Object cpu_ms -Sum).Sum / ($duration * 1000 * $cores) * 100
        $memory = ($processes | ForEach-Object { ($_.Group | Measure-Object working_mb -Average).Average } | Measure-Object -Sum).Sum
        [pscustomobject]@{Phase=$phase.Name;Seconds=[math]::Round($duration,1);CPUPercent=[math]::Round($cpu,2);WorkingSetMB=[math]::Round($memory,1)}
    }
    $summary | Export-Csv .runtime/performance-summary.csv -NoTypeInformation
    $summary | Format-Table -AutoSize
} finally { Pop-Location }
