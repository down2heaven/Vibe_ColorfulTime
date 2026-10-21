using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

internal static class TaskMapper
{
    public const string Columns = """
        id, title, kind, status_id, description, created_at, planned_start, due_at,
        actual_delivery_at, category, tags, priority, case_name, case_condition,
        estimated_minutes, calculation_notes, recurrence_id, occurrence_date, updated_at
        """;

    public static TaskItem Read(SqliteDataReader reader)
    {
        var task = new TaskItem
        {
            Id = WorkbenchDatabase.GetGuid(reader, 0),
            Title = reader.GetString(1),
            Kind = (TaskKind)reader.GetInt32(2),
            StatusId = WorkbenchDatabase.GetGuid(reader, 3),
            Description = WorkbenchDatabase.GetStringOrNull(reader, 4),
            CreatedAt = WorkbenchDatabase.GetDateTime(reader, 5),
            PlannedStart = WorkbenchDatabase.GetDateTimeOrNull(reader, 6),
            DueAt = WorkbenchDatabase.GetDateTimeOrNull(reader, 7),
            ActualDeliveryAt = WorkbenchDatabase.GetDateTimeOrNull(reader, 8),
            Category = WorkbenchDatabase.GetStringOrNull(reader, 9),
            Tags = WorkbenchDatabase.GetStringOrNull(reader, 10),
            Priority = (Priority)reader.GetInt32(11),
            CaseName = WorkbenchDatabase.GetStringOrNull(reader, 12),
            CaseCondition = WorkbenchDatabase.GetStringOrNull(reader, 13),
            EstimatedMinutes = WorkbenchDatabase.GetIntOrNull(reader, 14),
            CalculationNotes = WorkbenchDatabase.GetStringOrNull(reader, 15),
            RecurrenceId = WorkbenchDatabase.GetGuidOrNull(reader, 16),
            OccurrenceDate = WorkbenchDatabase.GetDateOrNull(reader, 17),
            UpdatedAt = WorkbenchDatabase.GetDateTime(reader, 18),
        };
        return task;
    }
}

/// <summary>任务数据访问（SQLite 实现）。</summary>
public sealed class TaskService : ITaskService
{
    private readonly WorkbenchDatabase _database;

    public TaskService(WorkbenchDatabase database) => _database = database;

