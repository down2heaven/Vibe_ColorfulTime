namespace SimWorkbench.Core.Models;

public enum CalendarItemKind
{
    PlannedStart = 0,
    Due = 1,
    Occurrence = 2,
}

/// <summary>月历上的一项（由任务日期或周期实例派生）。</summary>
public sealed class CalendarItem
{
    public Guid TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? Time { get; set; }
    public CalendarItemKind Kind { get; set; }
    public Guid StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string Color { get; set; } = "#8A8A8A";
    public TaskKind TaskKind { get; set; }
    public bool IsCompleted { get; set; }
    public Guid? RecurrenceId { get; set; }

    public string KindText => Kind switch
    {
        CalendarItemKind.PlannedStart => "开始",
        CalendarItemKind.Due => "交付",
        _ => "周期",
    };
}

/// <summary>月历单元格数据。</summary>
public sealed class CalendarDay
{
    public DateTime Date { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsToday { get; set; }
    public List<CalendarItem> Items { get; set; } = new();
    public int OverflowCount => Math.Max(0, Items.Count - 3);
}
