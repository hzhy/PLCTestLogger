using System.IO;
namespace PlcTestLogger.App.Services;

/// <summary>所有路径都相对于 exe 所在目录。</summary>
public static class AppPaths
{
    public static string BaseDir => AppContext.BaseDirectory;
    public static string ConfigDir => Path.Combine(BaseDir, "config");
    public static string RegistersConfig => Path.Combine(ConfigDir, "registers.json");
    /// <summary>选项名称覆盖文件；只在没有 registers.json 时生效。</summary>
    public static string RegisterOptionsConfig => Path.Combine(ConfigDir, "register-options.json");
    public static string DevicesConfig => Path.Combine(ConfigDir, "devices.json");
    public static string LoggerDir => Path.Combine(BaseDir, "logger");
    public static string LogsDir => Path.Combine(BaseDir, "logs");
    /// <summary>数据测试的默认测试文件。</summary>
    public static string TestCases => Path.Combine(BaseDir, "samples", "test", "test-cases.json");
}