    public IReadOnlyList<TaskItem> Query(TaskQuery query)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        var where = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            where.Add("""
                (title LIKE $search OR IFNULL(description,'') LIKE $search OR IFNULL(case_name,'') LIKE $search
                 OR IFNULL(tags,'') LIKE $search OR IFNULL(category,'') LIKE $search)
                """);
            command.Parameters.AddWithValue("$search", $"%{query.SearchText.Trim()}%");
        }

        if (query.Kind is { } kind)
        {
            where.Add("kind = $kind");
            command.Parameters.AddWithValue("$kind", (int)kind);
        }

        if (query.StatusId is { } statusId)
        {
            where.Add("status_id = $status");
            command.Parameters.AddWithValue("$status", statusId.ToString("D"));
        }

        if (query.Priority is { } priority)
        {
            where.Add("priority = $priority");
            command.Parameters.AddWithValue("$priority", (int)priority);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            where.Add("category = $category");
            command.Parameters.AddWithValue("$category", query.Category.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            where.Add("IFNULL(tags,'') LIKE $tag");
            command.Parameters.AddWithValue("$tag", $"%{query.Tag.Trim()}%");
        }

        if (query.OnlyOpen)
        {
            where.Add("actual_delivery_at IS NULL");
        }

        if (query.OnlyOverdue)
        {
            where.Add("due_at IS NOT NULL AND actual_delivery_at IS NULL AND date(due_at) < date('now','localtime')");
        }

        if (query.OnlyDueSoon)
        {
            where.Add("""
                due_at IS NOT NULL AND actual_delivery_at IS NULL
                AND date(due_at) >= date('now','localtime')
                AND date(due_at) <= date('now','localtime','+3 day')
                """);
        }

        if (query.DueBefore is { } dueBefore)
        {
            where.Add("date(due_at) <= $dueBefore");
            command.Parameters.AddWithValue("$dueBefore", dueBefore.ToString(WorkbenchDatabase.DateFormat));
        }

        if (query.DueAfter is { } dueAfter)
        {
            where.Add("date(due_at) >= $dueAfter");
            command.Parameters.AddWithValue("$dueAfter", dueAfter.ToString(WorkbenchDatabase.DateFormat));
        }

        if (query.RecurrenceId is { } recurrenceId)
        {
            where.Add("recurrence_id = $recurrence");
            command.Parameters.AddWithValue("$recurrence", recurrenceId.ToString("D"));
        }

        var orderBy = query.SortField switch
        {
            TaskSortField.CreatedAt => "created_at",
            TaskSortField.Title => "title COLLATE NOCASE",
            TaskSortField.Priority => "priority",
            TaskSortField.UpdatedAt => "updated_at",
            TaskSortField.Status => "status_id",
            _ => "CASE WHEN due_at IS NULL THEN 1 ELSE 0 END, due_at",
        };
        var direction = query.Descending ? "DESC" : "ASC";
        var sql = $"SELECT {TaskMapper.Columns} FROM tasks";
        if (where.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", where);
        }

        sql += $" ORDER BY {orderBy} {direction}, created_at DESC";
        if (query.Limit > 0)
        {
            sql += " LIMIT $limit";
            command.Parameters.AddWithValue("$limit", query.Limit);
        }

        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<TaskItem>();
        while (reader.Read())
        {
            list.Add(TaskMapper.Read(reader));
        }

        return list;
    }

    public TaskItem? Get(Guid id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {TaskMapper.Columns} FROM tasks WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? TaskMapper.Read(reader) : null;
    }

    public TaskItem Create(TaskItem task)
    {
        if (string.IsNullOrWhiteSpace(task.Title))
        {
            throw new ArgumentException("任务名称不能为空。", nameof(task));
        }

        task.UpdatedAt = DateTime.Now;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tasks (id, title, kind, status_id, description, created_at, planned_start, due_at,
                actual_delivery_at, category, tags, priority, case_name, case_condition,
                estimated_minutes, calculation_notes, recurrence_id, occurrence_date, updated_at)
            VALUES ($id, $title, $kind, $status, $description, $created, $plannedStart, $dueAt,
                $actualDelivery, $category, $tags, $priority, $caseName, $caseCondition,
                $estimated, $calcNotes, $recurrence, $occurrence, $updated);
            """;
        BindTask(command, task);
        command.ExecuteNonQuery();
        return task;
    }

    public TaskItem Update(TaskItem task)
    {
        if (string.IsNullOrWhiteSpace(task.Title))
        {
            throw new ArgumentException("任务名称不能为空。", nameof(task));
        }

        task.UpdatedAt = DateTime.Now;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tasks SET
                title = $title, kind = $kind, status_id = $status, description = $description,
                created_at = $created, planned_start = $plannedStart, due_at = $dueAt,
                actual_delivery_at = $actualDelivery, category = $category, tags = $tags,
                priority = $priority, case_name = $caseName, case_condition = $caseCondition,
                estimated_minutes = $estimated, calculation_notes = $calcNotes,
                recurrence_id = $recurrence, occurrence_date = $occurrence, updated_at = $updated
            WHERE id = $id;
            """;
        BindTask(command, task);
        command.ExecuteNonQuery();
        return task;
    }

    private static void BindTask(SqliteCommand command, TaskItem task)
    {
        command.Parameters.AddWithValue("$id", task.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", task.Title.Trim());
        command.Parameters.AddWithValue("$kind", (int)task.Kind);
        command.Parameters.AddWithValue("$status", task.StatusId.ToString("D"));
        command.Parameters.AddWithValue("$description", WorkbenchDatabase.Db(task.Description));
        command.Parameters.AddWithValue("$created", task.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.Parameters.AddWithValue("$plannedStart", WorkbenchDatabase.Db(task.PlannedStart));
        command.Parameters.AddWithValue("$dueAt", WorkbenchDatabase.Db(task.DueAt));
        command.Parameters.AddWithValue("$actualDelivery", WorkbenchDatabase.Db(task.ActualDeliveryAt));
        command.Parameters.AddWithValue("$category", WorkbenchDatabase.Db(task.Category));
        command.Parameters.AddWithValue("$tags", WorkbenchDatabase.Db(task.Tags));
        command.Parameters.AddWithValue("$priority", (int)task.Priority);
        command.Parameters.AddWithValue("$caseName", WorkbenchDatabase.Db(task.CaseName));
        command.Parameters.AddWithValue("$caseCondition", WorkbenchDatabase.Db(task.CaseCondition));
        command.Parameters.AddWithValue("$estimated", WorkbenchDatabase.Db(task.EstimatedMinutes));
        command.Parameters.AddWithValue("$calcNotes", WorkbenchDatabase.Db(task.CalculationNotes));
        command.Parameters.AddWithValue("$recurrence", WorkbenchDatabase.Db(task.RecurrenceId));
        command.Parameters.AddWithValue("$occurrence", WorkbenchDatabase.DbDate(task.OccurrenceDate));
        command.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
    }

    public void Delete(Guid id)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        using (var detach = connection.CreateCommand())
        {
            detach.Transaction = transaction;
            detach.CommandText = "UPDATE recurrence_instances SET task_id = NULL WHERE task_id = $id;";
            detach.Parameters.AddWithValue("$id", id.ToString("D"));
            detach.ExecuteNonQuery();
        }

        using (var unlink = connection.CreateCommand())
        {
            unlink.Transaction = transaction;
            unlink.CommandText = "DELETE FROM entity_links WHERE (owner_type = 'task' AND owner_id = $id) OR (target_type = 'task' AND target_id = $id);";
            unlink.Parameters.AddWithValue("$id", id.ToString("D"));
            unlink.ExecuteNonQuery();
        }

        using (var clearLinks = connection.CreateCommand())
        {
            clearLinks.Transaction = transaction;
            clearLinks.CommandText = "DELETE FROM daily_summary_tasks WHERE task_id = $id;";
            clearLinks.Parameters.AddWithValue("$id", id.ToString("D"));
            clearLinks.ExecuteNonQuery();
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM tasks WHERE id = $id;";
            delete.Parameters.AddWithValue("$id", id.ToString("D"));
            delete.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<TaskItem> GetByDateRange(DateTime fromInclusive, DateTime toInclusive)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {TaskMapper.Columns} FROM tasks
            WHERE date(IFNULL(planned_start, due_at)) <= $to OR date(IFNULL(due_at, planned_start)) >= $from
            ORDER BY IFNULL(due_at, created_at) ASC;
            """;
        command.Parameters.AddWithValue("$from", fromInclusive.ToString(WorkbenchDatabase.DateFormat));
        command.Parameters.AddWithValue("$to", toInclusive.ToString(WorkbenchDatabase.DateFormat));
        using var reader = command.ExecuteReader();
        var list = new List<TaskItem>();
        while (reader.Read())
        {
            list.Add(TaskMapper.Read(reader));
        }

        return list;
    }

    public IReadOnlyList<TaskItem> GetOpenTasks() =>
        Query(new TaskQuery { OnlyOpen = true, SortField = TaskSortField.DueAt });

    public int CountByStatus(Guid statusId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tasks WHERE status_id = $status;";
        command.Parameters.AddWithValue("$status", statusId.ToString("D"));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public int MigrateStatus(Guid fromStatusId, Guid toStatusId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE tasks SET status_id = $to, updated_at = $now WHERE status_id = $from;";
        command.Parameters.AddWithValue("$to", toStatusId.ToString("D"));
        command.Parameters.AddWithValue("$from", fromStatusId.ToString("D"));
        command.Parameters.AddWithValue("$now", DateTime.Now.ToString(WorkbenchDatabase.DateTimeFormat));
        return command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetCategories()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT category FROM tasks WHERE category IS NOT NULL AND TRIM(category) <> '' ORDER BY category;";
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    public IReadOnlyList<string> GetTags()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tags FROM tasks WHERE tags IS NOT NULL AND TRIM(tags) <> '';";
        using var reader = command.ExecuteReader();
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            foreach (var tag in reader.GetString(0).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                set.Add(tag);
            }
        }

        return set.ToList();
    }

    public IReadOnlyList<TaskItem> GetRecentlyUpdated(int count)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {TaskMapper.Columns} FROM tasks ORDER BY updated_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Max(1, count));
        using var reader = command.ExecuteReader();
        var list = new List<TaskItem>();
        while (reader.Read())
        {
            list.Add(TaskMapper.Read(reader));
        }

        return list;
    }
}
