param([Parameter(Mandatory=$true)][string]$ObsRoot,[Parameter(Mandatory=$true)][string]$RuntimeRoot)
$ErrorActionPreference='Stop'
$ObsRoot=(Resolve-Path -LiteralPath $ObsRoot).Path
$RuntimeRoot=[IO.Path]::GetFullPath($RuntimeRoot)
if(-not (Test-Path -LiteralPath (Join-Path $ObsRoot 'bin/64bit/obs64.exe'))){throw 'Invalid OBS installation.'}
New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null
# Reference installed binaries without copying or altering them. Never remove existing paths.
foreach($name in @('bin','core','data','obs-plugins')){
    $source=Join-Path $ObsRoot $name
    if(-not (Test-Path -LiteralPath $source)){continue}
    $link=Join-Path $RuntimeRoot $name
    if(Test-Path -LiteralPath $link){
        $item=Get-Item -LiteralPath $link -Force
        if($item.LinkType -ne 'Junction' -or [IO.Path]::GetFullPath([string]$item.Target) -ne [IO.Path]::GetFullPath($source)){throw "Unexpected existing OBS runtime path: $link"}
    }else{New-Item -ItemType Junction -Path $link -Target $source | Out-Null}
}
[IO.File]::WriteAllText((Join-Path $RuntimeRoot 'source.txt'),$ObsRoot)
