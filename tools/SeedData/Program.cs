using SimWorkbench.Core.Models;
using SimWorkbench.Data;

namespace SimWorkbench.SeedData;

/// <summary>为界面自测生成一套代表性数据（任务、周期、总结、日志、清单）。</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--stats")
        {
            var statsDir = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "seeddata");
            var dbFile = Path.Combine(statsDir, "workbench.db");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var svc = WorkbenchServices.Create(dbFile);
            Console.WriteLine($"任务={svc.Tasks.Query(new TaskQuery { Limit = 100000 }).Count}");
            Console.WriteLine($"仿真状态={svc.Statuses.GetByScope(StatusScope.Simulation).Count}");
            Console.WriteLine($"一般状态={svc.Statuses.GetByScope(StatusScope.General).Count}");
            Console.WriteLine($"周期规则={svc.Recurrences.GetAll().Count}");
            Console.WriteLine($"每日总结={svc.DailySummaries.GetRecent(10000).Count}");
            Console.WriteLine($"主题日志={svc.TopicLogs.Query(null, null, null).Count}");
            Console.WriteLine($"断点历史={svc.TopicLogs.GetRecent(1000).Sum(t => svc.TopicLogs.GetHistory(t.Id).Count)}");
            Console.WriteLine($"清单={svc.Checklists.GetAll().Count}");
            Console.WriteLine($"清单项={svc.Checklists.GetAll().Sum(c => c.Items.Count)}");
            Console.WriteLine($"隐藏导航={svc.Settings.Load().NavItems.Count(n => !n.IsVisible)}");
            Console.WriteLine($"主题={svc.Settings.Load().Theme}");
            var backupDir = Path.Combine(statsDir, "backups");
            Console.WriteLine($"备份文件={Directory.GetFiles(statsDir, "*.json", SearchOption.AllDirectories).Length}");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            return 0;
        }

        var dataDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "seeddata");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "workbench.db");

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(dataDir, "workbench.db*"))
        {
            File.Delete(file);
        }

        var services = WorkbenchServices.Create(dbPath);
        var sim = services.Statuses.GetByScope(StatusScope.Simulation).ToDictionary(s => s.Name);
        var general = services.Statuses.GetByScope(StatusScope.General).ToDictionary(s => s.Name);
        var today = DateTime.Today;

        var created = new List<TaskItem>();

        TaskItem AddTask(string title, TaskKind kind, WorkStatus status, DateTime? planned, DateTime? due,
            DateTime? actual = null, string? category = null, string? tags = null, Priority priority = Priority.Normal,
            string? caseName = null, string? condition = null, int? estimated = null, string? calcNotes = null,
            string? description = null, DateTime? createdAt = null)
        {
            var task = services.Tasks.Create(new TaskItem
            {
                Title = title,
                Kind = kind,
                StatusId = status.Id,
                PlannedStart = planned,
                DueAt = due,
                ActualDeliveryAt = actual,
                Category = category,
                Tags = tags,
                Priority = priority,
                CaseName = caseName,
                CaseCondition = condition,
                EstimatedMinutes = estimated,
                CalculationNotes = calcNotes,
                Description = description,
                CreatedAt = createdAt ?? DateTime.Now.AddDays(-10),
            });
            created.Add(task);
            return task;
        }

        // 仿真任务：覆盖七种状态
        AddTask("机翼气动仿真 - 工况 A", TaskKind.Simulation, sim["计算中"],
            today.AddDays(-1).AddHours(9), today.AddHours(20), category: "气动", tags: "CFD;关键",
            priority: Priority.High, caseName: "CASE-A-001", condition: "Ma=0.6, Alt=10km", estimated: 330,
            calcNotes: "网格 320 万，SST k-omega，收敛良好", description: "验证巡航升阻比");
        AddTask("机翼气动仿真 - 工况 B", TaskKind.Simulation, sim["待进行"],
            today.AddHours(14), today.AddDays(2).AddHours(18), category: "气动", tags: "CFD",
            priority: Priority.Normal, caseName: "CASE-A-002", condition: "Ma=0.8, Alt=8km", estimated: 240);
        AddTask("整机结构强度复核", TaskKind.Simulation, sim["正在处理"],
            today.AddDays(-2).AddHours(9), today.AddDays(3).AddHours(18), category: "结构", tags: "FEA;复核",
            priority: Priority.High, caseName: "STR-2024-07", condition: "极限载荷工况 1.5g", estimated: 480,
            calcNotes: "接触设置待确认");
        AddTask("热防护分析（已完成计算）", TaskKind.Simulation, sim["计算完成"],
            today.AddDays(-6).AddHours(9), today.AddDays(-1).AddHours(18), category: "热", tags: "热分析",
            caseName: "THERM-03", condition: "稳态 + 瞬态", estimated: 210, calcNotes: "等待后处理报告");
        AddTask("振动模态后处理", TaskKind.Simulation, sim["后处理完成"],
            today.AddDays(-9), today.AddDays(-3), today.AddDays(-3).AddHours(16), category: "结构",
            tags: "模态", caseName: "MOD-01", estimated: 120, description: "已完成前六阶模态整理");
        AddTask("外流场算例（发散，已中止）", TaskKind.Simulation, sim["计算中止"],
            today.AddDays(-4).AddHours(10), today.AddDays(-1), category: "气动", tags: "CFD;异常",
            priority: Priority.Urgent, caseName: "CASE-C-009", condition: "高攻角 18°", estimated: 200,
            calcNotes: "残差震荡后发散，疑似网格质量不足");
        AddTask("修复 CASE-C-009 网格", TaskKind.Simulation, sim["正在修复问题"],
            today.AddHours(9), today.AddDays(1).AddHours(18), category: "气动", tags: "CFD;修复",
            priority: Priority.Urgent, caseName: "CASE-C-009", condition: "细化前缘网格", estimated: 150,
            description: "修复后再提交计算");

        // 一般任务
        AddTask("整理本周仿真交付包", TaskKind.General, general["进行中"],
            today.AddHours(10), today.AddHours(17), category: "交付", tags: "交付;本周", priority: Priority.High);
        AddTask("提交组会汇报材料", TaskKind.General, general["待办"],
            today.AddDays(1).AddHours(9), today.AddDays(1).AddHours(18), category: "汇报", tags: "组会");
        AddTask("归档上周仿真结果", TaskKind.General, general["已完成"],
            today.AddDays(-8), today.AddDays(-2), today.AddDays(-2).AddHours(11), category: "归档");
        AddTask("复核仿真规范文档", TaskKind.General, general["待办"],
            today.AddDays(-3), today.AddDays(-1), category: "学习", tags: "文档", priority: Priority.High,
            description: "已逾期：交付前需完成");
        AddTask("学习 OpenFOAM 新版本特性", TaskKind.General, general["待办"],
            null, null, category: "学习", tags: "学习;长期");

        // 周期任务：每周组会 + 每日复盘
        var weekly = services.Recurrences.Create(new RecurrenceRule
        {
            Title = "每周组会准备",
            Kind = TaskKind.General,
            StatusId = general["待办"].Id,
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 1,
            WeekDays = "1",
            StartDate = today.AddDays(-14),
            EndDate = today.AddDays(60),
            Description = "整理上周进展与本周计划",
            Category = "汇报",
            Tags = "组会;周期",
        });
        var daily = services.Recurrences.Create(new RecurrenceRule
        {
            Title = "每日仿真日志复盘",
            Kind = TaskKind.General,
            StatusId = general["待办"].Id,
            Frequency = RecurrenceFrequency.Daily,
            Interval = 1,
            StartDate = today.AddDays(-5),
            Description = "记录当天计算状态与问题",
            Category = "日志",
            Tags = "复盘;周期",
        });
        services.Recurrences.EnsureMaterialized(today.AddDays(-14), today.AddDays(30));

        // 每日总结
        void SaveSummary(DateTime date, string summary, string completed, string problems, string pending, string tomorrow)
        {
            services.DailySummaries.Save(new DailySummary
            {
                Date = date,
                Summary = summary,
                CompletedItems = completed,
                Problems = problems,
                PendingItems = pending,
                TomorrowPlan = tomorrow,
            });
        }

        SaveSummary(today, "完成工况 A 的计算提交，修复了前缘网格问题。",
            "· 工况 A 残差收敛\n· 整理 CASE-C-009 网格问题清单",
            "高攻角算例发散，判断为前缘网格过渡过快；已加密网格重新提交。",
            "· 等待工况 B 计算结果\n· 准备组会材料",
            "上午看结果，下午整理交付包初稿。");
        SaveSummary(today.AddDays(-1), "结构强度复核推进，热分析计算完成。",
            "· 热防护分析进入后处理\n· 结构复核完成 60%",
            "接触面设置报错，改为绑定约束后通过。",
            "· 模态结果需要重新截图",
            "继续结构强度复核，输出阶段报告。");
        SaveSummary(today.AddDays(-3), "整理历史算例，梳理归档规范。",
            "· 归档上周 5 个算例\n· 更新算例命名规范",
            "无",
            "· 补全归档目录说明",
            "开始整机结构强度复核。");

        // 主题日志 + 断点历史
        var game = services.TopicLogs.Create(new TopicLog
        {
            Title = "游戏日记：艾尔登法环",
            Category = "游戏日记",
            Tags = "游戏;主线",
            CurrentBreakpoint = "第二章 湖之利耶尼亚：探索学院区域",
            Content = "击败魔法教授拉塔恩，获得 5 个记忆石。",
        });
        var edit1 = services.TopicLogs.Get(game.Id)!;
        edit1.CurrentBreakpoint = "第二章 湖之利耶尼亚：湖畔小径探索中";
        edit1.Content = "完成史东薇尔城探索，击败接肢葛瑞克。";
        services.TopicLogs.Update(edit1, saveBreakpointHistory: true);

        var edit2 = services.TopicLogs.Get(game.Id)!;
        edit2.CurrentBreakpoint = "第二章 湖之利耶尼亚：探索学院区域";
        edit2.Content = "击败魔法教授拉塔恩，获得 5 个记忆石。";
        services.TopicLogs.Update(edit2, saveBreakpointHistory: true);

        var study = services.TopicLogs.Create(new TopicLog
        {
            Title = "Abaqus 非线性学习笔记",
            Category = "学习笔记",
            Tags = "Abaqus;非线性",
            CurrentBreakpoint = "第 5 章 接触分析：完成绑定接触与摩擦接触对比",
            Content = "记录：摩擦系数对收敛影响明显，需配合自动稳定。",
        });
        var edit3 = services.TopicLogs.Get(study.Id)!;
        edit3.CurrentBreakpoint = "第 5 章 接触分析：进入摩擦接触部分";
        services.TopicLogs.Update(edit3, saveBreakpointHistory: true);

        services.TopicLogs.Create(new TopicLog
        {
            Title = "个人项目：工作台开发",
            Category = "个人项目",
            Tags = "开发;WinUI",
            CurrentBreakpoint = "P0 功能实现中：任务/日历/总结/日志/清单",
            Content = "完成 SQLite 数据层与主要页面，正在补全部局自定义。",
        });

        // Checklist
        var preCheck = services.Checklists.Create(new Checklist
        {
            Name = "仿真前检查",
            Category = "检查清单",
            Note = "提交计算前逐项确认",
        });
        var items = new[]
        {
            "网格质量检查（纵横比 / 雅可比）",
            "边界条件与初始条件确认",
            "材料参数与单位制确认",
            "求解器设置与收敛标准",
            "输出文件与监控点设置",
        };
        var added = items.Select(text => services.Checklists.AddItem(preCheck.Id, text)).ToList();
        services.Checklists.SetItemDone(added[0].Id, true);
        services.Checklists.SetItemDone(added[1].Id, true);

        var resultCheck = services.Checklists.Create(new Checklist
        {
            Name = "结果检查",
            Category = "检查清单",
            Note = "计算完成后确认结果可用性",
        });
        var r1 = services.Checklists.AddItem(resultCheck.Id, "残差曲线是否收敛");
        services.Checklists.AddItem(resultCheck.Id, "监测量是否稳定");
        services.Checklists.AddItem(resultCheck.Id, "与实验/经验数据对比");
        services.Checklists.SetItemDone(r1.Id, true);

        var deliveryCheck = services.Checklists.Create(new Checklist
        {
            Name = "交付检查",
            Category = "检查清单",
            Note = "对外交付前的最后确认",
        });
        services.Checklists.AddItem(deliveryCheck.Id, "报告目录与版本号");
        services.Checklists.AddItem(deliveryCheck.Id, "图表分辨率与单位");
        services.Checklists.AddItem(deliveryCheck.Id, "附件与原始数据路径");

        services.Checklists.Link(LinkOwnerTypes.Task, created[0].Id, resultCheck.Id);
        services.Checklists.Link(LinkOwnerTypes.Task, created[2].Id, preCheck.Id);
        services.Checklists.Link(LinkOwnerTypes.DailySummary, new Guid(
            System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"daily-summary:{today:yyyy-MM-dd}"))),
            deliveryCheck.Id);
        services.Checklists.Link(LinkOwnerTypes.TopicLog, game.Id, preCheck.Id);

        // 设置：保留默认主题与布局，开启一般任务共享状态集用于演示
        var settings = services.Settings.Load();
        settings.Theme = AppThemeMode.System;
        settings.StartPage = PageKeys.Workbench;
        services.Settings.Save(settings);

        Console.WriteLine($"已生成演示数据：{dbPath}");
        Console.WriteLine($"任务 {services.Tasks.Query(new TaskQuery { Limit = 1000 }).Count} 个，" +
                          $"周期规则 {services.Recurrences.GetAll().Count} 条，" +
                          $"每日总结 {services.DailySummaries.GetRecent(100).Count} 篇，" +
                          $"主题日志 {services.TopicLogs.Query(null, null, null).Count} 个，" +
                          $"清单 {services.Checklists.GetAll().Count} 个");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return 0;
    }
}
