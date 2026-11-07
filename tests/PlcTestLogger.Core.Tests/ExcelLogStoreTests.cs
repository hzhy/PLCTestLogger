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
    public void Append_WhenFileLocked_QueuesPending_ThenMergesOnNextWrite()
    {
        var store = new ExcelLogStore(_root) { RetryDelayMs = 1 };
        var t = new DateTime(2026, 11, 7, 9, 0, 0);
        Assert.Equal(AppendResult.Written, store.Append("DEV01", t, Headers, new[] { "a", "b" }));
        var path = store.GetFilePath("DEV01", t);
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            Assert.Equal(AppendResult.Pending, store.Append("DEV01", t.AddMinutes(1), Headers, new[] { "c", "d" }));
            // 被占用期间，查询仍能看到暂存的记录
            var rows = store.QueryDay("DEV01", new DateOnly(2026, 11, 7));
            Assert.Equal(3, rows.Count);
            Assert.Equal("c", rows[2][1]);
        }
        Assert.True(File.Exists(store.GetPendingPath("DEV01", t)));

        Assert.Equal(AppendResult.Written, store.Append("DEV01", t.AddMinutes(2), Headers, new[] { "e", "f" }));
        Assert.False(File.Exists(store.GetPendingPath("DEV01", t)));
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        Assert.Equal(4, ws.LastRowUsed()!.RowNumber());
        Assert.Equal("2026-11-07 09:01:00", ws.Cell(3, 1).GetString());
        Assert.Equal("c", ws.Cell(3, 2).GetString());
        Assert.Equal("e", ws.Cell(4, 2).GetString());
    }

    [Fact]
    public void Append_LeavesNoTempFiles_AndExistingFileIsReplacedAtomically()
    {
        var store = new ExcelLogStore(_root);
        var t = new DateTime(2026, 11, 7, 9, 0, 0);
        store.Append("DEV01", t, Headers, new[] { "a", "b" });
        store.Append("DEV01", t.AddMinutes(1), Headers, new[] { "c", "d" });
        var files = Directory.GetFiles(Path.Combine(_root, "DEV01")).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "2026-11.xlsx" }, files);
    }

    [Fact]
    public void Append_CorruptExistingFile_IsMovedAsideAndNewFileStarted()
    {
        var store = new ExcelLogStore(_root) { RetryDelayMs = 1 };
        var t = new DateTime(2026, 11, 7, 9, 0, 0);
        var path = store.GetFilePath("DEV01", t);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "this is not a zip");
        Assert.Equal(AppendResult.Written, store.Append("DEV01", t, Headers, new[] { "a", "b" }));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "DEV01"), "2026-11.corrupt-*.xlsx"));
        var rows = store.QueryDay("DEV01", new DateOnly(2026, 11, 7));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Append_HeaderChangedMidMonth_StartsVersionedFile_AndQueryMergesColumns()
    {
        var store = new ExcelLogStore(_root);
        var t = new DateTime(2026, 11, 7, 9, 0, 0);
        store.Append("DEV01", t, new[] { "产品编号", "温度" }, new[] { "a", "b" });
        store.Append("DEV01", t.AddMinutes(1), new[] { "产品编号", "压力" }, new[] { "c", "d" });
        Assert.True(File.Exists(Path.Combine(_root, "DEV01", "2026-11_v2.xlsx")));
        store.Append("DEV01", t.AddMinutes(2), new[] { "产品编号", "压力" }, new[] { "e", "f" });
        Assert.False(File.Exists(Path.Combine(_root, "DEV01", "2026-11_v3.xlsx")));

        var rows = store.QueryDay("DEV01", new DateOnly(2026, 11, 7));
        Assert.Equal(new[] { "保存时间", "产品编号", "温度", "压力" }, rows[0]);
        Assert.Equal(3, rows.Count - 1);
        Assert.Equal(new[] { "2026-11-07 09:00:00", "a", "b", "" }, rows[1]);
        Assert.Equal(new[] { "2026-11-07 09:01:00", "c", "", "d" }, rows[2]);
    }
}
