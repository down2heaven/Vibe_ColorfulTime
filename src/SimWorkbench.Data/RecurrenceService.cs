using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>周期规则与实例生成：每次执行生成独立任务，互不影响。</summary>
public sealed class RecurrenceService : IRecurrenceService
{
    private const int HorizonDays = 30;
    private const int PastHorizonDays = 3;

    private readonly WorkbenchDatabase _database;
    private readonly ITaskService _tasks;
    private readonly IStatusService _statuses;

    public RecurrenceService(WorkbenchDatabase database, ITaskService tasks, IStatusService statuses)
    {
        _database = database;
        _tasks = tasks;
        _statuses = statuses;
    }

    private const string Columns = """
        id, title, kind, status_id, description, category, tags, priority, case_name, case_condition,
        estimated_minutes, calculation_notes, frequency, interval, week_days, start_date, end_date,
        is_active, created_at, updated_at
        """;

    private static RecurrenceRule Read(SqliteDataReader reader) => new()
    {
        Id = WorkbenchDatabase.GetGuid(reader, 0),
        Title = reader.GetString(1),
        Kind = (TaskKind)reader.GetInt32(2),
        StatusId = WorkbenchDatabase.GetGuid(reader, 3),
        Description = WorkbenchDatabase.GetStringOrNull(reader, 4),
        Category = WorkbenchDatabase.GetStringOrNull(reader, 5),
        Tags = WorkbenchDatabase.GetStringOrNull(reader, 6),
        Priority = (Priority)reader.GetInt32(7),
        CaseName = WorkbenchDatabase.GetStringOrNull(reader, 8),
        CaseCondition = WorkbenchDatabase.GetStringOrNull(reader, 9),
        EstimatedMinutes = WorkbenchDatabase.GetIntOrNull(reader, 10),
        CalculationNotes = WorkbenchDatabase.GetStringOrNull(reader, 11),
        Frequency = (RecurrenceFrequency)reader.GetInt32(12),
        Interval = Math.Max(1, reader.GetInt32(13)),
        WeekDays = WorkbenchDatabase.GetStringOrNull(reader, 14),
        StartDate = WorkbenchDatabase.ParseDate(reader.GetString(15)),
        EndDate = WorkbenchDatabase.GetDateOrNull(reader, 16),
        IsActive = reader.GetInt32(17) != 0,
        CreatedAt = WorkbenchDatabase.GetDateTime(reader, 18),
        UpdatedAt = WorkbenchDatabase.GetDateTime(reader, 19),
    };

