$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$adbPath = Join-Path $projectRoot 'tools/platform-tools/adb.exe'
$env:ADB_LIBUSB = '1'
function Invoke-AdbProbe {
    param([string[]]$ProbeArguments)
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName=$adbPath; $info.Arguments=('-P 15037 '+($ProbeArguments -join ' '))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $process=New-Object Diagnostics.Process
    $process.StartInfo=$info
    try {
        $null=$process.Start(); $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(15000)) { $process.Kill(); $process.WaitForExit(); throw 'ADB probe timed out; unlock or reconnect the phone and retry.' }
        $global:LASTEXITCODE=$process.ExitCode
        $stdout.Result -split '\r?\n' | Where-Object { $_ -ne '' }
    } finally { $process.Dispose() }
}

# Read-only capability probe. Does not register cameras or change USB modes.
if (-not ('PocketLinkResearch.NativeExports' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PocketLinkResearch {
    public static class NativeExports {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        public static bool HasVirtualCameraApi() {
            IntPtr module=LoadLibraryEx("mfsensorgroup.dll",IntPtr.Zero,0x800);
            if(module==IntPtr.Zero)return false;
            try{return GetProcAddress(module,"MFCreateVirtualCamera")!=IntPtr.Zero;}
            finally{FreeLibrary(module);}
        }
    }
}
'@
}
$os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
Write-Output "Windows build: $($os.CurrentBuild).$($os.UBR) ($($os.DisplayVersion))"
Write-Output "MFCreateVirtualCamera export available: $([PocketLinkResearch.NativeExports]::HasVirtualCameraApi())"
Write-Output 'This checks API availability only, not registration or meeting-app compatibility.'

$deviceLines = Invoke-AdbProbe -ProbeArguments @('devices')
if ($LASTEXITCODE -ne 0) { throw 'ADB device enumeration failed.' }
$serials = @($deviceLines | ForEach-Object { if ($_ -match '^(\S+)\s+device$') { $Matches[1] } })
foreach ($serial in $serials) {
    Write-Output "Device: $serial"
    foreach ($property in @('ro.product.model','ro.build.version.release','ro.lineage.build.version','ro.usb.uvc.enabled','sys.usb.config')) {
        $value = (Invoke-AdbProbe -ProbeArguments @('-s',$serial,'shell','getprop',$property)) -join ''
        if ($LASTEXITCODE -ne 0) { throw "Unable to read $property" }
        Write-Output "$property=$value"
    }
    $packages = Invoke-AdbProbe -ProbeArguments @('-s',$serial,'shell','pm','list','packages')
    if ($LASTEXITCODE -ne 0) { throw 'Package enumeration failed.' }
    $matches = @($packages | Select-String -Pattern 'webcam|deviceas')
    Write-Output "Webcam-related package matches: $($matches.Count)"
    $matches | ForEach-Object { Write-Output $_.Line }
    $kernel = Invoke-AdbProbe -ProbeArguments @('-s',$serial,'shell','zcat','/proc/config.gz')
    if ($LASTEXITCODE -eq 0) {
        $kernel | Select-String -Pattern '^CONFIG_USB_CONFIGFS_F_UVC=|^CONFIG_USB_F_UVC=|^CONFIG_USB_CONFIGFS=|^# CONFIG_USB_CONFIGFS_F_UVC' | ForEach-Object { Write-Output $_.Line }
    } else { Write-Output 'Kernel configuration unavailable; UVC kernel support remains unknown.' }
}
if ($serials.Count -eq 0) { Write-Output 'No authorized phone; phone-side investigation is incomplete.' }
