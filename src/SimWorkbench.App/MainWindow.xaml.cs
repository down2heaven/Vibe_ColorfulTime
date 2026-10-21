using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using SimWorkbench.App.Common;
using SimWorkbench.App.Pages;
using SimWorkbench.Core.Logic;
using SimWorkbench.Core.Models;
using Windows.Graphics;

namespace SimWorkbench.App;

/// <summary>应用外壳：左侧导航 + 内容框架。导航项来自用户的界面自定义设置。</summary>
public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, NavigationViewItem> _navItems = new();
    private bool _suppressSelection;
    private string _currentPageKey = PageKeys.Workbench;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.DisplayName;
        App.Main = this;

        try
        {
            AppWindow.Resize(new SizeInt32(1360, 880));
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
        }

        App.ApplyTheme(RootGrid);

        if (!App.ServicesAvailable)
        {
            StartupInfoBar.Message =
                $"数据服务初始化失败，已进入只读降级界面：{App.StartupError ?? "未知错误"}。请检查数据目录权限后重启程序。";
            StartupInfoBar.IsOpen = true;
            ContentFrame.Content = UiUtil.EmptyState("数据服务不可用", "无法打开本地数据库，请检查数据目录权限。");
            return;
        }

        App.LayoutChanged += OnLayoutChanged;
        UpdateThemeFooter();
        BuildNavigation();
        NavigateToStartPage();
    }

    private void OnLayoutChanged()
    {
        App.ApplyTheme(RootGrid);
        BuildNavigation();
        Navigate(_currentPageKey, forceReload: true);
    }

    /// <summary>按当前设置构建导航项（隐藏入口只是不显示，不删除数据）。</summary>
    private void BuildNavigation()
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        _suppressSelection = true;
        try
        {
            Nav.MenuItems.Clear();
            Nav.FooterMenuItems.Clear();
            _navItems.Clear();

            foreach (var navItem in App.Services.Settings.Load().NavItems.OrderBy(n => n.Order))
            {
                AddNavigationItem(navItem.ModuleId);
            }

            // 设置入口固定显示在最下方
            var settingsItem = new NavigationViewItem
            {
                Content = "设置",
                Tag = PageKeys.Settings,
                Icon = new FontIcon { Glyph = "\uE713", Foreground = UiUtil.ModuleAccentBrush(PageKeys.Settings) },
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(settingsItem, "Nav_settings");
            Nav.FooterMenuItems.Add(settingsItem);
            _navItems[PageKeys.Settings] = settingsItem;
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private void AddNavigationItem(string key)
    {
        if (!TryGetModuleState(key, out var moduleState, out var title, out var glyph))
        {
            return;
        }

        if (moduleState is not null && (!moduleState.Definition.IsImplemented || !moduleState.ShowInNav))
        {
            return;
        }

        var item = new NavigationViewItem
        {
            Content = title,
            Tag = key,
            Icon = new FontIcon { Glyph = glyph, Foreground = UiUtil.ModuleAccentBrush(key) },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "Nav_" + key);
        Nav.MenuItems.Add(item);
        _navItems[key] = item;
    }

    private static bool TryGetModuleState(string key, out ModuleRuntimeState? state, out string title, out string glyph)
    {
        state = null;
        title = Defaults.GetPageTitle(key);
        glyph = key switch
        {
            PageKeys.Workbench => "\uE80F",
            ModuleIds.Tasks => "\uE8FD",
            ModuleIds.Calendar => "\uE787",
            ModuleIds.DailySummary => "\uE8A5",
            ModuleIds.TopicLog => "\uE70F",
            ModuleIds.Checklist => "\uE9D5",
            PageKeys.Settings => "\uE713",
            _ => "\uE8A5",
        };

        if (key == PageKeys.Workbench || !App.ServicesAvailable)
        {
            return true;
        }

        state = App.Services.Modules.Get(key);
        return true;
    }

    private void NavigateToStartPage()
    {
        var start = PageKeys.Workbench;
        try
        {
            var settings = App.Services.Settings.Load();
            if (settings.NavItems.Any(n => n.ModuleId == settings.StartPage && n.IsVisible)
                && (settings.StartPage == PageKeys.Workbench || App.Services.Modules.IsNavVisible(settings.StartPage)))
            {
                start = settings.StartPage;
            }
            else if (!settings.NavItems.Any(n => n.ModuleId == PageKeys.Workbench && n.IsVisible))
            {
                start = settings.NavItems.OrderBy(n => n.Order)
                    .Select(n => n.ModuleId)
                    .FirstOrDefault(id => id == PageKeys.Workbench || App.Services.Modules.IsNavVisible(id))
                    ?? PageKeys.Workbench;
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
        }

        Navigate(start);
        Common.AppLog.Info($"初始页面：{start}");
    }

    public void NavigateTo(string pageKey) => Navigate(pageKey);

    /// <summary>重新应用主题设置（设置页切换主题时调用）。</summary>
    public void RefreshTheme()
    {
        App.ApplyTheme(RootGrid);
        UpdateThemeFooter();
    }

    /// <summary>更新侧边栏底部的主题切换按钮文案与图标。</summary>
    private void UpdateThemeFooter()
    {
        try
        {
            var theme = App.Services.Settings.Load().Theme;
            ThemeToggleText.Text = theme switch
            {
                AppThemeMode.Light => "浅色外观",
                AppThemeMode.Dark => "深色外观",
                _ => "跟随系统外观",
            };
            ThemeIcon.Glyph = theme == AppThemeMode.Dark ? "\uE708" : "\uE706";
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
        }
    }

    /// <summary>循环切换外观：跟随系统 → 浅色 → 深色 → 跟随系统。</summary>
    private void OnCycleTheme(object sender, RoutedEventArgs e)
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        try
        {
            var settings = App.Services.Settings.Load();
            settings.Theme = settings.Theme switch
            {
                AppThemeMode.System => AppThemeMode.Light,
                AppThemeMode.Light => AppThemeMode.Dark,
                _ => AppThemeMode.System,
            };
            App.Services.Settings.Save(settings);
            RefreshTheme();
            AppLog.Info($"外观切换为：{settings.Theme}");
        }
        catch (Exception ex)
        {
            App.LogCrash(ex);
        }
    }

    public void Navigate(string pageKey, bool forceReload = false)
    {
        if (!App.ServicesAvailable)
        {
            return;
        }

        // 停用的模块不允许进入，回退到工作台
        if (pageKey is not (PageKeys.Workbench or PageKeys.Settings)
            && !App.Services.Modules.IsNavVisible(pageKey))
        {
            Common.AppLog.Warn($"页面 {pageKey} 当前不可用（模块已停用或隐藏），回退到工作台");
            pageKey = PageKeys.Workbench;
        }

        var type = pageKey switch
        {
            PageKeys.Workbench => typeof(WorkbenchPage),
            ModuleIds.Tasks => typeof(TasksPage),
            ModuleIds.Calendar => typeof(CalendarPage),
            ModuleIds.DailySummary => typeof(DailySummaryPage),
            ModuleIds.TopicLog => typeof(TopicLogPage),
            ModuleIds.Checklist => typeof(ChecklistPage),
            PageKeys.Settings => typeof(SettingsPage),
            _ => typeof(WorkbenchPage),
        };

        _currentPageKey = pageKey;
        Common.AppLog.Info($"导航：{pageKey}");
        if (!forceReload && ContentFrame.CurrentSourcePageType == type)
        {
            if (ContentFrame.Content is IPageRefresh refresh)
            {
                refresh.RefreshPage();
            }
        }
        else
        {
            ContentFrame.Navigate(type, null, new SuppressNavigationTransitionInfo());
        }

        _suppressSelection = true;
        try
        {
            if (_navItems.TryGetValue(pageKey, out var item))
            {
                Nav.SelectedItem = item;
            }
            else
            {
                Nav.SelectedItem = null;
            }
        }
        finally
        {
            _suppressSelection = false;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSelection)
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string key })
        {
            Navigate(key);
        }
    }
}

/// <summary>页面刷新接口：重复点击同一导航项时刷新数据。</summary>
public interface IPageRefresh
{
    void RefreshPage();
}