    public IReadOnlyList<RecurrenceRule> GetAll()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM recurrence_rules ORDER BY created_at DESC;";
        using var reader = command.ExecuteReader();
        var list = new List<RecurrenceRule>();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public RecurrenceRule? Get(Guid id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM recurrence_rules WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public RecurrenceRule Create(RecurrenceRule rule)
    {
        Validate(rule);
        rule.CreatedAt = DateTime.Now;
        rule.UpdatedAt = DateTime.Now;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO recurrence_rules (id, title, kind, status_id, description, category, tags, priority,
                case_name, case_condition, estimated_minutes, calculation_notes, frequency, interval, week_days,
                start_date, end_date, is_active, created_at, updated_at)
            VALUES ($id, $title, $kind, $status, $description, $category, $tags, $priority,
                $caseName, $caseCondition, $estimated, $calcNotes, $frequency, $interval, $weekDays,
                $startDate, $endDate, $isActive, $createdAt, $updatedAt);
            """;
        Bind(command, rule);
        command.ExecuteNonQuery();

        EnsureMaterialized(DateTime.Today.AddDays(-PastHorizonDays), DateTime.Today.AddDays(HorizonDays));
        return rule;
    }

    private static void Validate(RecurrenceRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Title))
        {
            throw new ArgumentException("周期任务名称不能为空。", nameof(rule));
        }

        if (rule.Interval < 1)
        {
            throw new ArgumentException("重复间隔必须大于 0。", nameof(rule));
        }

        if (rule.EndDate is { } end && end.Date < rule.StartDate.Date)
        {
            throw new ArgumentException("结束日期不能早于开始日期。", nameof(rule));
        }
    }

    private static void Bind(SqliteCommand command, RecurrenceRule rule)
    {
        command.Parameters.AddWithValue("$id", rule.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", rule.Title.Trim());
        command.Parameters.AddWithValue("$kind", (int)rule.Kind);
        command.Parameters.AddWithValue("$status", rule.StatusId.ToString("D"));
        command.Parameters.AddWithValue("$description", WorkbenchDatabase.Db(rule.Description));
        command.Parameters.AddWithValue("$category", WorkbenchDatabase.Db(rule.Category));
        command.Parameters.AddWithValue("$tags", WorkbenchDatabase.Db(rule.Tags));
        command.Parameters.AddWithValue("$priority", (int)rule.Priority);
        command.Parameters.AddWithValue("$caseName", WorkbenchDatabase.Db(rule.CaseName));
        command.Parameters.AddWithValue("$caseCondition", WorkbenchDatabase.Db(rule.CaseCondition));
        command.Parameters.AddWithValue("$estimated", WorkbenchDatabase.Db(rule.EstimatedMinutes));
        command.Parameters.AddWithValue("$calcNotes", WorkbenchDatabase.Db(rule.CalculationNotes));
        command.Parameters.AddWithValue("$frequency", (int)rule.Frequency);
        command.Parameters.AddWithValue("$interval", Math.Max(1, rule.Interval));
        command.Parameters.AddWithValue("$weekDays", WorkbenchDatabase.Db(rule.WeekDays));
        command.Parameters.AddWithValue("$startDate", rule.StartDate.ToString(WorkbenchDatabase.DateFormat));
        command.Parameters.AddWithValue("$endDate", WorkbenchDatabase.DbDate(rule.EndDate));
        command.Parameters.AddWithValue("$isActive", rule.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", rule.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.Parameters.AddWithValue("$updatedAt", rule.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
    }

    /// <summary>修改规则：重建今天及以后的实例，历史实例保持不变。</summary>
    public RecurrenceRule Update(RecurrenceRule rule)
    {
        Validate(rule);
        rule.UpdatedAt = DateTime.Now;
        RemoveFutureInstances(rule.Id, DateTime.Today, keepRule: true);

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE recurrence_rules SET
                title = $title, kind = $kind, status_id = $status, description = $description,
                category = $category, tags = $tags, priority = $priority, case_name = $caseName,
                case_condition = $caseCondition, estimated_minutes = $estimated,
                calculation_notes = $calcNotes, frequency = $frequency, interval = $interval,
                week_days = $weekDays, start_date = $startDate, end_date = $endDate,
                is_active = $isActive, updated_at = $updatedAt
            WHERE id = $id;
            """;
        Bind(command, rule);
        command.ExecuteNonQuery();

        EnsureMaterialized(DateTime.Today, DateTime.Today.AddDays(HorizonDays));
        return rule;
    }

    private void RemoveFutureInstances(Guid ruleId, DateTime fromDate, bool keepRule)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var taskIds = new List<Guid>();
        using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = """
                SELECT task_id FROM recurrence_instances
                WHERE rule_id = $rule AND occurrence_date >= $from AND task_id IS NOT NULL;
                """;
            query.Parameters.AddWithValue("$rule", ruleId.ToString("D"));
            query.Parameters.AddWithValue("$from", fromDate.ToString(WorkbenchDatabase.DateFormat));
            using var reader = query.ExecuteReader();
            while (reader.Read())
            {
                taskIds.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        using (var deleteInstances = connection.CreateCommand())
        {
            deleteInstances.Transaction = transaction;
            deleteInstances.CommandText = "DELETE FROM recurrence_instances WHERE rule_id = $rule AND occurrence_date >= $from;";
            deleteInstances.Parameters.AddWithValue("$rule", ruleId.ToString("D"));
            deleteInstances.Parameters.AddWithValue("$from", fromDate.ToString(WorkbenchDatabase.DateFormat));
            deleteInstances.ExecuteNonQuery();
        }

        transaction.Commit();
        foreach (var taskId in taskIds)
        {
            _tasks.Delete(taskId);
        }
    }

    public void Delete(Guid ruleId, RecurrenceDeleteScope scope)
    {
        switch (scope)
        {
            case RecurrenceDeleteScope.RuleOnly:
                DeleteRuleRow(ruleId);
                break;

            case RecurrenceDeleteScope.RuleAndFutureInstances:
                RemoveFutureInstances(ruleId, DateTime.Today, keepRule: true);
                DeleteRuleRow(ruleId);
                break;

            case RecurrenceDeleteScope.RuleAndInstances:
            {
                var taskIds = GetInstanceTaskIds(ruleId, null);
                RemoveFutureInstances(ruleId, DateTime.MinValue.Date, keepRule: true);
                foreach (var taskId in taskIds)
                {
                    _tasks.Delete(taskId);
                }

                DeleteRuleRow(ruleId);
                break;
            }
        }
    }

    private void DeleteRuleRow(Guid ruleId)
    {
        using var connection = _database.Open();
        using (var clearRefs = connection.CreateCommand())
        {
            clearRefs.CommandText = "UPDATE tasks SET recurrence_id = NULL WHERE recurrence_id = $id;";
            clearRefs.Parameters.AddWithValue("$id", ruleId.ToString("D"));
            clearRefs.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM recurrence_rules WHERE id = $id;";
        command.Parameters.AddWithValue("$id", ruleId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private List<Guid> GetInstanceTaskIds(Guid ruleId, DateTime? fromDate)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id FROM recurrence_instances WHERE rule_id = $rule AND task_id IS NOT NULL;";
        command.Parameters.AddWithValue("$rule", ruleId.ToString("D"));
        using var reader = command.ExecuteReader();
        var ids = new List<Guid>();
        while (reader.Read())
        {
            ids.Add(Guid.Parse(reader.GetString(0)));
        }

        return ids;
    }

    public IReadOnlyList<RecurrenceInstance> EnsureMaterialized(DateTime from, DateTime to)
    {
        var result = new List<RecurrenceInstance>();
        var rules = GetAll().Where(r => r.IsActive).ToList();
        foreach (var rule in rules)
        {
            var occurrences = RecurrenceCalculator.GetOccurrences(rule, from, to);
            if (occurrences.Count == 0)
            {
                continue;
            }

            var existing = GetInstanceMap(rule.Id);
            foreach (var date in occurrences)
            {
                var key = date.Date;
                if (existing.TryGetValue(key, out var instance))
                {
                    result.Add(instance);
                    continue;
                }

                var task = BuildInstanceTask(rule, date);
                var created = _tasks.Create(task);
                var record = new RecurrenceInstance
                {
                    RuleId = rule.Id,
                    OccurrenceDate = key,
                    TaskId = created.Id,
                };
                InsertInstance(record);
                result.Add(record);
                existing[key] = record;
            }
        }

        return result;
    }

    private TaskItem BuildInstanceTask(RecurrenceRule rule, DateTime occurrence)
    {
        var statusId = rule.StatusId;
        if (_statuses.Get(statusId) is null)
        {
            statusId = _statuses.GetDefaultStatus(rule.Kind == TaskKind.Simulation
                ? StatusScope.Simulation
                : StatusScope.General).Id;
        }

        return new TaskItem
        {
            Title = rule.Title,
            Kind = rule.Kind,
            StatusId = statusId,
            Description = rule.Description,
            Category = rule.Category,
            Tags = rule.Tags,
            Priority = rule.Priority,
            CaseName = rule.CaseName,
            CaseCondition = rule.CaseCondition,
            EstimatedMinutes = rule.EstimatedMinutes,
            CalculationNotes = rule.CalculationNotes,
            CreatedAt = DateTime.Now,
            PlannedStart = occurrence.Date.AddHours(9),
            DueAt = occurrence.Date.AddHours(18),
            RecurrenceId = rule.Id,
            OccurrenceDate = occurrence.Date,
        };
    }

    private Dictionary<DateTime, RecurrenceInstance> GetInstanceMap(Guid ruleId)
    {
        var map = new Dictionary<DateTime, RecurrenceInstance>();
        foreach (var instance in GetInstances(ruleId))
        {
            map[instance.OccurrenceDate.Date] = instance;
        }

        return map;
    }

    private void InsertInstance(RecurrenceInstance instance)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO recurrence_instances (id, rule_id, occurrence_date, task_id, is_skipped, created_at)
            VALUES ($id, $rule, $date, $task, $skipped, $created);
            """;
        command.Parameters.AddWithValue("$id", instance.Id.ToString("D"));
        command.Parameters.AddWithValue("$rule", instance.RuleId.ToString("D"));
        command.Parameters.AddWithValue("$date", instance.OccurrenceDate.ToString(WorkbenchDatabase.DateFormat));
        command.Parameters.AddWithValue("$task", WorkbenchDatabase.Db(instance.TaskId));
        command.Parameters.AddWithValue("$skipped", instance.IsSkipped ? 1 : 0);
        command.Parameters.AddWithValue("$created", instance.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.ExecuteNonQuery();
    }

    public void SkipOccurrence(Guid ruleId, DateTime occurrenceDate)
    {
        var date = occurrenceDate.Date;
        var instances = GetInstances(ruleId);
        var instance = instances.FirstOrDefault(i => i.OccurrenceDate.Date == date);
        if (instance?.TaskId is { } taskId)
        {
            _tasks.Delete(taskId);
        }

        if (instance is null)
        {
            instance = new RecurrenceInstance { RuleId = ruleId, OccurrenceDate = date };
        }

        instance.TaskId = null;
        instance.IsSkipped = true;
        InsertInstance(instance);
    }

    public IReadOnlyList<RecurrenceInstance> GetInstances(Guid ruleId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, rule_id, occurrence_date, task_id, is_skipped, created_at
            FROM recurrence_instances WHERE rule_id = $rule ORDER BY occurrence_date;
            """;
        command.Parameters.AddWithValue("$rule", ruleId.ToString("D"));
        using var reader = command.ExecuteReader();
        var list = new List<RecurrenceInstance>();
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

    public int CountFutureInstances(Guid ruleId, DateTime from)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM recurrence_instances
            WHERE rule_id = $rule AND occurrence_date >= $from AND is_skipped = 0;
            """;
        command.Parameters.AddWithValue("$rule", ruleId.ToString("D"));
        command.Parameters.AddWithValue("$from", from.Date.ToString(WorkbenchDatabase.DateFormat));
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
