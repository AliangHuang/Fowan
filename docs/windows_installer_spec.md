# Fowan Windows 安装包规范

## 目标

Fowan Windows 安装包用于把工具箱和随包工具部署到其他 Windows 机器。首版安装包采用离线全量 `.exe` 安装器，自带 .NET Runtime 和 Windows App SDK 依赖，用户不需要在新电脑上手动安装运行库。

首版不使用 MSIX，不做在线依赖下载，也不内置代码签名流程。正式外发前应对安装包进行 Authenticode 签名，降低 Windows SmartScreen 拦截概率。

## 安装器形态

- 安装器类型：Inno Setup 生成的管理员级 `.exe`。
- 默认安装范围：所有用户。
- 默认安装目录：`{autopf}\Fowan`，通常是 `C:\Program Files\Fowan`。
- 安装向导必须显示安装目录选择页，允许用户自定义安装目录。
- 安装向导必须显示隐私协议页，用户同意后才能继续安装。
- 安装向导必须询问是否创建桌面快捷方式，默认勾选创建。
- 安装器必须随包携带并静默安装 Microsoft Visual C++ Redistributable x64；如果目标机器已安装 x64 v14 运行库，可以跳过安装。
- 自包含发布时 WinUI 项目不得启用 Windows App SDK bootstrapper；必须依赖随应用发布的 Undocked RegFree WinRT 初始化，避免干净机器因缺少系统级 Windows App Runtime 在进入应用代码前失败。
- 第一版只生成 `win-x64` 安装包。

## 程序目录布局

安装后的程序文件必须保持以下结构：

```text
{app}\
  Fowan.Windows.exe
  ReleaseNotes\
    release-notes.txt
    toolbox.md
    todo.md
  Tools\
    Todo\
      Fowan.Todo.Windows.exe
      Fowan.Todo.Sticky.Windows.exe
```

主工具箱通过 `{app}\Tools\Todo\Fowan.Todo.Windows.exe` 启动 Todo 工具。Todo 主窗口和 Sticky 窗口必须在同一个工具目录下，以便 Sticky 能返回 Todo 主窗口。

程序文件不得写入用户业务数据。安装目录只用于应用二进制、资源、运行库和安装器辅助脚本。

`ReleaseNotes` 目录只包含当前安装包版本的用户可见更新日志。完整历史 changelog 保留在仓库内，不随安装包全量发布。

## 版本和更新日志

- 工具箱和每个随包内部工具都必须有独立版本号。
- 当前 Windows 首版允许工具箱和 Todo 使用同一个安装包版本号；后续工具需要独立节奏时，工具模型和 changelog 必须能分别记录。
- 每个组件必须维护仓库内 changelog：
  - 工具箱：`changelogs/toolbox/CHANGELOG.md`
  - Todo：`changelogs/tools/todo/CHANGELOG.md`
- changelog 必须按 `## <version> - <date>` 分段记录。打包脚本使用 `-Version` 参数抽取对应版本段落。
- 打包脚本只能把当前 `-Version` 对应段落写入 `ReleaseNotes` staging 目录；不得把完整 changelog 历史放进安装包。
- 如果某个随包组件缺少当前版本的 changelog 段落，打包必须失败，避免发布没有更新说明的安装包。
- 最终安装包生成后，打包脚本必须在同一目录生成 `fowan-update.json`，供工具箱从 GitHub Release 检查自动更新。
- `fowan-update.json` 必须包含 `version`、`channel`、`installerUrl`、`installerSha256`、`releaseNotesUrl` 和 `notes` 字段；稳定版发布使用 `channel: stable`。

## 用户数据规范

工具箱和所有工具的数据统一归属于每个 Windows 用户的 Fowan 数据根：

```text
%LOCALAPPDATA%\Fowan
```

安装器、卸载器和后续工具不得把工具箱数据、Todo 数据或其他工具数据拆开处理。备份、保留和清理都必须以整个 `%LOCALAPPDATA%\Fowan` 目录作为数据单元。

当前已知数据布局：

```text
%LOCALAPPDATA%\Fowan\
  client-settings.json
  Todo\
    todo-data.json
    todo-settings.json
```

