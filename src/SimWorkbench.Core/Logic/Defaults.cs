using SimWorkbench.Core.Models;

namespace SimWorkbench.Core.Logic;

/// <summary>内置默认数据（初始状态、内置模块、首页卡片注册表）。</summary>
public static class Defaults
{
    public static IReadOnlyList<WorkStatus> CreateDefaultStatuses()
    {
        var list = new List<WorkStatus>
        {
            // 七种仿真状态必须互相独立：计算完成 ≠ 后处理完成；计算中止 ≠ 正在修复问题
            new() { Scope = StatusScope.Simulation, Name = "待进行", Color = "#9AA0A6", SortOrder = 0, IsProtected = true },
            new() { Scope = StatusScope.Simulation, Name = "正在处理", Color = "#3A7BFF", SortOrder = 1, IsProtected = true },
            new() { Scope = StatusScope.Simulation, Name = "计算中", Color = "#F58A1F", SortOrder = 2, IsProtected = true },
            new() { Scope = StatusScope.Simulation, Name = "计算完成", Color = "#12A5A5", SortOrder = 3, IsProtected = true },
            new() { Scope = StatusScope.Simulation, Name = "后处理完成", Color = "#2EA043", SortOrder = 4, IsProtected = true },
            new() { Scope = StatusScope.Simulation, Name = "计算中止", Color = "#D13438", SortOrder = 5, IsProtected = true },
            new() { Scope = StatusScope.Simulation, Name = "正在修复问题", Color = "#8E5CD9", SortOrder = 6, IsProtected = true },

            new() { Scope = StatusScope.General, Name = "待办", Color = "#9AA0A6", SortOrder = 0, IsProtected = true },
            new() { Scope = StatusScope.General, Name = "进行中", Color = "#3A7BFF", SortOrder = 1, IsProtected = true },
            new() { Scope = StatusScope.General, Name = "已完成", Color = "#2EA043", SortOrder = 2, IsProtected = true },
        };
        return list;
    }

    public static IReadOnlyList<ModuleDefinition> CreateModuleDefinitions() => new List<ModuleDefinition>
    {
        new()
        {
            Id = ModuleIds.Tasks,
            Name = "任务管理",
            Description = "仿真任务与一般任务的创建、编辑、状态与筛选。核心业务数据始终保留，可隐藏导航入口。",
            IsCore = true,
            IsImplemented = true,
        },
        new()
        {
            Id = ModuleIds.Calendar,
            Name = "日历",
            Description = "月视图展示任务计划开始、计划交付与周期任务实例，可在导航与首页中显示或隐藏。",
            IsImplemented = true,
        },
        new()
        {
            Id = ModuleIds.DailySummary,
            Name = "每日总结",
            Description = "按日期记录今日总结、完成事项、问题处理、待办与明日计划，可关联当日任务。",
            IsImplemented = true,
        },
        new()
        {
            Id = ModuleIds.TopicLog,
            Name = "主题日志 / 断点",
            Description = "长期主题记录与当前断点，更新断点时自动保留历史版本。",
            IsImplemented = true,
        },
        new()
        {
            Id = ModuleIds.Checklist,
            Name = "Checklist",
            Description = "命名检查清单，可关联任务、每日总结或主题日志，支持进度统计。",
            IsImplemented = true,
        },
        new()
        {
            Id = ModuleIds.AiAssistant,
            Name = "AI 助手",
            Description = "后续版本规划：生成总结草稿、整理仿真日志、自然语言转待办。当前版本尚未实现，不会显示为可用入口。",
            IsImplemented = false,
            RequiresConfiguration = true,
            IsAiModule = true,
            Note = "当前版本未实现，启用开关仅在功能真实上线后开放。",
        },
        new()
        {
            Id = ModuleIds.WebConnectors,
            Name = "网络 API 连接",
            Description = "后续版本规划：在用户授权下获取外部数据（如仿真服务器状态）。当前版本尚未实现。",
            IsImplemented = false,
            RequiresConfiguration = true,
            Note = "当前版本未实现；未来各连接器将独立启停。",
        },
    };

    public static IReadOnlyList<HomeCardDefinition> CreateHomeCardDefinitions() => new List<HomeCardDefinition>
    {
        new(HomeCardIds.TodayTodos, "今日待办", "今天到期或今天开始的未完成任务"),
        new(HomeCardIds.InProgress, "正在处理的任务", "状态为准备/修复中的任务"),
        new(HomeCardIds.Computing, "正在计算的仿真任务", "仿真任务中状态为计算相关且未交付的任务"),
        new(HomeCardIds.DueSoon, "即将到期与已逾期", "3 天内到期以及已经逾期的任务"),
        new(HomeCardIds.TodaySchedule, "今日及近期日程", "今天与未来 7 天的计划开始/交付事项"),
        new(HomeCardIds.RecentBreakpoints, "最近更新的主题断点", "最近修改过的主题日志断点"),
        new(HomeCardIds.RecentSummaries, "最近编辑的每日总结", "最近保存的每日总结记录"),
        new(HomeCardIds.ChecklistProgress, "Checklist 完成进度", "各清单的完成数量与进度"),
    };

    /// <summary>默认导航顺序（仅包含可实现页面；设置页固定在末尾）。</summary>
    public static IReadOnlyList<string> DefaultNavOrder => new[]
    {
        PageKeys.Workbench,
        ModuleIds.Tasks,
        ModuleIds.Calendar,
        ModuleIds.DailySummary,
        ModuleIds.TopicLog,
        ModuleIds.Checklist,
    };

    public static AppSettings CreateDefaultSettings()
    {
        var settings = new AppSettings();
        var cards = CreateHomeCardDefinitions();
        for (var i = 0; i < cards.Count; i++)
        {
            settings.HomeCards.Add(new HomeCardSetting { CardId = cards[i].Id, IsVisible = true, Order = i });
        }

        var order = DefaultNavOrder;
        for (var i = 0; i < order.Count; i++)
        {
            settings.NavItems.Add(new NavItemSetting { ModuleId = order[i], IsVisible = true, Order = i });
        }

        foreach (var module in CreateModuleDefinitions())
        {
            settings.Modules.Add(new ModuleSetting { ModuleId = module.Id, IsEnabled = module.IsImplemented });
        }

        return settings;
    }

    /// <summary>页面 key → 中文标题。</summary>
    public static string GetPageTitle(string pageKey) => pageKey switch
    {
        PageKeys.Workbench => "工作台 / 总览",
        ModuleIds.Tasks => "任务",
        ModuleIds.Calendar => "日历",
        ModuleIds.DailySummary => "每日总结",
        ModuleIds.TopicLog => "主题日志 / 断点",
        ModuleIds.Checklist => "Checklist",
        PageKeys.Settings => "设置",
        _ => pageKey,
    };
}
