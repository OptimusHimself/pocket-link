# SoundLink：USB 手机资源控制台

一个脚本启动，选择一台 USB 手机，按需启用扬声器、摄像头和麦克风。

```powershell
./Start-SoundLink.ps1
```

## 使用

- 听音乐：默认仅勾选扬声器，传输完整立体声。不会启动摄像头、麦克风或 OBS。
- 视频会议：预选摄像头和麦克风，也可独立勾选。点击“启动所选资源”后才开始采集。
- 停止连接或关闭窗口，释放本次手机采集，并关闭本程序启动的 OBS 实例。
- 腾讯会议、钉钉中，摄像头选 OBS Virtual Camera，麦克风选 CABLE Output。
- 控制台的麦克风输出选 CABLE Input。手机不需要连接 Wi-Fi。
- 会议软件中的画面和输入音量条仍需实际检查；程序不会自动加入会议、直播或录制媒体。

## 首次配置

扬声器使用现有的项目内 AudioShare 和 ADB。摄像头与麦克风传输使用 scrcpy 5.0.1：

```powershell
./Setup-Resources.ps1
```

摄像头要求 Android 12+、OBS Studio 及已注册的 OBS Virtual Camera。本机 OBS 在 D:/obs-studio。
程序创建专用的 SoundLink 场景集合和配置，不修改其他场景；配置文件位于 OBS 用户目录。
支持前后镜头，预设 1280×720 / 30 fps，机型不支持时会报告错误。

麦克风要求 Android 11+，并需要 VB-CABLE 虚拟音频线。它是非开源的 Donationware。
已尝试调查开源虚拟音频驱动；当前实现选择 VB-CABLE 明确支持的播放端到录音端回环。
控制台“配置会议麦克风”按钮运行：

```powershell
./Setup-Resources.ps1 -InstallMicrophone
```

脚本下载官方安装包并验证签名，再打开安装器。用户需完成管理员授权和安装；官方提示可能需要重启。
安装后点击刷新设备。未安装时，可继续听歌，或取消麦克风后只用摄像头。
虚拟音频驱动必须注册到 Windows，无法仅保存在项目目录。程序不修改系统默认输入或输出。
同时用手机扬声器和麦克风时，会议软件的回声消除效果需实测。

## 范围与验收

- 已移除双手机分流、延迟补偿、响度校准、声道分析及其界面和测试。
- 每次只选一台手机。所有手机媒体流经 USB；不上传媒体或开放局域网流媒体服务。
- ADB 使用项目专属端口 15037、ADB_LIBUSB=1，不修改 PATH。关闭窗口后后台 ADB 可继续运行。
- 设备选择保存在 .runtime/devices.txt；依赖和构建产物不提交 Git。
- Start-AudioShare.ps1 仍可启动原版扬声器工具；原版会自行缓存 ADB 到用户临时目录。
- 扬声器此前通过网易云实机试听；本次控制台通过编译和逻辑自检。
- 摄像头会议预览、麦克风输入音量条及实际设备释放仍待实机验收。VB-CABLE 尚未安装。

## 开发

```powershell
./Build.ps1
./build/SoundLink.exe --self-test
```

使用 Windows 已有 .NET Framework 编译器，无需全局安装 SDK。
MainForm 管理页面；ResourceSession 管理资源生命周期；Relay 转发单手机完整音频；
CameraBridge 管理 scrcpy 和专用 OBS 实例；MicrophoneBridge 与 WaveOutput 把手机 PCM 送入虚拟音频线。
Adb 和 Audio 提供 USB 与音频协议。

来源与许可证：
- AudioShare 2.2.2：https://github.com/ysbing/AudioShare/releases/tag/v2.2.2 （third_party/AudioShare）
- scrcpy 5.0.1：https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1 （下载包内 Apache-2.0 许可证）
- OBS Virtual Camera：https://obsproject.com/kb/virtual-camera-guide
- VB-CABLE：https://vb-audio.com/Cable/
