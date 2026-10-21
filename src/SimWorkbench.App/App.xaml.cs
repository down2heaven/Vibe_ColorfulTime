using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimWorkbench.Core.Models;
using SimWorkbench.Data;

namespace SimWorkbench.App;

public partial class App : Application
{
    public static App Instance { get; private set; } = null!;

    /// <summary>业务服务集合（界面层唯一入口）。</summary>
    public static WorkbenchServices Services { get; private set; } = null!;

    public static bool ServicesAvailable => Services is not null;

    /// <summary>主窗口（供页面做跨模块跳转）。</summary>
    public static MainWindow? Main { get; set; }

    public static string? StartupError { get; private set; }

    /// <summary>设置变化后请求外壳重建导航（由 MainWindow 订阅）。</summary>
    public static event Action? LayoutChanged;

    private Window? _window;

    public App()
    {
        Instance = this;
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.Handled = true;
    }

    public static void LogCrash(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            AppPaths.EnsureDirectories();
            var path = Path.Combine(AppPaths.LogDirectory, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}\n\n");
        }
        catch
        {
            // 日志写入失败不影响程序运行
        }

        System.Diagnostics.Debug.WriteLine(exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            LaunchWindow();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            StartupError ??= ex.Message;
            try
            {
                LaunchWindow();
            }
            catch (Exception inner)
            {
                LogCrash(inner);
            }
        }
    }

    private void LaunchWindow()
    {
        if (_window is not null)
        {
            return;
        }

        // 支持 --data-dir <path> 指定数据目录（便携模式/多数据副本）
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--data-dir", StringComparison.OrdinalIgnoreCase))
            {
                AppPaths.OverrideDataDirectory(args[i + 1]);
                break;
            }
        }

        try
        {
            AppPaths.EnsureDirectories();
            Common.AppLog.Info($"启动：数据目录 = {AppPaths.DataDirectory}");
            Services = WorkbenchServices.Create();
            Common.AppLog.Info("数据服务初始化成功");
        }
        catch (Exception ex)
        {
            StartupError = ex.Message;
            LogCrash(ex);
            Common.AppLog.Error("数据服务初始化失败", ex);
        }

        if (Services is not null)
        {
            try
            {
                // 提前补齐全周期的实例，保证首页/日历数据一致
                Services.Recurrences.EnsureMaterialized(DateTime.Today.AddDays(-3), DateTime.Today.AddDays(30));
                Common.AppLog.Info($"周期实例补齐完成：规则 {Services.Recurrences.GetAll().Count} 条");
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Common.AppLog.Error("周期实例补齐失败", ex);
            }
        }

        _window = new MainWindow();
        _window.Activate();
    }

    public static void RequestLayoutChanged() => LayoutChanged?.Invoke();

    /// <summary>把主题设置应用到窗口根元素。</summary>
    public static void ApplyTheme(FrameworkElement root)
    {
        if (Services is null)
        {
            return;
        }

        try
        {
            var settings = Services.Settings.Load();
            root.RequestedTheme = settings.Theme switch
            {
                AppThemeMode.Light => ElementTheme.Light,
                AppThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
        catch (Exception ex)
        {
            LogCrash(ex);
        }
    }

    public static async Task ShowMessageAsync(Page? page, string title, string message)
    {
        if (page?.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "知道了",
            XamlRoot = page.XamlRoot,
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }
}
