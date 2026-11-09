using System.IO;
using PlcTestLogger.Core.Config;

namespace PlcTestLogger.App.Services;

/// <summary>
/// 寄存器定义的来源：exe 旁 config\registers.json 存在时优先加载它（现场临时覆盖），
/// 否则使用编译进程序的 <see cref="RegisterMap.Json"/>。
/// </summary>
public static class RegisterConfigSource
{
    public const string BuiltInLabel = "内置（随程序编译）";

    public sealed record Result(RegisterConfig Config, string Label, bool IsExternal);

    public static Result Load()
    {
        var path = AppPaths.RegistersConfig;
        if (File.Exists(path))
            return new Result(ConfigLoader.LoadRegisters(path), path, true);
        return new Result(ConfigLoader.ParseRegisters(RegisterMap.Json, "内置 RegisterMap.Json"), BuiltInLabel, false);
    }
}
