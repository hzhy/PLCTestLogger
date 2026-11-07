using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Mapping;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class SnapshotFormatterTests
{
    private static RegisterConfig Cfg()
    {
        var cfg = ConfigLoader.LoadRegisters(ConfigLoaderTests.SamplePath);
        return cfg;
    }

    private static ushort[] Snapshot(params (int addr, ushort val)[] values)
    {
        var s = new ushort[31];
        foreach (var (a, v) in values) s[a - 200] = v;
        return s;
    }

    [Fact]
    public void Enum_Hit_ReturnsLabel()
    {
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("JX321干式", f.FormatRegister(200, Snapshot((200, 1))));
        Assert.Equal("12接口", f.FormatRegister(203, Snapshot((203, 12))));
    }

    [Fact]
    public void Enum_Miss_ReturnsKValue()
    {
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("K37", f.FormatRegister(200, Snapshot((200, 37))));
    }

    [Fact]
    public void Number_AppliesScaleDecimalsUnit()
    {
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("36.5℃", f.FormatRegister(210, Snapshot((210, 365))));
        Assert.Equal("200rpm", f.FormatRegister(208, Snapshot((208, 200))));
    }

    [Fact]
    public void Result_UnitFromMethodRegister()
    {
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("0.01PsiG", f.FormatRegister(212, Snapshot((204, 1), (212, 1))));
        Assert.Equal("0.2ml/min", f.FormatRegister(212, Snapshot((204, 11), (212, 2))));
        // 测试方法未定义单位时回退到本列的 scale/unit
        Assert.Equal("0.03", f.FormatRegister(212, Snapshot((204, 7), (212, 3))));
    }

    [Fact]
    public void Signed_InterpretsNegative()
    {
        var cfg = Cfg();
        cfg.Find(208)!.Signed = true;
        var f = new SnapshotFormatter(cfg);
        Assert.Equal("-1rpm", f.FormatRegister(208, Snapshot((208, 65535))));
    }

    [Fact]
    public void Raw_ReturnsInteger()
    {
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("5", f.FormatRegister(211, Snapshot((211, 5))));
    }

    [Fact]
    public void Format_FollowsExcelColumnsOrder()
    {
        var f = new SnapshotFormatter(Cfg());
        var row = f.Format(Snapshot((200, 23), (202, 5), (203, 12), (204, 1), (208, 200), (210, 365), (209, 4), (212, 1)));
        Assert.Equal(new[] { "产品编号", "测试方式", "接口数量", "测试方法", "电机转速", "温度", "纯水", "接口1测试结果" }, f.Headers.Take(8));
        Assert.Equal(new[] { "JC003干湿式", "4H运转后试验", "12接口", "压力20PsiG", "200rpm", "36.5℃", "100~200ml", "0.01PsiG" }, row.Take(8).Select(c => c.Text));
        Assert.Equal(19, row.Count);
    }
}
