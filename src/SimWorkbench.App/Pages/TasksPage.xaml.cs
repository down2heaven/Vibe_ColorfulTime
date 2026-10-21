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

/// <summary>任务页：列表 + 详情 + 筛选 + 创建/编辑/删除 + 周期任务入口。</summary>
public sealed partial class TasksPage : Page, IPageRefresh
{
    private WorkbenchServices Services => App.Services;

    private Guid? _selectedTaskId;
    private bool _suppressFilterEvents;
    private bool _loadedOnce;

    public TasksPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        TaskList.DoubleTapped += OnTaskDoubleTapped;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
        ReloadStatusFilter();
        Refresh();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.ServicesAvailable && IsLoaded)
        {
            Refresh();
        }
    }

    public void RefreshPage() => Refresh();

    private void ReloadStatusFilter()
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        var previous = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        _suppressFilterEvents = true;
        try
        {
            StatusFilter.Items.Clear();
            StatusFilter.Items.Add(new ComboBoxItem { Content = "全部状态", Tag = null });
            foreach (var status in Services.Statuses.GetAll())
            {
                StatusFilter.Items.Add(new ComboBoxItem
                {
                    Content = status.Name,
                    Tag = status.Id.ToString("D"),
                });
            }

            var target = StatusFilter.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as string) == previous) ?? (ComboBoxItem)StatusFilter.Items[0];
            StatusFilter.SelectedItem = target;
        }
        finally
        {
            _suppressFilterEvents = false;
        }
    }

    private void Refresh()
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        try
        {
            Services.Recurrences.EnsureMaterialized(DateTime.Today.AddDays(-3), DateTime.Today.AddDays(30));
            var query = new TaskQuery
            {
                SearchText = SearchBox.Text,
                Kind = TypeFilter.SelectedIndex switch
                {
                    1 => TaskKind.General,
                    2 => TaskKind.Simulation,
                    _ => null,
                },
                StatusId = (StatusFilter.SelectedItem as ComboBoxItem)?.Tag is string statusText && Guid.TryParse(statusText, out var statusId)
                    ? statusId
                    : null,
                Priority = PriorityFilter.SelectedIndex switch
                {
                    1 => Priority.Normal,
                    2 => Priority.High,
                    3 => Priority.Urgent,
                    _ => null,
                },
                OnlyOpen = OnlyOpenBox.IsChecked == true,
                OnlyOverdue = OverdueBox.IsChecked == true,
                SortField = SortFilter.SelectedIndex switch
                {
                    1 => TaskSortField.CreatedAt,
                    2 => TaskSortField.Priority,
                    3 => TaskSortField.Title,
                    _ => TaskSortField.DueAt,
                },
                Descending = SortFilter.SelectedIndex == 1,
            };

            var tasks = Services.Tasks.Query(query);
            var statusMap = Services.Statuses.GetAll().ToDictionary(s => s.Id);
            var now = DateTime.Now;
            var rows = tasks.Select(t => new TaskRow(t, statusMap.GetValueOrDefault(t.StatusId), now)).ToList();
            TaskList.ItemsSource = rows;
            CountText.Text = $"共 {rows.Count} 项";

            if (_selectedTaskId is { } id)
            {
                var row = rows.FirstOrDefault(r => r.Id == id);
                if (row is null)
                {
                    _selectedTaskId = null;
                    TaskList.SelectedItem = null;
                    RenderEmptyDetail();
                }
                else
                {
                    TaskList.SelectedItem = row;
                }
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            _ = App.ShowMessageAsync(this, "加载任务失败", ex.Message);
        }
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        ReloadStatusFilter();
        Refresh();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadedOnce)
        {
            return;
        }

        Refresh();
    }

    private void OnFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || !_loadedOnce)
        {
            return;
        }

        Refresh();
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents || !_loadedOnce)
        {
            return;
        }

        Refresh();
    }

    private void OnTaskSelected(object sender, SelectionChangedEventArgs e)
    {
        if (TaskList.SelectedItem is TaskRow row)
        {
            _selectedTaskId = row.Id;
            RenderDetail(row.Task);
        }
        else
        {
            _selectedTaskId = null;
            RenderEmptyDetail();
        }
    }

    private async void OnTaskDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (TaskList.SelectedItem is TaskRow row)
        {
            await EditTaskAsync(row.Task);
        }
    }

    private async void OnNewTask(object sender, RoutedEventArgs e) => await EditTaskAsync(null);

    private async void OnNewSimulationTask(object sender, RoutedEventArgs e) => await EditTaskAsync(null, TaskKind.Simulation);

    private async Task EditTaskAsync(TaskItem? task, TaskKind defaultKind = TaskKind.General)
    {
        try
        {
            var dialog = new TaskEditorDialog(Services, task, task is null ? defaultKind : null) { XamlRoot = XamlRoot };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && dialog.ResultTask is { } saved)
            {
                _selectedTaskId = saved.Id;
                Refresh();
            }
            else if (dialog.SkippedOccurrence)
            {
                Refresh();
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            await App.ShowMessageAsync(this, "无法保存任务", ex.Message);
        }
    }

    private async Task DeleteTaskAsync(TaskItem task)
    {
        var linkedChecklists = Services.Checklists.GetLinkedChecklists(LinkOwnerTypes.Task, task.Id);
        var impact = new List<string>();
        if (linkedChecklists.Count > 0)
        {
            impact.Add($"已关联 {linkedChecklists.Count} 个 Checklist（关联关系会被移除，清单本身保留）");
        }

        var summaryLinks = Services.DailySummaries.GetRecent(120)
            .Count(s => Services.DailySummaries.GetLinkedTaskIds(s.Date).Contains(task.Id));
        if (summaryLinks > 0)
        {
            impact.Add($"已被 {summaryLinks} 篇每日总结关联（关联会被移除）");
        }

        if (task.RecurrenceId is not null)
        {
            impact.Add("该任务是周期实例，删除只影响这一次执行，不会改变周期规则");
        }

        var message = $"确定删除任务“{task.Title}”吗？此操作不可撤销。" +
                      (impact.Count > 0 ? "\n\n影响：\n· " + string.Join("\n· ", impact) : string.Empty);
        var dialog = new ContentDialog
        {
            Title = "删除任务",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            Services.Tasks.Delete(task.Id);
            _selectedTaskId = null;
            Refresh();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            await App.ShowMessageAsync(this, "删除失败", ex.Message);
        }
    }

    private async void OnManageRecurrences(object sender, RoutedEventArgs e)
    {
        try
        {
            while (true)
            {
                var listDialog = new RecurrenceListDialog(Services) { XamlRoot = XamlRoot };
                var listResult = await listDialog.ShowAsync();
                if (listDialog.RuleToEdit is { } rule)
                {
                    var editor = new RecurrenceEditorDialog(Services, rule) { XamlRoot = XamlRoot };
                    await editor.ShowAsync();
                    continue;
                }

                if (listResult == ContentDialogResult.Primary)
                {
                    // “新建规则”按钮：打开规则编辑器
                    var editor = new RecurrenceEditorDialog(Services, null) { XamlRoot = XamlRoot };
                    await editor.ShowAsync();
                    continue;
                }

                break;
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
            await App.ShowMessageAsync(this, "周期任务操作失败", ex.Message);
        }

        ReloadStatusFilter();
        Refresh();
    }

    private void RenderEmptyDetail()
    {
        DetailPanel.Children.Clear();
        DetailPanel.Children.Add(new TextBlock
        {
            Text = "在左侧选择一个任务查看详情，或点击“新建任务”创建。",
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
        });
    }

    private void RenderDetail(TaskItem task)
    {
        DetailPanel.Children.Clear();
        var status = Services.Statuses.Get(task.StatusId);
        var now = DateTime.Now;

        DetailPanel.Children.Add(new TextBlock
        {
            Text = task.Title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        badges.Children.Add(UiUtil.StatusPill(status?.Name ?? "未设置", status?.Color ?? "#8A8A8A"));
        badges.Children.Add(UiUtil.StatusPill(Presentation.FormatKind(task.Kind), "#0078D4"));
        if (task.Priority != Priority.Normal)
        {
            badges.Children.Add(UiUtil.StatusPill(
                "优先级 " + Presentation.FormatPriority(task.Priority),
                task.Priority == Priority.Urgent ? "#D13438" : "#F58A1F"));
        }

        if (task.IsOverdue(now))
        {
            badges.Children.Add(UiUtil.StatusPill("已逾期", "#D13438"));
        }

        DetailPanel.Children.Add(badges);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 6) };
        actions.Children.Add(UiUtil.MakeButton("编辑", async (_, _) => await EditTaskAsync(task), accent: true));
        actions.Children.Add(UiUtil.MakeButton("删除", async (_, _) => await DeleteTaskAsync(task)));
        if (task.ActualDeliveryAt is null)
        {
            actions.Children.Add(UiUtil.MakeButton("标记实际交付（现在）", (_, _) =>
            {
                try
                {
                    var clone = task.Clone();
                    clone.ActualDeliveryAt = DateTime.Now;
                    Services.Tasks.Update(clone);
                    Refresh();
                    _selectedTaskId = task.Id;
                    TaskList.SelectedItem = (TaskList.ItemsSource as IEnumerable<TaskRow>)?.FirstOrDefault(r => r.Id == task.Id);
                }
                catch (Exception ex)
                {
                    App.LogCrash(ex);
                    _ = App.ShowMessageAsync(this, "更新失败", ex.Message);
                }
            }));
        }

        actions.Children.Add(UiUtil.MakeButton("关联清单", async (_, _) =>
        {
            var dialog = new ChecklistLinkDialog(Services, LinkOwnerTypes.Task, task.Id) { XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                dialog.ApplyChanges();
                RenderDetail(task);
            }
        }));
        DetailPanel.Children.Add(actions);

        AddInfo("创建日期", Presentation.FormatDate(task.CreatedAt));
        AddInfo("计划开始", Presentation.FormatDateTime(task.PlannedStart));
        AddInfo("计划交付", Presentation.FormatDateTime(task.DueAt));
        AddInfo("实际交付", Presentation.FormatDateTime(task.ActualDeliveryAt));
        AddInfo("交付提示", Presentation.FormatDueHint(task.DueAt, now));
        AddInfo("分类", string.IsNullOrWhiteSpace(task.Category) ? "—" : task.Category!);
        AddInfo("标签", task.GetTagsDisplay());

        if (task.Kind == TaskKind.Simulation)
        {
            AddSectionTitle("仿真信息");
            AddInfo("关联算例", string.IsNullOrWhiteSpace(task.CaseName) ? "—" : task.CaseName!);
            AddInfo("计算工况", string.IsNullOrWhiteSpace(task.CaseCondition) ? "—" : task.CaseCondition!);
            AddInfo("计算所需时间", Presentation.FormatEstimatedTime(task.EstimatedMinutes));
        }

        if (task.RecurrenceId is { } ruleId)
        {
            AddSectionTitle("周期任务");
            var rule = Services.Recurrences.Get(ruleId);
            AddInfo("所属规则", rule is null ? "（规则已删除）" : $"{rule.Title}　{rule.GetFrequencyText()}");
            AddInfo("本次执行日期", Presentation.FormatDate(task.OccurrenceDate));
        }

        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            AddSectionTitle("描述 / 备注");
            AddBody(task.Description!);
        }

        if (!string.IsNullOrWhiteSpace(task.CalculationNotes))
        {
            AddSectionTitle("计算相关备注");
            AddBody(task.CalculationNotes!);
        }

        var linked = Services.Checklists.GetLinkedChecklists(LinkOwnerTypes.Task, task.Id);
        AddSectionTitle("关联 Checklist");
        if (linked.Count == 0)
        {
            DetailPanel.Children.Add(UiUtil.EmptyHint("尚未关联清单。点击“关联清单”添加。"));
        }
        else
        {
            foreach (var checklist in linked)
            {
                var bar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = checklist.Progress * 100,
                    Height = 4,
                    Margin = new Thickness(0, 4, 0, 0),
                };
                DetailPanel.Children.Add(new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{checklist.Name}　{checklist.ProgressText}",
                            Style = (Style)Application.Current.Resources["BodyTextStyle"],
                        },
                        bar,
                    },
                });
            }
        }
    }

    private void AddInfo(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
        };
        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
        };
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        DetailPanel.Children.Add(grid);
    }

    private void AddSectionTitle(string text)
    {
        DetailPanel.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(0, 10, 0, 2),
            Style = (Style)Application.Current.Resources["SectionTitleTextStyle"],
        });
    }

    private void AddBody(string text)
    {
        DetailPanel.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["InsetCardBorderStyle"],
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                IsTextSelectionEnabled = true,
            },
        });
    }
}
