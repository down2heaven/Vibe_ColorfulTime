namespace SimWorkbench.Core.Models;

/// <summary>任务类型。仿真任务用于仿真计算/交付管理，一般任务用于普通待办。</summary>
public enum TaskKind
{
    Simulation = 0,
    General = 1,
}

/// <summary>优先级（保留扩展空间）。</summary>
public enum Priority
{
    Normal = 0,
    High = 1,
    Urgent = 2,
}

/// <summary>状态集合归属：仿真任务状态集 / 一般任务状态集。</summary>
public enum StatusScope
{
    Simulation = 0,
    General = 1,
}

/// <summary>可自定义的任务状态。</summary>
public sealed class WorkStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StatusScope Scope { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>#RRGGBB 形式的状态颜色。</summary>
    public string Color { get; set; } = "#8A8A8A";
    public int SortOrder { get; set; }
    /// <summary>初始内置状态受保护，只允许改名/改色/排序，不允许删除。</summary>
    public bool IsProtected { get; set; }

    public WorkStatus Clone() => (WorkStatus)MemberwiseClone();
}

/// <summary>一条任务（仿真任务与一般任务共用同一模型，按 Kind 区分字段可用性）。</summary>
public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public TaskKind Kind { get; set; } = TaskKind.General;
    public Guid StatusId { get; set; }
    public string? Description { get; set; }

    /// <summary>创建日期（默认自动写入，允许手动修改）。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>计划开始时间（可选）。</summary>
    public DateTime? PlannedStart { get; set; }
    /// <summary>计划交付时间。</summary>
    public DateTime? DueAt { get; set; }
    /// <summary>实际交付时间（可为空）。</summary>
    public DateTime? ActualDeliveryAt { get; set; }

    public string? Category { get; set; }
    /// <summary>标签，分号分隔。</summary>
    public string? Tags { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;

    // ---- 仿真任务专属字段 ----
    public string? CaseName { get; set; }
    public string? CaseCondition { get; set; }
    /// <summary>用户手动维护的“计算所需时间”（分钟）。</summary>
    public int? EstimatedMinutes { get; set; }
    public string? CalculationNotes { get; set; }

    // ---- 周期任务实例信息 ----
    public Guid? RecurrenceId { get; set; }
    public DateTime? OccurrenceDate { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public bool IsOverdue(DateTime now) =>
        DueAt is { } due && ActualDeliveryAt is null && due.Date < now.Date;

    public bool IsDueSoon(DateTime now, int withinDays = 3) =>
        DueAt is { } due && ActualDeliveryAt is null
        && due.Date >= now.Date && due.Date <= now.Date.AddDays(withinDays);

    public TaskItem Clone() => (TaskItem)MemberwiseClone();

    public IReadOnlyList<string> GetTagList() =>
        string.IsNullOrWhiteSpace(Tags)
            ? Array.Empty<string>()
            : Tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string GetTagsDisplay() => string.Join("，", GetTagList());
}

/// <summary>任务查询条件。</summary>
public sealed class TaskQuery
{
    public string? SearchText { get; set; }
    public TaskKind? Kind { get; set; }
    public Guid? StatusId { get; set; }
    public Priority? Priority { get; set; }
    public string? Category { get; set; }
    public string? Tag { get; set; }
    public bool OnlyOverdue { get; set; }
    public bool OnlyDueSoon { get; set; }
    public bool OnlyOpen { get; set; }
    public DateTime? DueBefore { get; set; }
    public DateTime? DueAfter { get; set; }
    public Guid? RecurrenceId { get; set; }
    public TaskSortField SortField { get; set; } = TaskSortField.DueAt;
    public bool Descending { get; set; }
    public int Limit { get; set; } = 2000;
}

public enum TaskSortField
{
    CreatedAt = 0,
    DueAt = 1,
    Title = 2,
    Priority = 3,
    UpdatedAt = 4,
    Status = 5,
}

/// <summary>删除状态前的影响评估。</summary>
public sealed class StatusDeleteImpact
{
    public Guid StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public int TaskCount { get; set; }
    public bool CanDelete => TaskCount == 0;
}
