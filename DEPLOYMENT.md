# 开发目录与运行目录

- 开发源码：`C:\Projects\android-soundlink`。继续在当前工作空间修改和提交 Git。
- 日常运行：`D:\my-tools\PocketLink`。直接打开 `PocketLink.cmd`，或在新 PowerShell 输入 `pocket-link`。

更新日常版本，在开发目录执行：

```powershell
./Publish-PocketLink.ps1 -RegisterCommand
```

脚本使用当前工作区编译（包括尚未提交的界面改动），构建前后运行自检。只复制程序、必要脚本、项目 ADB、scrcpy 服务端和真实 FFmpeg 二进制；不复制源码、Git、开发资料、旧日志和 OBS 目录联接。运行目录已有的 `.runtime` 设置、日志与 OBS 配置保留；首次发布只迁移 `devices.txt` 中的设备和镜头偏好。

OBS Studio 和 VB-CABLE 继续使用本机已安装的组件。运行目录第一次开启摄像头时创建自己的 OBS 便携配置。

更新前若 D 盘版本正在运行，脚本会先请求退出并等待清理；不会因 C 盘开发版本正在运行而强制停止它。程序采用同一用户单实例策略，因此第一次改用 D 盘时，请先从托盘退出 C 盘版本，再运行 `pocket-link`，否则只会唤回当前 C 盘实例。

`pocketlink-release.json` 记录源目录、提交号、工作区是否有未提交改动、发布时间和发布状态。发布目录不应手动修改源码；修复在 C 盘完成，再发布。发布脚本不使用镜像删除，不会清理运行目录中已有的设置和日志。
