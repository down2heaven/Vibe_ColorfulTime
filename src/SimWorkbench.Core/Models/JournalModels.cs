namespace SimWorkbench.Core.Models;

/// <summary>每日总结：同一天只对应一份可持续编辑的记录。</summary>
public sealed class DailySummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Today;
    public string? Summary { get; set; }
    public string? CompletedItems { get; set; }
    public string? Problems { get; set; }
    public string? PendingItems { get; set; }
    public string? TomorrowPlan { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Summary) && string.IsNullOrWhiteSpace(CompletedItems)
        && string.IsNullOrWhiteSpace(Problems) && string.IsNullOrWhiteSpace(PendingItems)
        && string.IsNullOrWhiteSpace(TomorrowPlan);

    public string GetPreview()
    {
        var text = new[] { Summary, CompletedItems, Problems, PendingItems, TomorrowPlan }
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        return string.IsNullOrWhiteSpace(text) ? "（空记录）" : Summarize(text, 80);
    }

    internal static string Summarize(string text, int max)
    {
        var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}

/// <summary>主题日志（长期记录 + 当前断点）。</summary>
public sealed class TopicLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Category { get; set; }
    /// <summary>当前进度/断点，重点字段。</summary>
    public string? CurrentBreakpoint { get; set; }
    /// <summary>详细内容/本次记录。</summary>
    public string? Content { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public IReadOnlyList<string> GetTagList() =>
        string.IsNullOrWhiteSpace(Tags)
            ? Array.Empty<string>()
            : Tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string GetBreakpointPreview() =>
        string.IsNullOrWhiteSpace(CurrentBreakpoint) ? "（尚未记录断点）" : DailySummary.Summarize(CurrentBreakpoint, 60);
}

/// <summary>断点/内容的历史更新记录：更新断点时旧断点进入历史，不默认覆盖。</summary>
public sealed class TopicLogHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TopicId { get; set; }
    /// <summary>本次更新前的断点内容。</summary>
    public string? PreviousBreakpoint { get; set; }
    /// <summary>本次更新前的内容快照。</summary>
    public string? PreviousContent { get; set; }
    /// <summary>更新说明（记录本次新增了什么）。</summary>
    public string? ChangeNote { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.Now;

    public string GetTitle() => string.IsNullOrWhiteSpace(PreviousBreakpoint) ? "（无断点记录）" : PreviousBreakpoint!;
}
