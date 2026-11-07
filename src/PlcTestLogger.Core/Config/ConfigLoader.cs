using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlcTestLogger.Core.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static RegisterConfig LoadRegisters(string path)
    {
        var cfg = JsonSerializer.Deserialize<RegisterConfig>(File.ReadAllText(path), Options)
                  ?? throw new InvalidDataException($"配置文件为空: {path}");
        Validate(cfg, path);
        return cfg;
    }

    public static DevicesConfig LoadDevices(string path)
    {
        if (!File.Exists(path)) return new DevicesConfig();
        return JsonSerializer.Deserialize<DevicesConfig>(File.ReadAllText(path), Options) ?? new DevicesConfig();
    }

    public static void SaveDevices(string path, DevicesConfig cfg)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, Options));
    }

    private static void Validate(RegisterConfig cfg, string path)
    {
        if (cfg.Count <= 0 || cfg.Count > 125)
            throw new InvalidDataException($"{path}: count 必须在 1..125 之间（单次 Modbus 读取上限）");
        if (!cfg.Contains(cfg.TriggerRegister))
            throw new InvalidDataException($"{path}: triggerRegister D{cfg.TriggerRegister} 不在读取范围 D{cfg.StartRegister}..D{cfg.StartRegister + cfg.Count - 1} 内");
        foreach (var col in cfg.ExcelColumns)
        {
            if (!cfg.Contains(col))
                throw new InvalidDataException($"{path}: excelColumns 中的 D{col} 不在读取范围内");
            if (cfg.Find(col) is null)
                throw new InvalidDataException($"{path}: excelColumns 中的 D{col} 没有在 registers 中定义");
        }
        foreach (var r in cfg.Registers)
        {
            if (r.UnitFrom is int uf && cfg.Find(uf) is null)
                throw new InvalidDataException($"{path}: D{r.Address} 的 unitFrom 引用了未定义的 D{uf}");
        }
    }
}
