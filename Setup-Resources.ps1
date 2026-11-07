param([switch]$InstallMicrophone)
$ErrorActionPreference = 'Stop'
$tools = Join-Path $PSScriptRoot 'tools'
New-Item -ItemType Directory -Force $tools | Out-Null
if ($InstallMicrophone) {
    # Driver installation is an explicit user action; do not change audio defaults.
    $archive = Join-Path $tools 'vb-cable.zip'
    $folder = Join-Path $tools 'vb-cable'
    if (!(Test-Path $folder)) {
        Invoke-WebRequest 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip' -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $folder -Force
    }
    $installer = Get-ChildItem $folder -Filter '*Setup_x64.exe' -Recurse | Select-Object -First 1
    if (!$installer) { $installer = Get-ChildItem $folder -Filter '*Setup*.exe' -Recurse | Select-Object -First 1 }
    if (!$installer) { throw 'VB-CABLE installer was not found.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer.FullName
    if ($signature.Status -ne 'Valid') { throw 'VB-CABLE installer signature is not valid.' }
    Write-Host 'Install VB-CABLE in the installer. Windows may request administrator approval and a restart.'
    Start-Process -FilePath $installer.FullName -Verb RunAs -Wait
    exit
}
$scrcpy = Join-Path $tools 'scrcpy-win64-v5.0.1'
if (!(Test-Path (Join-Path $scrcpy 'scrcpy.exe'))) {
    $archive = Join-Path $tools 'scrcpy.zip'
    Invoke-WebRequest 'https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-win64-v5.0.1.zip' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
}
Write-Host 'Camera and microphone USB transport components are ready.'
Write-Host 'Camera also requires OBS Studio with OBS Virtual Camera.'
Write-Host 'For meeting microphone support, run: ./Setup-Resources.ps1 -InstallMicrophone'
