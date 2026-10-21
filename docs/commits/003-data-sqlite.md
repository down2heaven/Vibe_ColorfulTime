# Commit 003 — feat(data): SQLite 架构、迁移、业务服务实现与备份

## 目标

实现 Core 中所有服务接口的 SQLite 持久化版本，并保证旧数据升级、备份/导入、重启后数据一致。

## 改动

- `WorkbenchDatabase.cs`
  - 数据目录解析（`--data-dir` / `SIMWORKBENCH_DATA_DIR` → LocalAppData → 文档 → 程序目录回退）与可写性探测。
  - 连接管理（WAL、外键开启）、`PRAGMA user_version` 升级迁移、内置状态与默认设置种子。
  - 统一的日期/时间/可空值映射（`yyyy-MM-dd` / `yyyy-MM-ddTHH:mm:ss`）。
- schema v1：`work_statuses`、`tasks`、`recurrence_rules`、`recurrence_instances`（唯一约束防重复实例）、`daily_summaries`、`daily_summary_tasks`、`topic_logs`、`topic_log_history`、`checklists`、`checklist_items`、`entity_links`、`settings`。
- `TaskService`：多条件筛选/搜索/排序、按日期区间取任务、状态迁移、分类/标签聚合。
- `StatusService`：状态 CRUD、排序、删除影响评估与迁移保护（使用中禁止静默删除）。
- `RecurrenceService`：规则 CRUD、区间实例补齐、跳过执行、按范围删除、修改规则时重建“今天及以后”实例并保留历史。
- `CalendarService`：月视图 6×7 网格、按状态着色、计划开始/交付/周期实例三类条目。
- `DailySummaryService`：同日唯一保存、最近记录、搜索、关联任务。
- `TopicLogService`：主题查询、更新时自动归档旧断点/旧内容到历史、历史查询与删除。
- `ChecklistService`：清单与检查项 CRUD、排序、进度、正/反向关联查询。
- `SettingsService`（同时实现 `ISettingsService`/`IModuleService`）：设置 JSON 持久化、与注册表对齐、模块运行时状态计算、导航/卡片顺序与显隐、恢复默认布局。
- `BackupService`：全量 JSON 导出（含设置）、备份校验、合并（跳过重复、状态缺失自动映射）与替换（清空业务数据后导入）两种导入模式。
- `WorkbenchServices`：组合根，负责创建数据库并装配服务。

## 修复记录（自检驱动）

- 首页卡片顺序在设置保存后被注册表顺序覆盖 → 改为保留用户排序、仅补新增卡片。
- 备份导入期间跨连接读取状态导致 SQLite 写锁 → 在事务前读取状态并合并导入状态集合。
- 任务引用周期规则但先于规则插入导致外键失败 → 调整导入顺序为 状态 → 规则 → 任务 → 实例。
- `TaskStatus` 与 BCL 类型冲突 → 改名 `WorkStatus`（参见 002）。

## 验证

- `tests/SimWorkbench.SmokeTest`：通过 92 项、失败 0 项，覆盖状态/任务/周期/日历/总结/断点/清单/设置/备份/重启持久化/重复导入去重。

## 影响文件

`src/SimWorkbench.Data/**`、`src/SimWorkbench.Core/Services/Interfaces.cs`（反向关联方法）
