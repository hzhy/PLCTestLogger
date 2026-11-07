using System.Net.Sockets;
using NModbus;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Polling;

namespace PlcTestLogger.App.Services;

/// <summary>基于 NModbus 的 Modbus TCP 保持寄存器读写（功能码 03 / 06）。</summary>
public sealed class NModbusRegisterReader : IRegisterReader
{
    private readonly TcpClient _client;
    private readonly IModbusMaster _master;
    private readonly byte _unitId;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public NModbusRegisterReader(DeviceDefinition device, int connectTimeoutMs = 3000, int ioTimeoutMs = 2000)
    {
        _unitId = device.UnitId;
        _client = new TcpClient { SendTimeout = ioTimeoutMs, ReceiveTimeout = ioTimeoutMs, NoDelay = true };
        var connect = _client.ConnectAsync(device.Ip, device.Port);
        if (!connect.Wait(connectTimeoutMs))
        {
            _client.Dispose();
            throw new TimeoutException($"连接 {device.Ip}:{device.Port} 超时");
        }
        if (connect.IsFaulted) { _client.Dispose(); throw connect.Exception!.GetBaseException(); }
        var factory = new ModbusFactory();
        _master = factory.CreateMaster(_client);
        _master.Transport.ReadTimeout = ioTimeoutMs;
        _master.Transport.WriteTimeout = ioTimeoutMs;
        _master.Transport.Retries = 0;
    }

    public async Task<ushort[]> ReadAsync(ushort startAddress, ushort count, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try { return await _master.ReadHoldingRegistersAsync(_unitId, startAddress, count).WaitAsync(ct); }
        finally { _lock.Release(); }
    }

    public async Task WriteAsync(ushort address, ushort value, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try { await _master.WriteSingleRegisterAsync(_unitId, address, value).WaitAsync(ct); }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        try { _master.Dispose(); } catch { }
        try { _client.Dispose(); } catch { }
        _lock.Dispose();
    }
}

public sealed class NModbusRegisterReaderFactory : IRegisterReaderFactory
{
    public IRegisterReader Create(DeviceDefinition device) => new NModbusRegisterReader(device);
}
