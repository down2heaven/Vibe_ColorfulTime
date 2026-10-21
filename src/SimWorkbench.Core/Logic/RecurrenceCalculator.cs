using SimWorkbench.Core.Models;

namespace SimWorkbench.Core.Logic;

/// <summary>周期规则计算（纯逻辑，便于单独验证）。</summary>
public static class RecurrenceCalculator
{
    public const int MaxOccurrencesPerRange = 2000;

    /// <summary>判断某一天是否为该规则的一次执行。</summary>
    public static bool Matches(RecurrenceRule rule, DateTime date)
    {
        date = date.Date;
        if (!rule.IsActive)
        {
            return false;
        }

        var start = rule.StartDate.Date;
        if (date < start)
        {
            return false;
        }

        if (rule.EndDate is { } end && date > end.Date)
        {
            return false;
        }

        var interval = Math.Max(1, rule.Interval);
        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Daily:
                return (date - start).Days % interval == 0;

            case RecurrenceFrequency.Weekly:
            {
                var weekDays = rule.GetWeekDayList();
                var allowed = weekDays.Count == 0
                    ? date.DayOfWeek == start.DayOfWeek
                    : weekDays.Contains(date.DayOfWeek);
                if (!allowed)
                {
                    return false;
                }

                var anchor = start.DayOfWeek;
                var startWeek = StartOfWeek(start, anchor);
                var currentWeek = StartOfWeek(date, anchor);
                var weeks = (int)Math.Round((currentWeek - startWeek).TotalDays / 7.0);
                return weeks >= 0 && weeks % interval == 0;
            }

            case RecurrenceFrequency.Monthly:
            {
                var months = (date.Year - start.Year) * 12 + (date.Month - start.Month);
                if (months < 0 || months % interval != 0)
                {
                    return false;
                }

                if (date.Day == start.Day)
                {
                    return true;
                }

                // 29/30/31 日在较短月份回退到当月最后一天
                var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
                return start.Day > daysInMonth && date.Day == daysInMonth;
            }

            default:
                return false;
        }
    }

    public static DateTime StartOfWeek(DateTime date, DayOfWeek firstDayOfWeek)
    {
        var diff = ((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return date.Date.AddDays(-diff);
    }

    /// <summary>列出 [fromInclusive, toInclusive] 内的执行日期（含边界）。</summary>
    public static IReadOnlyList<DateTime> GetOccurrences(RecurrenceRule rule, DateTime fromInclusive, DateTime toInclusive)
    {
        var result = new List<DateTime>();
        var from = fromInclusive.Date;
        var to = toInclusive.Date;
        if (to < from)
        {
            return result;
        }

        // 不需要从规则起点开始遍历：按频率直接推进
        var cursor = from;
        if (cursor < rule.StartDate.Date)
        {
            cursor = rule.StartDate.Date;
        }

        while (cursor <= to && result.Count < MaxOccurrencesPerRange)
        {
            if (Matches(rule, cursor))
            {
                result.Add(cursor);
            }

            cursor = cursor.AddDays(1);
        }

        return result;
    }

    /// <summary>找出 fromInclusive 之后（含）的第一次执行。</summary>
    public static DateTime? GetNextOccurrence(RecurrenceRule rule, DateTime fromInclusive)
    {
        var found = GetOccurrences(rule, fromInclusive, fromInclusive.Date.AddDays(366 * 5));
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>生成下拉框中展示的频率文本。</summary>
    public static string Describe(RecurrenceFrequency frequency, int interval, IEnumerable<DayOfWeek>? weekDays = null, DateTime? endDate = null)
    {
        var unit = frequency switch
        {
            RecurrenceFrequency.Daily => "天",
            RecurrenceFrequency.Weekly => "周",
            _ => "月",
        };
        var text = interval <= 1 ? $"每{unit}" : $"每 {interval} {unit}";
        if (frequency == RecurrenceFrequency.Weekly && weekDays is not null)
        {
            var days = weekDays.ToList();
            if (days.Count > 0)
            {
                text += "（" + string.Join("、", days.Select(RecurrenceRule.WeekDayName)) + "）";
            }
        }

        if (endDate is { } end)
        {
            text += $" 至 {end:yyyy-MM-dd}";
        }

        return text;
    }
}
