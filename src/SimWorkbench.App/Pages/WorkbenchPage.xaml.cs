using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SimWorkbench.App.Common;
using SimWorkbench.App.Dialogs;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;

namespace SimWorkbench.App.Pages;

/// <summary>工作台首页：按用户设置显示/排序卡片，数据来自各业务服务。</summary>
public sealed partial class WorkbenchPage : Page, IPageRefresh
{
    private WorkbenchServices Services => App.Services;
    private bool _loadedOnce;

    public WorkbenchPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        Build();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.ServicesAvailable && IsLoaded)
        {
            Build();
        }
    }

    public void RefreshPage() => Build();

    private void Build()
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        try
        {
            GreetingText.Text = $"{UiUtil.Greeting()}，今天也把任务推进一点";
            DateText.Text = $"{UiUtil.FormatDateText(DateTime.Today)}　·　本地数据已自动保存";
            BuildTiles();

            var settings = Services.Settings.Load();
            var cardDefs = Services.Modules.GetHomeCardDefinitions().ToDictionary(c => c.Id);
            CardsHost.Children.Clear();
            foreach (var cardSetting in settings.HomeCards.OrderBy(c => c.Order))
            {
                if (!cardSetting.IsVisible || !cardDefs.TryGetValue(cardSetting.CardId, out var definition))
                {
                    continue;
                }

                if (!IsCardAvailable(cardSetting.CardId))
                {
                    continue;
                }

                var element = BuildCard(cardSetting.CardId, definition.Title, definition.Description);
                if (element is not null)
                {
                    CardsHost.Children.Add(element);
                }
            }

            if (CardsHost.Children.Count == 0)
            {
                CardsHost.Children.Add(new Border
                {
                    Style = (Style)Application.Current.Resources["CardBorderStyle"],
                    Child = UiUtil.EmptyHint("首页卡片已全部隐藏。可到“设置 → 界面与布局”中重新显示需要的卡片。"),
                });
            }


        }
        catch (Exception ex)
        {
            AppLog.Error("首页加载失败", ex);
            CardsHost.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["CardBorderStyle"],
                Child = UiUtil.EmptyHint($"首页加载失败：{ex.Message}"),
            });
        }
    }

    /// <summary>卡片依赖的模块是否启用（停用模块时卡片自动隐藏，数据仍保留）。</summary>
    private bool IsCardAvailable(string cardId) => cardId switch
    {
        HomeCardIds.TodaySchedule => Services.Modules.IsEnabled(ModuleIds.Calendar),
        HomeCardIds.RecentBreakpoints => Services.Modules.IsEnabled(ModuleIds.TopicLog),
        HomeCardIds.RecentSummaries => Services.Modules.IsEnabled(ModuleIds.DailySummary),
        HomeCardIds.ChecklistProgress => Services.Modules.IsEnabled(ModuleIds.Checklist),
        _ => true,
    };

    /// <summary>每张首页卡片对应的强调色，保持与模块主题色一致。</summary>
    private static string CardAccent(string cardId) => cardId switch
    {
        HomeCardIds.TodayTodos => "#0078D4",
        HomeCardIds.InProgress => "#2B88D8",
        HomeCardIds.Computing => "#F58A1F",
        HomeCardIds.DueSoon => "#D13438",
        HomeCardIds.TodaySchedule => "#7A3FD1",
        HomeCardIds.RecentBreakpoints => "#C239B3",
        HomeCardIds.RecentSummaries => "#12A5A5",
        HomeCardIds.ChecklistProgress => "#2EA043",
        _ => "#0078D4",
    };

    /// <summary>构建 Windows Phone 风格快捷磁贴。</summary>
    private void BuildTiles()
    {
        TilesHost.Children.Clear();
        TilesHost.Children.Add(UiUtil.LiveTile("\uE710", "新建任务", "#0078D4",
            async (_, _) => await OpenTaskEditorAsync(null)));
        TilesHost.Children.Add(UiUtil.LiveTile("\uE9D9", "新建仿真任务", "#F58A1F",
            async (_, _) => await OpenTaskEditorAsync(null, TaskKind.Simulation)));

        if (Services.Modules.IsEnabled(ModuleIds.DailySummary))
        {
            TilesHost.Children.Add(UiUtil.LiveTile("\uE8A5", "写今日总结", "#12A5A5",
                (_, _) => App.Main?.NavigateTo(ModuleIds.DailySummary)));
        }

        if (Services.Modules.IsEnabled(ModuleIds.Calendar))
        {
            TilesHost.Children.Add(UiUtil.LiveTile("\uE787", "打开日历", "#7A3FD1",
                (_, _) => App.Main?.NavigateTo(ModuleIds.Calendar)));
        }

        if (Services.Modules.IsEnabled(ModuleIds.Checklist))
        {
            TilesHost.Children.Add(UiUtil.LiveTile("\uE9D5", "打开清单", "#2EA043",
                (_, _) => App.Main?.NavigateTo(ModuleIds.Checklist)));
        }

        if (Services.Modules.IsEnabled(ModuleIds.TopicLog))
        {
            TilesHost.Children.Add(UiUtil.LiveTile("\uE70F", "记录断点", "#C239B3",
                (_, _) => App.Main?.NavigateTo(ModuleIds.TopicLog)));
        }
    }

    private Border? BuildCard(string cardId, string title, string description)
    {
        var content = new StackPanel { Spacing = 6 };
        switch (cardId)
        {
            case HomeCardIds.TodayTodos:
                BuildTaskCard(content, "今天没有需要处理的待办。", tasks =>
                {
                    var today = DateTime.Today;
                    return tasks.Where(t => t.ActualDeliveryAt is null
                                            && (t.DueAt?.Date == today || t.PlannedStart?.Date == today))
                        .Take(6).ToList();
                });
                break;

            case HomeCardIds.InProgress:
                BuildTaskCard(content, "没有正在处理的任务。", tasks =>
                {
                    var activeNames = new[] { "正在处理", "正在修复问题", "进行中" };
                    return tasks.Where(t => t.ActualDeliveryAt is null
                                            && Services.Statuses.Get(t.StatusId) is { } s
                                            && activeNames.Contains(s.Name))
                        .Take(6).ToList();
                });
                break;

            case HomeCardIds.Computing:
                BuildTaskCard(content, "当前没有计算中的仿真任务。", tasks =>
                    tasks.Where(t => t.Kind == TaskKind.Simulation
                                     && t.ActualDeliveryAt is null
                                     && Services.Statuses.Get(t.StatusId) is { } s
                                     && s.Name == "计算中")
                        .Take(6).ToList());
                break;

            case HomeCardIds.DueSoon:
                BuildTaskCard(content, "最近没有即将到期或逾期的任务。", tasks =>
                {
                    var now = DateTime.Now;
                    return tasks.Where(t => t.IsOverdue(now) || t.IsDueSoon(now, 3))
                        .OrderBy(t => t.DueAt).Take(6).ToList();
                });
                break;

            case HomeCardIds.TodaySchedule:
                BuildScheduleCard(content);
                break;

            case HomeCardIds.RecentBreakpoints:
                BuildRecentTopicsCard(content);
                break;

            case HomeCardIds.RecentSummaries:
                BuildRecentSummariesCard(content);
                break;

            case HomeCardIds.ChecklistProgress:
                BuildChecklistCard(content);
                break;

            default:
                return null;
        }

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBlock = new StackPanel { Spacing = 1 };
        titleBlock.Children.Add(UiUtil.SectionTitle(title));
        titleBlock.Children.Add(UiUtil.Caption(description));
        header.Children.Add(titleBlock);

        var moreKey = cardId switch
        {
            HomeCardIds.RecentBreakpoints => ModuleIds.TopicLog,
            HomeCardIds.RecentSummaries => ModuleIds.DailySummary,
            HomeCardIds.ChecklistProgress => ModuleIds.Checklist,
            HomeCardIds.TodaySchedule => ModuleIds.Calendar,
            _ => ModuleIds.Tasks,
        };
        var moreButton = UiUtil.MakeButton("查看全部 ›", (_, _) => App.Main?.NavigateTo(moreKey));
        moreButton.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(moreButton, 1);
        header.Children.Add(moreButton);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(header);
        panel.Children.Add(content);
        return UiUtil.AccentCard(panel, CardAccent(cardId));
    }

    private void BuildTaskCard(StackPanel host, string emptyText, Func<IReadOnlyList<TaskItem>, List<TaskItem>> selector)
    {
        var tasks = selector(Services.Tasks.Query(new TaskQuery { OnlyOpen = true, Limit = 300 }));
        if (tasks.Count == 0)
        {
            host.Children.Add(UiUtil.EmptyHint(emptyText));
            return;
        }

        foreach (var task in tasks)
        {
            host.Children.Add(CreateTaskLine(task));
        }
    }

    private FrameworkElement CreateTaskLine(TaskItem task)
    {
        var status = Services.Statuses.Get(task.StatusId);
        var color = status?.Color ?? "#8A8A8A";
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(UiUtil.ColorDot(color, 10));

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock
        {
            Text = task.Title,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = $"{Presentation.FormatKind(task.Kind)}　·　{status?.Name ?? "未设置"}　·　{Presentation.FormatDueHint(task.DueAt, DateTime.Now)}",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
        });
        // 状态颜色条放在文字下方，让状态更醒目
        text.Children.Add(UiUtil.StatusBar(color, 4));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var button = FlatButton(grid);
        button.Click += async (_, _) => await OpenTaskEditorAsync(task);
        return button;
    }

    private static Button FlatButton(UIElement content) => new()
    {
        Content = content,
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(2),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    private async Task OpenTaskEditorAsync(TaskItem? task, TaskKind defaultKind = TaskKind.General)
    {
        try
        {
            var dialog = new TaskEditorDialog(Services, task, task is null ? defaultKind : null) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Build();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("编辑任务失败", ex);
            await App.ShowMessageAsync(this, "无法打开任务", ex.Message);
        }
    }

    private void BuildScheduleCard(StackPanel host)
    {
        var today = DateTime.Today;
        var any = false;
        for (var i = 0; i < 7; i++)
        {
            var date = today.AddDays(i);
            var items = Services.Calendar.GetDayItems(date);
            if (items.Count == 0)
            {
                continue;
            }

            any = true;
            host.Children.Add(new TextBlock
            {
                Text = i == 0
                    ? $"今天　{UiUtil.FormatShortDate(date)}"
                    : $"{UiUtil.FormatShortDate(date)} {Presentation.FormatWeekDay(date)}",
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                Margin = new Thickness(0, 4, 0, 0),
            });

            foreach (var item in items.Take(4))
            {
                host.Children.Add(CreateScheduleLine(item));
            }
        }

        if (!any)
        {
            host.Children.Add(UiUtil.EmptyHint("最近 7 天没有安排计划开始或交付的任务。"));
        }
    }

    private FrameworkElement CreateScheduleLine(CalendarItem item)
    {
        var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 3, 4, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(UiUtil.ColorDot(item.Color, 8));

        var title = new TextBlock
        {
            Text = item.Title,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var kind = new TextBlock
        {
            Text = item.KindText,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
        };
        Grid.SetColumn(kind, 2);
        grid.Children.Add(kind);

        var button = FlatButton(grid);
        button.Click += async (_, _) =>
        {
            var task = Services.Tasks.Get(item.TaskId);
            if (task is not null)
            {
                await OpenTaskEditorAsync(task);
            }
        };
        return button;
    }

    private void BuildRecentTopicsCard(StackPanel host)
    {
        var topics = Services.TopicLogs.GetRecent(5);
        if (topics.Count == 0)
        {
            host.Children.Add(UiUtil.EmptyHint("还没有主题日志。可以在“主题日志 / 断点”中开始记录。"));
            return;
        }

        foreach (var topic in topics)
        {
            var panel = new StackPanel { Spacing = 1 };
            panel.Children.Add(new TextBlock { Text = topic.Title, FontSize = 13 });
            panel.Children.Add(new TextBlock
            {
                Text = $"{topic.GetBreakpointPreview()}　·　{topic.UpdatedAt:MM-dd HH:mm}",
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            });
            var button = FlatButton(panel);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => App.Main?.NavigateTo(ModuleIds.TopicLog);
            host.Children.Add(button);
        }
    }

    private void BuildRecentSummariesCard(StackPanel host)
    {
        var summaries = Services.DailySummaries.GetRecent(5);
        if (summaries.Count == 0)
        {
            host.Children.Add(UiUtil.EmptyHint("还没有每日总结。点击“写今日总结”开始记录。"));
            return;
        }

        foreach (var summary in summaries)
        {
            var panel = new StackPanel { Spacing = 1 };
            panel.Children.Add(new TextBlock { Text = UiUtil.FormatDateText(summary.Date), FontSize = 13 });
            panel.Children.Add(new TextBlock
            {
                Text = summary.GetPreview(),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            });
            var button = FlatButton(panel);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => App.Main?.NavigateTo(ModuleIds.DailySummary);
            host.Children.Add(button);
        }
    }

    private void BuildChecklistCard(StackPanel host)
    {
        var checklists = Services.Checklists.GetAll();
        if (checklists.Count == 0)
        {
            host.Children.Add(UiUtil.EmptyHint("还没有 Checklist。可以在“Checklist”中创建仿真前检查、结果检查等清单。"));
            return;
        }

        foreach (var checklist in checklists.Take(5))
        {
            var panel = new StackPanel { Spacing = 2 };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = checklist.Name, FontSize = 13 });
            var count = new TextBlock
            {
                Text = checklist.ProgressText,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            };
            Grid.SetColumn(count, 1);
            row.Children.Add(count);
            panel.Children.Add(row);
            panel.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = checklist.Progress * 100,
                Height = 4,
            });
            host.Children.Add(panel);
        }
    }


}
