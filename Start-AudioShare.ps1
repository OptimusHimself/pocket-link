$env:ADB_LIBUSB = '1'
$env:ADB_SERVER_SOCKET = 'tcp:localhost:15037'
& "$PSScriptRoot/tools/platform-tools/adb.exe" -P 15037 start-server
Start-Process -FilePath "$PSScriptRoot/tools/AudioShare/audioshare.exe" -WorkingDirectory "$PSScriptRoot/tools/AudioShare"
