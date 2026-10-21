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

# 构建并启动（自动清理旧进程）
powershell -ExecutionPolicy Bypass -File tools\dev-run.ps1

# 指定便携数据目录
.\...\SimWorkbench.exe --data-dir D:\WorkbenchData
```

## 数据与日志

- 数据库：`%LOCALAPPDATA%\SimWorkbench\workbench.db`
- 备份：`%LOCALAPPDATA%\SimWorkbench\backups\`
- 日志：`%LOCALAPPDATA%\SimWorkbench\logs\app.log`（运行日志）与 `crash.log`（未处理异常）
- 目录不可写时自动回退：LocalAppData → 文档 → 程序目录 `data\`
- 测试时可用 `--data-dir` 或 `SIMWORKBENCH_DATA_DIR` 指向独立目录，避免污染真实数据

## 测试体系

1. **数据层自检** `tests/SimWorkbench.SmokeTest`
   - 使用临时数据库，覆盖状态、任务、周期实例、日历、每日总结、断点历史、Checklist、设置/模块、备份导入导出、重启持久化。
   - 运行：`dotnet build tests\SimWorkbench.SmokeTest\SimWorkbench.SmokeTest.csproj` 后执行 `tests\...\bin\Debug\net8.0\SimWorkbench.SmokeTest.exe`。
   - 预期：`通过 92 项，失败 0 项`，退出码 0。

2. **UI 端到端自检** `tools/ui-smoke.ps1`
   - 重新生成演示数据 → 启动真实程序 → UIAutomation 点击导航/按钮/填写输入框 → 截图 → 关闭程序后用数据库校验落库结果。
   - 覆盖：新建任务、周期规则、主题日志、清单；隐藏/恢复导航入口；停用/启用模块；明暗主题切换；重新打开后数据存在。
   - 预期：`通过 31 项，失败 0 项`。截图输出在 `docs/ui-test/`。

3. **页面截图** `tools/screenshot-pages.ps1`：自动遍历各页面并保存到 `docs/shots/`（README 使用的截图已挑选到 `docs/screenshots/`）。

4. **演示数据** `tools/SeedData`：生成覆盖七种仿真状态、逾期/临期、周期实例、总结、断点历史与清单的代表性数据，便于人工验收。

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
