using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SimWorkbench.App.Common;
using SimWorkbench.App.Dialogs;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using SimWorkbench.Core.Services;
using SimWorkbench.Data;
using System.Diagnostics;

namespace SimWorkbench.App.Pages;

/// <summary>设置页：外观/布局、导航与首页自定义、状态管理、模块开关、数据备份。</summary>
public sealed partial class SettingsPage : Page, IPageRefresh
{
    private WorkbenchServices Services => App.Services;
    private bool _suppressEvents;
    private bool _loadedOnce;

    public SettingsPage()
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
        LoadAll();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.ServicesAvailable && IsLoaded)
        {
            LoadAll();
        }
    }

    public void RefreshPage() => LoadAll();

    private void LoadAll()
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        _suppressEvents = true;
        try
        {
            var settings = Services.Settings.Load();
            ThemeBox.SelectedIndex = (int)settings.Theme;
            GeneralShareStatusesBox.IsChecked = settings.GeneralTasksShareSimulationStatuses;
            BuildStartPageOptions(settings);
            BuildNavRows(settings);
            BuildCardRows(settings);
            BuildStatusRows();
            BuildModuleRows();
            DataPathText.Text = $"数据文件：{AppPaths.DatabasePath}\n备份目录：{AppPaths.BackupDirectory}";
            AboutText.Text = $"{AppInfo.DisplayName}（{AppInfo.EnglishName}）　版本 {AppInfo.Version}\n" +
                             "本地 SQLite 存储，不联网即可使用；可自定义导航入口、首页卡片、主题与任务状态。";
        }
        catch (Exception ex)
        {
            AppLog.Error("设置页加载失败", ex);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void BuildStartPageOptions(AppSettings settings)
    {
        StartPageBox.Items.Clear();
        foreach (var navItem in settings.NavItems.OrderBy(n => n.Order))
        {
            if (!IsNavPageVisible(navItem.ModuleId))
            {
                continue;
            }

            StartPageBox.Items.Add(new ComboBoxItem
            {
                Content = Defaults.GetPageTitle(navItem.ModuleId),
                Tag = navItem.ModuleId,
            });
        }

        var target = StartPageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (i.Tag as string) == settings.StartPage);
        StartPageBox.SelectedItem = target ?? StartPageBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private bool IsNavPageVisible(string moduleId)
    {
        if (moduleId == PageKeys.Workbench)
        {
            var settings = Services.Settings.Load();
            return settings.NavItems.Any(n => n.ModuleId == moduleId && n.IsVisible);
        }

        return Services.Modules.IsNavVisible(moduleId);
    }

    private void BuildNavRows(AppSettings settings)
    {
        NavHost.Children.Clear();
        var items = settings.NavItems.OrderBy(n => n.Order).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = Defaults.GetPageTitle(item.ModuleId);
            var label = new StackPanel { Spacing = 1 };
            label.Children.Add(new TextBlock { Text = title, FontSize = 13 });
            if (item.ModuleId is ModuleIds.Tasks or ModuleIds.Calendar or ModuleIds.DailySummary
                or ModuleIds.TopicLog or ModuleIds.Checklist or PageKeys.Workbench)
            {
                label.Children.Add(new TextBlock
                {
                    Text = item.ModuleId == PageKeys.Workbench ? "首页入口" : "功能入口",
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                });
            }

            grid.Children.Add(label);

            var toggle = new ToggleSwitch
            {
                IsOn = item.IsVisible,
                OnContent = "显示",
                OffContent = "隐藏",
                Tag = item.ModuleId,
                MinWidth = 0,
            };
            toggle.Toggled += OnNavVisibilityToggled;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "NavToggle_" + item.ModuleId);
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var up = UiUtil.MakeIconButton("\uE70E", "上移", (_, _) => MoveNav(item.ModuleId, -1));
            var down = UiUtil.MakeIconButton("\uE70D", "下移", (_, _) => MoveNav(item.ModuleId, 1));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(up, "NavUp_" + item.ModuleId);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(down, "NavDown_" + item.ModuleId);
            up.IsEnabled = i > 0;
            down.IsEnabled = i < items.Count - 1;
            buttons.Children.Add(up);
            buttons.Children.Add(down);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            NavHost.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["InsetCardBorderStyle"],
                Child = grid,
            });
        }
    }

    private void BuildCardRows(AppSettings settings)
    {
        CardsHost.Children.Clear();
        var defs = Services.Modules.GetHomeCardDefinitions().ToDictionary(d => d.Id);
        var cards = settings.HomeCards.OrderBy(c => c.Order).ToList();
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (!defs.TryGetValue(card.CardId, out var definition))
            {
                continue;
            }

            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new StackPanel { Spacing = 1 };
            label.Children.Add(new TextBlock { Text = definition.Title, FontSize = 13 });
            label.Children.Add(new TextBlock
            {
                Text = definition.Description,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            });
            grid.Children.Add(label);

            var toggle = new ToggleSwitch
            {
                IsOn = card.IsVisible,
                OnContent = "显示",
                OffContent = "隐藏",
                Tag = card.CardId,
                MinWidth = 0,
            };
            toggle.Toggled += OnCardVisibilityToggled;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(toggle, "CardToggle_" + card.CardId);
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var up = UiUtil.MakeIconButton("\uE70E", "上移", (_, _) => MoveCard(card.CardId, -1));
            var down = UiUtil.MakeIconButton("\uE70D", "下移", (_, _) => MoveCard(card.CardId, 1));
            up.IsEnabled = i > 0;
            down.IsEnabled = i < cards.Count - 1;
            buttons.Children.Add(up);
            buttons.Children.Add(down);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            CardsHost.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["InsetCardBorderStyle"],
                Child = grid,
            });
        }
    }

    private void BuildStatusRows()
    {
        BuildStatusHost(SimStatusHost, StatusScope.Simulation);
        BuildStatusHost(GeneralStatusHost, StatusScope.General);
    }

    private void BuildStatusHost(StackPanel host, StatusScope scope)
    {
        host.Children.Clear();
        var statuses = Services.Statuses.GetByScope(scope);
        for (var i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];
            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = UiUtil.ColorDot(status.Color, 14);
            grid.Children.Add(dot);

            var label = new StackPanel { Spacing = 1 };
            label.Children.Add(new TextBlock { Text = status.Name, FontSize = 13 });
            if (status.IsProtected)
            {
                label.Children.Add(new TextBlock
                {
                    Text = "内置状态（可改名/改色，不可删除）",
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                });
            }

            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var up = UiUtil.MakeIconButton("\uE70E", "上移", (_, _) => MoveStatus(status.Id, -1));
            var down = UiUtil.MakeIconButton("\uE70D", "下移", (_, _) => MoveStatus(status.Id, 1));
            up.IsEnabled = i > 0;
            down.IsEnabled = i < statuses.Count - 1;
            buttons.Children.Add(up);
            buttons.Children.Add(down);
            var editButton = UiUtil.MakeButton("编辑", async (_, _) => await EditStatusAsync(status));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(editButton, "StatusEdit_" + status.Id);
            buttons.Children.Add(editButton);
            var delete = UiUtil.MakeButton("删除", async (_, _) => await DeleteStatusAsync(status));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(delete, "StatusDelete_" + status.Id);
            delete.IsEnabled = !status.IsProtected;
            buttons.Children.Add(delete);
            Grid.SetColumn(buttons, 2);
            grid.Children.Add(buttons);

            host.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["InsetCardBorderStyle"],
                Child = grid,
            });
        }
    }

    private void BuildModuleRows()
    {
        ModulesHost.Children.Clear();
        foreach (var module in Services.Modules.GetAll())
        {
            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new StackPanel { Spacing = 2 };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            titleRow.Children.Add(new TextBlock { Text = module.Definition.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            titleRow.Children.Add(UiUtil.StatusPill(module.StatusText, module.Status switch
            {
                ModuleStatus.Enabled => "#2EA043",
                ModuleStatus.Disabled => "#9AA0A6",
                ModuleStatus.NotConfigured => "#F58A1F",
                _ => "#8A8A8A",
            }));
            if (module.Definition.IsCore)
            {
                titleRow.Children.Add(UiUtil.StatusPill("核心模块", "#0078D4"));
            }

            label.Children.Add(titleRow);
            label.Children.Add(new TextBlock
            {
                Text = module.Definition.Description,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            });
            if (module.Definition.Note is { } note)
            {
                label.Children.Add(new TextBlock
                {
                    Text = note,
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                });
            }

            if (!module.Definition.IsImplemented)
            {
                label.Children.Add(new TextBlock
                {
                    Text = "当前版本未实现，暂不提供启用开关。",
                    Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                });
            }

            grid.Children.Add(label);

            var switches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            if (module.CanToggleEnabled)
            {
                var enabledToggle = new ToggleSwitch
                {
                    IsOn = module.IsEnabled,
                    OnContent = "已启用",
                    OffContent = "已停用",
                    Tag = module.Definition.Id,
                    MinWidth = 0,
                };
                enabledToggle.Toggled += OnModuleEnabledToggled;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(enabledToggle, "ModuleEnabled_" + module.Definition.Id);
                switches.Children.Add(enabledToggle);
            }

            if (module.CanToggleNav)
            {
                var navToggle = new ToggleSwitch
                {
                    IsOn = module.ShowInNav,
                    OnContent = "显示导航",
                    OffContent = "隐藏导航",
                    Tag = module.Definition.Id,
                    MinWidth = 0,
                };
                navToggle.Toggled += OnModuleNavToggled;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(navToggle, "ModuleNav_" + module.Definition.Id);
                switches.Children.Add(navToggle);
            }

            Grid.SetColumn(switches, 1);
            grid.Children.Add(switches);

            ModulesHost.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["InsetCardBorderStyle"],
                Child = grid,
            });
        }
    }

    // ---------------- 事件处理 ----------------

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || !App.ServicesAvailable)
        {
            return;
        }

        var settings = Services.Settings.Load();
        settings.Theme = (AppThemeMode)ThemeBox.SelectedIndex;
        Services.Settings.Save(settings);
        App.Main?.RefreshTheme();
    }

    private void OnStartPageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || StartPageBox.SelectedItem is not ComboBoxItem item || item.Tag is not string key)
        {
            return;
        }

        var settings = Services.Settings.Load();
        settings.StartPage = key;
        Services.Settings.Save(settings);
    }

    private async void OnResetLayout(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "恢复默认布局",
            Content = new TextBlock
            {
                Text = "将恢复默认的导航入口、首页卡片与模块启用状态（不删除任何数据，主题设置保留）。确定继续吗？",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "恢复默认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        Services.Settings.ResetLayout();
        App.RequestLayoutChanged();
    }

    private void OnNavVisibilityToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || sender is not ToggleSwitch { Tag: string moduleId } toggle)
        {
            return;
        }

        Services.Modules.SetNavVisible(moduleId, toggle.IsOn);
        App.RequestLayoutChanged();
    }

    private void OnCardVisibilityToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || sender is not ToggleSwitch { Tag: string cardId } toggle)
        {
            return;
        }

        Services.Modules.SetHomeCardVisible(cardId, toggle.IsOn);
    }

    private void OnModuleEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || sender is not ToggleSwitch { Tag: string moduleId } toggle)
        {
            return;
        }

        try
        {
            Services.Modules.SetEnabled(moduleId, toggle.IsOn);
            App.RequestLayoutChanged();
        }
        catch (Exception ex)
        {
            AppLog.Error("切换模块状态失败", ex);
            _ = App.ShowMessageAsync(this, "无法切换模块", ex.Message);
        }
    }

    private void OnModuleNavToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents || sender is not ToggleSwitch { Tag: string moduleId } toggle)
        {
            return;
        }

        Services.Modules.SetNavVisible(moduleId, toggle.IsOn);
        App.RequestLayoutChanged();
    }

    private void OnGeneralShareChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var settings = Services.Settings.Load();
        settings.GeneralTasksShareSimulationStatuses = GeneralShareStatusesBox.IsChecked == true;
        Services.Settings.Save(settings);
    }

    private void MoveNav(string moduleId, int delta)
    {
        Services.Modules.MoveNavItem(moduleId, delta);
        App.RequestLayoutChanged();
    }

    private void MoveCard(string cardId, int delta)
    {
        Services.Modules.MoveHomeCard(cardId, delta);
        LoadAll();
    }

    private void MoveStatus(Guid statusId, int delta)
    {
        Services.Statuses.MoveStatus(statusId, delta);
        LoadAll();
    }

    private async void OnAddSimulationStatus(object sender, RoutedEventArgs e) =>
        await AddStatusAsync(StatusScope.Simulation);

    private async void OnAddGeneralStatus(object sender, RoutedEventArgs e) =>
        await AddStatusAsync(StatusScope.General);

    private async Task AddStatusAsync(StatusScope scope)
    {
        var dialog = new StatusEditorDialog(Services, null, scope) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            LoadAll();
        }
    }

    private async Task EditStatusAsync(WorkStatus status)
    {
        var dialog = new StatusEditorDialog(Services, status, status.Scope) { XamlRoot = XamlRoot };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            LoadAll();
        }
    }

    private async Task DeleteStatusAsync(WorkStatus status)
    {
        try
        {
            var impact = Services.Statuses.GetDeleteImpact(status.Id);
            if (impact.TaskCount > 0)
            {
                var targets = Services.Statuses.GetByScope(status.Scope).Where(s => s.Id != status.Id).ToList();
                if (targets.Count == 0)
                {
                    await App.ShowMessageAsync(this, "无法删除", "没有其他状态可以作为迁移目标，请先新建一个状态。");
                    return;
                }

                var combo = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    ItemsSource = targets,
                    DisplayMemberPath = "Name",
                    SelectedIndex = 0,
                };
                var panel = new StackPanel { Spacing = 8 };
                panel.Children.Add(new TextBlock
                {
                    Text = $"状态“{status.Name}”仍被 {impact.TaskCount} 个任务使用。请选择迁移到的目标状态：",
                    TextWrapping = TextWrapping.Wrap,
                });
                panel.Children.Add(combo);
                var dialog = new ContentDialog
                {
                    Title = "删除状态前迁移任务",
                    Content = panel,
                    PrimaryButtonText = "迁移并删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                var target = combo.SelectedItem as WorkStatus;
                Services.Statuses.Delete(status.Id, target?.Id);
                await App.ShowMessageAsync(this, "已删除状态",
                    $"任务已迁移到“{target?.Name}”，没有任务数据丢失。");
            }
            else
            {
                var confirm = new ContentDialog
                {
                    Title = "删除状态",
                    Content = new TextBlock { Text = $"确定删除状态“{status.Name}”吗？", TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                Services.Statuses.Delete(status.Id, null);
            }

            LoadAll();
        }
        catch (Exception ex)
        {
            AppLog.Error("删除状态失败", ex);
            await App.ShowMessageAsync(this, "删除失败", ex.Message);
        }
    }

    // ---------------- 备份 ----------------

    private async void OnExportBackup(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"simworkbench-backup-{DateTime.Now:yyyyMMdd-HHmm}",
            };
            picker.FileTypeChoices.Add("JSON 备份", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.Main));

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            var path = Services.Backup.Export(file.Path);
            var inspection = Services.Backup.Inspect(path);
            await App.ShowMessageAsync(this, "备份完成",
                $"已导出到：\n{path}\n\n包含：{inspection.TaskCount} 个任务、{inspection.DailySummaryCount} 篇每日总结、" +
                $"{inspection.TopicLogCount} 个主题日志、{inspection.ChecklistCount} 个 Checklist、{inspection.StatusCount} 个状态" +
                (inspection.HasSettings ? "以及界面与模块配置。" : "。"));
        }
        catch (Exception ex)
        {
            AppLog.Error("导出备份失败", ex);
            await App.ShowMessageAsync(this, "导出失败", ex.Message);
        }
    }

    private async void OnImportBackup(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.Main));

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            var inspection = Services.Backup.Inspect(file.Path);
            if (!inspection.IsValid)
            {
                await App.ShowMessageAsync(this, "无法导入", inspection.Error ?? "备份文件无效。");
                return;
            }

            var summary = $"备份时间：{inspection.ExportedAt:yyyy-MM-dd HH:mm}\n" +
                          $"任务 {inspection.TaskCount} 个、每日总结 {inspection.DailySummaryCount} 篇、主题日志 {inspection.TopicLogCount} 个、" +
                          $"Checklist {inspection.ChecklistCount} 个、周期规则 {inspection.RecurrenceRuleCount} 条。\n\n" +
                          "请选择导入方式：\n· 合并：跳过已存在的相同 ID 记录，不覆盖当前数据\n· 替换：先清空当前业务数据再导入（配置会被备份内容覆盖）";
            var dialog = new ContentDialog
            {
                Title = "导入备份",
                Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "合并导入",
                SecondaryButtonText = "替换导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            var mode = await dialog.ShowAsync();
            if (mode == ContentDialogResult.None)
            {
                return;
            }

            var importMode = mode == ContentDialogResult.Secondary ? BackupImportMode.Replace : BackupImportMode.Merge;
            var result = Services.Backup.Import(file.Path, importMode);
            LoadAll();
            App.RequestLayoutChanged();
            await App.ShowMessageAsync(this, "导入完成",
                $"{result.GetSummary()}" +
                (result.Warnings.Count > 0 ? $"\n\n注意：\n· {string.Join("\n· ", result.Warnings.Take(8))}" : string.Empty));
        }
        catch (Exception ex)
        {
            AppLog.Error("导入备份失败", ex);
            await App.ShowMessageAsync(this, "导入失败", ex.Message);
        }
    }

    private async void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = AppPaths.DataDirectory,
                UseShellExecute = true,
            });
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AppLog.Error("打开数据目录失败", ex);
            await App.ShowMessageAsync(this, "无法打开目录", ex.Message);
        }
    }
}
