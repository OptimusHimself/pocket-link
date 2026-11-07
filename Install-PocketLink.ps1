$ErrorActionPreference = 'Stop'
# Only a small per-user launcher is registered. Dependencies stay in the project.
$launcherFolder = Join-Path $env:LOCALAPPDATA 'PocketLink/bin'
New-Item -ItemType Directory -Force $launcherFolder | Out-Null
$escapedRoot = $PSScriptRoot.Replace("'", "''")
$launcher = "`$ErrorActionPreference = 'Stop'`r`n& '$escapedRoot/Start-Pocketlink.ps1'`r`n"
[IO.File]::WriteAllText((Join-Path $launcherFolder 'pocket-link.ps1'), $launcher, [Text.UTF8Encoding]::new($true))
@'
@echo off
powershell.exe -NoProfile -WindowStyle Hidden -File "%~dp0pocket-link.ps1"
'@ | Set-Content -LiteralPath (Join-Path $launcherFolder 'pocket-link.cmd') -Encoding ascii
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($null -eq $userPath) { $userPath = '' }
if (($userPath -split ';') -notcontains $launcherFolder) {
    [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $launcherFolder), 'User')
}
if (($env:Path -split ';') -notcontains $launcherFolder) { $env:Path += ';' + $launcherFolder }
# Tell Explorer about the user PATH change so newly opened terminals inherit it.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PocketLinkEnvironment {
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr SendMessageTimeout(IntPtr target,uint message,UIntPtr parameter,string text,uint flags,uint timeout,out UIntPtr result);
}
'@
$result = [UIntPtr]::Zero
[void][PocketLinkEnvironment]::SendMessageTimeout([IntPtr]0xffff,0x1a,[UIntPtr]::Zero,'Environment',2,5000,[ref]$result)
Write-Host 'PocketLink command registered. Open a new PowerShell and type pocket-link.'
Write-Host 'After moving or renaming the project folder, run this installer again.'
