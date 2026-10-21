using SimWorkbench.Core.Models;

namespace SimWorkbench.Core.Services;

public interface ITaskService
{
    IReadOnlyList<TaskItem> Query(TaskQuery query);
    TaskItem? Get(Guid id);
    TaskItem Create(TaskItem task);
    TaskItem Update(TaskItem task);
    void Delete(Guid id);
    IReadOnlyList<TaskItem> GetByDateRange(DateTime fromInclusive, DateTime toInclusive);
    IReadOnlyList<TaskItem> GetOpenTasks();
    int CountByStatus(Guid statusId);
    /// <summary>把使用指定状态的任务迁移到目标状态。</summary>
    int MigrateStatus(Guid fromStatusId, Guid toStatusId);
    IReadOnlyList<string> GetCategories();
    IReadOnlyList<string> GetTags();
    /// <summary>最近被更新的任务。</summary>
    IReadOnlyList<TaskItem> GetRecentlyUpdated(int count);
}

public interface IStatusService
{
    IReadOnlyList<WorkStatus> GetAll();
    IReadOnlyList<WorkStatus> GetByScope(StatusScope scope);
    WorkStatus? Get(Guid id);
    WorkStatus Create(WorkStatus status);
    WorkStatus Update(WorkStatus status);
    StatusDeleteImpact GetDeleteImpact(Guid statusId);
    /// <summary>删除状态；若仍被任务使用则必须提供迁移目标，否则抛出 InvalidOperationException。</summary>
    void Delete(Guid statusId, Guid? migrateToStatusId);
    void MoveStatus(Guid statusId, int delta);
    WorkStatus GetDefaultStatus(StatusScope scope);
}

public interface IRecurrenceService
{
    IReadOnlyList<RecurrenceRule> GetAll();
    RecurrenceRule? Get(Guid id);
    RecurrenceRule Create(RecurrenceRule rule);
    RecurrenceRule Update(RecurrenceRule rule);
    /// <summary>按作用范围删除周期规则。</summary>
    void Delete(Guid ruleId, RecurrenceDeleteScope scope);
    /// <summary>确保 [from, to] 区间内的周期实例已生成对应独立任务，返回受影响的实例。</summary>
    IReadOnlyList<RecurrenceInstance> EnsureMaterialized(DateTime from, DateTime to);
    /// <summary>跳过某次执行（删除对应任务，并记录跳过以免再次生成）。</summary>
    void SkipOccurrence(Guid ruleId, DateTime occurrenceDate);
    IReadOnlyList<RecurrenceInstance> GetInstances(Guid ruleId);
    int CountFutureInstances(Guid ruleId, DateTime from);
}

public interface ICalendarService
{
    /// <summary>生成整个月的日历数据（含前后补齐日期），必要时为周期规则补齐实例。</summary>
    IReadOnlyList<CalendarDay> GetMonth(DateTime anyDayInMonth);
    IReadOnlyList<CalendarItem> GetDayItems(DateTime date);
}

public interface IDailySummaryService
{
    /// <summary>获取指定日期的总结（不存在时返回空对象，不写库）。</summary>
    DailySummary Get(DateTime date);
    bool Exists(DateTime date);
    DailySummary Save(DailySummary summary);
    void Delete(DateTime date);
    IReadOnlyList<DailySummary> GetRecent(int count);
    IReadOnlyList<DailySummary> Search(string keyword, int limit = 100);
    IReadOnlyList<Guid> GetLinkedTaskIds(DateTime date);
    void SetLinkedTasks(DateTime date, IEnumerable<Guid> taskIds);
}

public interface ITopicLogService
{
    IReadOnlyList<TopicLog> Query(string? keyword, string? category, string? tag);
    IReadOnlyList<TopicLog> GetRecent(int count);
    TopicLog? Get(Guid id);
    IReadOnlyList<string> GetCategories();
    TopicLog Create(TopicLog log);
    /// <summary>保存主题日志；saveBreakpointHistory=true 时把旧断点/旧内容写入历史后再覆盖。</summary>
    TopicLog Update(TopicLog log, bool saveBreakpointHistory, string? changeNote = null);
    void Delete(Guid id);
    IReadOnlyList<TopicLogHistory> GetHistory(Guid topicId);
    void DeleteHistory(Guid historyId);
}

