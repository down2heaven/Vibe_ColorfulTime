# Commit 004 — feat(app): WinUI 3 应用外壳、导航、主题与编辑器对话框

## 目标

搭建可运行的 WinUI 3 应用骨架：启动与降级、服务装配、按用户设置构建导航、明暗主题、通用对话框与展示模型。

## 改动

- `App.xaml` / `App.xaml.cs`
  - 共享样式：卡片、页面大标题（轻量 + 字距）、色块标题、磁贴按钮、字段标签、任务行标题等。
  - 启动流程：初始化数据目录与服务、启动时补齐周期实例；数据服务失败时进入降级界面并在 InfoBar 显示原因（主程序仍能启动）。
  - 主题应用、未处理异常写 `logs/crash.log`、`--data-dir` 参数解析、`RequestLayoutChanged` 通知外壳重建导航。
- `MainWindow.xaml(.cs)`
  - `NavigationView` 左侧导航：按 `AppSettings.NavItems` 顺序与模块状态动态构建；未实现/停用的模块不出现。
  - 彩色模块图标、侧边栏底部外观切换按钮（跟随系统 → 浅色 → 深色循环，立即生效并持久化）。
  - 关闭当前页面时的同页刷新、停用模块页面回退到工作台、启动异常提示。
- `Common/`
  - `UiUtil`：颜色解析缓存、日期时间控件组合、卡片/徽标/空状态、模块主题色、Windows Phone 风格磁贴、强调色卡片、状态颜色条、统一按钮构造。
  - `AppLog`：运行日志（2MB 轮转）、`ModuleKeys`：模块 key 常量。
- `ViewModels/RowModels.cs`：`TaskRow`、`StatusChoice`、`RecurrenceRow`、`TopicRow`、`HistoryRow`、`SummaryRow`、`ChecklistProgressRow` —— 展示层投影（文案/画刷/可见性），页面不承担格式化逻辑。
- `Dialogs/`
  - `TaskEditorDialog`：新建/编辑任务，一般与仿真字段切换、状态按类型过滤、创建/计划开始/计划交付/实际交付（含“现在”）、计算所需时间（小时+分钟）、周期实例信息与“跳过本次执行”（二次确认）。
  - `RecurrenceListDialog` / `RecurrenceEditorDialog`：周期规则列表、新建/编辑、频率与间隔、每周星期、结束日期、删除范围选择。
  - `StatusEditorDialog`：名称 + 调色板 + 自定义颜色 + 预览。
  - `ChecklistLinkDialog`：勾选式关联清单。
  - `TextPromptDialog`：通用文本输入。

## 验证

- 应用可启动（标题、导航、页面切换正常），无数据目录写入权限时显示降级界面（截图留存）。
- 后续 UI 自检中，任务/周期/状态/清单对话框均通过真实点击验证。

## 影响文件

`src/SimWorkbench.App/App.xaml(.cs)`、`MainWindow.*`、`Common/**`、`ViewModels/**`、`Dialogs/**`、`tools/dev-run.ps1`
