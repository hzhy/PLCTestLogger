using PlcTestLogger.Core.Config;

namespace PlcTestLogger.Core.Polling;

/// <summary>对 PLC 保持寄存器的最小访问抽象（Modbus 地址，不含 D 偏移）。</summary>
public interface IRegisterReader : IDisposable
{
    Task<ushort[]> ReadAsync(ushort startAddress, ushort count, CancellationToken ct);
    Task WriteAsync(ushort address, ushort value, CancellationToken ct);
}

public interface IRegisterReaderFactory
{
    /// <summary>创建并连接；连接失败应抛异常。</summary>
    IRegisterReader Create(DeviceDefinition device);
}
