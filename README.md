# PocketLink：USB 手机资源控制台

一个脚本启动，选择一台 USB 手机，按需启用扬声器、摄像头和麦克风。

```powershell
./Start-Pocketlink.ps1
```

## 使用

- 听音乐：默认仅勾选扬声器，传输完整立体声。不会启动摄像头、麦克风或 OBS。
- 视频会议：预选摄像头和麦克风，也可独立勾选。点击“启动所选资源”后才开始采集。
- 关闭所有资源会释放采集；关闭主窗口只隐藏到托盘。托盘右键“退出 PocketLink”才会释放资源并退出。
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
- 扬声器此前通过网易云实机试听；本次控制台通过编译和逻辑自检。
- 本机已安装 VB-CABLE；已从 CABLE Output 测得手机麦克风信号，并在腾讯会议视频设置中确认手机实时画面。

## 开发

```powershell
./Build.ps1
./build/PocketLink.Tests.exe --self-test
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

## 腾讯会议使用注意

先在控制台启动视频会议资源，再在腾讯会议中选择 OBS Virtual Camera 和 CABLE Output。
控制台可隐藏到托盘；手机摄像头预览和对应 OBS 需保持运行。关闭预览只影响摄像头，不连带关闭麦克风或扬声器。
会议扬声器应选显示器、耳机等实际播放设备，不要选 CABLE Input 或 VB-Audio Virtual Cable，否则会议声音会进入虚拟麦克风。
“入会开启摄像头”默认未勾选时，进入会议后需手动开启视频。
诊断日志保存在 .runtime/session.log，仅含状态和组件日志，不录制媒体。

摄像头默认采用竖持方向（270°），可在镜头旁选择 90°、270°、0° 或 180°。更改前先断开连接，重新启动时生效；选择保存在项目运行配置中。竖屏按比例适配会议画面，不拉伸人物。

## 托盘常驻第一版

首次在项目目录运行 `./Install-PocketLink.ps1`，向用户 PATH 注册一个小启动器。
之后新开 PowerShell，输入 `pocket-link`；启动命令返回后可关闭 PowerShell。
它不安装 Windows 服务，也不设置开机自启。ADB、scrcpy 等仍保存在项目目录。
项目文件夹移动或重命名后，重新运行安装脚本更新入口。

- 单实例：重复命令唤回原窗口；窗口 X 隐藏到托盘，双击托盘打开。
- 右键托盘可独立启停扬声器、摄像头、麦克风，关闭所有资源，或退出。
- 使用过资源后，勾选框的改变会立即应用；初次启动可点击“应用资源选择”。
- 听音乐和视频会议预设会应用对应资源组合；不用退出程序重新启动。
- 各资源独立运行：新增资源、单项失败或关闭不会停止其他正常资源。
- USB 插拔采用 Windows 设备通知和阻塞式 ADB track-devices 订阅，1200 ms 合并事件，不周期性启动 adb devices。
- 拔出所选手机会停止采集和本程序管理的 OBS、scrcpy，保留托盘及轻量设备订阅。
- 插回后重新识别、待命；摄像头和麦克风不会自动恢复。勾选“听歌意外断开后自动重连”才会恢复同一序列号手机的扬声器。
- 退出会等资源清理完成，停止设备订阅，并关闭专属端口 15037 的 ADB；不影响其他端口的 ADB。
- 控制台隐藏后的一个 8 秒无设备待机采样中，进程 CPU 时间增量为 0 ms；这是一次采样，不代表所有电脑上的固定占用。

开发诊断命令：`build/PocketLink.exe --show`、`--hide`、`--exit`。
自动测试覆盖单实例之外的资源独立启停、单项失败隔离、停止期间重新启用、重复启用防重和 StopAll 清理。

## 麦克风与扬声器的路由隔离

- Windows 默认播放出口、会议/游戏的扬声器：选显示器或耳机等普通播放端。PocketLink 从该端采集电脑声音并发送到手机。
- 手机麦克风：仅写入虚拟音频线；会议/游戏的输入选 CABLE Output。
- 不把 CABLE Input 或 VB-Audio Virtual Cable 设置为默认播放出口。否则麦克风信号可能被再次采集到手机扬声器，也可能让其他应用声音进入会议输入。
- 扬声器和麦克风启动前检查默认播放出口；若发现它指向麦克风虚拟通道，拒绝启动并提示修正。
- 麦克风开始输出前重新检查设备 ID 确实对应虚拟音频线，避免热插拔后旧编号误指向普通扬声器。
- 此隔离解决数字路由回环。手机外放被同机麦克风拾到属于声学回声，效果还取决于终端和会议软件的回声消除，不能以路由隔离测试代替声学实测。

开发诊断：`build/PocketLink.Tests.exe --audio-route` 显示当前默认播放出口。
