using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SimWorkbench.App.Common;
using SimWorkbench.App.Dialogs;
using SimWorkbench.App.ViewModels;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;
using SimWorkbench.Data;

namespace SimWorkbench.App.Pages;

/// <summary>每日总结：按日期持续编辑同一份记录，可关联当日任务与清单。</summary>
public sealed partial class DailySummaryPage : Page, IPageRefresh
{
    private WorkbenchServices Services => App.Services;
    private DateTime _currentDate = DateTime.Today;
    private bool _suppressDateChange;
    private bool _loadedOnce;

    public DailySummaryPage()
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
        _suppressDateChange = true;
        UiUtil.SetDate(DateSelector, _currentDate);
        _suppressDateChange = false;
        LoadDate(_currentDate);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.ServicesAvailable && IsLoaded)
        {
            LoadDate(_currentDate);
        }
    }

    public void RefreshPage() => LoadDate(_currentDate);

    private void LoadDate(DateTime date)
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        try
        {
            _currentDate = date.Date;
            _suppressDateChange = true;
            UiUtil.SetDate(DateSelector, _currentDate);
            _suppressDateChange = false;

            var summary = Services.DailySummaries.Get(_currentDate);
            SummaryBox.Text = summary.Summary ?? string.Empty;
            CompletedBox.Text = summary.CompletedItems ?? string.Empty;
            ProblemsBox.Text = summary.Problems ?? string.Empty;
            PendingBox.Text = summary.PendingItems ?? string.Empty;
            TomorrowBox.Text = summary.TomorrowPlan ?? string.Empty;
            SaveStatusText.Text = Services.DailySummaries.Exists(_currentDate)
                ? $"上次保存：{summary.UpdatedAt:HH:mm}"
                : "尚未保存";

            BuildLinkedTasks();
            BuildLinkedChecklists();
            BuildHistory();
        }
        catch (Exception ex)
        {
            AppLog.Error("每日总结加载失败", ex);
        }
    }

    private void BuildLinkedTasks()
    {
        LinkedTasksHost.Children.Clear();
        var linked = Services.DailySummaries.GetLinkedTaskIds(_currentDate).ToHashSet();
        var tasks = Services.Tasks.Query(new TaskQuery
        {
            OnlyOpen = true,
            DueAfter = _currentDate.AddDays(-7),
            DueBefore = _currentDate.AddDays(30),
            Limit = 300,
        });

        var today = tasks.Where(t =>
                t.DueAt?.Date == _currentDate
                || t.PlannedStart?.Date == _currentDate
                || t.OccurrenceDate?.Date == _currentDate
                || linked.Contains(t.Id))
            .ToList();

        if (today.Count == 0)
        {
            LinkedTasksHost.Children.Add(UiUtil.EmptyHint("这一天没有相关的未完成任务。"));
            return;
        }

        foreach (var task in today)
        {
            var status = Services.Statuses.Get(task.StatusId);
            var checkbox = new CheckBox
            {
                IsChecked = linked.Contains(task.Id),
                Tag = task.Id,
                Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = task.Title, FontSize = 13, TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = $"{Presentation.FormatKind(task.Kind)}　·　{status?.Name ?? "未设置"}",
                            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                        },
                    },
                },
            };
            LinkedTasksHost.Children.Add(checkbox);
        }
    }

    private void BuildLinkedChecklists()
    {
        LinkedChecklistHost.Children.Clear();
        var linked = Services.Checklists.GetLinkedChecklists(LinkOwnerTypes.DailySummary, SummaryOwnerId())
            .Select(c => c.Id).ToHashSet();
        if (linked.Count == 0)
        {
            LinkedChecklistHost.Children.Add(UiUtil.EmptyHint("尚未关联清单。"));
            return;
        }

        foreach (var checklist in Services.Checklists.GetAll().Where(c => linked.Contains(c.Id)))
        {
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
            LinkedChecklistHost.Children.Add(row);
        }
    }

    /// <summary>每日总结的关联键固定由日期派生，保证“先关联清单再保存”也不会丢失。</summary>
    private Guid SummaryOwnerId() => StableDateId(_currentDate);

    /// <summary>未保存的每日总结用日期派生一个稳定 ID，避免关联丢失。</summary>
    private static Guid StableDateId(DateTime date)
    {
        var bytes = new byte[16];
        var text = System.Text.Encoding.UTF8.GetBytes($"daily-summary:{date:yyyy-MM-dd}");
        var hash = System.Security.Cryptography.MD5.HashData(text);
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private void BuildHistory()
    {
        var rows = Services.DailySummaries.GetRecent(60).Select(s => new SummaryRow(s)).ToList();
        HistoryList.ItemsSource = rows;
    }

    private void OnHistorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is SummaryRow row)
        {
            LoadDate(row.Summary.Date);
        }
    }

    private void OnPreviousDay(object sender, RoutedEventArgs e) => LoadDate(_currentDate.AddDays(-1));

    private void OnNextDay(object sender, RoutedEventArgs e) => LoadDate(_currentDate.AddDays(1));

    private void OnToday(object sender, RoutedEventArgs e) => LoadDate(DateTime.Today);

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_suppressDateChange || args.NewDate is null)
        {
            return;
        }

        LoadDate(args.NewDate.Value.LocalDateTime.Date);
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveCurrent(showConfirmation: true);

    private bool SaveCurrent(bool showConfirmation)
    {
        try
        {
            var summary = Services.DailySummaries.Get(_currentDate);
            summary.Date = _currentDate;
            summary.Summary = NullIfEmpty(SummaryBox.Text);
            summary.CompletedItems = NullIfEmpty(CompletedBox.Text);
            summary.Problems = NullIfEmpty(ProblemsBox.Text);
            summary.PendingItems = NullIfEmpty(PendingBox.Text);
            summary.TomorrowPlan = NullIfEmpty(TomorrowBox.Text);
            Services.DailySummaries.Save(summary);

            var taskIds = LinkedTasksHost.Children.OfType<CheckBox>()
                .Where(c => c.IsChecked == true)
                .Select(c => (Guid)c.Tag)
                .ToList();
            Services.DailySummaries.SetLinkedTasks(_currentDate, taskIds);

            SaveStatusText.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
            BuildLinkedChecklists();
            BuildHistory();
            if (showConfirmation)
            {
                SaveStatusText.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("每日总结保存失败", ex);
            _ = App.ShowMessageAsync(this, "保存失败", ex.Message);
            return false;
        }
    }

    private async void OnLinkChecklists(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ChecklistLinkDialog(Services, LinkOwnerTypes.DailySummary, SummaryOwnerId())
            {
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                dialog.ApplyChanges();
                BuildLinkedChecklists();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("关联清单失败", ex);
        }
    }

    private static string? NullIfEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
