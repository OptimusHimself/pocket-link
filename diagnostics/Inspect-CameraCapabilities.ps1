$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$adbPath = Join-Path $projectRoot 'tools/platform-tools/adb.exe'
$env:ADB_LIBUSB = '1'

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

$deviceLines = & $adbPath -P 15037 devices
if ($LASTEXITCODE -ne 0) { throw 'ADB device enumeration failed.' }
$serials = @($deviceLines | ForEach-Object { if ($_ -match '^(\S+)\s+device$') { $Matches[1] } })
foreach ($serial in $serials) {
    Write-Output "Device: $serial"
    foreach ($property in @('ro.product.model','ro.build.version.release','ro.usb.uvc.enabled')) {
        $value = (& $adbPath -P 15037 -s $serial shell getprop $property) -join ''
        if ($LASTEXITCODE -ne 0) { throw "Unable to read $property" }
        Write-Output "$property=$value"
    }
    $packages = & $adbPath -P 15037 -s $serial shell pm list packages
    if ($LASTEXITCODE -ne 0) { throw 'Package enumeration failed.' }
    $matches = @($packages | Select-String -Pattern 'webcam|deviceas')
    Write-Output "Webcam-related package matches: $($matches.Count)"
    $matches | ForEach-Object { Write-Output $_.Line }
}
if ($serials.Count -eq 0) { Write-Output 'No authorized phone; phone-side investigation is incomplete.' }
