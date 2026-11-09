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

    public static RegisterConfig LoadRegisters(string path) => ParseRegisters(File.ReadAllText(path), path);

    /// <summary>解析并校验寄存器定义 JSON；source 只用于错误提示（文件路径或“内置”）。</summary>
    public static RegisterConfig ParseRegisters(string json, string source)
    {
        var cfg = JsonSerializer.Deserialize<RegisterConfig>(json, Options)
                  ?? throw new InvalidDataException($"寄存器定义为空: {source}");
        Validate(cfg, source);
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
        if (d.PollIntervalMs < DeviceDefinition.MinPollIntervalMs) return $"轮询周期不能低于 {DeviceDefinition.MinPollIntervalMs} 毫秒";
        return null;
    }

    public static void SaveDevices(string path, DevicesConfig cfg)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var tmp = full + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Options));
        File.Move(tmp, full, overwrite: true);
    }

    private static void Validate(RegisterConfig cfg, string path)
    {
        if (cfg.Count <= 0 || cfg.Count > 125)
            throw new InvalidDataException($"{path}: count 必须在 1..125 之间（单次 Modbus 读取上限）");
        if (!cfg.Contains(cfg.TriggerRegister))
            throw new InvalidDataException($"{path}: triggerRegister D{cfg.TriggerRegister} 不在读取范围 D{cfg.StartRegister}..D{cfg.StartRegister + cfg.Count - 1} 内");
        if (cfg.WordOrder is not ("lowHigh" or "highLow"))
            throw new InvalidDataException($"{path}: wordOrder 只能是 lowHigh 或 highLow");
        var occupied = new Dictionary<int, RegisterDefinition>();
        foreach (var r in cfg.Registers.OrderBy(r => r.Address))
        {
            if (!RegisterDefinition.KnownDataTypes.Contains(r.EffectiveDataType))
                throw new InvalidDataException($"{path}: D{r.Address} 的 dataType \"{r.DataType}\" 不支持，可选: {string.Join(", ", RegisterDefinition.KnownDataTypes)}");
            if (r.WordOrder is not (null or "lowHigh" or "highLow"))
                throw new InvalidDataException($"{path}: D{r.Address} 的 wordOrder 只能是 lowHigh 或 highLow");
            if (!cfg.Contains(r))
                throw new InvalidDataException($"{path}: D{r.Address}（{r.EffectiveDataType}，占 {r.Width} 个寄存器）超出读取范围 D{cfg.StartRegister}..D{cfg.StartRegister + cfg.Count - 1}");
            for (var a = r.Address; a < r.Address + r.Width; a++)
            {
                if (occupied.TryGetValue(a, out var other))
                    throw new InvalidDataException($"{path}: D{a} 被 D{other.Address}（{other.EffectiveDataType}）和 D{r.Address} 重复占用；32 位数据占两个连续寄存器，后一个地址不能再定义");
                occupied[a] = r;
            }
            if (r.UnitFrom is int uf && cfg.Find(uf) is null)
                throw new InvalidDataException($"{path}: D{r.Address} 的 unitFrom 引用了未定义的 D{uf}");
        }
        foreach (var col in cfg.ExcelColumns)
        {
            var def = cfg.Find(col) ?? throw new InvalidDataException($"{path}: excelColumns 中的 D{col} 没有在 registers 中定义");
            if (!def.Enabled)
                throw new InvalidDataException($"{path}: excelColumns 中的 D{col} 是备用（enabled=false），不能输出到 Excel");
        }
    }
}
