# Commit 001 — chore(repo): 初始化解决方案、构建配置与忽略规则

## 目标

建立可构建、可扩展的三层解决方案骨架，并固定构建方式与产物忽略规则。

## 改动

- 新增 `SimWorkbench.sln`：包含 `src`（Core / Data / App）、`tests`（SmokeTest）、`tools`（SeedData）五个项目，解决方案平台映射中 App 固定为 x64。
- 新增 `Directory.Build.props`：统一 `LangVersion`、`Nullable`、`ImplicitUsings` 等编译设置。
- 项目文件：
  - `SimWorkbench.Core.csproj`：`net8.0`，无外部依赖；
  - `SimWorkbench.Data.csproj`：`net8.0` + `Microsoft.Data.Sqlite 8.0.32`；
  - `SimWorkbench.App.csproj`：`net8.0-windows10.0.19041.0`，WinUI 3（`Microsoft.WindowsAppSDK 1.8`）+ `WindowsAppSDKSelfContained` 未打包输出；
  - `SimWorkbench.SmokeTest.csproj` / `SeedData.csproj`：控制台自检与演示数据工具。
- 新增 `.gitignore`：忽略 `bin/`、`obj/`、本地测试数据、截图临时目录、日志与备份。
- 新增 `docs/commits/` 目录，用于保存每次提交的变更总结（本文件）。

## 验证

- `dotnet build SimWorkbench.sln` 通过（此时业务代码尚未提交，空项目可编译）。
- App 项目首次成功构建并启动，确认 WinUI 3 自包含未打包模式在目标机器可直接运行（无需安装 Windows App Runtime）。

## 影响文件

`SimWorkbench.sln`、`Directory.Build.props`、`src/*/*.csproj`、`tests/*/*.csproj`、`tools/SeedData/*.csproj`、`.gitignore`、`docs/commits/001-repo-scaffold.md`
