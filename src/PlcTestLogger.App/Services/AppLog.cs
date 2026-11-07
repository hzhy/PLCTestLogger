using System.IO;

namespace PlcTestLogger.App.Services;

/// <summary>简单的按天滚动文本日志：logs/app-yyyyMMdd.log。</summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            var path = Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
            lock (Gate) File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    public static void Info(string m) => Write("INFO", m);
    public static void Error(string m) => Write("ERROR", m);
}
