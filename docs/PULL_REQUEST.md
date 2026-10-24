# Pull Request：P0 功能实现——仿真工程师个人工作台

## 标题

`feat: P0 仿真工程师个人工作台（WinUI 3 + SQLite，多彩 UWP 风格）`

## 概述

本 PR 交付需求文档中 P0 的全部功能：任务（仿真/一般）、自定义状态、日历、周期任务独立实例、每日总结、主题日志与断点历史、Checklist、工作台首页卡片自定义、界面显隐与模块开关、主题（明暗）切换、本地持久化与备份导入导出。

界面采用 **UWP / Windows Phone 风格**：轻量大标题 + 模块主题色块、纯色强调磁贴、每个模块固定主题色、任务条目文字下方的加粗状态颜色条；支持浅色 / 深色 / 跟随系统即时切换。

## 动机与背景

需求是“面向仿真工程师的 Windows 桌面个人工作台”。原环境只有 VS 2022 Build Tools 与 Windows SDK，没有 .NET SDK 也没有既有项目，因此本 PR 同时包含：

1. 构建环境准备（用户目录安装 .NET 8 SDK，未改系统级环境）；
2. 从零搭建三层解决方案（Core / Data / App），不引入 EF 等重型 ORM，使用轻量手写 SQLite 访问层；
3. P0 全部功能与数据/UI 双端自检。

## 主要改动

### 1. 解决方案与构建

- `SimWorkbench.sln` + `Directory.Build.props`：三个主项目 + 测试/工具项目。
- `SimWorkbench.App` 以 `WindowsPackageType=None` + `WindowsAppSDKSelfContained=true` 输出，免安装运行时、免 MSIX 打包。

### 2. Core（业务层，无 UI/存储依赖）

- 领域模型：任务、状态、周期规则/实例、每日总结、主题日志/断点历史、清单/检查项、跨模块关联、界面设置与模块定义。
- 服务契约：`ITaskService`、`IStatusService`、`IRecurrenceService`、`ICalendarService`、`IDailySummaryService`、`ITopicLogService`、`IChecklistService`、`ISettingsService`/`IModuleService`、`IBackupService`。
- 纯逻辑：`RecurrenceCalculator`（每日/每周/每月 + 间隔 + 星期 + 结束日期 + 月末回退）、`Defaults`（七种仿真状态与三种一般状态、模块/首页卡片注册表）、`Presentation`（统一文案）。

### 3. Data（SQLite 数据访问层）

- `WorkbenchDatabase`：schema v1、`PRAGMA user_version` 迁移、WAL、日期/空值映射、内置数据种子。
- 各服务实现：任务筛选/排序、状态迁移删除保护、周期实例补齐与跳过、按状态着色的月历、每日总结唯一日、断点历史自动归档、清单进度与关联、设置持久化、JSON 备份导入（合并去重 / 替换）。
- `WorkbenchServices` 组合根，供界面层单点获取全部服务。

### 4. App（WinUI 3 界面层）

- 外壳：`NavigationView` 按用户设置动态构建导航，彩色模块图标，侧边栏底部“跟随系统/浅色/深色”一键循环切换，启动异常降级界面（显示原因，不阻挡启动）。
- 页面：工作台（WP 风格磁贴 + 可配置强调色卡片）、任务（列表+详情+筛选+周期任务管理）、日历（月视图 + 当日清单）、每日总结、主题日志/断点、Checklist、设置（外观/布局/导航/首页卡片/状态管理/模块管理/备份/关于）。
- 对话框：任务编辑器（仿真专属字段、周期实例跳过）、周期规则列表与编辑、状态编辑（调色板 + 自定义色）、Checklist 关联、通用文本输入。
- 展示模型：`TaskRow` 等把业务模型转换为绑定所需的文案与颜色，页面不写业务规则。

### 5. 测试与工具

- 自检工具（数据层 SmokeTest、UI 自动化、演示数据、截图脚本）保留在开发者本地环境，不随仓库分发。

## 测试计划

| 验证 | 命令 | 预期 |
| --- | --- | --- |
| 构建 | `dotnet build SimWorkbench.sln` | 0 警告 0 错误 |
| 数据层自检 | 本地 SmokeTest（不随仓库分发） | 通过 92 项，失败 0 项 |
| UI 端到端 | 本地 UI 自动化（不随仓库分发） | 通过 31 项，失败 0 项 |
| 手工验收 | 启动程序后按 `docs/P0_CHECKLIST.md` 逐条检查 | 截图见 `docs/screenshots/` |

验收标准与实现的逐条对照见 `docs/P0_CHECKLIST.md`。

## 截图

- 工作台：`docs/screenshots/workbench.png`
- 任务（状态颜色条在文字下方）：`docs/screenshots/tasks.png`
- 深色外观：`docs/screenshots/tasks-dark.png`
- 日历：`docs/screenshots/calendar.png`
- 每日总结 / 主题日志 / Checklist / 设置：`docs/screenshots/*.png`

## 风险与限制

- 数据层使用手写 SQL，schema 变更需新增迁移步骤（已在 `WorkbenchDatabase` 预留 `user_version` 迁移结构）；后续大改动建议引入迁移脚本清单。
- 周期任务“修改本次及以后”目前通过重建今天及以后实例实现，历史实例保留；更细粒度编辑属 P1。
- 未实现的 AI 助手与网络 API 模块在 UI 中显示为“不可用（未实现）”，不提供假开关（符合需求“避免假功能”）。
- 数据层为同步 SQLite 调用，对个人量级数据足够；若未来数据量显著增加，可在服务层加异步封装。

## 提交内容（按 commit 组织）

1. `chore(repo)`: 解决方案与构建配置
2. `feat(core)`: 领域模型、服务契约、周期规则计算
3. `feat(data)`: SQLite 架构、迁移与业务服务
4. `feat(app)`: WinUI 3 外壳、导航、主题、对话框
5. `feat(app)`: 功能页面与多彩 UWP 风格界面
6. `test(docs)`: 自检工具、截图与项目文档

每个 commit 的详细说明位于 `docs/commits/`。
