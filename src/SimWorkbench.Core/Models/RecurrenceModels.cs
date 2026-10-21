namespace SimWorkbench.Core.Models;

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
}

/// <summary>周期规则：生成互相独立的执行实例（实例是真实任务行）。</summary>
public sealed class RecurrenceRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public TaskKind Kind { get; set; } = TaskKind.General;
    public Guid StatusId { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Tags { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public string? CaseName { get; set; }
    public string? CaseCondition { get; set; }
    public int? EstimatedMinutes { get; set; }
    public string? CalculationNotes { get; set; }

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Weekly;
    /// <summary>间隔，例如每 2 周：Frequency=Weekly、Interval=2。</summary>
    public int Interval { get; set; } = 1;
    /// <summary>每周指定星期（0=周日 … 6=周六），分号分隔；仅 Weekly 使用。空表示使用开始日期的星期。</summary>
    public string? WeekDays { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public RecurrenceRule Clone() => (RecurrenceRule)MemberwiseClone();

    public IReadOnlyList<DayOfWeek> GetWeekDayList()    {
        if (string.IsNullOrWhiteSpace(WeekDays))
        {
            return Array.Empty<DayOfWeek>();
        }

        var result = new List<DayOfWeek>();
        foreach (var part in WeekDays.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var value) && value is >= 0 and <= 6)
            {
                result.Add((DayOfWeek)value);
            }
        }

        return result;
    }

    public void SetWeekDays(IEnumerable<DayOfWeek> days) =>
        WeekDays = string.Join(";", days.Select(d => ((int)d).ToString()).Distinct().OrderBy(v => v));

    public string GetFrequencyText()
    {
        var unit = Frequency switch
        {
            RecurrenceFrequency.Daily => "天",
            RecurrenceFrequency.Weekly => "周",
            _ => "月",
        };
        var text = Interval <= 1 ? $"每{unit}" : $"每 {Interval} {unit}";
        if (Frequency == RecurrenceFrequency.Weekly)
        {
            var days = GetWeekDayList();
            if (days.Count > 0)
            {
                text += "（" + string.Join("、", days.Select(WeekDayName)) + "）";
            }
        }

        if (EndDate is { } end)
        {
            text += $"，至 {end:yyyy-MM-dd}";
        }

        return text;
    }

    public static string WeekDayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日",
    };

    public bool IsOccurrenceAllowed(DateTime date)
    {
        if (!IsActive)
        {
            return false;
        }

        if (date.Date < StartDate.Date)
        {
            return false;
        }

        if (EndDate is { } end && date.Date > end.Date)
        {
            return false;
        }

        return Frequency switch
        {
            RecurrenceFrequency.Daily => true,
            RecurrenceFrequency.Weekly => IsWeekDayAllowed(date.DayOfWeek, StartDate.DayOfWeek),
            RecurrenceFrequency.Monthly => date.Day == StartDate.Day || IsLastDayOfMonthFallback(date),
            _ => false,
        };
    }

    private bool IsWeekDayAllowed(DayOfWeek day, DayOfWeek fallback)
    {
        var days = GetWeekDayList();
        return days.Count == 0 ? day == fallback : days.Contains(day);
    }

    /// <summary>每月 29/30/31 日在较短月份回退到当月最后一天。</summary>
    private bool IsLastDayOfMonthFallback(DateTime date) =>
        StartDate.Day > DateTime.DaysInMonth(date.Year, date.Month)
        && date.Day == DateTime.DaysInMonth(date.Year, date.Month);
}

/// <summary>周期实例记录：保证同一规则同一天只生成一次，并支持“跳过某次”。</summary>
public sealed class RecurrenceInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public DateTime OccurrenceDate { get; set; }
    public Guid? TaskId { get; set; }
    public bool IsSkipped { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>删除周期规则时的作用范围。</summary>
public enum RecurrenceDeleteScope
{
    /// <summary>只删除规则，保留已生成的实例任务。</summary>
    RuleOnly = 0,
    /// <summary>删除规则与其生成的全部实例任务。</summary>
    RuleAndInstances = 1,
    /// <summary>删除规则，同时删除今天及以后的实例任务，保留历史实例。</summary>
    RuleAndFutureInstances = 2,
}
