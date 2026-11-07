# SoundLink：双安卓手机 USB 立体声

将 Windows 系统音频采集一次：单手机播放完整立体声，双手机分别播放左、右声道。
使用 AudioShare 2.2.2 的开源采集 DLL 和免安装 Android 播放组件。

## 使用

1. 一台或两台手机通过 USB 接入电脑，开启 USB 调试并允许本电脑。
2. 在 PowerShell 运行 `./Start-Stereo.ps1`。
3. 仅一台设备时自动选择“单手机模式（完整立体声）”；两台时选择左右手机并开始播放。
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
- SoundLink 使用项目专属 ADB 端口 15037，避免误用其他工具在 5037 启动的非兼容 USB 后端；认证密钥通常由 ADB 放在用户 `.android` 目录。退出窗口会结束音频连接，ADB 服务保持后台运行。
- 启动脚本检测源码更新时间；源码更新后会重新编译，不再静默使用旧程序。
- SoundLink 不启动原版 Flutter 界面，因此自身不会额外复制 ADB 到临时目录。
- 旧版 AudioShare 界面仍会自行使用 `%TEMP%/ysbing/AudioShare/adb` 缓存。
- 上游许可证及来源见 `third_party/AudioShare/`，其二进制保持独立可替换。

## 验收

2026-11-07：两台设备均有活动的 48 kHz 单声道播放轨道，持续接收同一源的左右声道。
用户确认网易云音乐“两台正常，没有明显回声”。左右测试音的实际听感待确认。

普通歌曲只能验证能否出声；单声道或左右内容接近的歌曲不能验收声道隔离。
双手机声道验收须用“左右声道测试”：左低音时右手机必须安静，右高音时左手机必须安静。
单手机模式会播放两种测试音；这不能代替双手机声道隔离验收。

连接修复：已复现 5037 设备列表为空、15037 使用 libusb 能识别荣耀手机。
新版单手机 48 kHz 立体声轨道已在手机端检测到活动；双手机修复后的复验待第二台设备接入。
