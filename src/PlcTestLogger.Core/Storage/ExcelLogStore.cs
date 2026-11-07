using System.Globalization;
using ClosedXML.Excel;

namespace PlcTestLogger.Core.Storage;

public sealed class ExcelWriteException : Exception
{
    public ExcelWriteException(string message, Exception inner) : base(message, inner) { }
}

public enum AppendResult
{
    /// <summary>已写入 xlsx。</summary>
    Written,
    /// <summary>xlsx 暂时不可写（通常被 Excel 打开），已暂存到 .pending.tsv，下次写入成功时自动合并。</summary>
    Pending,
}

/// <summary>
/// 按设备、按月保存测试记录到 Excel：&lt;root&gt;/&lt;设备编号&gt;/&lt;yyyy-MM&gt;.xlsx。
/// 写入先落到临时文件再原子替换；文件损坏时移到一旁重新开始；表头变化时另起 &lt;yyyy-MM&gt;_v2.xlsx；
/// 文件被占用时暂存到 &lt;yyyy-MM&gt;.pending.tsv，之后自动合并。
/// </summary>
public sealed class ExcelLogStore
{
    public const string TimeHeader = "保存时间";
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string SheetName = "记录";
    private const int MaxVersions = 50;

    private readonly string _root;
    private readonly object _gate = new();

    public ExcelLogStore(string root) => _root = root;

    public string Root => _root;
    public int RetryCount { get; set; } = 2;
    public int RetryDelayMs { get; set; } = 300;

    /// <summary>最近一次写入失败的原因（用于日志）。</summary>
    public string? LastWriteError { get; private set; }

    /// <summary>当月的主文件路径（表头变化后实际写入的可能是 _v2/_v3 版本，见 ResolveFilePath）。</summary>
    public string GetFilePath(string deviceId, DateTime at) => Path.Combine(DeviceDir(deviceId), Month(at) + ".xlsx");

    public string GetPendingPath(string deviceId, DateTime at) => Path.Combine(DeviceDir(deviceId), Month(at) + ".pending.tsv");

    /// <summary>追加一行。返回 Written 或 Pending；连暂存都失败时抛 ExcelWriteException。</summary>
    public AppendResult Append(string deviceId, DateTime at, IReadOnlyList<string> headers, IReadOnlyList<string> cells)
    {
        var row = new List<string>(cells.Count + 1) { at.ToString(TimeFormat, CultureInfo.InvariantCulture) };
        row.AddRange(cells);
        var fullHeaders = new List<string>(headers.Count + 1) { TimeHeader };
        fullHeaders.AddRange(headers);
        var pendingPath = GetPendingPath(deviceId, at);

        lock (_gate)
        {
            Directory.CreateDirectory(DeviceDir(deviceId));
            Exception? last = null;
            for (var attempt = 0; attempt <= RetryCount; attempt++)
            {
                try
                {
                    var pending = ReadPending(pendingPath);
                    var rows = new List<(string[] headers, string[] row)>(pending) { (fullHeaders.ToArray(), row.ToArray()) };
                    WriteRows(deviceId, at, rows);
                    TryDelete(pendingPath);
                    return AppendResult.Written;
                }
                catch (Exception ex)
                {
                    last = ex;
                    LastWriteError = ex.ToString();
                    if (attempt < RetryCount) Thread.Sleep(RetryDelayMs);
                }
            }
            try
            {
                AppendPending(pendingPath, fullHeaders, row);
                return AppendResult.Pending;
            }
            catch (Exception ex)
            {
                throw new ExcelWriteException($"写入 Excel 失败且无法暂存: {string.Join("\t", row)} ({last!.Message}; {ex.Message})", last);
            }
        }
    }

