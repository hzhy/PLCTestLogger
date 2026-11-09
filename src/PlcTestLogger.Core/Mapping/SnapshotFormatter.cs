using System.Globalization;
using PlcTestLogger.Core.Config;

namespace PlcTestLogger.Core.Mapping;

/// <param name="Raw">原始字：16 位为十进制整数，32 位为 "0x低字 0x高字"（按地址顺序）。</param>
public readonly record struct FormattedCell(int Address, string Name, string Raw, string Text);

/// <summary>把一次读取的寄存器快照（ushort[]，按地址顺序的 16 位字）按配置转换成语义化文本。</summary>
public sealed class SnapshotFormatter
{
    private readonly RegisterConfig _cfg;
    private readonly List<RegisterDefinition> _enabledInOrder;

    public SnapshotFormatter(RegisterConfig cfg)
    {
        _cfg = cfg;
        _enabledInOrder = cfg.Registers.Where(r => r.Enabled).OrderBy(r => r.Address).ToList();
        Headers = cfg.ExcelColumns.Select(a => cfg.Find(a)?.Name ?? $"D{a}").ToList();
    }

    /// <summary>Excel 数据列表头（不含“保存时间”）。</summary>
    public IReadOnlyList<string> Headers { get; }

    /// <summary>按 ExcelColumns 顺序输出一行。</summary>
    public IReadOnlyList<FormattedCell> Format(ushort[] snapshot)
        => _cfg.ExcelColumns.Select(a => Cell(a, snapshot)).ToList();

    /// <summary>按地址顺序输出全部启用的寄存器定义（32 位数据只出现一次），用于实时显示。</summary>
    public IReadOnlyList<FormattedCell> FormatAll(ushort[] snapshot)
        => _enabledInOrder.Select(d => Cell(d, snapshot)).ToList();

    public string FormatRegister(int address, ushort[] snapshot) => Cell(address, snapshot).Text;

    /// <summary>读取寄存器定义对应的数值（已按 dataType / 字序解码，未乘倍率）。</summary>
    public double ReadValue(RegisterDefinition def, ushort[] snapshot) => ReadValue(def, snapshot, out _);

    /// <summary>同 ReadValue；isFloat 指出该值是否为浮点数（float32，或 auto32 判定为浮点）。</summary>
    public double ReadValue(RegisterDefinition def, ushort[] snapshot, out bool isFloat)
    {
        isFloat = false;
        var w0 = Word(def.Address, snapshot);
        if (def.Width == 1)
            return def.EffectiveDataType == "int16" ? (short)w0 : w0;

        var w1 = Word(def.Address + 1, snapshot);
        var lowFirst = (def.WordOrder ?? _cfg.WordOrder) != "highLow";
        var (low, high) = lowFirst ? (w0, w1) : (w1, w0);
        var bits = ((uint)high << 16) | low;
        switch (def.EffectiveDataType)
        {
            case "int32": return (int)bits;
            case "float32": isFloat = true; return BitConverter.Int32BitsToSingle((int)bits);
            case "auto32":
                var f = BitConverter.Int32BitsToSingle((int)bits);
                if (LooksLikeFloat(f)) { isFloat = true; return f; }
                return (int)bits;
            default: return bits;
        }
    }

    /// <summary>auto32 的判定：浮点解释在 0.001 ~ 1e9 之间视为浮点数；0、NaN、无穷、极小值（整数的位模式）视为整数。</summary>
    public const double AutoFloatMin = 1e-3, AutoFloatMax = 1e9;
    private static bool LooksLikeFloat(float f)
    {
        if (!float.IsFinite(f) || f == 0) return false;
        var a = Math.Abs(f);
        return a >= AutoFloatMin && a < AutoFloatMax;
    }

    private ushort Word(int address, ushort[] snapshot)
    {
        var idx = _cfg.IndexOf(address);
        return idx >= 0 && idx < snapshot.Length ? snapshot[idx] : (ushort)0;
    }

    private FormattedCell Cell(int address, ushort[] snapshot)
    {
        var def = _cfg.Find(address);
        if (def is null)
        {
            var w = Word(address, snapshot);
            return new FormattedCell(address, $"D{address}", w.ToString(CultureInfo.InvariantCulture), w.ToString(CultureInfo.InvariantCulture));
        }
        return Cell(def, snapshot);
    }

    private FormattedCell Cell(RegisterDefinition def, ushort[] snapshot)
    {
        var raw = def.Width == 1
            ? Word(def.Address, snapshot).ToString(CultureInfo.InvariantCulture)
            : $"0x{Word(def.Address, snapshot):X4} 0x{Word(def.Address + 1, snapshot):X4}";
        var value = ReadValue(def, snapshot, out var isFloat);
        string text;
        if (def.IsEnum)
        {
            var key = ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            text = def.Options is not null && def.Options.TryGetValue(key, out var label) ? label : $"K{key}";
        }
        else if (def.IsNumber)
        {
            var scale = def.Scale;
            var unit = def.Unit;
            var decimals = def.Decimals;
            if (def.UnitFrom is int from && _cfg.Find(from) is { } src)
            {
                var srcKey = ((long)ReadValue(src, snapshot)).ToString(CultureInfo.InvariantCulture);
                if (src.ResultUnits is not null && src.ResultUnits.TryGetValue(srcKey, out var u)) unit = u;
                if (src.ResultScales is not null && src.ResultScales.TryGetValue(srcKey, out var s)) scale = s;
                if (src.ResultDecimals is not null && src.ResultDecimals.TryGetValue(srcKey, out var d)) decimals = d;
            }
            // auto32 判定为浮点时，PLC 已直接给出工程值，不再乘倍率
            var scaled = def.IsAuto32 && isFloat ? value : value * scale;
            text = scaled.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture) + unit;
        }
        else
        {
            text = isFloat
                ? value.ToString("R", CultureInfo.InvariantCulture)
                : ((long)value).ToString(CultureInfo.InvariantCulture);
        }
        return new FormattedCell(def.Address, def.Name, raw, text);
    }
}
