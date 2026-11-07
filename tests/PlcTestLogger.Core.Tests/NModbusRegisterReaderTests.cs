using System.Net;
using System.Net.Sockets;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Polling;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class NModbusRegisterReaderTests
{
    [Fact]
    public void Ctor_ConnectionRefused_ThrowsSocketException_NotAggregate()
    {
        // 找一个空闲端口：绑定后立刻释放
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        var dev = new DeviceDefinition { Id = "X", Ip = "127.0.0.1", Port = port };
        var ex = Assert.ThrowsAny<Exception>(() => new NModbusRegisterReader(dev, connectTimeoutMs: 8000));
        Assert.IsNotType<AggregateException>(ex);
        Assert.IsType<SocketException>(ex);
    }

    [Fact]
    public void Ctor_Timeout_ThrowsTimeoutException()
    {
        // 10.255.255.1 为不可达地址，连接会一直挂起直到超时
        var dev = new DeviceDefinition { Id = "X", Ip = "10.255.255.1", Port = 502 };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => new NModbusRegisterReader(dev, connectTimeoutMs: 300));
        Assert.True(sw.ElapsedMilliseconds < 3000);
    }
}