public interface IChecklistService
{
    IReadOnlyList<Checklist> GetAll();
    Checklist? Get(Guid id);
    Checklist Create(Checklist checklist);
    void UpdateHeader(Checklist checklist);
    void Delete(Guid checklistId);
    ChecklistItem AddItem(Guid checklistId, string text, string? note = null);
    ChecklistItem UpdateItem(ChecklistItem item);
    void DeleteItem(Guid itemId);
    void SetItemDone(Guid itemId, bool isDone);
    /// <summary>调整检查项顺序（delta = -1 上移 / +1 下移）。</summary>
    void MoveItem(Guid itemId, int delta);
    // ---- 关联 ----
    void Link(string ownerType, Guid ownerId, Guid checklistId);
    void Unlink(string ownerType, Guid ownerId, Guid checklistId);
    IReadOnlyList<Checklist> GetLinkedChecklists(string ownerType, Guid ownerId);
    /// <summary>反向查询：该清单被哪些对象关联。</summary>
    IReadOnlyList<EntityLink> GetLinksForChecklist(Guid checklistId);
}

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    void ResetLayout();
    string GetStorageDescription();
}

public interface IModuleService
{
    IReadOnlyList<ModuleRuntimeState> GetAll();
    ModuleRuntimeState Get(string moduleId);
    bool IsNavVisible(string moduleId);
    bool IsEnabled(string moduleId);
    void SetEnabled(string moduleId, bool enabled);
    void SetNavVisible(string moduleId, bool visible);
    void MoveNavItem(string moduleId, int delta);
    IReadOnlyList<HomeCardDefinition> GetHomeCardDefinitions();
    IReadOnlyList<HomeCardSetting> GetHomeCards();
    void SetHomeCardVisible(string cardId, bool visible);
    void MoveHomeCard(string cardId, int delta);
}

public interface IBackupService
{
    /// <summary>导出为 JSON 备份文件，返回实际写入路径。</summary>
    string Export(string targetPath);
    BackupInspection Inspect(string filePath);
    BackupImportResult Import(string filePath, BackupImportMode mode);
}

public enum BackupImportMode
{
    /// <summary>合并：跳过已存在的记录，不覆盖当前数据。</summary>
    Merge = 0,
    /// <summary>替换：清空当前业务数据后导入（需用户确认）。</summary>
    Replace = 1,
}

public sealed class BackupInspection
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public DateTime? ExportedAt { get; set; }
    public string? AppVersion { get; set; }
    public int TaskCount { get; set; }
    public int StatusCount { get; set; }
    public int TopicLogCount { get; set; }
    public int DailySummaryCount { get; set; }
    public int ChecklistCount { get; set; }
    public int RecurrenceRuleCount { get; set; }
    public bool HasSettings { get; set; }
}

public sealed class BackupImportResult
{
    public int AddedTasks { get; set; }
    public int SkippedTasks { get; set; }
    public int AddedStatuses { get; set; }
    public int SkippedStatuses { get; set; }
    public int AddedTopicLogs { get; set; }
    public int SkippedTopicLogs { get; set; }
    public int AddedDailySummaries { get; set; }
    public int SkippedDailySummaries { get; set; }
    public int AddedChecklists { get; set; }
    public int SkippedChecklists { get; set; }
    public int AddedRecurrenceRules { get; set; }
    public bool SettingsImported { get; set; }
    public List<string> Warnings { get; set; } = new();

    public int TotalAdded => AddedTasks + AddedStatuses + AddedTopicLogs + AddedDailySummaries + AddedChecklists + AddedRecurrenceRules;
    public int TotalSkipped => SkippedTasks + SkippedStatuses + SkippedTopicLogs + SkippedDailySummaries + SkippedChecklists;

    public string GetSummary()
    {
        var parts = new List<string>
        {
            $"任务 +{AddedTasks}/跳过 {SkippedTasks}",
            $"状态 +{AddedStatuses}/跳过 {SkippedStatuses}",
            $"主题日志 +{AddedTopicLogs}/跳过 {SkippedTopicLogs}",
            $"每日总结 +{AddedDailySummaries}/跳过 {SkippedDailySummaries}",
            $"清单 +{AddedChecklists}/跳过 {SkippedChecklists}",
        };
        if (AddedRecurrenceRules > 0)
        {
            parts.Add($"周期规则 +{AddedRecurrenceRules}");
        }

        if (SettingsImported)
        {
            parts.Add("已导入设置");
        }

        return string.Join("；", parts);
    }
}
