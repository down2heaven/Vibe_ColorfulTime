using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;
using SimWorkbench.Data;

namespace SimWorkbench.SmokeTest;

/// <summary>数据层端到端自检（不使用 UI），对应验收标准中的数据相关条目。</summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "testdata-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "workbench.db");
        Console.WriteLine($"临时数据目录：{root}");
        Console.WriteLine(new string('-', 70));

        try
        {
            var services = WorkbenchServices.Create(dbPath);
            Test_DefaultStatuses(services);
            Test_SimulationTask(services);
            Test_GeneralTaskAndStatusCustomization(services);
            Test_Recurrence(services);
            Test_DailySummary(services);
            Test_TopicLogHistory(services);
            Test_Checklist(services);
            Test_SettingsAndModules(services);
            Test_CalendarSync(services);
            Test_Persistence(services, dbPath);
            Test_Backup(services, root);
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"[FAIL] 未预期异常：{ex}");
        }
        finally
        {
            Console.WriteLine(new string('-', 70));
            Console.WriteLine($"通过 {_passed} 项，失败 {_failed} 项");
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // 清理失败不影响结果
            }
        }

        return _failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string name, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"[FAIL] {name}{(detail is null ? string.Empty : " -> " + detail)}");
        }
    }

    private static void Test_DefaultStatuses(WorkbenchServices services)
    {
        var sim = services.Statuses.GetByScope(StatusScope.Simulation);
        var general = services.Statuses.GetByScope(StatusScope.General);
        Check(sim.Count == 7, "初始配置提供七种仿真状态", $"实际 {sim.Count}");
        Check(sim.Select(s => s.Name).SequenceEqual(new[] { "待进行", "正在处理", "计算中", "计算完成", "后处理完成", "计算中止", "正在修复问题" }),
            "七种仿真状态名称正确且顺序稳定");
        Check(sim.All(s => s.Color.StartsWith('#')), "每个状态都有颜色");
        Check(general.Count == 3, "一般任务默认提供三种通用状态", $"实际 {general.Count}");
        Check(sim.Single(s => s.Name == "计算完成").Id != sim.Single(s => s.Name == "后处理完成").Id,
            "“计算完成”与“后处理完成”是互相独立的状态");
        Check(sim.Single(s => s.Name == "计算中止").Id != sim.Single(s => s.Name == "正在修复问题").Id,
            "“计算中止”与“正在修复问题”是互相独立的状态");
    }

    private static Guid _simTaskId;

    private static void Test_SimulationTask(WorkbenchServices services)
    {
        var statuses = services.Statuses.GetByScope(StatusScope.Simulation);
        var computing = statuses.Single(s => s.Name == "计算中");
        var task = services.Tasks.Create(new TaskItem
        {
            Title = "机翼气动仿真 - 工况 A",
            Kind = TaskKind.Simulation,
            StatusId = computing.Id,
            Description = "验证验收标准 1",
            CreatedAt = new DateTime(2026, 1, 5),
            PlannedStart = new DateTime(2026, 1, 6, 9, 0, 0),
            DueAt = new DateTime(2026, 1, 12, 18, 0, 0),
            ActualDeliveryAt = new DateTime(2026, 1, 11, 16, 30, 0),
            Priority = Priority.High,
            Category = "气动",
            Tags = "CFD;关键",
            CaseName = "CASE-A-001",
            CaseCondition = "Ma=0.6, Alt=10km",
            EstimatedMinutes = 150,
            CalculationNotes = "网格 300 万，SST k-omega",
        });
        _simTaskId = task.Id;

        var loaded = services.Tasks.Get(task.Id);
        Check(loaded is not null, "仿真任务创建后可以读取");
        Check(loaded!.CaseName == "CASE-A-001" && loaded.CaseCondition == "Ma=0.6, Alt=10km", "关联计算任务与工况保存正确");
        Check(loaded.EstimatedMinutes == 150, "手动计算所需时间保存正确");
        Check(loaded.CreatedAt == new DateTime(2026, 1, 5), "创建日期允许手动填写");
        Check(loaded.DueAt == new DateTime(2026, 1, 12, 18, 0, 0), "计划交付时间保存正确");
        Check(loaded.ActualDeliveryAt == new DateTime(2026, 1, 11, 16, 30, 0), "实际交付时间保存正确");
        Check(loaded.StatusId == computing.Id, "仿真状态可以独立选择");

        var filtered = services.Tasks.Query(new TaskQuery { Kind = TaskKind.Simulation, StatusId = computing.Id });
        Check(filtered.Any(t => t.Id == task.Id), "按类型与状态筛选任务生效");

        var searched = services.Tasks.Query(new TaskQuery { SearchText = "机翼" });
        Check(searched.Any(t => t.Id == task.Id), "任务搜索生效");
    }

    private static void Test_GeneralTaskAndStatusCustomization(WorkbenchServices services)
    {
        var custom = services.Statuses.Create(new WorkStatus
        {
            Scope = StatusScope.General,
            Name = "等待反馈",
            Color = "#FF8800",
        });
        Check(services.Statuses.Get(custom.Id)?.Color == "#FF8800", "一般任务支持新建自定义状态及颜色");

        var task = services.Tasks.Create(new TaskItem
        {
            Title = "整理仿真交付包",
            Kind = TaskKind.General,
            StatusId = custom.Id,
            DueAt = DateTime.Today.AddDays(2),
        });

        var impact = services.Statuses.GetDeleteImpact(custom.Id);
        Check(impact.TaskCount == 1 && !impact.CanDelete, "删除使用中的状态时给出影响评估");

        var blocked = false;
        try
        {
            services.Statuses.Delete(custom.Id, null);
        }
        catch (InvalidOperationException)
        {
            blocked = true;
        }

        Check(blocked, "状态仍被任务占用时禁止静默删除");

        var fallback = services.Statuses.GetByScope(StatusScope.General).First(s => s.Name == "进行中");
        var migrated = services.Tasks.MigrateStatus(custom.Id, fallback.Id);
        services.Statuses.Delete(custom.Id, fallback.Id);
        Check(services.Statuses.Get(custom.Id) is null, "迁移任务后可以删除自定义状态");
        Check(migrated == 1 && services.Tasks.Get(task.Id)?.StatusId == fallback.Id, "删除状态时任务迁移到目标状态，数据不丢失");
        Check(services.Tasks.Get(task.Id) is not null, "一般任务数据保留");
    }

    private static void Test_Recurrence(WorkbenchServices services)
    {
        var status = services.Statuses.GetByScope(StatusScope.General).First();
        var rule = services.Recurrences.Create(new RecurrenceRule
        {
            Title = "每周组会准备",
            Kind = TaskKind.General,
            StatusId = status.Id,
            Frequency = RecurrenceFrequency.Weekly,
            Interval = 1,
            StartDate = DateTime.Today,
            WeekDays = "1",
            EndDate = DateTime.Today.AddDays(60),
        });
        Check(services.Recurrences.Get(rule.Id) is not null, "周期规则创建成功");

        var instances = services.Recurrences.EnsureMaterialized(DateTime.Today, DateTime.Today.AddDays(30));
        var ruleInstances = services.Recurrences.GetInstances(rule.Id);
        Check(ruleInstances.Count >= 4, "周期规则生成了多个执行实例", $"实际 {ruleInstances.Count}");

        var taskIds = ruleInstances.Where(i => i.TaskId is not null).Select(i => i.TaskId!.Value).ToList();
        Check(taskIds.Distinct().Count() == taskIds.Count, "每次执行生成独立任务实例");

        if (taskIds.Count >= 2)
        {
            var first = services.Tasks.Get(taskIds[0])!;
            var statusDone = services.Statuses.GetByScope(StatusScope.General).First(s => s.Name == "已完成");
            first.StatusId = statusDone.Id;
            first.ActualDeliveryAt = DateTime.Now;
            services.Tasks.Update(first);

            var second = services.Tasks.Get(taskIds[1])!;
            Check(second.StatusId != statusDone.Id, "完成一次周期实例不会影响后续实例");
            Check(second.ActualDeliveryAt is null, "后续实例的实际交付时间为空");
        }

        var occurrences = RecurrenceCalculator.GetOccurrences(rule, DateTime.Today, DateTime.Today.AddDays(28));
        Check(occurrences.All(d => d.DayOfWeek == DayOfWeek.Monday), "每周指定星期（周一）生效");

        // 跳过某次
        if (occurrences.Count > 2)
        {
            var skipDate = occurrences[^1];
            var before = services.Recurrences.GetInstances(rule.Id).FirstOrDefault(i => i.OccurrenceDate == skipDate);
            services.Recurrences.SkipOccurrence(rule.Id, skipDate);
            var after = services.Recurrences.GetInstances(rule.Id).FirstOrDefault(i => i.OccurrenceDate == skipDate);
            Check(after is { IsSkipped: true, TaskId: null }, "可以跳过某次执行");
            if (before?.TaskId is { } skippedTask)
            {
                Check(services.Tasks.Get(skippedTask) is null, "跳过执行后对应任务被移除");
            }

            services.Recurrences.EnsureMaterialized(DateTime.Today, DateTime.Today.AddDays(30));
            var stillSkipped = services.Recurrences.GetInstances(rule.Id).FirstOrDefault(i => i.OccurrenceDate == skipDate);
            Check(stillSkipped is { IsSkipped: true, TaskId: null }, "被跳过的执行不会在补齐实例时重新生成");
        }

        var monthly = services.Recurrences.Create(new RecurrenceRule
        {
            Title = "每月归档",
            StatusId = status.Id,
            Frequency = RecurrenceFrequency.Monthly,
            Interval = 1,
            StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
        });
        Check(services.Recurrences.Get(monthly.Id) is not null, "支持每月周期规则");

        var daily = services.Recurrences.Create(new RecurrenceRule
        {
            Title = "每日回顾",
            StatusId = status.Id,
            Frequency = RecurrenceFrequency.Daily,
            Interval = 2,
            StartDate = DateTime.Today,
        });
        Check(services.Recurrences.GetInstances(daily.Id).Count > 2, "支持自定义间隔的每日周期规则");
    }

    private static void Test_DailySummary(WorkbenchServices services)
    {
        var date = new DateTime(2026, 2, 3);
        var summary = services.DailySummaries.Get(date);
        Check(!services.DailySummaries.Exists(date), "尚未创建时每日总结不存在");

        summary.Summary = "完成气动计算前后处理";
        summary.CompletedItems = "跑完 CASE-A-001";
        summary.Problems = "残差波动，已加密网格";
        summary.PendingItems = "整理报告";
        summary.TomorrowPlan = "交付评审材料";
        services.DailySummaries.Save(summary);
        Check(services.DailySummaries.Exists(date), "每日总结可以新增并保存");

        var loaded = services.DailySummaries.Get(date);
        Check(loaded.Summary!.Contains("气动"), "每日总结内容可以读取");
        Check(loaded.Problems!.Contains("残差"), "问题及处理过程区域保留");

        loaded.TomorrowPlan = "更新评审材料";
        services.DailySummaries.Save(loaded);
        Check(services.DailySummaries.Get(date).TomorrowPlan == "更新评审材料", "同一天可持续编辑同一份每日总结");

        var second = new DateTime(2026, 2, 4);
        var other = services.DailySummaries.Get(second);
        other.Summary = "第二天记录";
        services.DailySummaries.Save(other);
        Check(services.DailySummaries.GetRecent(5).Count == 2, "支持查看历史每日总结");

        services.DailySummaries.SetLinkedTasks(date, new[] { _simTaskId });
        Check(services.DailySummaries.GetLinkedTaskIds(date).Contains(_simTaskId), "每日总结可以关联当日任务");
        Check(services.Tasks.Get(_simTaskId) is not null, "关联总结不改变任务数据");
    }

    private static void Test_TopicLogHistory(WorkbenchServices services)
    {
        var topic = services.TopicLogs.Create(new TopicLog
        {
            Title = "艾尔登法环进度",
            Category = "游戏日记",
            Tags = "游戏;主线",
            CurrentBreakpoint = "第一章 到达史东薇尔城",
            Content = "击败大树守卫",
        });
        Check(services.TopicLogs.Get(topic.Id) is not null, "可以创建主题日志");

        var edit = services.TopicLogs.Get(topic.Id)!;
        edit.CurrentBreakpoint = "第二章 通关史东薇尔城";
        edit.Content = "击败接肢葛瑞克";
        services.TopicLogs.Update(edit, saveBreakpointHistory: true, changeNote: "推进主线");

        var history = services.TopicLogs.GetHistory(topic.Id);
        Check(history.Count == 1, "更新断点时写入历史记录");
        Check(history[0].PreviousBreakpoint!.Contains("第一章"), "历史中保留旧断点，未被覆盖");
        Check(history[0].PreviousContent!.Contains("大树守卫"), "历史中保留旧详细内容");

        var edit2 = services.TopicLogs.Get(topic.Id)!;
        edit2.CurrentBreakpoint = "第三章 湖之利耶尼亚";
        services.TopicLogs.Update(edit2, saveBreakpointHistory: true);
        Check(services.TopicLogs.GetHistory(topic.Id).Count == 2, "多次更新断点累积历史记录");
        Check(services.TopicLogs.Get(topic.Id)!.CurrentBreakpoint!.Contains("第三章"), "当前断点更新为最新内容");

        var filtered = services.TopicLogs.Query(null, "游戏日记", null);
        Check(filtered.Any(t => t.Id == topic.Id), "主题日志支持按类别筛选");
    }

    private static void Test_Checklist(WorkbenchServices services)
    {
        var checklist = services.Checklists.Create(new Checklist
        {
            Name = "仿真前检查",
            Category = "检查模板",
            Note = "提交计算前逐项确认",
        });
        var i1 = services.Checklists.AddItem(checklist.Id, "网格质量检查");
        var i2 = services.Checklists.AddItem(checklist.Id, "边界条件确认");
        var i3 = services.Checklists.AddItem(checklist.Id, "求解器设置复核");
        Check(services.Checklists.Get(checklist.Id)!.Items.Count == 3, "Checklist 可以添加检查项");

        services.Checklists.SetItemDone(i1.Id, true);
        var loaded = services.Checklists.Get(checklist.Id)!;
        Check(loaded.DoneCount == 1 && loaded.TotalCount == 3, "Checklist 显示完成数量与总数");
        Check(Math.Abs(loaded.Progress - 1.0 / 3) < 0.001, "Checklist 进度计算正确");

        services.Checklists.SetItemDone(i1.Id, false);
        Check(services.Checklists.Get(checklist.Id)!.DoneCount == 0, "检查项可以取消勾选");

        services.Checklists.MoveItem(i3.Id, -1);
        var moved = services.Checklists.Get(checklist.Id)!;
        Check(moved.Items.OrderBy(i => i.SortOrder).Select(i => i.Id).ToList().IndexOf(i3.Id) == 1, "检查项可以排序");

        services.Checklists.DeleteItem(i2.Id);
        Check(services.Checklists.Get(checklist.Id)!.Items.Count == 2, "检查项可以删除");
        Check(services.Checklists.Get(checklist.Id)!.Items.Any(i => i.Id == i1.Id), "未完成项目持续保留");

        services.Checklists.Link(LinkOwnerTypes.Task, _simTaskId, checklist.Id);
        var linked = services.Checklists.GetLinkedChecklists(LinkOwnerTypes.Task, _simTaskId);
        Check(linked.Any(c => c.Id == checklist.Id), "Checklist 可以关联到任务");
        services.Checklists.Unlink(LinkOwnerTypes.Task, _simTaskId, checklist.Id);
        Check(services.Checklists.GetLinkedChecklists(LinkOwnerTypes.Task, _simTaskId).Count == 0, "可以取消关联");
    }

    private static void Test_SettingsAndModules(WorkbenchServices services)
    {
        var settings = services.Settings.Load();
        Check(settings.HomeCards.Count >= 8, "首页卡片设置初始化完整");
        Check(settings.NavItems.Count >= 6, "导航入口设置初始化完整");
        Check(settings.Theme == AppThemeMode.System, "默认跟随系统主题");

        settings.Theme = AppThemeMode.Dark;
        services.Settings.Save(settings);
        Check(services.Settings.Load().Theme == AppThemeMode.Dark, "主题设置持久化");

        var firstCard = services.Modules.GetHomeCards().First();
        services.Modules.SetHomeCardVisible(firstCard.CardId, false);
        Check(services.Modules.GetHomeCards().First(c => c.CardId == firstCard.CardId).IsVisible == false, "首页卡片可以隐藏");

        var cards = services.Modules.GetHomeCards().ToList();
        services.Modules.MoveHomeCard(cards[1].CardId, -1);
        var reordered = services.Modules.GetHomeCards();
        Check(reordered[0].CardId == cards[1].CardId, "首页卡片可以调整顺序");

        services.Modules.SetNavVisible(ModuleIds.Checklist, false);
        Check(!services.Modules.IsNavVisible(ModuleIds.Checklist), "导航入口可以隐藏");
        Check(services.Checklists.GetAll().Count > 0, "隐藏导航入口不会删除清单数据");

        services.Modules.SetEnabled(ModuleIds.TopicLog, false);
        var state = services.Modules.Get(ModuleIds.TopicLog);
        Check(!state.IsEnabled && state.Status == ModuleStatus.Disabled, "可选模块可以停用");
        Check(services.TopicLogs.Query(null, null, null).Count > 0, "停用模块后数据仍然保留");

        services.Modules.SetEnabled(ModuleIds.TopicLog, true);
        Check(services.Modules.Get(ModuleIds.TopicLog).IsEnabled, "重新启用模块后数据可继续使用");

        var blocked = false;
        try
        {
            services.Modules.SetEnabled(ModuleIds.Tasks, false);
        }
        catch (InvalidOperationException)
        {
            blocked = true;
        }

        Check(blocked, "核心任务模块不能被停用");

        var aiState = services.Modules.Get(ModuleIds.AiAssistant);
        Check(aiState.Status == ModuleStatus.Unavailable && !aiState.CanToggleEnabled, "未实现的 AI 模块不可伪装成可用");

        services.Settings.ResetLayout();
        Check(services.Modules.GetHomeCards().All(c => c.IsVisible), "恢复默认布局生效");
    }

    private static void Test_CalendarSync(WorkbenchServices services)
    {
        var task = services.Tasks.Get(_simTaskId)!;
        var target = new DateTime(2026, 3, 10);
        task.DueAt = target;
        task.PlannedStart = target.AddDays(-2);
        services.Tasks.Update(task);

        var month = services.Calendar.GetMonth(target);
        Check(month.Count == 42, "月历返回 6×7 网格");
        var dueDay = month.First(d => d.Date.Date == target.Date);
        Check(dueDay.Items.Any(i => i.TaskId == task.Id && i.Kind == CalendarItemKind.Due),
            "修改计划交付日期后日历同步更新");
        var startDay = month.First(d => d.Date.Date == target.AddDays(-2).Date);
        Check(startDay.Items.Any(i => i.TaskId == task.Id), "计划开始日期也出现在日历中");

        var item = dueDay.Items.First(i => i.TaskId == task.Id);
        var status = services.Statuses.Get(task.StatusId)!;
        Check(string.Equals(item.Color, status.Color, StringComparison.OrdinalIgnoreCase), "日历颜色与状态颜色一致");
    }

    private static void Test_Persistence(WorkbenchServices services, string dbPath)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var reopened = WorkbenchServices.Create(dbPath);
        Check(reopened.Tasks.Get(_simTaskId) is not null, "重新打开程序后任务仍然存在");
        Check(reopened.Statuses.GetAll().Count == 10, "重新打开后自定义状态保留", $"实际 {reopened.Statuses.GetAll().Count}");
        Check(reopened.Settings.Load().Theme == AppThemeMode.Dark, "重新打开后主题设置保留");
        Check(reopened.TopicLogs.Query(null, null, null).Count > 0, "重新打开后主题日志与断点历史保留");
        Check(reopened.DailySummaries.GetRecent(10).Count > 0, "重新打开后每日总结保留");
        Check(reopened.Checklists.GetAll().Count > 0, "重新打开后 Checklist 保留");
    }

    private static void Test_Backup(WorkbenchServices services, string root)
    {
        var exportPath = Path.Combine(root, "backup.json");
        services.Backup.Export(exportPath);
        Check(File.Exists(exportPath), "备份文件成功导出");
        var json = File.ReadAllText(exportPath);
        Check(json.Contains("simworkbench-backup"), "备份文件格式可检查");
        Check(json.Contains("机翼气动仿真"), "备份包含任务数据");

        var inspection = services.Backup.Inspect(exportPath);
        Check(inspection.IsValid, "备份文件可以校验");
        Check(inspection.TaskCount > 0 && inspection.TopicLogCount > 0 && inspection.ChecklistCount > 0,
            "备份范围覆盖任务/记录/清单/配置",
            $"任务 {inspection.TaskCount} / 主题 {inspection.TopicLogCount} / 清单 {inspection.ChecklistCount}");
        Check(inspection.HasSettings, "备份包含配置");

        // 导入到新的数据文件
        var otherDb = Path.Combine(root, "second.db");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = WorkbenchServices.Create(otherDb);
        Check(fresh.Tasks.Query(new TaskQuery { Limit = 100 }).Count == 0, "新数据文件初始为空");

        var importResult = fresh.Backup.Import(exportPath, BackupImportMode.Merge);
        Check(importResult.TotalAdded > 0, "备份可以导入", importResult.GetSummary());
        Check(fresh.Tasks.Get(_simTaskId) is not null, "导入后任务数据完整");
        Check(fresh.TopicLogs.Query(null, null, null).Count > 0, "导入后主题日志完整");
        Check(fresh.Checklists.GetAll().Count > 0, "导入后 Checklist 完整");

        var secondImport = fresh.Backup.Import(exportPath, BackupImportMode.Merge);
        Check(secondImport.TotalSkipped > 0 && secondImport.AddedTasks == 0, "重复导入不会产生重复数据");
    }
}
