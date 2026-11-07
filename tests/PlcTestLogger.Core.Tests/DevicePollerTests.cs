using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Polling;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class DevicePollerTests
{
    private sealed class ScriptedReader : IRegisterReader
    {
        private readonly Queue<Func<ushort[]>> _script;
        public List<(ushort addr, ushort val)> Writes { get; } = new();
        public ScriptedReader(IEnumerable<Func<ushort[]>> script) => _script = new Queue<Func<ushort[]>>(script);
        public Task<ushort[]> ReadAsync(ushort startAddress, ushort count, CancellationToken ct)
        {
            if (_script.Count == 0) return Task.FromResult(new ushort[count]);
            return Task.FromResult(_script.Dequeue()());
        }
        public Task WriteAsync(ushort address, ushort value, CancellationToken ct) { Writes.Add((address, value)); return Task.CompletedTask; }
        public void Dispose() { }
    }

    private sealed class Factory : IRegisterReaderFactory
    {
        private readonly IRegisterReader _r;
        public int Created;
        public Factory(IRegisterReader r) => _r = r;
        public IRegisterReader Create(DeviceDefinition d) { Created++; return _r; }
    }

    private static RegisterConfig Cfg() => new() { StartRegister = 200, Count = 31, TriggerRegister = 230 };
    private static DeviceDefinition Dev() => new() { Id = "DEV01", Ip = "127.0.0.1", PollIntervalMs = 1 };

    private static ushort[] Snap(ushort trigger) { var s = new ushort[31]; s[30] = trigger; s[0] = 23; return s; }

    [Fact]
    public async Task RisingEdge_RaisesSaveTriggeredOnce()
    {
        var reader = new ScriptedReader(new Func<ushort[]>[] { () => Snap(0), () => Snap(0), () => Snap(1), () => Snap(1), () => Snap(0) });
        var poller = new DevicePoller(Dev(), Cfg(), new Factory(reader));
        var saves = new List<ushort[]>();
        poller.SaveTriggered += (s, at) => saves.Add(s);
        poller.Start();
        await WaitUntil(() => poller.ReadCount >= 6);
        await poller.StopAsync();
        Assert.Single(saves);
        Assert.Equal(23, saves[0][0]);
    }

    [Fact]
    public async Task ReadFailure_SetsReconnecting_ThenRecovers_WithoutFalseTrigger()
    {
        var reader = new ScriptedReader(new Func<ushort[]>[] { () => Snap(0), () => throw new IOException("boom"), () => Snap(1), () => Snap(1) });
        var factory = new Factory(reader);
        var poller = new DevicePoller(Dev(), Cfg(), factory) { ReconnectDelayMs = 1 };
        var states = new List<ConnectionState>();
        var saves = 0;
        poller.StateChanged += s => states.Add(s);
        poller.SaveTriggered += (_, _) => saves++;
        poller.Start();
        await WaitUntil(() => poller.ReadCount >= 4);
        await poller.StopAsync();
        Assert.Contains(ConnectionState.Reconnecting, states);
        Assert.Equal(ConnectionState.Connected, states.Last(s => s != ConnectionState.Disconnected));
        Assert.Equal(0, saves);
        Assert.True(factory.Created >= 2);
    }

    [Fact]
    public async Task ResetTriggerAfterSave_WritesZero()
    {
        var reader = new ScriptedReader(new Func<ushort[]>[] { () => Snap(0), () => Snap(1) });
        var cfg = Cfg(); cfg.ResetTriggerAfterSave = true;
        var poller = new DevicePoller(Dev(), cfg, new Factory(reader));
        poller.SaveTriggered += (_, _) => { };
        poller.Start();
        await WaitUntil(() => reader.Writes.Count >= 1);
        await poller.StopAsync();
        Assert.Equal((230, 0), reader.Writes[0]);
    }

    private static async Task WaitUntil(Func<bool> cond, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(5);
        Assert.True(cond(), "timeout waiting for condition");
    }
}
