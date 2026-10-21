using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Backup;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>数据文件位置解析（本地用户目录，不写入程序目录）。</summary>
public static class AppPaths
{
    /// <summary>可通过环境变量 SIMWORKBENCH_DATA_DIR 指定数据目录（便携/测试场景）。</summary>
    public const string DataDirectoryEnvironmentVariable = "SIMWORKBENCH_DATA_DIR";

    public static string DataDirectory { get; private set; } = ResolveDataDirectory();

    public static string DatabasePath => Path.Combine(DataDirectory, "workbench.db");

    public static string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>解析可写的数据目录：环境变量 → LocalAppData → 文档目录 → 程序目录旁（最后兜底）。</summary>
    private static string ResolveDataDirectory()
    {
        var candidates = new List<string>();
        var custom = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            candidates.Add(custom.Trim());
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.DataFolderName));
        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppInfo.DataFolderName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "data"));

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, ".write-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine($"[Paths] 数据目录不可用：{candidate} -> {ex.Message}");
            }
        }

        // 全部不可写时仍返回默认位置，由上层显示明确错误
        return candidates.Count > 0
            ? candidates[0]
            : Path.Combine(AppContext.BaseDirectory, "data");
    }

    /// <summary>测试或迁移场景可覆盖数据目录。</summary>
    public static void OverrideDataDirectory(string directory)
    {
        DataDirectory = directory;
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}

