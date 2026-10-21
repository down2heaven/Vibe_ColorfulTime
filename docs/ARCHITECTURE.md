# 架构说明

## 分层与依赖方向

```
┌────────────────────────────────────────────────┐
│ SimWorkbench.App（WinUI 3 界面层）              │
│  MainWindow / Pages / Dialogs / ViewModels      │
└───────────────┬────────────────────────────────┘
                │ 只依赖接口
┌───────────────▼────────────────────────────────┐
│ SimWorkbench.Core（业务层）                     │
│   Models / Services（接口）/ Logic（纯计算）     │
└───────────────▲────────────────────────────────┘
                │ 实现接口
┌───────────────┴────────────────────────────────┐
│ SimWorkbench.Data（数据访问层）                 │
│  SQLite schema+迁移 / 各 Service 实现 / 备份     │
│  WorkbenchServices（组合根）                    │
└────────────────────────────────────────────────┘
```

- `Core` 不引用 `Data`，也不引用任何 UI 程序集；只包含数据模型、服务契约、纯业务计算与内置默认数据。
- `Data` 依赖 `Core`，实现所有服务；SQLite 访问、事务、迁移、备份都在这一层，界面不出现 SQL。
- `App` 依赖 `Core` 与 `Data`（仅通过 `WorkbenchServices` 组合根获取服务实例），页面不直接访问数据库/文件。
- 依赖方向单向：`App → Data → Core`，没有反向引用，便于未来替换存储或增加 AI/网络 API 模块。

## 关键类型

| 层 | 类型 | 职责 |
| --- | --- | --- |
| Core.Models | `TaskItem` / `WorkStatus` | 仿真任务与一般任务共用模型；状态支持自定义名称/颜色/排序 |
| Core.Models | `RecurrenceRule` / `RecurrenceInstance` | 周期规则与“每次执行一个独立任务”的实例记录 |
| Core.Models | `DailySummary` / `TopicLog` / `TopicLogHistory` | 每日总结、主题日志与断点历史 |
| Core.Models | `Checklist` / `ChecklistItem` / `EntityLink` | 清单、检查项与跨模块关联 |
| Core.Models | `AppSettings` / `ModuleDefinition` / `ModuleRuntimeState` | 界面偏好、模块定义与运行时状态 |
| Core.Logic | `RecurrenceCalculator` | 周期规则匹配与实例日期计算（纯函数，可单测） |
| Core.Logic | `Defaults` / `Presentation` | 内置七种仿真状态、模块/卡片注册表；统一的展示文案格式化 |
| Data | `WorkbenchDatabase` | 建库、`PRAGMA user_version` 迁移、日期/空值映射 |
| Data | `TaskService` … `BackupService` | 各服务实现（每个服务只操作自己负责的表） |
| Data | `WorkbenchServices` | 组合根：创建数据库并装配全部服务 |
| App | `MainWindow` | 导航外壳：按用户设置构建导航、主题切换、页面切换 |
| App | `Pages/*` | 页面：只负责界面与交互，业务调用服务接口 |
| App | `Dialogs/*` | 任务/周期/状态/清单关联/文本输入等对话框 |
| App | `ViewModels/RowModels` | 展示投影（如 `TaskRow`），把模型转换为绑定所需的文案/画刷/可见性 |

## 数据模型（SQLite，schema v1）

| 表 | 说明 | 关键约束 |
| --- | --- | --- |
| `work_statuses` | 状态集合（scope 区分仿真/一般） | `id` 主键；内置状态 `is_protected=1` |
| `tasks` | 任务（含仿真专属列与周期实例列） | `status_id` 外键；`recurrence_id` 外键（删除规则时置空） |
| `recurrence_rules` | 周期规则 | 频率/间隔/星期/开始结束日期 |
| `recurrence_instances` | 周期实例 | `UNIQUE(rule_id, occurrence_date)` 保证同一天只生成一次 |
| `daily_summaries` | 每日总结 | `date` 唯一，同一天只有一份可编辑记录 |
| `daily_summary_tasks` | 总结↔任务关联 | 复合主键 |
| `topic_logs` / `topic_log_history` | 主题日志与断点历史 | 历史表 `ON DELETE CASCADE` |
| `checklists` / `checklist_items` | 清单与检查项 | 检查项随清单级联删除 |
| `entity_links` | 通用关联（清单 ↔ 任务/总结/主题/周期） | 唯一约束避免重复关联 |
| `settings` | JSON 设置（`app.settings`） | 键值表，升级时自动与注册表对齐 |

数据库位置默认 `%LOCALAPPDATA%\SimWorkbench\workbench.db`，启用 WAL；结构变更通过 `PRAGMA user_version` 迁移，旧数据升级后可读。

## 模块与可配置项

- 模块注册表在 `Core.Logic.Defaults` 中静态定义，包含核心模块（任务）与可选模块（日历、每日总结、主题日志、Checklist），以及尚未实现、只能显示为“不可用”的 AI 助手与网络 API 模块。
- 用户设置（`AppSettings`）持久化：主题、默认首页、列表密度预留、导航入口显隐与顺序、首页卡片显隐与顺序、模块启用状态、状态集合共享开关。
- 隐藏入口只影响导航/卡片显示；停用模块只影响界面可用性与后续后台工作，**不会删除任何数据**。
- 已实现模块的停用通过 `IModuleService.SetEnabled` 生效；未实现模块的开关在 UI 上不渲染，避免“假开关”。

## 周期任务实现要点

1. 规则保存后，`IRecurrenceService.EnsureMaterialized(from, to)` 按规则计算区间内每个执行日期。
2. 每个执行日期创建一条**真实任务行**，并写入 `recurrence_instances`（`task_id` 指向该任务）。
3. 因 `UNIQUE(rule_id, occurrence_date)`，重复补齐不会产生重复实例；被“跳过”的日期以 `is_skipped=1` 记录，不再生成。
4. 完成某个实例只是修改该任务行，不会影响其他实例；修改规则会重建“今天及以后”的实例并保留历史实例。

## UI 主题与风格

- `App.xaml` 定义卡片、字体层级、磁贴等共享样式；`UiUtil` 提供模块主题色、磁贴、强调色卡片、状态颜色条等构造函数。
- 每个模块有固定主题色（任务 `#2B88D8`、日历 `#7A3FD1`、每日总结 `#12A5A5`、主题日志 `#C239B3`、Checklist `#2EA043`），用于导航图标、页面标题色块、首页卡片色条。
- 状态颜色是任务状态的唯一视觉标识：列表条目文字下方显示 5–6px 状态颜色条，日历条目、详情徽标使用同一颜色。
- 明暗主题：`AppThemeMode.System/Light/Dark`，侧边栏底部按钮循环切换，设置页也可选择，选择持久化。
