$env:ADB_LIBUSB = '1'
& "$PSScriptRoot/tools/platform-tools/adb.exe" start-server
Start-Process -FilePath "$PSScriptRoot/tools/AudioShare/audioshare.exe" -WorkingDirectory "$PSScriptRoot/tools/AudioShare"
