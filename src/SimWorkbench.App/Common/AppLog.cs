using SimWorkbench.Data;

namespace SimWorkbench.App.Common;

/// <summary>轻量运行日志（写到数据目录 logs/app.log），便于排查启动与模块问题。</summary>
public static class AppLog
{
    private static readonly object Sync = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var path = Path.Combine(AppPaths.LogDirectory, "app.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (Sync)
            {
                File.AppendAllText(path, line + Environment.NewLine);
                // 简单体积控制：超过 ~2MB 时轮转
                var info = new FileInfo(path);
                if (info.Length > 2 * 1024 * 1024)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }
            }
        }
        catch
        {
            // 日志失败不影响程序
        }
    }
}
