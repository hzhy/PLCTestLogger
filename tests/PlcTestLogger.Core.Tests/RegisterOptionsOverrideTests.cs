using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Mapping;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class RegisterOptionsOverrideTests
{
    [Fact]
    public void SampleFile_MatchesBuiltInOptions()
    {
        // samples/config/register-options.json 是模板：内容必须与内置选项完全相同，套用后不改变任何显示
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "register-options.json");
        var overrides = RegisterOptionsOverride.Load(path);
        var builtIn = ConfigLoaderTests.BuiltIn();
        Assert.Equal(RegisterOptionsOverride.AllowedAddresses, overrides.Keys.OrderBy(a => a));
        foreach (var address in RegisterOptionsOverride.AllowedAddresses)
            Assert.Equal(builtIn.Find(address)!.Options!.OrderBy(kv => kv.Key), overrides[address].OrderBy(kv => kv.Key));
        Assert.Equal(0, RegisterOptionsOverride.Apply(builtIn, overrides, path));
    }

    [Fact]
    public void Apply_ReplacesAddsAndKeepsUnlisted()
    {
        var json = """
            {
              "_comment": "x",
              "D200": { "1": "J0321-A 干式", "99": "新产品" },
              "204": { "11": "流量A" },
              "D209": { }
            }
            """;
        var cfg = ConfigLoaderTests.BuiltIn();
        var overrides = RegisterOptionsOverride.Parse(json, "test");
        Assert.Equal(3, RegisterOptionsOverride.Apply(cfg, overrides, "test"));

        var f = new SnapshotFormatter(cfg);
        ushort[] Snap(int address, ushort value) { var s = new ushort[cfg.Count]; s[cfg.IndexOf(address)] = value; return s; }
        Assert.Equal("J0321-A 干式", f.FormatRegister(200, Snap(200, 1)));
        Assert.Equal("新产品", f.FormatRegister(200, Snap(200, 99)));
        Assert.Equal("J0362干湿式", f.FormatRegister(200, Snap(200, 2)));   // 没写的保持内置
        Assert.Equal("流量A", f.FormatRegister(204, Snap(204, 11)));
        // 只改名称：D204=11 时结果列的单位、倍率不变
        var s = Snap(204, 11); s[cfg.IndexOf(212)] = 40;
        Assert.Equal("0.40ml/min", f.FormatRegister(212, s));
    }

    [Theory]
    [InlineData("""{ "D208": { "1": "x" } }""")]          // 不在允许范围
    [InlineData("""{ "DX": { "1": "x" } }""")]            // 地址写法错误
    [InlineData("""{ "D200": { "a": "x" } }""")]          // 选项值不是整数
    [InlineData("""{ "D200": { "1": "" } }""")]           // 显示文字为空
    [InlineData("""{ "D200": { "1": 5 } }""")]            // 显示文字不是字符串
    [InlineData("""{ "D200": ["x"] }""")]                 // 不是对象
    [InlineData("""{ "D200": {}, "200": {} }""")]         // 重复
    public void Parse_RejectsInvalid(string json)
    {
        Assert.Throws<InvalidDataException>(() => RegisterOptionsOverride.Parse(json, "test"));
    }
}