/// <summary>SQLite 数据库访问与结构迁移。</summary>
public sealed class WorkbenchDatabase
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";
    public const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;

    public WorkbenchDatabase(string? databasePath = null)
    {
        DatabasePath = databasePath ?? AppPaths.DatabasePath;
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>初始化数据库：建表、内置状态与默认设置（幂等）。</summary>
    public void Initialize()
    {
        using var connection = Open();
        var version = GetUserVersion(connection);
        if (version < 1)
        {
            using var transaction = connection.BeginTransaction();
            ExecuteScript(connection, SchemaV1, transaction);
            SeedDefaults(connection, transaction);
            SetUserVersion(connection, 1, transaction);
            transaction.Commit();
        }

        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"数据文件版本（{version}）高于当前程序支持的版本（{CurrentSchemaVersion}），请升级程序后再打开。");
        }

        // 触发一次 WAL checkpoint，避免频繁小文件
        using var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        try
        {
            checkpoint.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // checkpoint 失败不影响使用
        }
    }

    public static object? Db(Guid? value) => value is null ? DBNull.Value : value.Value.ToString("D");

    public static object? Db(DateTime? value) => value is null ? DBNull.Value : value.Value.ToString(DateTimeFormat);

    public static object? DbDate(DateTime? value) => value is null ? DBNull.Value : value.Value.ToString(DateFormat);

    public static object? Db(string? value) => string.IsNullOrEmpty(value) ? DBNull.Value : value;

    public static object? Db(int? value) => value is null ? DBNull.Value : value.Value;

    public static Guid GetGuid(SqliteDataReader reader, int index) =>
        Guid.Parse(reader.GetString(index));

    public static Guid? GetGuidOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Guid.Parse(reader.GetString(index));

    public static DateTime GetDateTime(SqliteDataReader reader, int index) =>
        ParseDateTime(reader.GetString(index));

    public static DateTime? GetDateTimeOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : ParseDateTime(reader.GetString(index));

    public static DateTime? GetDateOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : ParseDate(reader.GetString(index));

    public static string? GetStringOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);

    public static int? GetIntOrNull(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetInt32(index);

    public static DateTime ParseDateTime(string value)
    {
        if (DateTime.TryParseExact(value, DateTimeFormat, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        return DateTime.TryParse(value, out var fallback) ? fallback : DateTime.MinValue;
    }

    public static DateTime ParseDate(string value)
    {
        if (DateTime.TryParseExact(value, DateFormat, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        return DateTime.TryParse(value, out var fallback) ? fallback.Date : DateTime.MinValue;
    }

    private static int GetUserVersion(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SetUserVersion(SqliteConnection connection, int version, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static void ExecuteScript(SqliteConnection connection, string script, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = script;
        command.ExecuteNonQuery();
    }

    private static void SeedDefaults(SqliteConnection connection, SqliteTransaction transaction)
    {
        foreach (var status in Defaults.CreateDefaultStatuses())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO work_statuses (id, scope, name, color, sort_order, is_protected)
                VALUES ($id, $scope, $name, $color, $sort, $protected);
                """;
            command.Parameters.AddWithValue("$id", status.Id.ToString("D"));
            command.Parameters.AddWithValue("$scope", (int)status.Scope);
            command.Parameters.AddWithValue("$name", status.Name);
            command.Parameters.AddWithValue("$color", status.Color);
            command.Parameters.AddWithValue("$sort", status.SortOrder);
            command.Parameters.AddWithValue("$protected", status.IsProtected ? 1 : 0);
            command.ExecuteNonQuery();
        }

        var settings = Defaults.CreateDefaultSettings();
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        using var settingsCommand = connection.CreateCommand();
        settingsCommand.Transaction = transaction;
        settingsCommand.CommandText = "INSERT INTO settings (key, value) VALUES ($key, $value);";
        settingsCommand.Parameters.AddWithValue("$key", SettingsService.SettingsKey);
        settingsCommand.Parameters.AddWithValue("$value", json);
        settingsCommand.ExecuteNonQuery();
    }

    private const string SchemaV1 = """
        CREATE TABLE IF NOT EXISTS work_statuses (
            id TEXT PRIMARY KEY,
            scope INTEGER NOT NULL,
            name TEXT NOT NULL,
            color TEXT NOT NULL,
            sort_order INTEGER NOT NULL,
            is_protected INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS tasks (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            kind INTEGER NOT NULL,
            status_id TEXT NOT NULL REFERENCES work_statuses(id),
            description TEXT,
            created_at TEXT NOT NULL,
            planned_start TEXT,
            due_at TEXT,
            actual_delivery_at TEXT,
            category TEXT,
            tags TEXT,
            priority INTEGER NOT NULL DEFAULT 0,
            case_name TEXT,
            case_condition TEXT,
            estimated_minutes INTEGER,
            calculation_notes TEXT,
            recurrence_id TEXT REFERENCES recurrence_rules(id) ON DELETE SET NULL,
            occurrence_date TEXT,
            updated_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_tasks_status ON tasks(status_id);
        CREATE INDEX IF NOT EXISTS ix_tasks_due ON tasks(due_at);
        CREATE INDEX IF NOT EXISTS ix_tasks_kind ON tasks(kind);
        CREATE INDEX IF NOT EXISTS ix_tasks_recurrence ON tasks(recurrence_id, occurrence_date);

        CREATE TABLE IF NOT EXISTS recurrence_rules (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            kind INTEGER NOT NULL,
            status_id TEXT NOT NULL,
            description TEXT,
            category TEXT,
            tags TEXT,
            priority INTEGER NOT NULL DEFAULT 0,
            case_name TEXT,
            case_condition TEXT,
            estimated_minutes INTEGER,
            calculation_notes TEXT,
            frequency INTEGER NOT NULL,
            interval INTEGER NOT NULL DEFAULT 1,
            week_days TEXT,
            start_date TEXT NOT NULL,
            end_date TEXT,
            is_active INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS recurrence_instances (
            id TEXT PRIMARY KEY,
            rule_id TEXT NOT NULL REFERENCES recurrence_rules(id) ON DELETE CASCADE,
            occurrence_date TEXT NOT NULL,
            task_id TEXT,
            is_skipped INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            UNIQUE (rule_id, occurrence_date)
        );

        CREATE TABLE IF NOT EXISTS daily_summaries (
            id TEXT PRIMARY KEY,
            date TEXT NOT NULL UNIQUE,
            summary TEXT,
            completed_items TEXT,
            problems TEXT,
            pending_items TEXT,
            tomorrow_plan TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS daily_summary_tasks (
            summary_date TEXT NOT NULL,
            task_id TEXT NOT NULL,
            PRIMARY KEY (summary_date, task_id)
        );

        CREATE TABLE IF NOT EXISTS topic_logs (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            category TEXT,
            current_breakpoint TEXT,
            content TEXT,
            tags TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_topic_logs_updated ON topic_logs(updated_at);

        CREATE TABLE IF NOT EXISTS topic_log_history (
            id TEXT PRIMARY KEY,
            topic_id TEXT NOT NULL REFERENCES topic_logs(id) ON DELETE CASCADE,
            previous_breakpoint TEXT,
            previous_content TEXT,
            change_note TEXT,
            changed_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_topic_history_topic ON topic_log_history(topic_id, changed_at);

        CREATE TABLE IF NOT EXISTS checklists (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            category TEXT,
            note TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS checklist_items (
            id TEXT PRIMARY KEY,
            checklist_id TEXT NOT NULL REFERENCES checklists(id) ON DELETE CASCADE,
            text TEXT NOT NULL,
            is_done INTEGER NOT NULL DEFAULT 0,
            sort_order INTEGER NOT NULL,
            note TEXT,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_checklist_items ON checklist_items(checklist_id, sort_order);

        CREATE TABLE IF NOT EXISTS entity_links (
            id TEXT PRIMARY KEY,
            owner_type TEXT NOT NULL,
            owner_id TEXT NOT NULL,
            target_type TEXT NOT NULL,
            target_id TEXT NOT NULL,
            created_at TEXT NOT NULL,
            UNIQUE (owner_type, owner_id, target_type, target_id)
        );

        CREATE INDEX IF NOT EXISTS ix_entity_links_owner ON entity_links(owner_type, owner_id);
        CREATE INDEX IF NOT EXISTS ix_entity_links_target ON entity_links(target_type, target_id);

        CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;
}
