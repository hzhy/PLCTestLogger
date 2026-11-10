using System.Globalization;
using System.Text.Json;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Mapping;

namespace PlcTestLogger.Core.Testing;

/// <summary>一条测试用例：PLC 原始字（地址 → 16 位字）和期望显示文字（地址 → 文字）。</summary>
public sealed record TestCase(string Name, IReadOnlyDictionary<int, ushort> Words, IReadOnlyDictionary<int, string> Expect);

/// <summary>一项检查结果，对应界面表格的一行。</summary>
public sealed record TestCheck(string Case, string Register, string Name, string Raw, string Expected, string Actual, bool Passed)
{
    public string ResultText => Passed ? "通过" : "失败";
}

/// <summary>
/// 离线数据测试：读取测试文件中的 PLC 原始字，按当前寄存器定义换算，与期望文字比对。不连接 PLC。
/// 文件格式见 samples/test/test-cases.json 中的 _comment。
/// </summary>
public static class TestCaseRunner
{
    private static readonly JsonDocumentOptions DocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static List<TestCase> Load(string path) => Parse(File.ReadAllText(path));

    public static List<TestCase> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, DocOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("cases", out var cases)
            || cases.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("测试文件缺少 cases 数组");

        var list = new List<TestCase>();
        var n = 0;
        foreach (var c in cases.EnumerateArray())
        {
            n++;
            var name = c.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString()! : $"用例 {n}";
            var words = new Dictionary<int, ushort>();
            if (c.TryGetProperty("words", out var w))
                foreach (var p in w.EnumerateObject())
                    words[ParseAddress(p.Name, name)] = ParseWord(p.Value, name, p.Name);

            var expect = new Dictionary<int, string>();
            if (c.TryGetProperty("expect", out var e))
                foreach (var p in e.EnumerateObject())
                    expect[ParseAddress(p.Name, name)] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();

            if (expect.Count == 0) throw new InvalidDataException($"用例「{name}」没有 expect（期望显示）");
            list.Add(new TestCase(name, words, expect));
        }
        if (list.Count == 0) throw new InvalidDataException("测试文件中没有用例");
        return list;
    }

    /// <summary>逐条用例换算并比对；没写的原始字按 0 处理。</summary>
    public static List<TestCheck> Run(IEnumerable<TestCase> cases, RegisterConfig cfg)
    {
        var formatter = new SnapshotFormatter(cfg);
        var results = new List<TestCheck>();
        var range = $"D{cfg.StartRegister}~D{cfg.StartRegister + cfg.Count - 1}";
        foreach (var c in cases)
        {
            var snapshot = new ushort[cfg.Count];
            var outside = c.Words.Keys.Where(a => !cfg.Contains(a)).OrderBy(a => a).ToList();
            if (outside.Count > 0)
            {
                results.Add(new TestCheck(c.Name, "—", "", "", "", $"原始字 {string.Join("、", outside.Select(a => "D" + a))} 不在读取范围 {range} 内", false));
                continue;
            }
            foreach (var (address, word) in c.Words) snapshot[cfg.IndexOf(address)] = word;

            foreach (var (address, expected) in c.Expect.OrderBy(kv => kv.Key))
            {
                var def = cfg.Find(address);
                if (def is null)
                {
                    results.Add(new TestCheck(c.Name, $"D{address}", "", "", expected, "（未定义的寄存器；32 位数据请写起始地址）", false));
                    continue;
                }
                var cell = formatter.FormatCell(address, snapshot);
                results.Add(new TestCheck(c.Name, $"D{address}", def.Name, cell.Raw, expected, cell.Text, cell.Text == expected));
            }
        }
        return results;
    }

    private static int ParseAddress(string key, string caseName)
    {
        var k = key.Trim();
        if (k.StartsWith("D", StringComparison.OrdinalIgnoreCase)) k = k[1..];
        return int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out var a)
            ? a
            : throw new InvalidDataException($"用例「{caseName}」中的寄存器地址 \"{key}\" 不正确，应写成 212 或 D212");
    }

    /// <summary>原始字可写十进制（0~65535，或 -32768~-1 表示 16 位负数）或十六进制字符串 "0x41A4"。</summary>
    private static ushort ParseWord(JsonElement v, string caseName, string key)
    {
        long value;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var num)) value = num;
        else if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString()!.Trim();
            var ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                : long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
            if (!ok) throw new InvalidDataException($"用例「{caseName}」中 {key} 的原始字 \"{s}\" 不是有效数字");
        }
        else throw new InvalidDataException($"用例「{caseName}」中 {key} 的原始字必须是数字或 \"0x....\" 字符串");

        if (value is < short.MinValue or > ushort.MaxValue)
            throw new InvalidDataException($"用例「{caseName}」中 {key} 的原始字 {value} 超出 16 位范围");
        return unchecked((ushort)value);
    }
}