后续新增工具必须使用 `%LOCALAPPDATA%\Fowan\<ToolName>` 或同一根目录下的明确子目录。不得把用户数据保存到安装目录、构建输出目录或临时目录。

## 隐私协议

安装器使用 `installer/windows/privacy-zh-CN.txt` 作为首版隐私协议。协议至少说明：

- Fowan 会在本机保存工具箱设置、工具数据、头像选择、诊断信息和相关本地状态。
- 当前版本默认不主动上传本地数据，不包含远程同步、账号云服务或遥测。
- 如果后续加入 AI、同步、云服务或遥测，必须更新协议并在产品内提供相应说明。
- 默认卸载只删除程序并保留用户数据。
- 干净卸载会先备份全部 Fowan 用户数据，再删除原始数据。

## 快捷方式规则

开始菜单快捷方式：

- 安装时始终创建 `Fowan` 开始菜单快捷方式。
- 快捷方式指向 `{app}\Fowan.Windows.exe`。
- 卸载时始终删除开始菜单快捷方式和空的开始菜单文件夹。

桌面快捷方式：

- 安装时询问是否创建桌面快捷方式，默认创建。
- 所有用户安装模式下，桌面快捷方式创建在公共桌面。
- 桌面快捷方式文件名固定为 `Fowan.lnk`。
- 卸载时默认检查并删除公共桌面上的 `Fowan.lnk`。
- 卸载时也尽力检查当前用户桌面上的旧版 `Fowan.lnk` 并删除。
- 如果快捷方式不存在，卸载继续进行，不报错。
- 首次安装、更新和重装完成页默认勾选“运行 Fowan”；用户可以取消，静默安装不得自动启动工具箱。
- 完成页启动工具箱时必须使用原始登录用户的普通权限并显示主界面，不得继承安装器管理员权限或使用 `--start-hidden`。

## 开机自启动规则

- 首次安装时询问是否在登录 Windows 后自动启动 Fowan，默认勾选。
- 自启动按当前用户生效，使用 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `Fowan` 值。
- 自启动命令必须使用 `--start-hidden`，只初始化隐藏窗口和托盘图标，不得闪现主窗口或任务栏按钮。
- 升级和重装必须保留已有自启动状态；旧版本没有自启动注册表项时保持关闭。
- 工具箱设置页必须可以即时开关同一个注册表项；卸载时删除该项。

## 更新规则

安装器必须使用稳定 `AppId` 写入标准卸载注册表。新版安装包双击运行时：

- 自动检测是否已有 Fowan 安装。
- 发现旧版本时提示用户是否更新到当前版本。
- 用户选择更新时，复用已安装目录并覆盖程序文件；用户数据保持不变。
- 用户确认更新后，安装器必须展示当前安装包版本的更新日志。
- 首次安装不得展示更新日志提示。
- 用户拒绝更新时，直接退出安装器。
- 发现相同版本或更高版本时，提示用户选择重新安装或退出。
- 更新前先通过 Windows Restart Manager 请求应用正常退出，再强制清理安装目录内仍在运行的 `Fowan.Windows.exe`、`Fowan.Todo.Windows.exe`、`Fowan.Todo.Sticky.Windows.exe` 和 `Fowan.Diary.Windows.exe` 进程树。
- 强制清理必须同时校验完整进程名和可执行文件路径；不得终止安装目录以外的同名进程。仍有残留时必须中止覆盖并提示用户手动退出。
- 安装器更新日志必须由打包脚本写为 UTF-8，并使用支持 UTF-8 的 Unicode 文本接口读取，不能经过 ANSI 字符串转换。

## 卸载规则

默认卸载：

- 删除程序文件。
- 删除开始菜单快捷方式。
- 删除公共桌面 `Fowan.lnk` 和当前用户桌面旧版 `Fowan.lnk`。
- 删除卸载注册项。
- 保留所有用户的 `%LOCALAPPDATA%\Fowan` 数据。

干净卸载：

- 用户必须在卸载时主动选择干净卸载。
- 卸载器枚举本机正常用户配置目录下的 `AppData\Local\Fowan`。
- 卸载器先把所有找到的 Fowan 数据按用户名分组复制到临时备份目录。
- 卸载器把备份压缩到公共桌面：

