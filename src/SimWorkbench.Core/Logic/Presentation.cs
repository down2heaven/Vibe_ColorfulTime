using SimWorkbench.Core.Models;

namespace SimWorkbench.Core.Logic;

/// <summary>展示层格式化（保持 UI 文案统一）。</summary>
public static class Presentation
{
    public static string FormatDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm") ?? "—";

    public static string FormatDate(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? "—";

    public static string FormatKind(TaskKind kind) => kind == TaskKind.Simulation ? "仿真任务" : "一般任务";

    public static string FormatPriority(Priority priority) => priority switch
    {
        Priority.High => "高",
        Priority.Urgent => "紧急",
        _ => "普通",
    };

    /// <summary>把分钟数格式化为“2 小时 30 分钟”。</summary>
    public static string FormatEstimatedTime(int? minutes)
    {
        if (minutes is null)
        {
            return "未填写";
        }

        var total = Math.Max(0, minutes.Value);
        if (total == 0)
        {
            return "0 分钟";
        }

        var hours = total / 60;
        var mins = total % 60;
        if (hours == 0)
        {
            return $"{mins} 分钟";
        }

        return mins == 0 ? $"{hours} 小时" : $"{hours} 小时 {mins} 分钟";
    }

    /// <summary>交付时间提示：逾期 / 今天到期 / 明天到期 / 未来 n 天。</summary>
    public static string FormatDueHint(DateTime? due, DateTime now)
    {
        if (due is null)
        {
            return "未设置";
        }

        var days = (due.Value.Date - now.Date).Days;
        return days switch
        {
            < 0 => $"逾期 {-days} 天",
            0 => "今天到期",
            1 => "明天到期",
            <= 7 => $"{days} 天后到期",
            _ => due.Value.ToString("yyyy-MM-dd"),
        };
    }

    public static string FormatWeekDay(DateTime date)
    {
        var name = date.DayOfWeek switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            _ => "周日",
        };
        return name;
    }

    public static string FormatMonthTitle(DateTime date) => $"{date:yyyy 年 M 月}";

    public static string FormatTags(string? tags) => string.IsNullOrWhiteSpace(tags) ? "—" : tags.Replace(";", "，");
}
