using System.Globalization;
using PlcTestLogger.Core.Config;

namespace PlcTestLogger.Core.Mapping;

public readonly record struct FormattedCell(int Address, string Name, ushort Raw, string Text);

/// <summary>把一次读取的寄存器快照（ushort[]）按配置转换成语义化文本。</summary>
public sealed class SnapshotFormatter
{
    private readonly RegisterConfig _cfg;

    public SnapshotFormatter(RegisterConfig cfg)
    {
        _cfg = cfg;
        Headers = cfg.ExcelColumns.Select(a => cfg.Find(a)?.Name ?? $"D{a}").ToList();
    }

    /// <summary>Excel 数据列表头（不含“保存时间”）。</summary>
    public IReadOnlyList<string> Headers { get; }

    /// <summary>按 ExcelColumns 顺序输出一行。</summary>
    public IReadOnlyList<FormattedCell> Format(ushort[] snapshot)
        => _cfg.ExcelColumns.Select(a => Cell(a, snapshot)).ToList();

    /// <summary>按读取范围顺序输出全部寄存器（用于实时显示）。</summary>
    public IReadOnlyList<FormattedCell> FormatAll(ushort[] snapshot)
    {
        var list = new List<FormattedCell>(_cfg.Count);
        for (var a = _cfg.StartRegister; a < _cfg.StartRegister + _cfg.Count; a++) list.Add(Cell(a, snapshot));
        return list;
    }

    public string FormatRegister(int address, ushort[] snapshot) => Cell(address, snapshot).Text;

    private FormattedCell Cell(int address, ushort[] snapshot)
    {
        var idx = _cfg.IndexOf(address);
        var raw = idx >= 0 && idx < snapshot.Length ? snapshot[idx] : (ushort)0;
        var def = _cfg.Find(address);
        var name = def?.Name ?? $"D{address}";
        if (def is null) return new FormattedCell(address, name, raw, raw.ToString(CultureInfo.InvariantCulture));

        var value = def.Signed ? (short)raw : (double)raw;
        string text;
        if (def.IsEnum)
        {
            var key = ((long)value).ToString(CultureInfo.InvariantCulture);
            text = def.Options is not null && def.Options.TryGetValue(key, out var label) ? label : $"K{key}";
        }
        else if (def.IsNumber)
        {
            var scale = def.Scale;
            var unit = def.Unit;
            var decimals = def.Decimals;
            if (def.UnitFrom is int from && _cfg.Find(from) is { } src)
            {
                var srcIdx = _cfg.IndexOf(from);
                var srcKey = (srcIdx >= 0 && srcIdx < snapshot.Length ? snapshot[srcIdx] : 0).ToString(CultureInfo.InvariantCulture);
                if (src.ResultUnits is not null && src.ResultUnits.TryGetValue(srcKey, out var u)) unit = u;
                if (src.ResultScales is not null && src.ResultScales.TryGetValue(srcKey, out var s)) scale = s;
                if (src.ResultDecimals is not null && src.ResultDecimals.TryGetValue(srcKey, out var d)) decimals = d;
            }
            var num = value * scale;
            text = num.ToString("F" + Math.Max(0, decimals), CultureInfo.InvariantCulture) + unit;
        }
        else
        {
            text = ((long)value).ToString(CultureInfo.InvariantCulture);
        }
        return new FormattedCell(address, name, raw, text);
    }
}
