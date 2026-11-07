namespace PlcTestLogger.Core.Config;

public sealed class DeviceDefinition
{
    /// <summary>设备编号，也是 logger 下的子文件夹名。</summary>
    public string Id { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public int PollIntervalMs { get; set; } = 200;
}

public sealed class DevicesConfig
{
    public List<DeviceDefinition> Devices { get; set; } = new();
}
