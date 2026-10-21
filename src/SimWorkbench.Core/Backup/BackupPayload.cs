using SimWorkbench.Core.Models;

namespace SimWorkbench.Core.Backup;

/// <summary>备份文件的数据容器（JSON）。</summary>
public sealed class BackupPayload
{
    public string Format { get; set; } = "simworkbench-backup";
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public string AppVersion { get; set; } = AppInfo.Version;

    public List<WorkStatus> Statuses { get; set; } = new();
    public List<TaskItem> Tasks { get; set; } = new();
    public List<RecurrenceRule> RecurrenceRules { get; set; } = new();
    public List<RecurrenceInstance> RecurrenceInstances { get; set; } = new();
    public List<DailySummary> DailySummaries { get; set; } = new();
    public List<DailySummaryTaskLink> DailySummaryTaskLinks { get; set; } = new();
    public List<TopicLog> TopicLogs { get; set; } = new();
    public List<TopicLogHistory> TopicLogHistories { get; set; } = new();
    public List<Checklist> Checklists { get; set; } = new();
    public List<ChecklistItem> ChecklistItems { get; set; } = new();
    public List<EntityLink> EntityLinks { get; set; } = new();
    public AppSettings? Settings { get; set; }
}

/// <summary>每日总结与任务的关联。</summary>
public sealed class DailySummaryTaskLink
{
    public DateTime SummaryDate { get; set; }
    public Guid TaskId { get; set; }
}

/// <summary>设置键值对（备份时以 JSON 键值保存，便于检查）。</summary>
public sealed class SettingEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
