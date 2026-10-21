# Commit 002 — feat(core): 领域模型、服务契约与周期规则计算

## 目标

建立与界面、存储完全解耦的业务层：定义领域模型、业务服务接口、纯逻辑计算和内置默认数据。

## 改动

- `Models/TaskModels.cs`：`TaskKind`、`Priority`、`StatusScope`、`WorkStatus`、`TaskItem`（含仿真专属字段与周期实例字段）、`TaskQuery`、`TaskSortField`、`StatusDeleteImpact`。
- `Models/RecurrenceModels.cs`：`RecurrenceFrequency`、`RecurrenceRule`（含 `Clone`、星期集合、频率文案）、`RecurrenceInstance`、`RecurrenceDeleteScope`。
- `Models/JournalModels.cs`：`DailySummary`（同日唯一、预览）、`TopicLog`、`TopicLogHistory`。
- `Models/ChecklistModels.cs`：`Checklist`（进度计算）、`ChecklistItem`、`LinkOwnerTypes`、`EntityLink`。
- `Models/CalendarModels.cs`：`CalendarItem` / `CalendarDay`（月历展示模型）。
- `Models/UiModels.cs`：`AppThemeMode`、`HomeCardDefinition`、`ModuleDefinition`/`ModuleRuntimeState`、`AppSettings`、`PageKeys`/`ModuleIds`/`HomeCardIds`/`AppInfo`。
- `Services/Interfaces.cs`：全部服务契约（任务、状态、周期、日历、每日总结、主题日志、清单、设置/模块、备份）以及备份结果 DTO。
- `Logic/RecurrenceCalculator.cs`：每日/每周/每月 + 间隔 + 指定星期 + 结束日期 + 月末回退的日期匹配与区间枚举。
- `Logic/Defaults.cs`：七种仿真状态（待进行、正在处理、计算中、计算完成、后处理完成、计算中止、正在修复问题）与三种一般状态的内置定义；模块与首页卡片注册表；默认布局。
- `Logic/Presentation.cs`：时间/日期/优先级/类型/逾期提示等统一文案格式化。
- `Backup/BackupPayload.cs`：备份 JSON 容器。

## 关键设计决定

- `TaskStatus` 与 `System.Threading.Tasks.TaskStatus` 冲突，模型命名为 `WorkStatus`，避免全仓库别名。
- 状态按 `StatusScope` 分为仿真/一般两个集合；通过设置项允许一般任务复用仿真状态集合（满足“状态集合可共享”）。
- “计算完成”与“后处理完成”、“计算中止”与“正在修复问题”是四个独立状态记录，不共用 ID。
- 周期的日期计算保持纯函数，便于后续替换调度实现或单测。

## 验证

- `dotnet build SimWorkbench.sln` 通过。
- 后续数据层自检直接验证了默认状态数量、名称与独立性，以及周期规则的星期/间隔/结束日期行为。

## 影响文件

`src/SimWorkbench.Core/**`
