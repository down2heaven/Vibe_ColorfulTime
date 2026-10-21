# Commit 006 — test(docs): 端到端自检工具、演示数据、截图与项目文档

## 目标

让交付可验证、可复现：提供数据层自检、UI 端到端自检、演示数据与完整文档。

## 改动

- `tests/SimWorkbench.SmokeTest`（数据层自检，92 项）
  - 覆盖：默认状态/独立性、仿真任务全字段、筛选搜索、自定义状态与删除迁移保护、周期实例独立性/跳过/频率、日历同步与颜色、每日总结与关联、断点历史、Checklist 全流程与关联、设置/模块启停/恢复布局、重启持久化、备份导出/校验/合并导入/重复导入去重。
- `tools/SeedData`：生成代表性演示数据（七种仿真状态、逾期/临期、周期实例、三篇总结、主题日志 + 断点历史、三个清单与关联）；`--stats` 输出数据库统计，供 UI 自检比对。
- `tools/ui-smoke.ps1`（UI 端到端自检，31 项）
  - 重新生成数据 → 启动应用 → UIAutomation 点击/填写/切换 → 截图 → 关闭 → 数据库统计比对；覆盖新建任务/周期规则/主题/清单、导航入口隐藏恢复、模块停用启用、深色主题切换与持久化。
- `tools/screenshot-pages.ps1`、`tools/dev-run.ps1`：页面截图与开发启动脚本（UTF-8 BOM 脚本，兼容 Windows PowerShell 5.1）。
- 文档：
  - `README.md`：项目定位、功能一览、构建运行、数据位置、测试方式、架构、限制与版本记录。
  - `docs/ARCHITECTURE.md`：分层与依赖方向、关键类型、数据模型、模块与配置、周期任务实现要点、主题风格。
  - `docs/P0_CHECKLIST.md`：P0 功能清单与第 13 节验收标准逐条对照、测试结果、明确未完成项。
  - `docs/DEVELOPMENT.md`：环境、命令、测试体系、编码约定、发布。
  - `docs/PULL_REQUEST.md`：本批提交的 PR 描述。
  - `docs/commits/001~006-*.md`：每次提交的总结。
  - `docs/screenshots/`：README 使用的界面截图。

## 验证结果

```
dotnet build SimWorkbench.sln        → 0 警告 0 错误
数据层自检                            → 通过 92 项，失败 0 项
UI 端到端自检                         → 通过 31 项，失败 0 项
```

## 影响文件

`tests/**`、`tools/**`、`README.md`、`docs/**`