```text
C:\Users\Public\Desktop\Fowan_UserData_Backup_<yyyyMMdd_HHmmss>.zip
```

- 只有压缩包创建成功后，才删除各用户的 `AppData\Local\Fowan` 原始目录。
- 如果备份失败，卸载器必须保留原始用户数据，并提示用户干净卸载的数据清理没有执行。
- 如果没有找到任何 Fowan 用户数据，卸载器继续卸载程序并提示未发现可备份的数据。

## 打包命令

从仓库根目录运行：

```powershell
.\scripts\package-windows.ps1 -Version 0.2.0
```

成功发布后，版本目录只保留安装包、免安装压缩包、更新清单和 SHA-256 校验清单：

```text
publish\windows\win-x64\0.2.0\FowanSetup-0.2.0-win-x64.exe
publish\windows\win-x64\0.2.0\Fowan-0.2.0-portable.zip
publish\windows\win-x64\0.2.0\fowan-update.json
publish\windows\win-x64\0.2.0\SHA256SUMS.txt
```

如果当前机器没有安装 Inno Setup 编译器，正式发布整体失败；脚本清理隔离 staging，且不创建不完整的版本目录。

发布前脚本按版本号检查 `publish\windows\win-x64\`，最多保留最新四个版本。为新版本腾出位置时，最旧版本会先移入隔离 staging；只有新版本原子写入成功后才永久清理，失败会自动恢复旧版本。

打包脚本在 `build/staging/` 中生成应用树和更新日志，再将更新日志同时放入安装包和免安装压缩包。发布目录不保留应用树、运行库或安装器 staging。

```text
Fowan-0.2.0-portable\app\ReleaseNotes\release-notes.txt
Fowan-0.2.0-portable\app\ReleaseNotes\toolbox.md
Fowan-0.2.0-portable\app\ReleaseNotes\todo.md
Fowan-0.2.0-portable\app\ReleaseNotes\diary.md
Fowan-0.2.0-portable\app\ReleaseNotes\report.md
Fowan-0.2.0-portable\app\ReleaseNotes\ai-chat.md
Fowan-0.2.0-portable\app\ReleaseNotes\ai-config.md
```

GitHub Release 自动更新发布要求：

- Release tag 使用 `v<version>`，例如 `v0.1.1`。
- Release 必须公开可读，客户端不内置 GitHub token。
- Release assets 必须包含 `FowanSetup-<version>-win-x64.exe` 和 `fowan-update.json`。
- 工具箱默认读取 `https://github.com/AliangHuang/Fowan/releases/latest/download/fowan-update.json`。
- 客户端必须校验 `installerSha256` 后才能启动安装器。

打包脚本会从 Microsoft 官方固定链接下载 Visual C++ Redistributable x64 到隔离 staging；免安装压缩包保留该运行库和安装说明：

```text
Fowan-0.2.0-portable\prerequisites\vc_redist.x64.exe
```

下载文件必须通过 Authenticode 签名校验，签名发布者必须是 Microsoft Corporation。安装器会把该运行库打入最终 setup exe。

## 验收场景

- 干净 Windows 10 19041+ 或 Windows 11 机器上安装成功，无需手动安装 .NET Runtime 或 Windows App SDK。
- 干净机器缺少 Visual C++ Redistributable 时，安装器自动静默安装该运行库。
- 安装时必须同意隐私协议。
- 安装时可以自定义安装目录。
- 安装时可以选择创建或不创建桌面快捷方式。
- 安装后开始菜单可以启动工具箱，工具箱可以启动 Todo，Todo 可以启动 Sticky。
- 新版本安装包可以识别旧版本并询问是否更新。
- 工具箱启动后可以通过公开 GitHub Release 检查稳定版更新，用户可立即更新、忽略当前版本、稍后提醒或禁用自动检查。
- 默认卸载删除程序和快捷方式，但保留所有用户数据。
- 干净卸载先在公共桌面生成备份 zip，再删除所有用户的 Fowan 数据。
