using PlcTestLogger.Core.Config;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class ConfigLoaderTests
{
    /// <summary>内置寄存器定义（RegisterMap.Json）。</summary>
    public static RegisterConfig BuiltIn() => ConfigLoader.ParseRegisters(RegisterMap.Json, "RegisterMap.Json");

    [Fact]
    public void BuiltInRegisterMap_ParsesAndValidates()
    {
        var cfg = BuiltIn();
        Assert.Equal(200, cfg.StartRegister);
        Assert.Equal(41, cfg.Count);
        Assert.Equal(240, cfg.TriggerRegister);
        Assert.Equal("highLow", cfg.WordOrder);
        var product = cfg.Find(200)!;
        Assert.True(product.IsEnum);
        Assert.Equal("J0321干式", product.Options!["1"]);
        Assert.Equal(20, cfg.ExcelColumns.Count);
        Assert.Equal(204, cfg.Find(212)!.UnitFrom);
        Assert.Equal("auto32", cfg.Find(212)!.EffectiveDataType);
        Assert.Equal(2, cfg.Find(204)!.Width);
        Assert.Null(cfg.Find(213));
    }

    [Fact]
    public void SampleOverrideFile_MatchesBuiltIn()
    {
        // samples/config/registers.json 是给现场覆盖用的模板，必须和内置定义保持一致
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "registers.json");
        var file = System.Text.Json.JsonSerializer.Serialize(ConfigLoader.LoadRegisters(path));
        var builtIn = System.Text.Json.JsonSerializer.Serialize(BuiltIn());
        Assert.Equal(builtIn, file);
    }

    [Fact]
    public void LoadRegisters_RejectsTriggerOutsideRange()
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, """{ "startRegister": 200, "count": 10, "triggerRegister": 230, "registers": [], "excelColumns": [] }""");
        Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadRegisters(tmp));
    }

    [Fact]
    public void SaveDevices_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var cfg = new DevicesConfig { Devices = { new DeviceDefinition { Id = "DEV01", Ip = "10.0.0.5", Port = 503, UnitId = 2, PollIntervalMs = 500 } } };
        ConfigLoader.SaveDevices(path, cfg);
        var back = ConfigLoader.LoadDevices(path);
        var d = Assert.Single(back.Devices);
        Assert.Equal("DEV01", d.Id);
        Assert.Equal("10.0.0.5", d.Ip);
        Assert.Equal(503, d.Port);
        Assert.Equal((byte)2, d.UnitId);
        Assert.Equal(500, d.PollIntervalMs);
    }

    [Theory]
    [InlineData("""{ "devices": [ { "id": "DEV01", "ip": "10.0.0.1" }, { "id": "dev01", "ip": "10.0.0.2" } ] }""")]
    [InlineData("""{ "devices": [ { "id": "", "ip": "10.0.0.1" } ] }""")]
    [InlineData("""{ "devices": [ { "id": "A", "ip": "not-an-ip" } ] }""")]
    [InlineData("""{ "devices": [ { "id": "A", "ip": "10.0.0.1", "port": 70000 } ] }""")]
    [InlineData("""{ "devices": [ { "id": "A", "ip": "10.0.0.1", "pollIntervalMs": 150 } ] }""")]
    [InlineData("""{ "devices": [ { "id": "A/B", "ip": "10.0.0.1" } ] }""")]
    public void LoadDevices_RejectsInvalidEntries(string json)
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, json);
        Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadDevices(tmp));
    }

    [Fact]
    public void LoadDevices_Accepts200msInterval()
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, """{ "devices": [ { "id": "A", "ip": "10.0.0.1", "pollIntervalMs": 200 } ] }""");
        Assert.Equal(200, ConfigLoader.LoadDevices(tmp).Devices[0].PollIntervalMs);
    }

    [Fact]
    public void SaveDevices_WritesCamelCase()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        ConfigLoader.SaveDevices(path, new DevicesConfig { Devices = { new DeviceDefinition { Id = "D", Ip = "1.2.3.4" } } });
        Assert.Contains("\"pollIntervalMs\"", File.ReadAllText(path));
    }
}

public class RegisterConfigValidationTests
{
    private static string Write(string json) { var p = Path.GetTempFileName(); File.WriteAllText(p, json); return p; }

    [Fact]
    public void Rejects_32BitRegister_OverlappingNext()
    {
        var p = Write("""{ "startRegister": 200, "count": 31, "triggerRegister": 230, "excelColumns": [], "registers": [ { "address": 212, "name": "a", "type": "number", "dataType": "int32" }, { "address": 213, "name": "b", "type": "number" } ] }""");
        var ex = Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadRegisters(p));
        Assert.Contains("D213", ex.Message);
    }

    [Fact]
    public void Rejects_32BitRegister_BeyondReadRange()
    {
        var p = Write("""{ "startRegister": 200, "count": 31, "triggerRegister": 229, "excelColumns": [], "registers": [ { "address": 230, "name": "a", "type": "number", "dataType": "float32" } ] }""");
        Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadRegisters(p));
    }

    [Fact]
    public void Rejects_UnknownDataType_And_DisabledInExcelColumns()
    {
        var bad = Write("""{ "startRegister": 200, "count": 31, "triggerRegister": 230, "excelColumns": [], "registers": [ { "address": 200, "name": "a", "type": "number", "dataType": "int64" } ] }""");
        Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadRegisters(bad));
        var disabled = Write("""{ "startRegister": 200, "count": 31, "triggerRegister": 230, "excelColumns": [201], "registers": [ { "address": 201, "name": "a", "type": "raw", "enabled": false } ] }""");
        Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadRegisters(disabled));
    }

    [Fact]
    public void SampleConfig_HasSparesDisabled()
    {
        var cfg = ConfigLoaderTests.BuiltIn();
        Assert.False(cfg.Find(201)!.Enabled);
        Assert.True(cfg.Find(200)!.Enabled);
    }

    [Fact]
    public void SaveDevices_LeavesNoTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        ConfigLoader.SaveDevices(path, new DevicesConfig { Devices = { new DeviceDefinition { Id = "D", Ip = "1.2.3.4", Enabled = false } } });
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(ConfigLoader.LoadDevices(path).Devices[0].Enabled);
    }
}
