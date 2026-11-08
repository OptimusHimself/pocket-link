# 摄像头后端调查

调查日期：2026-11-08。分支：`codex/investigate-camera-backend`，基于 `main` 的 `c36adc6`。当前只增加调查文件与只读能力探测脚本，不修改 `src/`、启动入口、构建脚本、音频链路或现有 OBS 摄像头链路。

## 调查次序与通过条件

1. 检查手机原生 UVC 与 Windows 接口能力，记录事实和未知项。
2. 在独立实验目录实现生成色条的 Windows 虚拟摄像头，不接入正式控制台。先验证注册、取帧、启停和删除，再分别由用户在腾讯会议、钉钉验证设备可选、画面正常。接口存在不能代替兼容性测试。
3. 色条原型通过后，才接入手机 H.264，解码为原生视频帧，支持旋转、镜像和前后置切换；避免 JPEG 和浏览器中转。先允许复用现有解码器，再比较 Media Foundation 解码，不同时替换全部组件。
4. 使用相同手机、分辨率、目标帧率和统计方法，与现有基线比较 CPU、工作集、启动/停止时间；补测实际帧率及画面延迟。没有测量前不承诺内存目标。
5. 调查分支给出采用/放弃结论。即使原型成功，也不直接删除现有链路或合并主分支；后续集成保留现有 OBS 后端作为回退，等待明确选择。

## 本机只读检查结果

复现：`./diagnostics/Inspect-CameraCapabilities.ps1`。脚本不修改 USB 功能、相机注册、系统默认音频设备，也不安装组件。运行时会使用项目专属 ADB 服务枚举手机；运行数据如需保存应放在被 Git 忽略的 `.runtime/`。

| 项目 | 结果 | 能推断什么 |
| --- | --- | --- |
| 已连接手机 | 荣耀 ELZ-AN20，Android 14 | 本轮只调查这一台；LineageOS 手机未连接 |
| `ro.usb.uvc.enabled` | 空值 | 未发现 AOSP 开启标记，不能据此断言硬件完全不支持 |
| 已安装包中 webcam/deviceas 匹配 | 0 | 未发现标准 DeviceAsWebcam 包；厂商实现仍可能使用其他名称 |
| 当前 USB 功能 | MTP、HISUITE、MASS_STORAGE | 当前没有启用 UVC；未尝试切换 USB 模式 |
| Windows | build 26200.8875，25H2 | 注册表 ProductName 仍含 Windows 10 旧名称，按实际 build 判断系统代际 |
| 系统 DLL 的 `MFCreateVirtualCamera` 导出 | 存在 | 系统具备该 API 入口，不代表媒体源已实现或会议兼容性通过 |
| 本轮开发工具探测 | PATH 未发现 cl/cmake，常见 Windows Kits Include 目录和 vswhere 未发现 | 尚未确定可用的 C++/Windows SDK 工具链；未安装全局工具 |

原生 UVC 路线：暂不能作为当前荣耀机型的默认方案。还需要检查手机 USB 设置是否提供“网络摄像头”选项；若用户连接 LineageOS 手机，再运行同一只读探测。Android 版本号本身不足以证明支持。

### 第二台手机：LineageOS 补充检查

2026-11-08 用户换接 PJX110 后，只读检查得到：

| 项目 | 结果 |
| --- | --- |
| 型号 / 系统 | PJX110 / Android 16 / LineageOS 23.2 |
| DeviceAsWebcam 包 | `com.android.DeviceAsWebcam` 存在，系统包，版本 16，用户 0 已安装且未禁用 |
| `ro.usb.uvc.enabled` | 空值 |
| 当前 `sys.usb.config` | `adb`，当前没有 UVC |
| 内核只读配置 | `CONFIG_USB_F_UVC=y`、`CONFIG_USB_CONFIGFS=y`、`CONFIG_USB_CONFIGFS_F_UVC=y`，UVC gadget 相关支持已编译 |
| USB 配置目录读取 | shell 权限不足，未尝试 root 或修改权限 |
| `cmd usb get-functions` | 此 ROM 未实现该 shell 命令，不能据此判断 UVC 支持情况 |
| 实机菜单与视频输出 | 检查时手机锁屏，已请用户查看 USB 用途是否有 Webcam；尚未切换或验收 |

结论：该手机有原生 UVC 路线的服务组件，且内核配置包含相关 UVC gadget 支持，但这不代表 USB HAL、设置入口和实际输出全部可用。暂不能确认其可用，也不能因属性为空就认定完全不支持。下一步以用户可见的 Webcam 设置入口和 Windows 是否枚举到 UVC 相机作为实机证据。

Windows 路线：优先调查 Media Foundation 软件虚拟摄像头及自定义媒体源。官方示例的最低版本是 Windows 11 build 22000。它仍需要实现 COM 媒体源、帧格式协商、时间戳、帧请求和清理，不是调用一个注册函数就能把任意字节流当摄像头。

下一阶段最小原型应优先选择仅当前用户、仅当前会话的虚拟摄像头生命周期，退出时释放并移除实验设备；媒体源的 COM 部署与清理也必须单独验证。接口支持当前用户范围不代表所有部署步骤天然无需系统配置。

目前没有创建虚拟摄像头，没有验证腾讯会议或钉钉的新后端，没有改变生产传输结构，也没有取得新方案的性能数字。

## 技术方向比较

| 路线 | 主要组件 | 调查结论 |
| --- | --- | --- |
| 当前 OBS 后端 | ADB/scrcpy → H.264 → FFmpeg/JPEG → HTTP/Canvas/CEF → OBS Virtual Camera | 已工作，保留作为稳定参照 |
| 手机原生 UVC | 手机系统 UVC → Windows 摄像头栈 | 开销潜力好，但依赖手机 ROM，当前荣耀证据不足 |
| Windows MF 虚拟摄像头 | ADB/scrcpy → 解码/图像变换 → 自定义 MF 媒体源 → 虚拟摄像头 | 本机 API 可用，值得做独立色条原型；会议兼容性未知 |
| DirectShow 输出过滤器 | 解码/图像变换 → 自定义 DirectShow filter | 作为兼容性备选；不同时开发两套原型 |

调查重点是减少浏览器与 OBS 中转，而非重写扬声器、麦克风或整个 UI。用户从 PowerShell 启动、托盘常驻、USB 连接和三项资源独立开关的需求保持为后续方案约束。

## 官方资料

- [Android DeviceAsWebcam](https://source.android.com/docs/core/camera/webcam)：ROM 需启用系统属性并集成服务；支持选择前后镜头。
- [MFCreateVirtualCamera](https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera)：软件相机、自定义媒体源 CLSID、生命周期、用户范围及权限说明。
- [Microsoft Windows-Camera VirtualCamera 示例](https://github.com/microsoft/Windows-Camera/blob/master/Samples/VirtualCamera/README.md)：最低 OS 和媒体源示例实现。
- [Frame Server Custom Media Source](https://learn.microsoft.com/en-us/windows-hardware/drivers/stream/frame-server-custom-media-source)：媒体源与 Frame Server 的集成要求。
- [DirectShow 视频捕获设备](https://learn.microsoft.com/en-us/windows/win32/directshow/about-video-capture-devices)：微软建议新代码尽可能采用 Media Foundation。

## 版本隔离

当前稳定代码：`main` / `c36adc6`。调查分支只追加独立文件；恢复稳定代码可用 `git switch main`。本轮没有切换或重启正在运行的 PocketLink，也没有重编译覆盖 `build/PocketLink.exe`。
