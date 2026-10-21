using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>Checklist 数据访问：检查项独立勾选，未完成项持续保留；支持跨模块关联。</summary>
public sealed class ChecklistService : IChecklistService
{
    private readonly WorkbenchDatabase _database;

    public ChecklistService(WorkbenchDatabase database) => _database = database;

    public IReadOnlyList<Checklist> GetAll()
    {
        using var connection = _database.Open();
        var checklists = new List<Checklist>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name, category, note, created_at, updated_at FROM checklists ORDER BY updated_at DESC;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                checklists.Add(new Checklist
                {
                    Id = WorkbenchDatabase.GetGuid(reader, 0),
                    Name = reader.GetString(1),
                    Category = WorkbenchDatabase.GetStringOrNull(reader, 2),
                    Note = WorkbenchDatabase.GetStringOrNull(reader, 3),
                    CreatedAt = WorkbenchDatabase.GetDateTime(reader, 4),
                    UpdatedAt = WorkbenchDatabase.GetDateTime(reader, 5),
                });
            }
        }

        foreach (var checklist in checklists)
        {
            checklist.Items = LoadItems(connection, checklist.Id).ToList();
        }

        return checklists;
    }

    private static IEnumerable<ChecklistItem> LoadItems(SqliteConnection connection, Guid checklistId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, checklist_id, text, is_done, sort_order, note, created_at, updated_at
            FROM checklist_items WHERE checklist_id = $id ORDER BY sort_order, created_at;
            """;
        command.Parameters.AddWithValue("$id", checklistId.ToString("D"));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return new ChecklistItem
            {
                Id = WorkbenchDatabase.GetGuid(reader, 0),
                ChecklistId = WorkbenchDatabase.GetGuid(reader, 1),
                Text = reader.GetString(2),
                IsDone = reader.GetInt32(3) != 0,
                SortOrder = reader.GetInt32(4),
                Note = WorkbenchDatabase.GetStringOrNull(reader, 5),
                CreatedAt = WorkbenchDatabase.GetDateTime(reader, 6),
                UpdatedAt = WorkbenchDatabase.GetDateTime(reader, 7),
            };
        }
    }

    public Checklist? Get(Guid id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, category, note, created_at, updated_at FROM checklists WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var checklist = new Checklist
        {
            Id = WorkbenchDatabase.GetGuid(reader, 0),
            Name = reader.GetString(1),
            Category = WorkbenchDatabase.GetStringOrNull(reader, 2),
            Note = WorkbenchDatabase.GetStringOrNull(reader, 3),
            CreatedAt = WorkbenchDatabase.GetDateTime(reader, 4),
            UpdatedAt = WorkbenchDatabase.GetDateTime(reader, 5),
        };
        reader.Close();
        checklist.Items = LoadItems(connection, checklist.Id).ToList();
        return checklist;
    }

    public Checklist Create(Checklist checklist)
    {
        if (string.IsNullOrWhiteSpace(checklist.Name))
        {
            throw new ArgumentException("清单名称不能为空。", nameof(checklist));
        }

        checklist.CreatedAt = DateTime.Now;
        checklist.UpdatedAt = DateTime.Now;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO checklists (id, name, category, note, created_at, updated_at)
            VALUES ($id, $name, $category, $note, $created, $updated);
            """;
        command.Parameters.AddWithValue("$id", checklist.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", checklist.Name.Trim());
        command.Parameters.AddWithValue("$category", WorkbenchDatabase.Db(checklist.Category));
        command.Parameters.AddWithValue("$note", WorkbenchDatabase.Db(checklist.Note));
        command.Parameters.AddWithValue("$created", checklist.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.Parameters.AddWithValue("$updated", checklist.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.ExecuteNonQuery();
        return checklist;
    }

    public void UpdateHeader(Checklist checklist)
    {
        checklist.UpdatedAt = DateTime.Now;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE checklists SET name = $name, category = $category, note = $note, updated_at = $updated WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", checklist.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", checklist.Name.Trim());
        command.Parameters.AddWithValue("$category", WorkbenchDatabase.Db(checklist.Category));
        command.Parameters.AddWithValue("$note", WorkbenchDatabase.Db(checklist.Note));
        command.Parameters.AddWithValue("$updated", checklist.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.ExecuteNonQuery();
    }

    public void Delete(Guid checklistId)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        using (var links = connection.CreateCommand())
        {
            links.Transaction = transaction;
            links.CommandText = "DELETE FROM entity_links WHERE target_type = 'checklist' AND target_id = $id;";
            links.Parameters.AddWithValue("$id", checklistId.ToString("D"));
            links.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM checklists WHERE id = $id;";
            command.Parameters.AddWithValue("$id", checklistId.ToString("D"));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public ChecklistItem AddItem(Guid checklistId, string text, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("检查项内容不能为空。", nameof(text));
        }

        using var connection = _database.Open();
        int sortOrder;
        using (var max = connection.CreateCommand())
        {
            max.CommandText = "SELECT IFNULL(MAX(sort_order), -1) + 1 FROM checklist_items WHERE checklist_id = $id;";
            max.Parameters.AddWithValue("$id", checklistId.ToString("D"));
            sortOrder = Convert.ToInt32(max.ExecuteScalar());
        }

        var item = new ChecklistItem
        {
            ChecklistId = checklistId,
            Text = text.Trim(),
            Note = note,
            SortOrder = sortOrder,
        };

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO checklist_items (id, checklist_id, text, is_done, sort_order, note, created_at, updated_at)
            VALUES ($id, $checklist, $text, 0, $sort, $note, $created, $updated);
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$checklist", checklistId.ToString("D"));
        command.Parameters.AddWithValue("$text", item.Text);
        command.Parameters.AddWithValue("$sort", item.SortOrder);
        command.Parameters.AddWithValue("$note", WorkbenchDatabase.Db(item.Note));
        command.Parameters.AddWithValue("$created", item.CreatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.Parameters.AddWithValue("$updated", item.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.ExecuteNonQuery();
        TouchChecklist(connection, checklistId);
        return item;
    }

    private static void TouchChecklist(SqliteConnection connection, Guid checklistId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE checklists SET updated_at = $updated WHERE id = $id;";
        command.Parameters.AddWithValue("$updated", DateTime.Now.ToString(WorkbenchDatabase.DateTimeFormat));
        command.Parameters.AddWithValue("$id", checklistId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public ChecklistItem UpdateItem(ChecklistItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Text))
        {
            throw new ArgumentException("检查项内容不能为空。", nameof(item));
        }

        item.UpdatedAt = DateTime.Now;
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE checklist_items SET text = $text, note = $note, is_done = $done, updated_at = $updated WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$text", item.Text.Trim());
        command.Parameters.AddWithValue("$note", WorkbenchDatabase.Db(item.Note));
        command.Parameters.AddWithValue("$done", item.IsDone ? 1 : 0);
        command.Parameters.AddWithValue("$updated", item.UpdatedAt.ToString(WorkbenchDatabase.DateTimeFormat));
        command.ExecuteNonQuery();
        TouchChecklist(connection, item.ChecklistId);
        return item;
    }

    public void DeleteItem(Guid itemId)
    {
        using var connection = _database.Open();
        Guid checklistId;
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT checklist_id FROM checklist_items WHERE id = $id LIMIT 1;";
            query.Parameters.AddWithValue("$id", itemId.ToString("D"));
            var scalar = query.ExecuteScalar();
            if (scalar is not string value)
            {
                return;
            }

            checklistId = Guid.Parse(value);
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM checklist_items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", itemId.ToString("D"));
            command.ExecuteNonQuery();
        }

        TouchChecklist(connection, checklistId);
    }

    public void SetItemDone(Guid itemId, bool isDone)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE checklist_items SET is_done = $done, updated_at = $updated WHERE id = $id;";
        command.Parameters.AddWithValue("$done", isDone ? 1 : 0);
        command.Parameters.AddWithValue("$updated", DateTime.Now.ToString(WorkbenchDatabase.DateTimeFormat));
        command.Parameters.AddWithValue("$id", itemId.ToString("D"));
        command.ExecuteNonQuery();

        Guid? checklistId = null;
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT checklist_id FROM checklist_items WHERE id = $id LIMIT 1;";
            query.Parameters.AddWithValue("$id", itemId.ToString("D"));
            if (query.ExecuteScalar() is string value)
            {
                checklistId = Guid.Parse(value);
            }
        }

        if (checklistId is { } id)
        {
            TouchChecklist(connection, id);
        }
    }

    public void MoveItem(Guid itemId, int delta)
    {
        using var connection = _database.Open();
        Guid checklistId;
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT checklist_id FROM checklist_items WHERE id = $id LIMIT 1;";
            query.Parameters.AddWithValue("$id", itemId.ToString("D"));
            var scalar = query.ExecuteScalar();
            if (scalar is not string value)
            {
                return;
            }

            checklistId = Guid.Parse(value);
        }

        var items = LoadItems(connection, checklistId).OrderBy(i => i.SortOrder).ToList();
        var index = items.FindIndex(i => i.Id == itemId);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= items.Count)
        {
            return;
        }

        (items[index], items[target]) = (items[target], items[index]);
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < items.Count; i++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE checklist_items SET sort_order = $sort WHERE id = $id;";
            command.Parameters.AddWithValue("$sort", i);
            command.Parameters.AddWithValue("$id", items[i].Id.ToString("D"));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void Link(string ownerType, Guid ownerId, Guid checklistId)
    {
        if (!LinkOwnerTypes.IsKnown(ownerType))
        {
            throw new ArgumentException($"未知的关联对象类型：{ownerType}", nameof(ownerType));
        }

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO entity_links (id, owner_type, owner_id, target_type, target_id, created_at)
            VALUES ($id, $ownerType, $ownerId, 'checklist', $checklist, $created);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$ownerType", ownerType);
        command.Parameters.AddWithValue("$ownerId", ownerId.ToString("D"));
        command.Parameters.AddWithValue("$checklist", checklistId.ToString("D"));
        command.Parameters.AddWithValue("$created", DateTime.Now.ToString(WorkbenchDatabase.DateTimeFormat));
        command.ExecuteNonQuery();
    }

    public void Unlink(string ownerType, Guid ownerId, Guid checklistId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM entity_links
            WHERE owner_type = $ownerType AND owner_id = $ownerId AND target_type = 'checklist' AND target_id = $checklist;
            """;
        command.Parameters.AddWithValue("$ownerType", ownerType);
        command.Parameters.AddWithValue("$ownerId", ownerId.ToString("D"));
        command.Parameters.AddWithValue("$checklist", checklistId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Checklist> GetLinkedChecklists(string ownerType, Guid ownerId)
    {
        using var connection = _database.Open();
        var ids = new List<Guid>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT target_id FROM entity_links
                WHERE owner_type = $ownerType AND owner_id = $ownerId AND target_type = 'checklist';
                """;
            command.Parameters.AddWithValue("$ownerType", ownerType);
            command.Parameters.AddWithValue("$ownerId", ownerId.ToString("D"));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        var result = new List<Checklist>();
        foreach (var id in ids)
        {
            var checklist = Get(id);
            if (checklist is not null)
            {
                result.Add(checklist);
            }
        }

        return result;
    }

    public IReadOnlyList<EntityLink> GetLinksForChecklist(Guid checklistId)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, owner_type, owner_id, target_type, target_id, created_at
            FROM entity_links WHERE target_type = 'checklist' AND target_id = $id;
            """;
        command.Parameters.AddWithValue("$id", checklistId.ToString("D"));
        using var reader = command.ExecuteReader();
        var links = new List<EntityLink>();
        while (reader.Read())
        {
            links.Add(new EntityLink
            {
                Id = WorkbenchDatabase.GetGuid(reader, 0),
                OwnerType = reader.GetString(1),
                OwnerId = WorkbenchDatabase.GetGuid(reader, 2),
                TargetType = reader.GetString(3),
                TargetId = WorkbenchDatabase.GetGuid(reader, 4),
                CreatedAt = WorkbenchDatabase.GetDateTime(reader, 5),
            });
        }

        return links;
    }
}
