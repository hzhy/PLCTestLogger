using ClosedXML.Excel;
using PlcTestLogger.Core.Storage;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class ExcelLogStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "plclogger-" + Guid.NewGuid().ToString("N"));
    private static readonly string[] Headers = { "产品编号", "温度" };

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void GetFilePath_IsPerDevicePerMonth()
    {
        var store = new ExcelLogStore(_root);
        var p = store.GetFilePath("DEV01", new DateTime(2026, 11, 7, 10, 0, 0));
        Assert.Equal(Path.Combine(_root, "DEV01", "2026-11.xlsx"), p);
    }

    [Fact]
    public void Append_CreatesFileWithHeaderAndRow_ThenAppends()
    {
        var store = new ExcelLogStore(_root);
        var t1 = new DateTime(2026, 11, 7, 10, 1, 2);
        store.Append("DEV01", t1, Headers, new[] { "JX321干式", "36.5℃" });
        store.Append("DEV01", t1.AddMinutes(1), Headers, new[] { "JX362干湿式", "37.0℃" });

        using var wb = new XLWorkbook(store.GetFilePath("DEV01", t1));
        var ws = wb.Worksheets.First();
        Assert.Equal("保存时间", ws.Cell(1, 1).GetString());
        Assert.Equal("产品编号", ws.Cell(1, 2).GetString());
        Assert.Equal("2026-11-07 10:01:02", ws.Cell(2, 1).GetString());
        Assert.Equal("JX321干式", ws.Cell(2, 2).GetString());
        Assert.Equal("37.0℃", ws.Cell(3, 3).GetString());
        Assert.Equal(3, ws.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void Append_CrossMonth_UsesTwoFiles()
    {
        var store = new ExcelLogStore(_root);
        store.Append("DEV01", new DateTime(2026, 11, 30, 23, 59, 59), Headers, new[] { "a", "b" });
        store.Append("DEV01", new DateTime(2026, 12, 1, 0, 0, 0), Headers, new[] { "c", "d" });
        Assert.True(File.Exists(Path.Combine(_root, "DEV01", "2026-11.xlsx")));
        Assert.True(File.Exists(Path.Combine(_root, "DEV01", "2026-12.xlsx")));
    }

    [Fact]
    public void QueryDay_FiltersByDate()
    {
        var store = new ExcelLogStore(_root);
        store.Append("DEV01", new DateTime(2026, 11, 7, 9, 0, 0), Headers, new[] { "a", "b" });
        store.Append("DEV01", new DateTime(2026, 11, 8, 9, 0, 0), Headers, new[] { "c", "d" });
        store.Append("DEV01", new DateTime(2026, 11, 7, 18, 0, 0), Headers, new[] { "e", "f" });

        var rows = store.QueryDay("DEV01", new DateOnly(2026, 11, 7));
        Assert.Equal(new[] { "保存时间", "产品编号", "温度" }, rows[0]);
        Assert.Equal(2, rows.Count - 1);
        Assert.Equal("e", rows[2][1]);

        Assert.Empty(store.QueryDay("DEV01", new DateOnly(2025, 1, 1)));
        Assert.Empty(store.QueryDay("NOPE", new DateOnly(2026, 11, 7)));
    }

    [Fact]
    public void Append_WhenFileLocked_WritesFailedLogAndThrows()
    {
        var store = new ExcelLogStore(_root) { RetryDelayMs = 1 };
        var t = new DateTime(2026, 11, 7, 9, 0, 0);
        store.Append("DEV01", t, Headers, new[] { "a", "b" });
        using (File.Open(store.GetFilePath("DEV01", t), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<ExcelWriteException>(() => store.Append("DEV01", t.AddMinutes(1), Headers, new[] { "c", "d" }));
        }
        var failed = Path.Combine(_root, "DEV01", "failed.log");
        Assert.True(File.Exists(failed));
        Assert.Contains("2026-11-07 09:01:00", File.ReadAllText(failed));
        Assert.Contains("c\td", File.ReadAllText(failed));
    }
}
