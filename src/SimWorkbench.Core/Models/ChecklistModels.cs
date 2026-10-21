namespace SimWorkbench.Core.Models;

/// <summary>命名清单。</summary>
public sealed class Checklist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<ChecklistItem> Items { get; set; } = new();

    public int DoneCount => Items.Count(i => i.IsDone);
    public int TotalCount => Items.Count;
    public double Progress => TotalCount == 0 ? 0 : (double)DoneCount / TotalCount;
    public string ProgressText => $"{DoneCount}/{TotalCount}";
}

/// <summary>清单中的检查项，未完成项持续保留直到用户修改或删除。</summary>
public sealed class ChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChecklistId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>跨模块关联对象类型。</summary>
public static class LinkOwnerTypes
{
    public const string Task = "task";
    public const string DailySummary = "daily-summary";
    public const string TopicLog = "topic-log";
    public const string RecurrenceRule = "recurrence";

    public static bool IsKnown(string type) =>
        type is Task or DailySummary or TopicLog or RecurrenceRule;
}

/// <summary>通用关联关系（任务 / 每日总结 / 主题日志 / 周期规则 ↔ 清单）。</summary>
public sealed class EntityLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
