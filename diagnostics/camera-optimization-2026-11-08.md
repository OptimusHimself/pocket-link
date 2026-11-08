# 现有摄像头链路优化 · 2026-11-08

分支：`codex/investigate-camera-backend`。保留 scrcpy → FFmpeg → JPEG/Canvas → OBS Virtual Camera，不替换为 UVC 或 Media Foundation，不合并 `main`。

## 改动

- 拆分 `CameraVideoInput`（单次手机采集）与 `CameraBridge`（预览服务、OBS、虚拟摄像头）。切换前后镜头只重建输入，保留 OBS PID 和预览 URL。连续选择只保留最新意图，取消摄像头仍释放所有组件。
- FFmpeg H.264 解码与 MJPEG 编码各限制为 2 个线程，保持 1280×720、输入目标 30 fps、JPEG 目标 20 fps、JPEG 质量参数 5。
- JPEG 管道由逐字节 OS 读取改为 32 KB 块读取，保留单个最新不可变帧和 4 MB 帧上限。HTTP 响应携带方向与镜像元数据，浏览器每轮从两次请求减为一次，解码位图在 finally 中释放。
- OBS 使用项目内 `.runtime/obs-runtime/config/obs-studio` 的便携配置，关闭自己的 UI 预览和退出确认。通过目录联接复用已安装的 `bin/core/data/obs-plugins`，不复制 OBS，也不修改普通 OBS 用户配置。
- 退出请求按本程序持有的 OBS PID 定位隐藏主窗口；正常关闭失败时仍保留有界强制结束兜底。视频输入等待期间可取消，清理异常也会结束资源状态，避免控制台永远显示停止中。

## 同机前后对比

荣耀 ELZ-AN20 / Android 14，12 个逻辑处理器。同一次工作阶段中分别编译 `main` 的 `c36adc6` 与优化分支，使用同一个 `CameraLifecycleProbe` 测试。每个前/后置阶段预热 5 秒，采样约 30.2 秒。基线在 10:43—10:44，最终版本在 10:49—10:51（北京时间）。画面内容和系统调度不是严格受控实验，因此差值是本机短期样本。

| 指标 | 主分支旧代码 | 最终优化版本 |
| --- | ---: | ---: |
| 前置：进程工作集合计 | 749.7 MB | 641.0 MB |
| 后置：进程工作集合计 | 748.3 MB | 651.0 MB |
| 前置：Private Bytes 合计 | 901.5 MB | 738.6 MB |
| 后置：Private Bytes 合计 | 900.4 MB | 744.7 MB |
| 前置：整机 CPU 占比 | 3.55% | 2.83% |
| 后置：整机 CPU 占比 | 2.53% | 2.10% |
| 前置初次启动 | 2231 ms | 2624 ms |
| 前置切换后置 | 5541 ms | 960 ms |
| 完全关闭摄像头 | 3054 ms | 329 ms |
| 切换时复用 OBS / URL | 否 / 否 | 是 / 是 |
| 完全关闭后剩余视频进程 | 无 | 无 |

前置工作集减少约 108.7 MB（14.5%），后置减少约 97.3 MB（13.0%）。第一次启动没有变快，首次准备便携配置也有开销，不能宣称所有启动阶段都已加速。OBS 与 CEF 仍是较大的固定开销，本轮没有降到几十 MB 的水平。

这里统计执行器、项目 ADB、FFmpeg（含 shim）、OBS 及 OBS 浏览器子进程，不含正式 GUI，因为测试期间 GUI 已退出。执行器承载生产摄像头代码，有少量测量开销；工作集会重复计算共享页，Private Bytes 也不是物理 RAM 占用。不能把本表与之前包含待机 GUI 的约 800 MB 基线直接相减。没有测 GPU、手机功耗、音画端到端延迟或精确实际输入帧率。

## 验证

- 编译和现有自检通过；新增测试覆盖 JPEG 分块边界、不完整尾帧、帧上限、旋转镜像随帧传输、清除旧帧、复用输出、连续切换和取消回调。
- 同机前后测试均取得 1280×720 JPEG；旋转与镜像状态检查通过。
- 使用生产 ResourceController/ResourceSession 同时开启扬声器、麦克风和摄像头。切换后保持同一 OBS PID/预览 URL，音频资源状态继续运行。
- 通过 FFmpeg DirectShow 实际读取 `OBS Virtual Camera` 两秒到 null 输出，检测到随时间变化的视频帧（`YDIF > 0`），未保存音视频文件。这证明虚拟输出有动态内容，不能代替腾讯会议/钉钉验收。
- 在再次切换前置输入已开始后取消摄像头，输入与 OBS 正常退出；扬声器、麦克风继续运行，随后 StopAll 成功。
- 最终运行后，普通 OBS 的 `user.ini` SHA-256 与运行前一致：`4B49191D0D8FA381F5D8B06FE510FACBE1485EF1EEC042D21862E87B18E43A2E`。确认仅操作独立便携配置。
- 最终生命周期测试未走强制退出兜底，且等待后未发现 FFmpeg/OBS/OBS 浏览器残留。
- 物理 USB 拔插、实际听感与腾讯会议/钉钉画面仍需后续实机验收，不在本轮通过项中。

## 复现与回退

```powershell
./Test-CameraLifecycle.ps1 -Baseline   # 独立导出并编译 c36adc6，不切换主分支
./Test-CameraLifecycle.ps1             # 优化版本，约 75 秒
./Test-CameraLifecycle.ps1 -Smoke      # 三项资源及动态虚拟输出，约 8 秒
```

脚本会停止 PocketLink 当前资源。不要同时运行其他 OBS/FFmpeg/ADB 工作负载，否则按进程名聚合的数据可能混入其他任务。日志、CSV、便携配置和基线源码副本都放在被忽略的 `.runtime/`，生产源码仍可逐文件比较。未增加全局依赖、虚拟设备或常驻控制端口。

回到稳定版本：先退出 PocketLink，再 `git switch main`，执行 `./Build.ps1` 后重新启动。原有 OBS 安装和普通用户配置保持可用，不必删除联接或运行目录来回退。
