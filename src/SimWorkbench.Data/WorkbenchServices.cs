using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>服务组合根：界面层通过此类获取业务服务，保持 UI 与数据访问解耦。</summary>
public sealed class WorkbenchServices
{
    private WorkbenchServices(WorkbenchDatabase database)
    {
        Database = database;

        var taskService = new TaskService(database);
        var statusService = new StatusService(database, taskService);
        var recurrenceService = new RecurrenceService(database, taskService, statusService);
        var settingsService = new SettingsService(database);
        var topicLogService = new TopicLogService(database);
        var checklistService = new ChecklistService(database);

        Tasks = taskService;
        Statuses = statusService;
        Recurrences = recurrenceService;
        Calendar = new CalendarService(database, recurrenceService, statusService);
        DailySummaries = new DailySummaryService(database);
        TopicLogs = topicLogService;
        Checklists = checklistService;
        Settings = settingsService;
        Modules = settingsService;
        Backup = new BackupService(database, taskService, statusService, recurrenceService,
            topicLogService, checklistService, settingsService);
    }

    public WorkbenchDatabase Database { get; }
    public ITaskService Tasks { get; }
    public IStatusService Statuses { get; }
    public IRecurrenceService Recurrences { get; }
    public ICalendarService Calendar { get; }
    public IDailySummaryService DailySummaries { get; }
    public ITopicLogService TopicLogs { get; }
    public IChecklistService Checklists { get; }
    public ISettingsService Settings { get; }
    public IModuleService Modules { get; }
    public IBackupService Backup { get; }

    /// <summary>创建并初始化服务（建库、迁移、内置数据）。</summary>
    public static WorkbenchServices Create(string? databasePath = null)
    {
        var database = new WorkbenchDatabase(databasePath);
        database.Initialize();
        return new WorkbenchServices(database);
    }
}
