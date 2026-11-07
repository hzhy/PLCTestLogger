using System.Globalization;
using ClosedXML.Excel;

namespace PlcTestLogger.Core.Storage;

public sealed class ExcelWriteException : Exception
{
    public ExcelWriteException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>按设备、按月保存测试记录到 Excel：&lt;root&gt;/&lt;设备编号&gt;/&lt;yyyy-MM&gt;.xlsx。</summary>
public sealed class ExcelLogStore
{
    public const string TimeHeader = "保存时间";
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
    private const string SheetName = "记录";

    private readonly string _root;
    private readonly object _gate = new();

    public ExcelLogStore(string root) => _root = root;

    public string Root => _root;
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 500;

    public string GetFilePath(string deviceId, DateTime at)
        => Path.Combine(_root, SafeName(deviceId), at.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".xlsx");

    /// <summary>追加一行；失败重试 RetryCount 次后写入 failed.log 并抛出 ExcelWriteException。</summary>
    public void Append(string deviceId, DateTime at, IReadOnlyList<string> headers, IReadOnlyList<string> cells)
    {
        var path = GetFilePath(deviceId, at);
        var timeText = at.ToString(TimeFormat, CultureInfo.InvariantCulture);
        Exception? last = null;
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            for (var attempt = 0; attempt <= RetryCount; attempt++)
            {
                try
                {
                    WriteRow(path, timeText, headers, cells);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt < RetryCount) Thread.Sleep(RetryDelayMs);
                }
            }
            var failedLog = Path.Combine(Path.GetDirectoryName(path)!, "failed.log");
            var line = timeText + "\t" + string.Join("\t", cells) + "\t# " + last!.Message.Replace('\n', ' ') + Environment.NewLine;
            try { File.AppendAllText(failedLog, line); } catch { /* 尽力而为 */ }
        }
        throw new ExcelWriteException($"写入 Excel 失败（已记录到 failed.log）: {path}", last!);
    }

    /// <summary>返回某设备某天的全部记录；首行为表头。无数据时返回空列表。</summary>
    public List<string[]> QueryDay(string deviceId, DateOnly day)
    {
        var path = GetFilePath(deviceId, day.ToDateTime(TimeOnly.MinValue));
        if (!File.Exists(path)) return new List<string[]>();
        var prefix = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var result = new List<string[]>();
        lock (_gate)
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(stream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow == 0) return result;
            result.Add(RowToArray(ws, 1, lastCol));
            for (var r = 2; r <= lastRow; r++)
            {
                if (ws.Cell(r, 1).GetString().StartsWith(prefix, StringComparison.Ordinal))
                    result.Add(RowToArray(ws, r, lastCol));
            }
        }
        return result.Count > 1 ? result : new List<string[]>();
    }

    /// <summary>列出某设备有记录的月份文件（yyyy-MM）。</summary>
    public IReadOnlyList<string> ListMonths(string deviceId)
    {
        var dir = Path.Combine(_root, SafeName(deviceId));
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir, "????-??.xlsx").Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToList()!;
    }

    private static void WriteRow(string path, string timeText, IReadOnlyList<string> headers, IReadOnlyList<string> cells)
    {
        var exists = File.Exists(path);
        using var wb = exists ? new XLWorkbook(path) : new XLWorkbook();
        var ws = exists ? wb.Worksheets.First() : wb.AddWorksheet(SheetName);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow == 0)
        {
            ws.Cell(1, 1).Value = TimeHeader;
            for (var i = 0; i < headers.Count; i++) ws.Cell(1, i + 2).Value = headers[i];
            ws.Row(1).Style.Font.Bold = true;
            lastRow = 1;
        }
        var row = lastRow + 1;
        ws.Cell(row, 1).SetValue(timeText);
        for (var i = 0; i < cells.Count; i++) ws.Cell(row, i + 2).SetValue(cells[i]);
        if (!exists) ws.Columns().AdjustToContents();
        wb.SaveAs(path);
    }

    private static string[] RowToArray(IXLWorksheet ws, int row, int lastCol)
    {
        var arr = new string[lastCol];
        for (var c = 1; c <= lastCol; c++) arr[c - 1] = ws.Cell(row, c).GetString();
        return arr;
    }

    private static string SafeName(string name)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
        return string.IsNullOrWhiteSpace(name) ? "_" : name.Trim();
    }
}
