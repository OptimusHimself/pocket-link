param([string]$Destination='D:\my-tools\PocketLink',[switch]$RegisterCommand)
$ErrorActionPreference='Stop'
$sourceRoot=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\','/')
$releaseRoot=[IO.Path]::GetFullPath($Destination).TrimEnd('\','/')
if($releaseRoot -eq $sourceRoot -or $sourceRoot.StartsWith($releaseRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $releaseRoot -eq [IO.Path]::GetPathRoot($releaseRoot).TrimEnd('\')){throw 'Choose a dedicated release directory, not the source directory or a drive root.'}
$marker=Join-Path $releaseRoot 'pocketlink-release.json'
if(Test-Path -LiteralPath $releaseRoot){
    if(Test-Path -LiteralPath $marker){$previous=Get-Content -LiteralPath $marker -Raw|ConvertFrom-Json;if($previous.kind -ne 'PocketLinkRelease'){throw 'Destination is not a managed PocketLink release.'}}
    elseif(Get-ChildItem -LiteralPath $releaseRoot -Force|Select-Object -First 1){throw 'Destination contains unmanaged files; choose an empty dedicated folder.'}
}

# Build elsewhere so a running development copy remains available.
& "$sourceRoot/Build.ps1" -OutputDirectory '.runtime/publish-build'
$built=Join-Path $sourceRoot '.runtime/publish-build'
& "$built/PocketLink.Tests.exe" --self-test
if($LASTEXITCODE -ne 0){throw 'Release self-tests failed.'}

# Resolve Scoop shims to their actual executable so the copy is independent of the shim.
$decoder=Join-Path $sourceRoot 'tools/ffmpeg/ffmpeg.exe'
if(-not (Test-Path -LiteralPath $decoder)){
    $command=Get-Command ffmpeg.exe -ErrorAction Stop
    $decoder=$command.Source
    $shim=[IO.Path]::ChangeExtension($decoder,'.shim')
    if(Test-Path -LiteralPath $shim){$line=Get-Content -LiteralPath $shim|Where-Object {$_ -match '^path\s*=\s*"([^"]+)"'}|Select-Object -First 1;if($line -match '^path\s*=\s*"([^"]+)"'){$decoder=$Matches[1]}}
}
if(-not (Test-Path -LiteralPath $decoder)){throw 'FFmpeg executable was not found.'}

$targetExe=Join-Path $releaseRoot 'build/PocketLink.exe'
$running=Get-Process PocketLink -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq $targetExe}
if($running){& $targetExe --exit;foreach($process in $running){if(-not $process.WaitForExit(30000)){throw 'Installed PocketLink is still using its files.'}}}
New-Item -ItemType Directory -Path $releaseRoot -Force|Out-Null
$commit=(& git -C $sourceRoot rev-parse --short HEAD) -join ''
$dirty=[bool](& git -C $sourceRoot status --porcelain)
$release=[ordered]@{kind='PocketLinkRelease';source=$sourceRoot;commit=$commit;workingTreeChanges=$dirty;publishedUtc=[DateTime]::UtcNow.ToString('o');state='publishing'}
$release|ConvertTo-Json|Set-Content -LiteralPath $marker -Encoding UTF8
function Copy-ReleaseFile([string]$From,[string]$Relative){$target=Join-Path $releaseRoot $Relative;New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null;Copy-Item -LiteralPath $From -Destination $target -Force}
foreach($file in @('PocketLink.exe','PocketLink.Tests.exe','audio_capture.dll','Orbitron-OFL.txt')){Copy-ReleaseFile (Join-Path $built $file) ('build/'+$file)}
foreach($file in Get-ChildItem "$sourceRoot/tools/platform-tools" -File){Copy-ReleaseFile $file.FullName ('tools/platform-tools/'+$file.Name)}
Copy-ReleaseFile "$sourceRoot/tools/AudioShare/server" 'tools/AudioShare/server'
foreach($file in Get-ChildItem "$sourceRoot/tools/AudioShare" -File|Where-Object {$_.Name -match '^(vcruntime|msvcp|concrt).*\.dll$'}){Copy-ReleaseFile $file.FullName ('build/'+$file.Name)}
Copy-ReleaseFile "$sourceRoot/tools/scrcpy-win64-v5.0.1/scrcpy-server" 'tools/scrcpy-win64-v5.0.1/scrcpy-server'
Copy-ReleaseFile "$sourceRoot/tools/scrcpy-win64-v5.0.1/LICENSE.txt" 'tools/scrcpy-win64-v5.0.1/LICENSE.txt'
Copy-ReleaseFile $decoder 'tools/ffmpeg/ffmpeg.exe'
$decoderFolder=Split-Path $decoder -Parent
foreach($file in Get-ChildItem -LiteralPath $decoderFolder -Filter '*.dll' -File){Copy-ReleaseFile $file.FullName ('tools/ffmpeg/'+$file.Name)}
foreach($relative in @('scripts/Prepare-ObsRuntime.ps1','Setup-Resources.ps1','Install-PocketLink.ps1')){Copy-ReleaseFile (Join-Path $sourceRoot $relative) $relative}
if(-not (Test-Path -LiteralPath (Join-Path $releaseRoot '.runtime/devices.txt')) -and (Test-Path -LiteralPath "$sourceRoot/.runtime/devices.txt")){Copy-ReleaseFile "$sourceRoot/.runtime/devices.txt" '.runtime/devices.txt'}

# Release launcher never reads or compiles the development sources.
@'
$ErrorActionPreference='Stop'
Start-Process -FilePath "$PSScriptRoot/build/PocketLink.exe" -WorkingDirectory $PSScriptRoot
'@ | Set-Content -LiteralPath (Join-Path $releaseRoot 'Start-Pocketlink.ps1') -Encoding UTF8
@'
@echo off
start "" "%~dp0build\PocketLink.exe"
'@ | Set-Content -LiteralPath (Join-Path $releaseRoot 'PocketLink.cmd') -Encoding ascii
@'
PocketLink runtime folder. Source code and future development remain in C:\Projects\android-soundlink.
Launch PocketLink.cmd or use the registered pocket-link command.
OBS Studio and VB-CABLE remain installed on this computer. ADB/scrcpy/FFmpeg are included here.
Updates: run Publish-PocketLink.ps1 in the development project. Runtime settings/logs are preserved.
'@ | Set-Content -LiteralPath (Join-Path $releaseRoot 'READ-ME.txt') -Encoding UTF8

& "$releaseRoot/build/PocketLink.Tests.exe" --self-test
if($LASTEXITCODE -ne 0){throw 'Copied release self-tests failed.'}
$release.state='ready';$release|ConvertTo-Json|Set-Content -LiteralPath $marker -Encoding UTF8
if($RegisterCommand){& "$releaseRoot/Install-PocketLink.ps1"}
Write-Host "PocketLink published to $releaseRoot (commit $commit, working tree changes: $dirty)."
Write-Host 'Existing .runtime settings and logs were preserved; no development sources were moved.'
