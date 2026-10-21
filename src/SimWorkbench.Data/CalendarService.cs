using Microsoft.Data.Sqlite;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;

namespace SimWorkbench.Data;

/// <summary>月历数据：由任务计划开始、计划交付与周期实例派生，颜色取自状态颜色。</summary>
public sealed class CalendarService : ICalendarService
{
    private readonly WorkbenchDatabase _database;
    private readonly IRecurrenceService _recurrences;
    private readonly IStatusService _statuses;

    public CalendarService(WorkbenchDatabase database, IRecurrenceService recurrences, IStatusService statuses)
    {
        _database = database;
        _recurrences = recurrences;
        _statuses = statuses;
    }

    public IReadOnlyList<CalendarDay> GetMonth(DateTime anyDayInMonth)
    {
        var first = new DateTime(anyDayInMonth.Year, anyDayInMonth.Month, 1);
        var gridStart = first.AddDays(-(int)first.DayOfWeek); // 周日为一周起点
        var gridEnd = gridStart.AddDays(41); // 6 行 × 7 列
        try
        {
            _recurrences.EnsureMaterialized(gridStart, gridEnd);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            // 实例补齐失败不应阻塞日历显示
            System.Diagnostics.Debug.WriteLine($"[Calendar] 周期实例补齐失败：{ex.Message}");
        }

        var items = LoadItems(gridStart, gridEnd);
        var statusMap = _statuses.GetAll().ToDictionary(s => s.Id);
        var today = DateTime.Today;
        var days = new List<CalendarDay>(42);
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            var day = new CalendarDay
            {
                Date = date,
                IsCurrentMonth = date.Month == first.Month && date.Year == first.Year,
                IsToday = date.Date == today,
            };

            foreach (var item in items.Where(it => it.Date.Date == date))
            {
                Enrich(item, statusMap);
                day.Items.Add(item);
            }

            day.Items.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            days.Add(day);
        }

        return days;
    }

    public IReadOnlyList<CalendarItem> GetDayItems(DateTime date)
    {
        var items = LoadItems(date.Date, date.Date);
        var statusMap = _statuses.GetAll().ToDictionary(s => s.Id);
        foreach (var item in items)
        {
            Enrich(item, statusMap);
        }

        items.Sort((a, b) => a.Kind.CompareTo(b.Kind));
        return items;
    }

    private void Enrich(CalendarItem item, IReadOnlyDictionary<Guid, WorkStatus> statusMap)
    {
        if (statusMap.TryGetValue(item.StatusId, out var status))
        {
            item.StatusName = status.Name;
            item.Color = status.Color;
            item.IsCompleted = status.Name is "后处理完成" or "已完成" or "计算完成";
        }
    }

    private List<CalendarItem> LoadItems(DateTime from, DateTime to)
    {
        var result = new List<CalendarItem>();
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, kind, status_id, planned_start, due_at, actual_delivery_at, recurrence_id,
                   occurrence_date, CASE WHEN date(occurrence_date) = date(IFNULL(planned_start, due_at)) THEN 1 ELSE 0 END
            FROM tasks
            WHERE date(IFNULL(planned_start, due_at)) BETWEEN $from AND $to
               OR date(IFNULL(due_at, planned_start)) BETWEEN $from AND $to
               OR date(occurrence_date) BETWEEN $from AND $to
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("$from", from.ToString(WorkbenchDatabase.DateFormat));
        command.Parameters.AddWithValue("$to", to.ToString(WorkbenchDatabase.DateFormat));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var taskId = WorkbenchDatabase.GetGuid(reader, 0);
            var title = reader.GetString(1);
            var kind = (TaskKind)reader.GetInt32(2);
            var statusId = WorkbenchDatabase.GetGuid(reader, 3);
            var plannedStart = WorkbenchDatabase.GetDateTimeOrNull(reader, 4);
            var dueAt = WorkbenchDatabase.GetDateTimeOrNull(reader, 5);
            var actualDelivery = WorkbenchDatabase.GetDateTimeOrNull(reader, 6);
            var recurrenceId = WorkbenchDatabase.GetGuidOrNull(reader, 7);
            var occurrence = WorkbenchDatabase.GetDateOrNull(reader, 8);

            if (plannedStart is { } start && start.Date >= from.Date && start.Date <= to.Date)
            {
                result.Add(new CalendarItem
                {
                    TaskId = taskId,
                    Title = title,
                    Date = start.Date,
                    Time = start,
                    Kind = recurrenceId is not null ? CalendarItemKind.Occurrence : CalendarItemKind.PlannedStart,
                    StatusId = statusId,
                    TaskKind = kind,
                    RecurrenceId = recurrenceId,
                });
            }

            if (dueAt is { } due && due.Date >= from.Date && due.Date <= to.Date)
            {
                result.Add(new CalendarItem
                {
                    TaskId = taskId,
                    Title = title,
                    Date = due.Date,
                    Time = due,
                    Kind = CalendarItemKind.Due,
                    StatusId = statusId,
                    TaskKind = kind,
                    IsCompleted = actualDelivery is not null,
                    RecurrenceId = recurrenceId,
                });
            }

            if (occurrence is { } occ && occ.Date >= from.Date && occ.Date <= to.Date
                && plannedStart?.Date != occ.Date)
            {
                result.Add(new CalendarItem
                {
                    TaskId = taskId,
                    Title = title,
                    Date = occ.Date,
                    Kind = CalendarItemKind.Occurrence,
                    StatusId = statusId,
                    TaskKind = kind,
                    RecurrenceId = recurrenceId,
                });
            }
        }

        return result;
    }
}
