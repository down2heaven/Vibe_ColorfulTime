using System.Text.Json;
using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Backup;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>JSON 备份导出与导入（合并时跳过重复记录，替换时需显式确认）。</summary>
public sealed class BackupService : IBackupService
{
    private readonly WorkbenchDatabase _database;
    private readonly ITaskService _tasks;
    private readonly IStatusService _statuses;
    private readonly IRecurrenceService _recurrences;
    private readonly ITopicLogService _topicLogs;
    private readonly IChecklistService _checklists;
    private readonly ISettingsService _settings;

    public BackupService(
        WorkbenchDatabase database,
        ITaskService tasks,
        IStatusService statuses,
        IRecurrenceService recurrences,
        ITopicLogService topicLogs,
        IChecklistService checklists,
        ISettingsService settings)
    {
        _database = database;
        _tasks = tasks;
        _statuses = statuses;
        _recurrences = recurrences;
        _topicLogs = topicLogs;
        _checklists = checklists;
        _settings = settings;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Export(string targetPath)
    {
        var payload = BuildPayload();
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(targetPath, json);
        return targetPath;
    }

    private BackupPayload BuildPayload()
    {
        var payload = new BackupPayload
        {
            ExportedAt = DateTime.Now,
            AppVersion = AppInfo.Version,
            Statuses = _statuses.GetAll().ToList(),
            Tasks = _tasks.Query(new TaskQuery { Limit = 1_000_000 }).ToList(),
            RecurrenceRules = _recurrences.GetAll().ToList(),
            TopicLogs = _topicLogs.Query(null, null, null).ToList(),
            Checklists = _checklists.GetAll().ToList(),
            Settings = _settings.Load(),
        };

        payload.ChecklistItems = payload.Checklists.SelectMany(c => c.Items).ToList();
        payload.TopicLogHistories = ReadTopicHistory();
        payload.RecurrenceInstances = ReadRecurrenceInstances();
        payload.DailySummaries = ReadDailySummaries();
        payload.DailySummaryTaskLinks = ReadDailySummaryTaskLinks();
        payload.EntityLinks = ReadEntityLinks();
        return payload;
    }

    private List<TopicLogHistory> ReadTopicHistory()
    {
        var list = new List<TopicLogHistory>();
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, topic_id, previous_breakpoint, previous_content, change_note, changed_at FROM topic_log_history;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TopicLogHistory
            {
                Id = WorkbenchDatabase.GetGuid(reader, 0),
                TopicId = WorkbenchDatabase.GetGuid(reader, 1),
                PreviousBreakpoint = WorkbenchDatabase.GetStringOrNull(reader, 2),
                PreviousContent = WorkbenchDatabase.GetStringOrNull(reader, 3),
                ChangeNote = WorkbenchDatabase.GetStringOrNull(reader, 4),
                ChangedAt = WorkbenchDatabase.GetDateTime(reader, 5),
            });
        }

