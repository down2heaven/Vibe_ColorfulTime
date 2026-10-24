# 开发与验证指南

## 环境准备

| 依赖 | 版本 | 说明 |
| --- | --- | --- |
| Windows | 10 1809+ / 11 | WinUI 3 需要 Windows App Runtime 或自包含部署（本项目自包含） |
| .NET SDK | 8.0.x | 目标框架 `net8.0-windows10.0.19041.0` |
| Windows SDK | 10.0.19041+ | 通过 `Microsoft.Windows.SDK.BuildTools` NuGet 包提供 |
| Visual Studio | 2022（可选） | 便于调试 XAML；命令行构建不需要 IDE |

本项目通过 `WindowsAppSDKSelfContained=true` + `WindowsPackageType=None` 以**未打包自包含**方式输出，无需安装 Windows App Runtime，也无需 MSIX 打包即可运行。

## 常用命令

```powershell
# 构建解决方案（默认 Any CPU 解决方案配置，App 项目内部为 x64）
dotnet build SimWorkbench.sln

# 构建单个项目
dotnet build src\SimWorkbench.App\SimWorkbench.App.csproj -p:Platform=x64

# 运行
.\src\SimWorkbench.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\SimWorkbench.exe

# 指定便携数据目录
.\...\SimWorkbench.exe --data-dir <数据目录>
```

## 数据与日志

- 数据库：`%LOCALAPPDATA%\SimWorkbench\workbench.db`
- 备份：`%LOCALAPPDATA%\SimWorkbench\backups\`
- 日志：`%LOCALAPPDATA%\SimWorkbench\logs\app.log`（运行日志）与 `crash.log`（未处理异常）
- 目录不可写时自动回退：LocalAppData → 文档 → 程序目录 `data\`
- 测试时可用 `--data-dir` 或 `SIMWORKBENCH_DATA_DIR` 指向独立目录，避免污染真实数据

## 测试体系（本地工具，不随仓库分发）

自检与辅助工具（数据层 SmokeTest、UI 自动化、SeedData 演示数据、截图脚本、发布打包脚本）保留在开发者本地环境，
不纳入仓库；发布前由维护者执行并记录结果，验收标准见 `docs/P0_CHECKLIST.md`。

## 编码约定

- 依赖方向：`App → Data → Core`，`Core` 不引用任何实现层。
- 界面层不写 SQL、不直接读写文件；所有业务能力通过 `Core.Services` 接口调用。
- 页面只做交互与展示；展示所需的文案/颜色/可见性在 `ViewModels/RowModels` 或 `Common/UiUtil` 中统一处理。
- 时间统一以本地时间字符串 `yyyy-MM-ddTHH:mm:ss` / 日期 `yyyy-MM-dd` 落库，避免跨时区歧义。
- 用户可见文案使用中文，代码注释说明“为什么”，不写无信息量注释。
- 任何破坏性操作（删除任务/状态/主题/清单）都要有确认对话框并说明影响范围；删除使用中的状态必须先迁移任务。
- 新增模块：在 `Core.Logic.Defaults.CreateModuleDefinitions()` 注册，实现 `IsImplemented=true` 后才允许在设置页显示启用开关。

## 发布

```powershell
# 版本号：src\SimWorkbench.Core\Models\UiModels.cs → AppInfo.Version
dotnet publish src\SimWorkbench.App\SimWorkbench.App.csproj -c Release -p:Platform=x64
# 产物目录：src\SimWorkbench.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish
```

发布包为自包含 Windows App SDK 的未打包目录，解压后运行 `SimWorkbench.exe` 即可。
