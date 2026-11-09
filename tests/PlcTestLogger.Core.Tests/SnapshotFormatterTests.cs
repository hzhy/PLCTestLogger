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
        var s = new ushort[41];
        foreach (var (a, v) in values) s[a - 200] = v;
        return s;
    }

    [Fact]
    public void Enum_Hit_ReturnsLabel()
    {
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("J0321干式", f.FormatRegister(200, Snapshot((200, 1))));
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
        // 32 位高字在前：D204/D212 为高字，D205/D213 为低字；整数位模式按倍率换算
        Assert.Equal("0.01PsiG", f.FormatRegister(212, Snapshot((205, 1), (213, 1))));
        Assert.Equal("0.2ml/min", f.FormatRegister(212, Snapshot((205, 11), (213, 2))));
        // 测试方法未定义单位时回退到本列的 scale/unit
        Assert.Equal("0.03", f.FormatRegister(212, Snapshot((205, 7), (213, 3))));
        // 浮点位模式（20.5f = 0x41A40000）直接取工程值
        Assert.Equal("20.50PsiG", f.FormatRegister(212, Snapshot((205, 1), (212, 0x41A4), (213, 0))));
        Assert.Equal("0.4ml/min", f.FormatRegister(212, Snapshot((205, 11), (212, 0x3ECC), (213, 0xCCCD))));
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
        Assert.Equal("1", f.FormatRegister(240, Snapshot((240, 1))));
    }

    [Fact]
    public void Format_FollowsExcelColumnsOrder()
    {
        var f = new SnapshotFormatter(Cfg());
        var row = f.Format(Snapshot((200, 23), (202, 5), (203, 12), (205, 1), (208, 200), (210, 365), (211, 12), (209, 4), (213, 1)));
        Assert.Equal(new[] { "产品编号", "测试方式", "接口数量", "测试方法", "电机转速", "温度", "扭矩", "纯水流量", "接口1测试结果" }, f.Headers.Take(9));
        Assert.Equal(new[] { "JC003干湿式", "4H运转后试验", "12接口", "压力20PsiG", "200rpm", "36.5℃", "12", "100~200ml", "0.01PsiG" }, row.Take(9).Select(c => c.Text));
        Assert.Equal(20, row.Count);
    }
}

public class SnapshotFormatterDataTypeTests
{
    private static RegisterConfig Cfg(string wordOrder = "lowHigh") => new()
    {
        StartRegister = 200, Count = 31, TriggerRegister = 230, WordOrder = wordOrder,
        Registers =
        {
            new RegisterDefinition { Address = 200, Name = "u16", Type = "number" },
            new RegisterDefinition { Address = 201, Name = "i16", Type = "number", DataType = "int16" },
            new RegisterDefinition { Address = 202, Name = "u32", Type = "number", DataType = "uint32" },
            new RegisterDefinition { Address = 204, Name = "i32", Type = "number", DataType = "int32" },
            new RegisterDefinition { Address = 206, Name = "f32", Type = "number", DataType = "float32", Decimals = 2, Unit = "℃" },
            new RegisterDefinition { Address = 208, Name = "spare", Type = "raw", Enabled = false },
            new RegisterDefinition { Address = 209, Name = "e32", Type = "enum", DataType = "uint32", Options = new() { ["70000"] = "big" } },
            new RegisterDefinition { Address = 211, Name = "a32n", Type = "number", DataType = "auto32", Scale = 0.01, Decimals = 2, Unit = "PsiG" },
            new RegisterDefinition { Address = 213, Name = "a32e", Type = "enum", DataType = "auto32", Options = new() { ["11"] = "flow" } },
            new RegisterDefinition { Address = 215, Name = "a32r", Type = "raw", DataType = "auto32" },
        },
        ExcelColumns = { 200, 202, 206 },
    };

    private static ushort[] Snap(params (int addr, ushort val)[] values)
    {
        var s = new ushort[31];
        foreach (var (a, v) in values) s[a - 200] = v;
        return s;
    }

