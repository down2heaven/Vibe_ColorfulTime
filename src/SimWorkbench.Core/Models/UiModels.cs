namespace SimWorkbench.Core.Models;

public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>首页卡片定义（静态注册表，顺序/可见性由用户设置控制）。</summary>
public sealed class HomeCardDefinition
{
    public HomeCardDefinition(string id, string title, string description)
    {
        Id = id;
        Title = title;
        Description = description;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
}

/// <summary>模块状态。</summary>
public enum ModuleStatus
{
    /// <summary>已启用且可用。</summary>
    Enabled = 0,
    /// <summary>用户已停用。</summary>
    Disabled = 1,
    /// <summary>已实现但需要配置。</summary>
    NotConfigured = 2,
    /// <summary>尚未实现，不可启用。</summary>
    Unavailable = 3,
}

/// <summary>模块定义（第一版只注册自身已实现的模块，未来 AI / 网络 API 以 Unavailable 状态列出）。</summary>
public sealed class ModuleDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    /// <summary>核心基础设施或核心任务能力：不允许普通用户停用。</summary>
    public bool IsCore { get; init; }
    /// <summary>功能是否已在当前版本真实实现。</summary>
    public bool IsImplemented { get; init; }
    /// <summary>是否为需要独立配置的模块。</summary>
    public bool RequiresConfiguration { get; init; }
    /// <summary>是否为未来的 AI 模块。</summary>
    public bool IsAiModule { get; init; }
    public string? Note { get; init; }
}

/// <summary>模块运行时状态（定义 + 用户偏好）。</summary>
public sealed class ModuleRuntimeState
{
    public required ModuleDefinition Definition { get; init; }
    public bool IsEnabled { get; init; }
    public bool ShowInNav { get; init; }
    /// <summary>模块当前对用户而言的真实可用状态。</summary>
    public ModuleStatus Status { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public bool CanToggleEnabled => Definition.IsImplemented && !Definition.IsCore;
    public bool CanToggleNav => Definition.IsImplemented && !Definition.IsCore;
}

/// <summary>首页卡片用户设置。</summary>
public sealed class HomeCardSetting
{
    public string CardId { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int Order { get; set; }
}

/// <summary>导航入口设置（只控制显示，不删除数据）。</summary>
public sealed class NavItemSetting
{
    public string ModuleId { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public int Order { get; set; }
}

/// <summary>可选模块启停设置。</summary>
public sealed class ModuleSetting
{
    public string ModuleId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

/// <summary>应用设置（持久化到 SQLite settings 表）。</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;
    /// <summary>首页默认打开的页面（PageKeys）。</summary>
    public string StartPage { get; set; } = PageKeys.Workbench;
    public bool CompactDensity { get; set; }
    /// <summary>一般任务是否允许使用仿真状态集（状态集合共享）。</summary>
    public bool GeneralTasksShareSimulationStatuses { get; set; }
    public List<HomeCardSetting> HomeCards { get; set; } = new();
    public List<NavItemSetting> NavItems { get; set; } = new();
    public List<ModuleSetting> Modules { get; set; } = new();
    public DateTime? LastLayoutResetAt { get; set; }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Version = Version,
            Theme = Theme,
            StartPage = StartPage,
            CompactDensity = CompactDensity,
            GeneralTasksShareSimulationStatuses = GeneralTasksShareSimulationStatuses,
            HomeCards = HomeCards.Select(c => new HomeCardSetting { CardId = c.CardId, IsVisible = c.IsVisible, Order = c.Order }).ToList(),
            NavItems = NavItems.Select(n => new NavItemSetting { ModuleId = n.ModuleId, IsVisible = n.IsVisible, Order = n.Order }).ToList(),
            Modules = Modules.Select(m => new ModuleSetting { ModuleId = m.ModuleId, IsEnabled = m.IsEnabled }).ToList(),
            LastLayoutResetAt = LastLayoutResetAt,
        };
    }
}

/// <summary>页面标识（导航 key 与 StartPage 设置共用）。</summary>
public static class PageKeys
{
    public const string Workbench = "workbench";
    public const string Tasks = "tasks";
    public const string Calendar = "calendar";
    public const string DailySummary = "daily-summary";
    public const string TopicLog = "topic-log";
    public const string Checklist = "checklist";
    public const string Settings = "settings";
}

/// <summary>模块标识。</summary>
public static class ModuleIds
{
    public const string Tasks = "tasks";
    public const string Calendar = "calendar";
    public const string DailySummary = "daily-summary";
    public const string TopicLog = "topic-log";
    public const string Checklist = "checklist";
    public const string AiAssistant = "ai-assistant";
    public const string WebConnectors = "web-connectors";
}

/// <summary>首页卡片标识。</summary>
public static class HomeCardIds
{
    public const string TodayTodos = "today-todos";
    public const string InProgress = "in-progress";
    public const string Computing = "computing";
    public const string DueSoon = "due-soon";
    public const string TodaySchedule = "today-schedule";
    public const string RecentBreakpoints = "recent-breakpoints";
    public const string RecentSummaries = "recent-summaries";
    public const string ChecklistProgress = "checklist-progress";
}

/// <summary>应用版本信息。</summary>
public static class AppInfo
{
    public const string DisplayName = "仿真工程师工作台";
    public const string EnglishName = "Simulation Engineer Workbench";
    public const string Version = "0.1.0";
    public const string DataFolderName = "SimWorkbench";
}