        return list;
    }

    private List<RecurrenceInstance> ReadRecurrenceInstances()
    {
        var list = new List<RecurrenceInstance>();
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, rule_id, occurrence_date, task_id, is_skipped, created_at FROM recurrence_instances;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new RecurrenceInstance
            {
                Id = WorkbenchDatabase.GetGuid(reader, 0),
                RuleId = WorkbenchDatabase.GetGuid(reader, 1),
                OccurrenceDate = WorkbenchDatabase.ParseDate(reader.GetString(2)),
                TaskId = WorkbenchDatabase.GetGuidOrNull(reader, 3),
                IsSkipped = reader.GetInt32(4) != 0,
                CreatedAt = WorkbenchDatabase.GetDateTime(reader, 5),
            });
        }

        return list;
    }

    private List<DailySummary> ReadDailySummaries()
    {
        var list = new List<DailySummary>();
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, date, summary, completed_items, problems, pending_items, tomorrow_plan, created_at, updated_at
            FROM daily_summaries ORDER BY date;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new DailySummary
            {
                Id = WorkbenchDatabase.GetGuid(reader, 0),
                Date = WorkbenchDatabase.ParseDate(reader.GetString(1)),
                Summary = WorkbenchDatabase.GetStringOrNull(reader, 2),
                CompletedItems = WorkbenchDatabase.GetStringOrNull(reader, 3),
                Problems = WorkbenchDatabase.GetStringOrNull(reader, 4),
                PendingItems = WorkbenchDatabase.GetStringOrNull(reader, 5),
                TomorrowPlan = WorkbenchDatabase.GetStringOrNull(reader, 6),
                CreatedAt = WorkbenchDatabase.GetDateTime(reader, 7),
                UpdatedAt = WorkbenchDatabase.GetDateTime(reader, 8),
            });
        }

        return list;
    }

    private List<DailySummaryTaskLink> ReadDailySummaryTaskLinks()
    {
        var list = new List<DailySummaryTaskLink>();
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT summary_date, task_id FROM daily_summary_tasks;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new DailySummaryTaskLink
            {
                SummaryDate = WorkbenchDatabase.ParseDate(reader.GetString(0)),
                TaskId = Guid.Parse(reader.GetString(1)),
            });
        }

        return list;
    }

    private List<EntityLink> ReadEntityLinks()
    {
        var list = new List<EntityLink>();
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, owner_type, owner_id, target_type, target_id, created_at FROM entity_links;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new EntityLink
            {
                Id = WorkbenchDatabase.GetGuid(reader, 0),
                OwnerType = reader.GetString(1),
                OwnerId = WorkbenchDatabase.GetGuid(reader, 2),
                TargetType = reader.GetString(3),
                TargetId = WorkbenchDatabase.GetGuid(reader, 4),
                CreatedAt = WorkbenchDatabase.GetDateTime(reader, 5),
            });
        }

        return list;
    }

    public BackupInspection Inspect(string filePath)
    {
        var inspection = new BackupInspection();
        try
        {
            if (!File.Exists(filePath))
            {
                inspection.Error = "文件不存在。";
                return inspection;
            }

            var json = File.ReadAllText(filePath);
            var payload = JsonSerializer.Deserialize<BackupPayload>(json);
            if (payload is null || payload.Format != "simworkbench-backup")
            {
                inspection.Error = "不是有效的仿真工程师工作台备份文件。";
                return inspection;
            }

            inspection.IsValid = true;
            inspection.ExportedAt = payload.ExportedAt;
            inspection.AppVersion = payload.AppVersion;
            inspection.TaskCount = payload.Tasks.Count;
            inspection.StatusCount = payload.Statuses.Count;
            inspection.TopicLogCount = payload.TopicLogs.Count;
            inspection.DailySummaryCount = payload.DailySummaries.Count;
            inspection.ChecklistCount = payload.Checklists.Count;
            inspection.RecurrenceRuleCount = payload.RecurrenceRules.Count;
            inspection.HasSettings = payload.Settings is not null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            inspection.Error = ex.Message;
        }

        return inspection;
    }

    public BackupImportResult Import(string filePath, BackupImportMode mode)
    {
        var payload = JsonSerializer.Deserialize<BackupPayload>(File.ReadAllText(filePath))
                      ?? throw new InvalidOperationException("备份文件内容为空。");
        if (payload.Format != "simworkbench-backup")
        {
            throw new InvalidOperationException("不是有效的仿真工作台备份文件。");
        }

        var result = new BackupImportResult();

        // 在开启写事务前读取现有状态，避免跨连接读取被写锁阻塞
        var knownStatusIds = _statuses.GetAll().Select(s => s.Id).ToHashSet();
        knownStatusIds.UnionWith(payload.Statuses.Select(s => s.Id));
        var fallbackStatus = _statuses.GetDefaultStatus(StatusScope.General).Id;
        var fallbackSimStatus = _statuses.GetDefaultStatus(StatusScope.Simulation).Id;

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        if (mode == BackupImportMode.Replace)
        {
            ClearBusinessTables(connection, transaction);
        }

        foreach (var status in payload.Statuses)
        {
            var added = Insert(connection, transaction, "work_statuses",
                new Dictionary<string, object?>
                {
                    ["id"] = status.Id.ToString("D"),
                    ["scope"] = (int)status.Scope,
                    ["name"] = status.Name,
                    ["color"] = status.Color,
                    ["sort_order"] = status.SortOrder,
                    ["is_protected"] = status.IsProtected ? 1 : 0,
                });
            result.AddedStatuses += added;
            result.SkippedStatuses += 1 - added;
        }

        foreach (var rule in payload.RecurrenceRules)
        {
            var statusId = knownStatusIds.Contains(rule.StatusId)
                ? rule.StatusId
                : rule.Kind == TaskKind.Simulation ? fallbackSimStatus : fallbackStatus;
            var added = Insert(connection, transaction, "recurrence_rules",
                new Dictionary<string, object?>
                {
                    ["id"] = rule.Id.ToString("D"),
                    ["title"] = rule.Title,
                    ["kind"] = (int)rule.Kind,
                    ["status_id"] = statusId.ToString("D"),
                    ["description"] = WorkbenchDatabase.Db(rule.Description),
                    ["category"] = WorkbenchDatabase.Db(rule.Category),
                    ["tags"] = WorkbenchDatabase.Db(rule.Tags),
                    ["priority"] = (int)rule.Priority,
                    ["case_name"] = WorkbenchDatabase.Db(rule.CaseName),
                    ["case_condition"] = WorkbenchDatabase.Db(rule.CaseCondition),
                    ["estimated_minutes"] = WorkbenchDatabase.Db(rule.EstimatedMinutes),
                    ["calculation_notes"] = WorkbenchDatabase.Db(rule.CalculationNotes),
                    ["frequency"] = (int)rule.Frequency,
                    ["interval"] = Math.Max(1, rule.Interval),
                    ["week_days"] = WorkbenchDatabase.Db(rule.WeekDays),
                    ["start_date"] = rule.StartDate.ToString(WorkbenchDatabase.DateFormat),
                    ["end_date"] = WorkbenchDatabase.DbDate(rule.EndDate),
                    ["is_active"] = rule.IsActive ? 1 : 0,
                    ["created_at"] = rule.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                    ["updated_at"] = rule.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                });
            result.AddedRecurrenceRules += added;
        }

        // 任务引用 recurrence_rules(id)，必须晚于规则插入
        foreach (var task in payload.Tasks)
        {
            if (!knownStatusIds.Contains(task.StatusId))
            {
                task.StatusId = task.Kind == TaskKind.Simulation ? fallbackSimStatus : fallbackStatus;
                result.Warnings.Add($"任务“{task.Title}”的状态不存在，已映射到默认状态。");
            }

            var added = Insert(connection, transaction, "tasks", TaskColumns(task));
            result.AddedTasks += added;
            result.SkippedTasks += 1 - added;
        }

        foreach (var instance in payload.RecurrenceInstances)
        {
            Insert(connection, transaction, "recurrence_instances",
                new Dictionary<string, object?>
                {
                    ["id"] = instance.Id.ToString("D"),
                    ["rule_id"] = instance.RuleId.ToString("D"),
                    ["occurrence_date"] = instance.OccurrenceDate.ToString(WorkbenchDatabase.DateFormat),
                    ["task_id"] = WorkbenchDatabase.Db(instance.TaskId),
                    ["is_skipped"] = instance.IsSkipped ? 1 : 0,
                    ["created_at"] = instance.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                });
        }

        foreach (var summary in payload.DailySummaries)
        {
            var added = Insert(connection, transaction, "daily_summaries",
                new Dictionary<string, object?>
                {
                    ["id"] = summary.Id.ToString("D"),
                    ["date"] = summary.Date.ToString(WorkbenchDatabase.DateFormat),
                    ["summary"] = WorkbenchDatabase.Db(summary.Summary),
                    ["completed_items"] = WorkbenchDatabase.Db(summary.CompletedItems),
                    ["problems"] = WorkbenchDatabase.Db(summary.Problems),
                    ["pending_items"] = WorkbenchDatabase.Db(summary.PendingItems),
                    ["tomorrow_plan"] = WorkbenchDatabase.Db(summary.TomorrowPlan),
                    ["created_at"] = summary.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                    ["updated_at"] = summary.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                });
            result.AddedDailySummaries += added;
            result.SkippedDailySummaries += 1 - added;
        }

        foreach (var link in payload.DailySummaryTaskLinks)
        {
            Insert(connection, transaction, "daily_summary_tasks",
                new Dictionary<string, object?>
                {
                    ["summary_date"] = link.SummaryDate.ToString(WorkbenchDatabase.DateFormat),
                    ["task_id"] = link.TaskId.ToString("D"),
                });
        }

        foreach (var topic in payload.TopicLogs)
        {
            var added = Insert(connection, transaction, "topic_logs",
                new Dictionary<string, object?>
                {
                    ["id"] = topic.Id.ToString("D"),
                    ["title"] = topic.Title,
                    ["category"] = WorkbenchDatabase.Db(topic.Category),
                    ["current_breakpoint"] = WorkbenchDatabase.Db(topic.CurrentBreakpoint),
                    ["content"] = WorkbenchDatabase.Db(topic.Content),
                    ["tags"] = WorkbenchDatabase.Db(topic.Tags),
                    ["created_at"] = topic.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                    ["updated_at"] = topic.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                });
            result.AddedTopicLogs += added;
            result.SkippedTopicLogs += 1 - added;
        }

        foreach (var history in payload.TopicLogHistories)
        {
            Insert(connection, transaction, "topic_log_history",
                new Dictionary<string, object?>
                {
                    ["id"] = history.Id.ToString("D"),
                    ["topic_id"] = history.TopicId.ToString("D"),
                    ["previous_breakpoint"] = WorkbenchDatabase.Db(history.PreviousBreakpoint),
                    ["previous_content"] = WorkbenchDatabase.Db(history.PreviousContent),
                    ["change_note"] = WorkbenchDatabase.Db(history.ChangeNote),
                    ["changed_at"] = history.ChangedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                });
        }

        foreach (var checklist in payload.Checklists)
        {
            var added = Insert(connection, transaction, "checklists",
                new Dictionary<string, object?>
                {
                    ["id"] = checklist.Id.ToString("D"),
                    ["name"] = checklist.Name,
                    ["category"] = WorkbenchDatabase.Db(checklist.Category),
                    ["note"] = WorkbenchDatabase.Db(checklist.Note),
                    ["created_at"] = checklist.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                    ["updated_at"] = checklist.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                });
            result.AddedChecklists += added;
            result.SkippedChecklists += 1 - added;
        }

        foreach (var item in payload.ChecklistItems)
        {
            Insert(connection, transaction, "checklist_items",
                new Dictionary<string, object?>
                {
                    ["id"] = item.Id.ToString("D"),
                    ["checklist_id"] = item.ChecklistId.ToString("D"),
                    ["text"] = item.Text,
                    ["is_done"] = item.IsDone ? 1 : 0,
                    ["sort_order"] = item.SortOrder,
                    ["note"] = WorkbenchDatabase.Db(item.Note),
                    ["created_at"] = item.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                    ["updated_at"] = item.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                });
        }

        foreach (var link in payload.EntityLinks)
        {
            Insert(connection, transaction, "entity_links",
                new Dictionary<string, object?>
                {
                    ["id"] = link.Id.ToString("D"),
                    ["owner_type"] = link.OwnerType,
                    ["owner_id"] = link.OwnerId.ToString("D"),
                    ["target_type"] = link.TargetType,
                    ["target_id"] = link.TargetId.ToString("D"),
                    ["created_at"] = link.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
                });
        }

        transaction.Commit();

        if (payload.Settings is not null)
        {
            _settings.Save(payload.Settings);
            result.SettingsImported = true;
        }

        return result;
    }

    private static void ClearBusinessTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        var tables = new[]
        {
            "entity_links", "checklist_items", "checklists", "topic_log_history", "topic_logs",
            "daily_summary_tasks", "daily_summaries", "recurrence_instances", "tasks",
        };
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table};";
            command.ExecuteNonQuery();
        }
    }

    private static Dictionary<string, object?> TaskColumns(TaskItem task) => new()
    {
        ["id"] = task.Id.ToString("D"),
        ["title"] = task.Title,
        ["kind"] = (int)task.Kind,
        ["status_id"] = task.StatusId.ToString("D"),
        ["description"] = WorkbenchDatabase.Db(task.Description),
        ["created_at"] = task.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
        ["planned_start"] = WorkbenchDatabase.Db(task.PlannedStart),
        ["due_at"] = WorkbenchDatabase.Db(task.DueAt),
        ["actual_delivery_at"] = WorkbenchDatabase.Db(task.ActualDeliveryAt),
        ["category"] = WorkbenchDatabase.Db(task.Category),
        ["tags"] = WorkbenchDatabase.Db(task.Tags),
        ["priority"] = (int)task.Priority,
        ["case_name"] = WorkbenchDatabase.Db(task.CaseName),
        ["case_condition"] = WorkbenchDatabase.Db(task.CaseCondition),
        ["estimated_minutes"] = WorkbenchDatabase.Db(task.EstimatedMinutes),
        ["calculation_notes"] = WorkbenchDatabase.Db(task.CalculationNotes),
        ["recurrence_id"] = WorkbenchDatabase.Db(task.RecurrenceId),
        ["occurrence_date"] = WorkbenchDatabase.DbDate(task.OccurrenceDate),
        ["updated_at"] = task.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat),
    };

    private static int Insert(SqliteConnection connection, SqliteTransaction transaction, string table,
        IReadOnlyDictionary<string, object?> values)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var columns = string.Join(", ", values.Keys);
        var parameters = string.Join(", ", values.Keys.Select((_, index) => $"$p{index}"));
        command.CommandText = $"INSERT OR IGNORE INTO {table} ({columns}) VALUES ({parameters});";
        var i = 0;
        foreach (var value in values.Values)
        {
            command.Parameters.AddWithValue($"$p{i++}", value ?? DBNull.Value);
        }

        return command.ExecuteNonQuery();
    }
}