    [Fact]
    public void Int16_And_Uint16()
    {
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("65535", f.FormatRegister(200, Snap((200, 65535))));
        Assert.Equal("-1", f.FormatRegister(201, Snap((201, 65535))));
    }

    [Fact]
    public void Uint32_LowWordFirst_ByDefault()
    {
        // 70000 = 0x00011170 → 低字 0x1170, 高字 0x0001
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("70000", f.FormatRegister(202, Snap((202, 0x1170), (203, 0x0001))));
        Assert.Equal("big", f.FormatRegister(209, Snap((209, 0x1170), (210, 0x0001))));
    }

    [Fact]
    public void Uint32_HighWordFirst_WhenConfigured()
    {
        var f = new SnapshotFormatter(Cfg("highLow"));
        Assert.Equal("70000", f.FormatRegister(202, Snap((202, 0x0001), (203, 0x1170))));
    }

    [Fact]
    public void Auto32_IntegerBits_AppliesScale()
    {
        // 2050 → 整数分支 ×0.01 = 20.50
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("20.50PsiG", f.FormatRegister(211, Snap((211, 2050), (212, 0))));
        // -5 = 0xFFFFFFFB → 浮点解释为 NaN → 整数
        Assert.Equal("-0.05PsiG", f.FormatRegister(211, Snap((211, 0xFFFB), (212, 0xFFFF))));
        Assert.Equal("0.00PsiG", f.FormatRegister(211, Snap()));
    }

    [Fact]
    public void Auto32_FloatBits_NoScale_BothWordOrders()
    {
        // 20.5f = 0x41A40000
        Assert.Equal("20.50PsiG", new SnapshotFormatter(Cfg()).FormatRegister(211, Snap((211, 0x0000), (212, 0x41A4))));
        Assert.Equal("20.50PsiG", new SnapshotFormatter(Cfg("highLow")).FormatRegister(211, Snap((211, 0x41A4), (212, 0x0000))));
        // 0.4f = 0x3ECCCCCD
        Assert.Equal("0.40PsiG", new SnapshotFormatter(Cfg("highLow")).FormatRegister(211, Snap((211, 0x3ECC), (212, 0xCCCD))));
    }

    [Fact]
    public void Auto32_Enum_And_Raw()
    {
        var f = new SnapshotFormatter(Cfg("highLow"));
        Assert.Equal("flow", f.FormatRegister(213, Snap((213, 0), (214, 11))));
        // 11.0f = 0x41300000 也能命中选项
        Assert.Equal("flow", f.FormatRegister(213, Snap((213, 0x4130), (214, 0))));
        Assert.Equal("K12", f.FormatRegister(213, Snap((213, 0), (214, 12))));
        Assert.Equal("7", f.FormatRegister(215, Snap((215, 0), (216, 7))));
        // 0.5f = 0x3F000000
        Assert.Equal("0.5", f.FormatRegister(215, Snap((215, 0x3F00), (216, 0))));
    }

    [Fact]
    public void Int32_Negative()
    {
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("-2", f.FormatRegister(204, Snap((204, 0xFFFE), (205, 0xFFFF))));
    }

    [Fact]
    public void Float32()
    {
        // 36.5f = 0x42120000 → 低字 0x0000, 高字 0x4212
        var f = new SnapshotFormatter(Cfg());
        Assert.Equal("36.50℃", f.FormatRegister(206, Snap((206, 0x0000), (207, 0x4212))));
    }

    [Fact]
    public void FormatAll_SkipsDisabled_AndListsEachDefinitionOnce()
    {
        var f = new SnapshotFormatter(Cfg());
        var all = f.FormatAll(Snap((202, 0x1170), (203, 0x0001)));
        Assert.DoesNotContain(all, c => c.Address == 208);
        Assert.Single(all, c => c.Address == 202);
        Assert.Equal("0x1170 0x0001", all.Single(c => c.Address == 202).Raw);
        Assert.Equal(9, all.Count);
    }
}
