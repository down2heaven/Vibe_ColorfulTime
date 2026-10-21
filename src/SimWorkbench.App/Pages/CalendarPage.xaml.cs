using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SimWorkbench.App.Common;
using SimWorkbench.App.Dialogs;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;

namespace SimWorkbench.App.Pages;

/// <summary>月历视图：展示任务计划开始、计划交付与周期实例，颜色使用状态颜色。</summary>
public sealed partial class CalendarPage : Page, IPageRefresh
{
    private static readonly string[] WeekDayNames = { "日", "一", "二", "三", "四", "五", "六" };

    private WorkbenchServices Services => App.Services;
    private DateTime _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDate = DateTime.Today;
    private bool _loadedOnce;

    public CalendarPage()
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
        BuildMonth();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.ServicesAvailable && IsLoaded)
        {
            BuildMonth();
        }
    }

    public void RefreshPage() => BuildMonth();

    private void BuildMonth()
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        try
        {
            MonthText.Text = Presentation.FormatMonthTitle(_currentMonth);
            CalendarGrid.Children.Clear();
            CalendarGrid.ColumnDefinitions.Clear();
            CalendarGrid.RowDefinitions.Clear();
            for (var c = 0; c < 7; c++)
            {
                CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            for (var r = 0; r < 7; r++)
            {
                CalendarGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = r == 0 ? GridLength.Auto : new GridLength(1, GridUnitType.Star),
                });
            }

            for (var i = 0; i < 7; i++)
            {
                var header = new TextBlock
                {
                    Text = WeekDayNames[i],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                    Margin = new Thickness(0, 2, 0, 4),
                };
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, i);
                CalendarGrid.Children.Add(header);
            }

            var days = Services.Calendar.GetMonth(_currentMonth);
            for (var i = 0; i < days.Count; i++)
            {
                var day = days[i];
                var cell = BuildDayCell(day);
                Grid.SetRow(cell, i / 7 + 1);
                Grid.SetColumn(cell, i % 7);
                CalendarGrid.Children.Add(cell);
            }

            RenderDayDetail();
        }
        catch (Exception ex)
        {
            AppLog.Error("日历加载失败", ex);
        }
    }

    private Border BuildDayCell(CalendarDay day)
    {
        var panel = new StackPanel { Spacing = 2 };
        var isSelected = day.Date.Date == _selectedDate.Date;
        var dayNumber = new TextBlock
        {
            Text = day.Date.Day.ToString(),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = UiUtil.BrushFromHex(day.IsCurrentMonth
                ? (day.IsToday ? "#0078D4" : "#8A8A8A")
                : "#B0B0B0"),
        };
        if (day.IsToday)
        {
            dayNumber.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        }

        panel.Children.Add(dayNumber);

        foreach (var item in day.Items.Take(3))
        {
            var bar = new Border
            {
                Background = UiUtil.WithOpacity(item.Color, 200),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = item.Title,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                },
            };
            bar.Tapped += async (_, args) =>
            {
                args.Handled = true;
                await OpenTaskAsync(item.TaskId);
            };
            panel.Children.Add(bar);
        }

        if (day.OverflowCount > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"还有 {day.OverflowCount} 项",
                FontSize = 9,
                Foreground = new SolidColorBrush(Colors.Gray),
            });
        }

        var border = new Border
        {
            Background = day.IsToday
                ? UiUtil.WithOpacity("#0078D4", 22)
                : isSelected
                    ? new SolidColorBrush(Colors.Transparent)
                    : new SolidColorBrush(Colors.Transparent),
            BorderBrush = isSelected
                ? UiUtil.BrushFromHex("#0078D4")
                : UiUtil.WithOpacity("#8A8A8A", 90),
            BorderThickness = new Thickness(isSelected ? 1.5 : 0.8),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 4, 6, 4),
            MinHeight = 64,
            Child = panel,
        };
        border.Tapped += (_, _) =>
        {
            _selectedDate = day.Date.Date;
            BuildMonth();
        };
        return border;
    }

    private void RenderDayDetail()
    {
        DayTitleText.Text = UiUtil.FormatDateText(_selectedDate);
        DayItemsHost.Children.Clear();

        var items = Services.Calendar.GetDayItems(_selectedDate);
        var button = UiUtil.MakeButton($"在 {_selectedDate:MM-dd} 新建任务", async (_, _) =>
        {
            var dialog = new TaskEditorDialog(Services, null) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                BuildMonth();
            }
        });
        button.HorizontalAlignment = HorizontalAlignment.Left;
        DayItemsHost.Children.Add(button);

        if (items.Count == 0)
        {
            DayItemsHost.Children.Add(UiUtil.EmptyHint("这一天没有计划开始、计划交付或周期任务。"));
            return;
        }

        foreach (var item in items)
        {
            var panel = new StackPanel { Spacing = 2 };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(UiUtil.ColorDot(item.Color));
            var title = new TextBlock
            {
                Text = item.Title,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            panel.Children.Add(row);
            panel.Children.Add(new TextBlock
            {
                Text = $"{item.KindText}　·　{item.StatusName}　·　{Presentation.FormatKind(item.TaskKind)}"
                       + (item.Time is { } time ? $"　·　{time:HH:mm}" : string.Empty),
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            });

            var button2 = new Button
            {
                Content = panel,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button2.Click += async (_, _) => await OpenTaskAsync(item.TaskId);
            DayItemsHost.Children.Add(button2);
        }
    }

    private async Task OpenTaskAsync(Guid taskId)
    {
        try
        {
            var task = Services.Tasks.Get(taskId);
            if (task is null)
            {
                return;
            }

            var dialog = new TaskEditorDialog(Services, task) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                BuildMonth();
            }
            else if (dialog.SkippedOccurrence)
            {
                BuildMonth();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("日历打开任务失败", ex);
        }
    }

    private void OnPreviousMonth(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        BuildMonth();
    }

    private void OnNextMonth(object sender, RoutedEventArgs e)
    {
        _currentMonth = _currentMonth.AddMonths(1);
        BuildMonth();
    }

    private void OnToday(object sender, RoutedEventArgs e)
    {
        _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _selectedDate = DateTime.Today;
        BuildMonth();
    }

    private async void OnNewTask(object sender, RoutedEventArgs e)
    {
        var dialog = new TaskEditorDialog(Services, null) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            BuildMonth();
        }
    }
}
