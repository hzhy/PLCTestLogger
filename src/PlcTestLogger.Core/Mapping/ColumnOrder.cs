using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Storage;

namespace PlcTestLogger.Core.Mapping;

/// <summary>界面表格的列顺序：保存时间在最前，其余按寄存器地址从低到高，认不出的列（旧版本列名）放最后。</summary>
public static class ColumnOrder
{
    /// <summary>返回按寄存器地址排序后的列下标（相同键保持原顺序）。</summary>
    public static int[] ByRegisterAddress(IReadOnlyList<string> headers, RegisterConfig cfg)
    {
        var addressByName = cfg.Registers
            .GroupBy(r => r.Name)
            .ToDictionary(g => g.Key, g => g.Min(r => r.Address));
        return headers
            .Select((h, i) => (h, i))
            .OrderBy(x => x.h == ExcelLogStore.TimeHeader ? -1
                        : addressByName.TryGetValue(x.h, out var a) ? a
                        : int.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.i)
            .ToArray();
    }
}
