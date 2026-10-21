using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>状态数据访问：支持自定义名称/颜色/排序，删除时要求迁移。</summary>
public sealed class StatusService : IStatusService
{
    private readonly WorkbenchDatabase _database;
    private readonly ITaskService _tasks;

    public StatusService(WorkbenchDatabase database, ITaskService tasks)
    {
        _database = database;
        _tasks = tasks;
    }

    public IReadOnlyList<WorkStatus> GetAll()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, scope, name, color, sort_order, is_protected FROM work_statuses ORDER BY scope, sort_order, name;";
        using var reader = command.ExecuteReader();
        var list = new List<WorkStatus>();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }

        return list;
    }

    private static WorkStatus Read(SqliteDataReader reader) => new()
    {
        Id = WorkbenchDatabase.GetGuid(reader, 0),
        Scope = (StatusScope)reader.GetInt32(1),
        Name = reader.GetString(2),
        Color = reader.GetString(3),
        SortOrder = reader.GetInt32(4),
        IsProtected = reader.GetInt32(5) != 0,
    };

    public IReadOnlyList<WorkStatus> GetByScope(StatusScope scope) =>
        GetAll().Where(s => s.Scope == scope).ToList();

    public WorkStatus? Get(Guid id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, scope, name, color, sort_order, is_protected FROM work_statuses WHERE id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public WorkStatus Create(WorkStatus status)
    {
        using var connection = _database.Open();
        var sortOrder = status.SortOrder;
        if (sortOrder <= 0)
        {
            using var maxCommand = connection.CreateCommand();
            maxCommand.CommandText = "SELECT IFNULL(MAX(sort_order), -1) + 1 FROM work_statuses WHERE scope = $scope;";
            maxCommand.Parameters.AddWithValue("$scope", (int)status.Scope);
            sortOrder = Convert.ToInt32(maxCommand.ExecuteScalar());
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO work_statuses (id, scope, name, color, sort_order, is_protected)
            VALUES ($id, $scope, $name, $color, $sort, $protected);
            """;
        command.Parameters.AddWithValue("$id", status.Id.ToString("D"));
        command.Parameters.AddWithValue("$scope", (int)status.Scope);
        command.Parameters.AddWithValue("$name", status.Name.Trim());
        command.Parameters.AddWithValue("$color", status.Color);
        command.Parameters.AddWithValue("$sort", sortOrder);
        command.Parameters.AddWithValue("$protected", status.IsProtected ? 1 : 0);
        command.ExecuteNonQuery();
        status.SortOrder = sortOrder;
        return status;
    }

    public WorkStatus Update(WorkStatus status)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE work_statuses SET name = $name, color = $color, sort_order = $sort WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", status.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", status.Name.Trim());
        command.Parameters.AddWithValue("$color", status.Color);
        command.Parameters.AddWithValue("$sort", status.SortOrder);
        command.ExecuteNonQuery();
        return status;
    }

    public StatusDeleteImpact GetDeleteImpact(Guid statusId)
    {
        var status = Get(statusId);
        return new StatusDeleteImpact
        {
            StatusId = statusId,
            StatusName = status?.Name ?? string.Empty,
            TaskCount = _tasks.CountByStatus(statusId),
        };
    }

    public void Delete(Guid statusId, Guid? migrateToStatusId)
    {
        var status = Get(statusId) ?? throw new InvalidOperationException("状态不存在。");
        if (status.IsProtected)
        {
            throw new InvalidOperationException("内置状态不允许删除，可以改名、改色或调整顺序。");
        }

        var inUse = _tasks.CountByStatus(statusId);
        if (inUse > 0)
        {
            if (migrateToStatusId is null)
            {
                throw new InvalidOperationException($"该状态仍被 {inUse} 个任务使用，请先选择迁移目标状态或取消操作。");
            }

            if (migrateToStatusId == statusId)
            {
                throw new InvalidOperationException("迁移目标不能是状态本身。");
            }

            var target = Get(migrateToStatusId.Value)
                         ?? throw new InvalidOperationException("迁移目标状态不存在。");
            if (target.Scope != status.Scope)
            {
                throw new InvalidOperationException("迁移目标必须属于同一状态集合。");
            }

            _tasks.MigrateStatus(statusId, migrateToStatusId.Value);
        }

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM work_statuses WHERE id = $id;";
        command.Parameters.AddWithValue("$id", statusId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public void MoveStatus(Guid statusId, int delta)
    {
        var list = GetAll().Where(s => s.Id == statusId || GetScopeOf(statusId) == s.Scope).ToList();
        var ordered = list.OrderBy(s => s.SortOrder).ToList();
        var index = ordered.FindIndex(s => s.Id == statusId);
        if (index < 0)
        {
            return;
        }

        var target = index + delta;
        if (target < 0 || target >= ordered.Count)
        {
            return;
        }

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < ordered.Count; i++)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE work_statuses SET sort_order = $sort WHERE id = $id;";
            command.Parameters.AddWithValue("$sort", i);
            command.Parameters.AddWithValue("$id", ordered[i].Id.ToString("D"));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private StatusScope GetScopeOf(Guid statusId) => Get(statusId)?.Scope ?? StatusScope.General;

    public WorkStatus GetDefaultStatus(StatusScope scope)
    {
        var list = GetByScope(scope);
        return list.Count > 0 ? list[0] : throw new InvalidOperationException("状态集合为空。");
    }
}
