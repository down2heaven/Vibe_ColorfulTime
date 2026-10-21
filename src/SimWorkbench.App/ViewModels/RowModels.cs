using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SimWorkbench.App.Common;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;

namespace SimWorkbench.App.ViewModels;

/// <summary>任务列表行（展示层投影，不承担业务逻辑）。</summary>
public sealed class TaskRow
{
    public TaskRow(TaskItem task, WorkStatus? status, DateTime now)
    {
        Task = task;
        StatusName = status?.Name ?? "未设置";
        StatusBrush = UiUtil.BrushFromHex(status?.Color);
        KindText = Presentation.FormatKind(task.Kind);
        DueText = Presentation.FormatDueHint(task.DueAt, now);
        IsOverdue = task.IsOverdue(now);
        var dueHint = DueText;
        DueBrush = IsOverdue
            ? UiUtil.BrushFromHex("#D13438")
            : UiUtil.BrushFromHex("#2EA043");

        var time = task.Kind == TaskKind.Simulation
            ? $"计算所需：{Presentation.FormatEstimatedTime(task.EstimatedMinutes)}"
            : string.Empty;
        TimeText = time;
        TimeVisibility = string.IsNullOrEmpty(time) ? Visibility.Collapsed : Visibility.Visible;

        var tags = task.GetTagsDisplay();
        TagText = tags == "—" ? string.Empty : tags;
        TagVisibility = string.IsNullOrEmpty(TagText) ? Visibility.Collapsed : Visibility.Visible;

        var recur = task.RecurrenceId is null ? string.Empty : "周期实例";
        RecurrenceText = recur;
        RecurrenceVisibility = string.IsNullOrEmpty(recur) ? Visibility.Collapsed : Visibility.Visible;

        OverdueVisibility = IsOverdue ? Visibility.Visible : Visibility.Collapsed;
        PriorityText = task.Priority == Priority.Normal ? string.Empty : Presentation.FormatPriority(task.Priority);
        PriorityVisibility = string.IsNullOrEmpty(PriorityText) ? Visibility.Collapsed : Visibility.Visible;
        PriorityBrush = task.Priority switch
        {
            Priority.Urgent => UiUtil.BrushFromHex("#D13438"),
            Priority.High => UiUtil.BrushFromHex("#F58A1F"),
            _ => UiUtil.BrushFromHex("#9AA0A6"),
        };

        var parts = new List<string> { KindText };
        if (task.DueAt is not null)
        {
            parts.Add($"交付 {task.DueAt:MM-dd HH:mm}");
        }

        if (task.ActualDeliveryAt is { } actual)
        {
            parts.Add($"实际 {actual:MM-dd}");
        }

        Subtitle = string.Join("　·　", parts);
        DueVisibility = task.DueAt is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public TaskItem Task { get; }
    public Guid Id => Task.Id;
    public string Title => Task.Title;
    public string StatusName { get; }
    public SolidColorBrush StatusBrush { get; }
    public string KindText { get; }
    public string DueText { get; }
    public SolidColorBrush DueBrush { get; }
    public bool IsOverdue { get; }
    public string TimeText { get; }
    public Visibility TimeVisibility { get; }
    public string TagText { get; }
    public Visibility TagVisibility { get; }
    public string RecurrenceText { get; }
    public Visibility RecurrenceVisibility { get; }
    public Visibility OverdueVisibility { get; }
    public string PriorityText { get; }
    public Visibility PriorityVisibility { get; }
    public SolidColorBrush PriorityBrush { get; }
    public string Subtitle { get; }
    public Visibility DueVisibility { get; }
}

/// <summary>状态下拉框选项。</summary>
public sealed class StatusChoice
{
    public StatusChoice(WorkStatus status)
    {
        Status = status;
        Name = status.Name;
        Brush = UiUtil.BrushFromHex(status.Color);
    }

    public WorkStatus Status { get; }
    public Guid Id => Status.Id;
    public string Name { get; }
    public SolidColorBrush Brush { get; }

    public override string ToString() => Name;
}

/// <summary>周期规则下拉/列表行。</summary>
public sealed class RecurrenceRow
{
    public RecurrenceRow(RecurrenceRule rule, int futureCount)
    {
        Rule = rule;
        Title = rule.Title;
        FrequencyText = rule.GetFrequencyText();
        FutureText = $"未来实例 {futureCount} 个";
        KindText = Presentation.FormatKind(rule.Kind);
    }

    public RecurrenceRule Rule { get; }
    public string Title { get; }
    public string FrequencyText { get; }
    public string FutureText { get; }
    public string KindText { get; }
}

/// <summary>主题日志列表行。</summary>
public sealed class TopicRow
{
    public TopicRow(TopicLog topic)
    {
        Topic = topic;
        Title = topic.Title;
        CategoryText = string.IsNullOrWhiteSpace(topic.Category) ? "未分类" : topic.Category!;
        BreakpointText = topic.GetBreakpointPreview();
        UpdatedText = $"更新于 {topic.UpdatedAt:yyyy-MM-dd HH:mm}";
    }

    public TopicLog Topic { get; }
    public string Title { get; }
    public string CategoryText { get; }
    public string BreakpointText { get; }
    public string UpdatedText { get; }
}

/// <summary>断点历史行。</summary>
public sealed class HistoryRow
{
    public HistoryRow(TopicLogHistory history)
    {
        ChangedText = $"更新于 {history.ChangedAt:yyyy-MM-dd HH:mm}";
        BreakpointText = "旧断点：" + (string.IsNullOrWhiteSpace(history.PreviousBreakpoint)
            ? "（空）"
            : history.PreviousBreakpoint!);
        var content = string.IsNullOrWhiteSpace(history.PreviousContent)
            ? string.Empty
            : "旧内容：" + UiUtil.ShortText(history.PreviousContent, 120);
        ContentText = content;
        ContentVisibility = string.IsNullOrEmpty(content) ? Visibility.Collapsed : Visibility.Visible;
    }

    public string ChangedText { get; }
    public string BreakpointText { get; }
    public string ContentText { get; }
    public Visibility ContentVisibility { get; }
}

/// <summary>每日总结历史行。</summary>
public sealed class SummaryRow
{
    public SummaryRow(DailySummary summary)
    {
        Summary = summary;
        DateText = UiUtil.FormatDateText(summary.Date);
        Preview = summary.GetPreview();
    }

    public DailySummary Summary { get; }
    public string DateText { get; }
    public string Preview { get; }
}

/// <summary>清单进度行（首页卡片 / 清单页）。</summary>
public sealed class ChecklistProgressRow
{
    public ChecklistProgressRow(Checklist checklist)
    {
        Checklist = checklist;
        Name = checklist.Name;
        ProgressText = $"{checklist.DoneCount}/{checklist.TotalCount}";
        ProgressPercent = checklist.Progress * 100;
        CategoryText = string.IsNullOrWhiteSpace(checklist.Category) ? "未分类" : checklist.Category!;
    }

    public Checklist Checklist { get; }
    public string Name { get; }
    public string ProgressText { get; }
    public double ProgressPercent { get; }
    public string CategoryText { get; }
}
