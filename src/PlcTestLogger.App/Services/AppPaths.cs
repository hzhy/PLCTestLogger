using System.IO;
namespace PlcTestLogger.App.Services;

/// <summary>所有路径都相对于 exe 所在目录。</summary>
public static class AppPaths
{
    public static string BaseDir => AppContext.BaseDirectory;
    public static string ConfigDir => Path.Combine(BaseDir, "config");
    public static string RegistersConfig => Path.Combine(ConfigDir, "registers.json");
    public static string DevicesConfig => Path.Combine(ConfigDir, "devices.json");
    public static string LoggerDir => Path.Combine(BaseDir, "logger");
    public static string LogsDir => Path.Combine(BaseDir, "logs");
}
