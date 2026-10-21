using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>每日总结：同一天对应一份可持续编辑的记录。</summary>
public sealed class DailySummaryService : IDailySummaryService
{
    private readonly WorkbenchDatabase _database;

    public DailySummaryService(WorkbenchDatabase database) => _database = database;

    private const string Columns = """
        id, date, summary, completed_items, problems, pending_items, tomorrow_plan, created_at, updated_at
        """;

    private static DailySummary Read(SqliteDataReader reader) => new()
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
    };

    public DailySummary Get(DateTime date)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM daily_summaries WHERE date = $date LIMIT 1;";
        command.Parameters.AddWithValue("$date", date.Date.ToString(WorkbenchDatabase.DateFormat));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : new DailySummary { Date = date.Date };
    }

    public bool Exists(DateTime date)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM daily_summaries WHERE date = $date;";
        command.Parameters.AddWithValue("$date", date.Date.ToString(WorkbenchDatabase.DateFormat));
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    public DailySummary Save(DailySummary summary)
    {
        if (summary.IsEmpty)
        {
            Delete(summary.Date);
            return summary;
        }

        summary.Date = summary.Date.Date;
        summary.UpdatedAt = DateTime.Now;
        using var connection = _database.Open();
        Guid? existingId = null;
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT id FROM daily_summaries WHERE date = $date LIMIT 1;";
            query.Parameters.AddWithValue("$date", summary.Date.ToString(WorkbenchDatabase.DateFormat));
            var scalar = query.ExecuteScalar();
            if (scalar is string value && Guid.TryParse(value, out var parsed))
            {
                existingId = parsed;
            }
        }

        if (existingId is { } id)
        {
            summary.Id = id;
            using var update = connection.CreateCommand();
            update.CommandText = """
                UPDATE daily_summaries SET summary = $summary, completed_items = $completed, problems = $problems,
                    pending_items = $pending, tomorrow_plan = $tomorrow, updated_at = $updated
                WHERE id = $id;
                """;
            Bind(update, summary);
            update.ExecuteNonQuery();
        }
        else
        {
            summary.CreatedAt = DateTime.Now;
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO daily_summaries (id, date, summary, completed_items, problems, pending_items,
                    tomorrow_plan, created_at, updated_at)
                VALUES ($id, $date, $summary, $completed, $problems, $pending, $tomorrow, $created, $updated);
                """;
            Bind(insert, summary);
            insert.ExecuteNonQuery();
        }

        return summary;
    }

    private static void Bind(SqliteCommand command, DailySummary summary)
    {
        command.Parameters.AddWithValue("$id", summary.Id.ToString("D"));
        command.Parameters.AddWithValue("$date", summary.Date.ToString(WorkbenchDatabase.DateFormat));
        command.Parameters.AddWithValue("$summary", WorkbenchDatabase.Db(summary.Summary));
        command.Parameters.AddWithValue("$completed", WorkbenchDatabase.Db(summary.CompletedItems));
        command.Parameters.AddWithValue("$problems", WorkbenchDatabase.Db(summary.Problems));
        command.Parameters.AddWithValue("$pending", WorkbenchDatabase.Db(summary.PendingItems));
        command.Parameters.AddWithValue("$tomorrow", WorkbenchDatabase.Db(summary.TomorrowPlan));
        command.Parameters.AddWithValue("$created", summary.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.Parameters.AddWithValue("$updated", summary.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
    }

    public void Delete(DateTime date)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM daily_summaries WHERE date = $date;";
            command.Parameters.AddWithValue("$date", date.Date.ToString(WorkbenchDatabase.DateFormat));
            command.ExecuteNonQuery();
        }

        using (var links = connection.CreateCommand())
        {
            links.Transaction = transaction;
            links.CommandText = "DELETE FROM daily_summary_tasks WHERE summary_date = $date;";
            links.Parameters.AddWithValue("$date", date.Date.ToString(WorkbenchDatabase.DateFormat));
            links.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<DailySummary> GetRecent(int count)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM daily_summaries ORDER BY date DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Max(1, count));
        using var reader = command.ExecuteReader();
        var list = new List<DailySummary>();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public IReadOnlyList<DailySummary> Search(string keyword, int limit = 100)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM daily_summaries
            WHERE IFNULL(summary,'') LIKE $kw OR IFNULL(completed_items,'') LIKE $kw
               OR IFNULL(problems,'') LIKE $kw OR IFNULL(pending_items,'') LIKE $kw
               OR IFNULL(tomorrow_plan,'') LIKE $kw
            ORDER BY date DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$kw", $"%{keyword.Trim()}%");
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        using var reader = command.ExecuteReader();
        var list = new List<DailySummary>();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public IReadOnlyList<Guid> GetLinkedTaskIds(DateTime date)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT task_id FROM daily_summary_tasks WHERE summary_date = $date;";
        command.Parameters.AddWithValue("$date", date.Date.ToString(WorkbenchDatabase.DateFormat));
        using var reader = command.ExecuteReader();
        var ids = new List<Guid>();
        while (reader.Read())
        {
            ids.Add(Guid.Parse(reader.GetString(0)));
        }

        return ids;
    }

    public void SetLinkedTasks(DateTime date, IEnumerable<Guid> taskIds)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM daily_summary_tasks WHERE summary_date = $date;";
            clear.Parameters.AddWithValue("$date", date.Date.ToString(WorkbenchDatabase.DateFormat));
            clear.ExecuteNonQuery();
        }

        foreach (var taskId in taskIds.Distinct())
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO daily_summary_tasks (summary_date, task_id) VALUES ($date, $task);";
            insert.Parameters.AddWithValue("$date", date.Date.ToString(WorkbenchDatabase.DateFormat));
            insert.Parameters.AddWithValue("$task", taskId.ToString("D"));
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
