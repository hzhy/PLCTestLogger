using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlcTestLogger.Core.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
        var cfg = JsonSerializer.Deserialize<DevicesConfig>(File.ReadAllText(path), Options) ?? new DevicesConfig();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in cfg.Devices)
        {
            var err = ValidateDevice(d);
            if (err is not null) throw new InvalidDataException($"{path}: 设备 \"{d.Id}\" {err}");
            if (!seen.Add(d.Id)) throw new InvalidDataException($"{path}: 设备编号 \"{d.Id}\" 重复（不区分大小写）");
        }
        return cfg;
    }

    /// <summary>返回 null 表示合法，否则为错误描述。</summary>
    public static string? ValidateDevice(DeviceDefinition d)
    {
        if (string.IsNullOrWhiteSpace(d.Id)) return "设备编号不能为空";
        if (d.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || d.Id.Trim() != d.Id) return "设备编号不能包含 \\ / : * ? \" < > | 等字符或首尾空格";
        if (!System.Net.IPAddress.TryParse(d.Ip, out _)) return "IP 地址格式不正确";
        if (d.Port is < 1 or > 65535) return "端口必须在 1~65535 之间";
        if (d.PollIntervalMs < 20) return "轮询周期不能小于 20 毫秒";
        return null;
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
