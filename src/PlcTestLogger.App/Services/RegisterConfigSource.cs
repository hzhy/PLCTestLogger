using System.IO;
using PlcTestLogger.Core.Config;

namespace PlcTestLogger.App.Services;

/// <summary>
/// 寄存器定义的来源：
/// 1. exe 旁 config\registers.json 存在时优先加载它（现场整体覆盖），此时 register-options.json 不起作用；
/// 2. 否则使用编译进程序的 <see cref="RegisterMap.Json"/>，
///    若 config\register-options.json 存在，再用它覆盖 D200/D202/D203/D204/D209 的选项名称。
/// </summary>
public static class RegisterConfigSource
{
    public const string BuiltInLabel = "内置（随程序编译）";

    /// <param name="Note">需要写进运行日志的附加说明（例如选项覆盖文件被忽略），没有则为 null。</param>
    public sealed record Result(RegisterConfig Config, string Label, bool IsExternal, string? Note = null);

    public static Result Load()
    {
        var path = AppPaths.RegistersConfig;
        var optionsPath = AppPaths.RegisterOptionsConfig;
        if (File.Exists(path))
        {
            var note = File.Exists(optionsPath)
                ? $"已使用 {path}，选项覆盖文件 {optionsPath} 不起作用"
                : null;
            return new Result(ConfigLoader.LoadRegisters(path), path, true, note);
        }

        var cfg = ConfigLoader.ParseRegisters(RegisterMap.Json, "内置 RegisterMap.Json");
        if (!File.Exists(optionsPath))
            return new Result(cfg, BuiltInLabel, false);

        var overrides = RegisterOptionsOverride.Load(optionsPath);
        var changed = RegisterOptionsOverride.Apply(cfg, overrides, optionsPath);
        var regs = string.Join("、", overrides.Keys.OrderBy(a => a).Select(a => "D" + a));
        return new Result(cfg, $"{BuiltInLabel} + 选项名称覆盖 {optionsPath}", false,
            $"选项名称覆盖：{(regs.Length == 0 ? "文件中没有寄存器" : regs)}，与内置不同的选项 {changed} 个");
    }
}
