# SoundLink：双安卓手机 USB 立体声

将 Windows 系统音频采集一次，左声道发送给一台手机，右声道发送给另一台。
使用 AudioShare 2.2.2 的开源采集 DLL 和免安装 Android 播放组件。

## 使用

1. 两台手机通过 USB 接入电脑，开启 USB 调试并允许本电脑。
2. 在 PowerShell 运行 `./Start-Stereo.ps1`。
3. 选择左、右手机，点击“开始双声道”，然后在电脑播放网易云音乐。
4. 点击“左右声道测试”：依次为左低音、静音、右高音、静音、左低音。
5. 若有回声，停止后给较早发声的手机增加延迟补偿（0–500 ms），再开始。
6. 停止按钮或关闭窗口会关闭本次连接和本次专用隧道，不停止共享 ADB 服务。

当前配置：荣耀 ELZ_AN20 在左，PJX110 在右。手机各自媒体音量控制响度。
两台手机的硬件时钟及播放缓冲独立；延迟补偿是手工调整，不保证采样级同步。
任一设备断开会停止整组传输；重新插入后刷新设备并重新开始。
音乐中间出现的静音是正常音频内容，不触发超时退出。

## 构建与验证

使用 Windows 已有的 .NET Framework 4.x C# 编译器，无需全局安装 SDK：

```powershell
./Build.ps1
./build/SoundLink.exe --self-test
```

源代码位于 `src/SoundLink.cs`。可执行程序和下载内容不提交 Git。
单手机旧版保留：`./Start-AudioShare.ps1`。

## 依赖及范围

- `tools/AudioShare/`：解压自 https://github.com/ysbing/AudioShare/releases/tag/v2.2.2
- `tools/platform-tools/`：Google Android platform-tools 37.0.1，https://developer.android.com/tools/releases/platform-tools
- SoundLink 直接使用项目内 ADB；`ADB_LIBUSB=1` 仅对进程树有效，不修改 PATH。
- ADB 的 5037 后台服务与其他安卓工具共享，认证密钥通常由 ADB 放在用户 `.android` 目录。
- SoundLink 不启动原版 Flutter 界面，因此自身不会额外复制 ADB 到临时目录。
- 旧版 AudioShare 界面仍会自行使用 `%TEMP%/ysbing/AudioShare/adb` 缓存。
- 上游许可证及来源见 `third_party/AudioShare/`，其二进制保持独立可替换。

## 验收

2026-11-07：两台设备均有活动的 48 kHz 单声道播放轨道，持续接收同一源的左右声道。
用户确认网易云音乐“两台正常，没有明显回声”。左右测试音的实际听感待确认。
