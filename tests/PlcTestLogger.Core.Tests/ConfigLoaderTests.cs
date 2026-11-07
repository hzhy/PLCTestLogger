using PlcTestLogger.Core.Config;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class ConfigLoaderTests
{
    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "config", "registers.json");

    [Fact]
    public void LoadRegisters_ParsesSampleFile()
    {
        var cfg = ConfigLoader.LoadRegisters(SamplePath);
        Assert.Equal(200, cfg.StartRegister);
        Assert.Equal(31, cfg.Count);
        Assert.Equal(230, cfg.TriggerRegister);
        var product = cfg.Find(200)!;
        Assert.True(product.IsEnum);
        Assert.Equal("JX321干式", product.Options!["1"]);
        Assert.Equal(19, cfg.ExcelColumns.Count);
        Assert.Equal(204, cfg.Find(212)!.UnitFrom);
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
    [InlineData("""{ "devices": [ { "id": "A", "ip": "10.0.0.1", "pollIntervalMs": 0 } ] }""")]
    [InlineData("""{ "devices": [ { "id": "A/B", "ip": "10.0.0.1" } ] }""")]
    public void LoadDevices_RejectsInvalidEntries(string json)
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, json);
        Assert.Throws<InvalidDataException>(() => ConfigLoader.LoadDevices(tmp));
    }

    [Fact]
    public void SaveDevices_WritesCamelCase()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        ConfigLoader.SaveDevices(path, new DevicesConfig { Devices = { new DeviceDefinition { Id = "D", Ip = "1.2.3.4" } } });
        Assert.Contains("\"pollIntervalMs\"", File.ReadAllText(path));
    }
}
