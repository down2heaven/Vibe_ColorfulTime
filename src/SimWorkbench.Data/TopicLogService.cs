using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>主题日志与断点：更新断点时旧内容自动进入历史，默认不覆盖历史。</summary>
public sealed class TopicLogService : ITopicLogService
{
    private readonly WorkbenchDatabase _database;

    public TopicLogService(WorkbenchDatabase database) => _database = database;

    private const string Columns = "id, title, category, current_breakpoint, content, tags, created_at, updated_at";

    private static TopicLog Read(SqliteDataReader reader) => new()
    {
        Id = WorkbenchDatabase.GetGuid(reader, 0),
        Title = reader.GetString(1),
        Category = WorkbenchDatabase.GetStringOrNull(reader, 2),
        CurrentBreakpoint = WorkbenchDatabase.GetStringOrNull(reader, 3),
        Content = WorkbenchDatabase.GetStringOrNull(reader, 4),
        Tags = WorkbenchDatabase.GetStringOrNull(reader, 5),
        CreatedAt = WorkbenchDatabase.GetDateTime(reader, 6),
        UpdatedAt = WorkbenchDatabase.GetDateTime(reader, 7),
    };

    public IReadOnlyList<TopicLog> Query(string? keyword, string? category, string? tag)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            where.Add("(title LIKE $kw OR IFNULL(current_breakpoint,'') LIKE $kw OR IFNULL(content,'') LIKE $kw OR IFNULL(tags,'') LIKE $kw)");
            command.Parameters.AddWithValue("$kw", $"%{keyword.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            where.Add("category = $category");
            command.Parameters.AddWithValue("$category", category.Trim());
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            where.Add("IFNULL(tags,'') LIKE $tag");
            command.Parameters.AddWithValue("$tag", $"%{tag.Trim()}%");
        }

        var sql = $"SELECT {Columns} FROM topic_logs";
        if (where.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", where);
        }

        sql += " ORDER BY updated_at DESC;";
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<TopicLog>();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public IReadOnlyList<TopicLog> GetRecent(int count)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM topic_logs ORDER BY updated_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Max(1, count));
        using var reader = command.ExecuteReader();
        var list = new List<TopicLog>();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public TopicLog? Get(Guid id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM topic_logs WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<string> GetCategories()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT category FROM topic_logs WHERE category IS NOT NULL AND TRIM(category) <> '' ORDER BY category;";
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    public TopicLog Create(TopicLog log)
    {
        if (string.IsNullOrWhiteSpace(log.Title))
        {
            throw new ArgumentException("主题名称不能为空。", nameof(log));
        }

        log.CreatedAt = DateTime.Now;
        log.UpdatedAt = DateTime.Now;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO topic_logs (id, title, category, current_breakpoint, content, tags, created_at, updated_at)
            VALUES ($id, $title, $category, $breakpoint, $content, $tags, $created, $updated);
            """;
        Bind(command, log);
        command.ExecuteNonQuery();
        return log;
    }

    private static void Bind(SqliteCommand command, TopicLog log)
    {
        command.Parameters.AddWithValue("$id", log.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", log.Title.Trim());
        command.Parameters.AddWithValue("$category", WorkbenchDatabase.Db(log.Category));
        command.Parameters.AddWithValue("$breakpoint", WorkbenchDatabase.Db(log.CurrentBreakpoint));
        command.Parameters.AddWithValue("$content", WorkbenchDatabase.Db(log.Content));
        command.Parameters.AddWithValue("$tags", WorkbenchDatabase.Db(log.Tags));
        command.Parameters.AddWithValue("$created", log.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.Parameters.AddWithValue("$updated", log.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
    }

    public TopicLog Update(TopicLog log, bool saveBreakpointHistory, string? changeNote = null)
    {
        if (string.IsNullOrWhiteSpace(log.Title))
        {
            throw new ArgumentException("主题名称不能为空。", nameof(log));
        }

        var existing = Get(log.Id) ?? throw new InvalidOperationException("主题日志不存在。");
        var breakpointChanged = !string.Equals(existing.CurrentBreakpoint ?? string.Empty, log.CurrentBreakpoint ?? string.Empty, StringComparison.Ordinal);
        var contentChanged = !string.Equals(existing.Content ?? string.Empty, log.Content ?? string.Empty, StringComparison.Ordinal);
        var shouldArchive = saveBreakpointHistory && (breakpointChanged || contentChanged);

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        if (shouldArchive)
        {
            using var history = connection.CreateCommand();
            history.Transaction = transaction;
            history.CommandText = """
                INSERT INTO topic_log_history (id, topic_id, previous_breakpoint, previous_content, change_note, changed_at)
                VALUES ($id, $topic, $breakpoint, $content, $note, $changed);
                """;
            history.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            history.Parameters.AddWithValue("$topic", log.Id.ToString("D"));
            history.Parameters.AddWithValue("$breakpoint", WorkbenchDatabase.Db(existing.CurrentBreakpoint));
            history.Parameters.AddWithValue("$content", WorkbenchDatabase.Db(existing.Content));
            history.Parameters.AddWithValue("$note", WorkbenchDatabase.Db(changeNote));
            history.Parameters.AddWithValue("$changed", DateTime.Now.ToString(WorkbenchDatabase.DateTimeFormat));
            history.ExecuteNonQuery();
        }

        log.CreatedAt = existing.CreatedAt;
        log.UpdatedAt = DateTime.Now;
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE topic_logs SET title = $title, category = $category, current_breakpoint = $breakpoint,
                    content = $content, tags = $tags, updated_at = $updated
                WHERE id = $id;
                """;
            Bind(update, log);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        return log;
    }

    public void Delete(Guid id)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        using (var links = connection.CreateCommand())
        {
            links.Transaction = transaction;
            links.CommandText = "DELETE FROM entity_links WHERE (owner_type = 'topic-log' AND owner_id = $id) OR (target_type = 'topic-log' AND target_id = $id);";
            links.Parameters.AddWithValue("$id", id.ToString("D"));
            links.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM topic_logs WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<TopicLogHistory> GetHistory(Guid topicId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, topic_id, previous_breakpoint, previous_content, change_note, changed_at
            FROM topic_log_history WHERE topic_id = $topic ORDER BY changed_at DESC;
            """;
        command.Parameters.AddWithValue("$topic", topicId.ToString("D"));
        using var reader = command.ExecuteReader();
        var list = new List<TopicLogHistory>();
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

    public void DeleteHistory(Guid historyId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM topic_log_history WHERE id = $id;";
        command.Parameters.AddWithValue("$id", historyId.ToString("D"));
        command.ExecuteNonQuery();
    }
}