    /// <summary>返回某设备某天的全部记录（含当月所有版本文件与暂存记录）；首行为表头（各文件表头的并集）。无数据时返回空列表。</summary>
    public List<string[]> QueryDay(string deviceId, DateOnly day)
    {
        var at = day.ToDateTime(TimeOnly.MinValue);
        var prefix = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tables = new List<(string[] headers, List<string[]> rows)>();
        lock (_gate)
        {
            foreach (var path in MonthFiles(deviceId, at))
            {
                try
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var wb = new XLWorkbook(stream);
                    var ws = wb.Worksheets.First();
                    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                    var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                    if (lastRow == 0) continue;
                    var headers = RowToArray(ws, 1, lastCol);
                    var rows = new List<string[]>();
                    for (var r = 2; r <= lastRow; r++)
                        if (ws.Cell(r, 1).GetString().StartsWith(prefix, StringComparison.Ordinal)) rows.Add(RowToArray(ws, r, lastCol));
                    tables.Add((headers, rows));
                }
                catch (Exception) { /* 损坏或被独占的文件跳过 */ }
            }
            foreach (var (headers, row) in ReadPending(GetPendingPath(deviceId, at)))
            {
                if (row.Length > 0 && row[0].StartsWith(prefix, StringComparison.Ordinal))
                    tables.Add((headers, new List<string[]> { row }));
            }
        }
        return Merge(tables, prefix);
    }

    /// <summary>列出某设备有记录的月份（yyyy-MM）。</summary>
    public IReadOnlyList<string> ListMonths(string deviceId)
    {
        var dir = DeviceDir(deviceId);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "????-??*.xlsx").Select(f => Path.GetFileName(f)[..7]).Where(m => !m.StartsWith('~')).Distinct().OrderBy(x => x).ToList();
    }

    // ---------- 写入 ----------

    private void WriteRows(string deviceId, DateTime at, List<(string[] headers, string[] row)> rows)
    {
        // 按表头分组，每组写到表头匹配的文件
        foreach (var group in rows.GroupBy(r => string.Join("\u0001", r.headers)))
        {
            var headers = group.First().headers;
            var path = ResolveFilePath(deviceId, at, headers);
            WriteToFile(path, headers, group.Select(g => g.row).ToList());
        }
    }

    /// <summary>找到当月表头匹配的文件；主文件损坏则移走；没有匹配则用下一个未用的 _vN。</summary>
    private string ResolveFilePath(string deviceId, DateTime at, string[] headers)
    {
        for (var v = 1; v <= MaxVersions; v++)
        {
            var path = VersionPath(deviceId, at, v);
            if (!File.Exists(path)) return path;
            string[]? existing;
            try { existing = ReadHeader(path); }
            catch (Exception) when (!IsSharingViolation(path))
            {
                var aside = Path.Combine(DeviceDir(deviceId), $"{Month(at)}{(v == 1 ? "" : "_v" + v)}.corrupt-{DateTime.Now:yyyyMMddHHmmss}.xlsx");
                File.Move(path, aside, overwrite: true);
                return path;
            }
            if (existing is null || existing.SequenceEqual(headers)) return path;
        }
        throw new InvalidOperationException("当月表头版本过多，请清理 logger 目录");
    }

    private static void WriteToFile(string path, string[] headers, List<string[]> rows)
    {
        var exists = File.Exists(path);
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "~" + Path.GetFileNameWithoutExtension(path) + ".tmp.xlsx");
        // 整个文件先读进内存再解析，保证替换文件时没有任何句柄打开着原文件
        MemoryStream? source = null;
        if (exists)
        {
            source = new MemoryStream();
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.CopyTo(source);
            source.Position = 0;
        }
        using (source)
        using (var wb = source is not null ? new XLWorkbook(source) : new XLWorkbook())
        {
            var ws = exists ? wb.Worksheets.First() : wb.AddWorksheet(SheetName);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            if (lastRow == 0)
            {
                for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
                ws.Row(1).Style.Font.Bold = true;
                lastRow = 1;
            }
            foreach (var row in rows)
            {
                lastRow++;
                for (var i = 0; i < row.Length; i++) ws.Cell(lastRow, i + 1).SetValue(row[i]);
            }
            if (!exists) ws.Columns().AdjustToContents();
            wb.SaveAs(tmp);
        }
        try
        {
            if (exists) File.Replace(tmp, path, null);
            else File.Move(tmp, path, overwrite: true);
        }
        finally { TryDelete(tmp); }
    }

    private static string[]? ReadHeader(string path)
    {
        // 先整体读入内存并关闭文件，再解析：解析失败时（文件损坏）不会有句柄占着文件
        using var stream = new MemoryStream();
        using (var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) fs.CopyTo(stream);
        stream.Position = 0;
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        if ((ws.LastRowUsed()?.RowNumber() ?? 0) == 0) return null;
        return RowToArray(ws, 1, ws.LastColumnUsed()!.ColumnNumber());
    }

    private static bool IsSharingViolation(string path)
    {
        try { using var _ = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    // ---------- 暂存 ----------

    private static void AppendPending(string path, List<string> headers, List<string> row)
    {
        var line = Escape(headers) + "\u0001" + Escape(row) + Environment.NewLine;
        File.AppendAllText(path, line);
    }

    private static List<(string[] headers, string[] row)> ReadPending(string path)
    {
        var list = new List<(string[], string[])>();
        if (!File.Exists(path)) return list;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\u0001');
            if (parts.Length != 2) continue;
            list.Add((Unescape(parts[0]), Unescape(parts[1])));
        }
        return list;
    }

    private static string Escape(IEnumerable<string> cells) => string.Join("\t", cells.Select(c => c.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "")));
    private static string[] Unescape(string s) => s.Split('\t').Select(c => c.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\")).ToArray();

    // ---------- 查询 ----------

    private static List<string[]> Merge(List<(string[] headers, List<string[]> rows)> tables, string prefix)
    {
        var union = new List<string>();
        foreach (var (headers, _) in tables)
            foreach (var h in headers)
                if (!union.Contains(h)) union.Add(h);
        var result = new List<string[]>();
        foreach (var (headers, rows) in tables)
        {
            var map = headers.Select(h => union.IndexOf(h)).ToArray();
            foreach (var row in rows)
            {
                var outRow = new string[union.Count];
                Array.Fill(outRow, "");
                for (var i = 0; i < row.Length && i < map.Length; i++) outRow[map[i]] = row[i];
                result.Add(outRow);
            }
        }
        if (result.Count == 0) return new List<string[]>();
        result.Sort((a, b) => string.CompareOrdinal(a[0], b[0]));
        result.Insert(0, union.ToArray());
        return result;
    }

    // ---------- 工具 ----------

    private string DeviceDir(string deviceId) => Path.Combine(_root, SafeName(deviceId));
    private static string Month(DateTime at) => at.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    private string VersionPath(string deviceId, DateTime at, int v) => Path.Combine(DeviceDir(deviceId), v == 1 ? Month(at) + ".xlsx" : $"{Month(at)}_v{v}.xlsx");

    private IEnumerable<string> MonthFiles(string deviceId, DateTime at)
    {
        for (var v = 1; v <= MaxVersions; v++)
        {
            var p = VersionPath(deviceId, at, v);
            if (!File.Exists(p)) yield break;
            yield return p;
        }
    }

    private static string[] RowToArray(IXLWorksheet ws, int row, int lastCol)
    {
        var arr = new string[lastCol];
        for (var c = 1; c <= lastCol; c++) arr[c - 1] = ws.Cell(row, c).GetString();
        return arr;
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

    private static string SafeName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(name) ? "_" : name.Trim();
    }
}
