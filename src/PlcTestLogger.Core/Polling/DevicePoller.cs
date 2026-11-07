using PlcTestLogger.Core.Config;

namespace PlcTestLogger.Core.Polling;

public enum ConnectionState { Disconnected, Connected, Reconnecting }

/// <summary>单台设备的后台轮询：周期读取寄存器块，检测触发寄存器上升沿，自动重连。</summary>
public sealed class DevicePoller
{
    private readonly DeviceDefinition _device;
    private readonly RegisterConfig _cfg;
    private readonly IRegisterReaderFactory _factory;
    private readonly RisingEdgeDetector _edge = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private ConnectionState _state = ConnectionState.Disconnected;

    public DevicePoller(DeviceDefinition device, RegisterConfig cfg, IRegisterReaderFactory factory)
    {
        _device = device;
        _cfg = cfg;
        _factory = factory;
    }

    public DeviceDefinition Device => _device;
    public ConnectionState State => _state;
    public long ReadCount { get; private set; }
    public string? LastError { get; private set; }
    public ushort[]? LastSnapshot { get; private set; }

    /// <summary>重连初始等待（毫秒），每次失败翻倍，上限 ReconnectMaxDelayMs。</summary>
    public int ReconnectDelayMs { get; set; } = 1000;
    public int ReconnectMaxDelayMs { get; set; } = 5000;

    public event Action<ConnectionState>? StateChanged;
    public event Action<ushort[]>? SnapshotUpdated;
    /// <summary>触发寄存器 0-&gt;1：参数为触发时刻的完整快照与时间。</summary>
    public event Action<ushort[], DateTime>? SaveTriggered;
    public event Action<string>? ErrorOccurred;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null || _loop is null) return;
        _cts.Cancel();
        try { await _loop; } catch (OperationCanceledException) { }
        _loop = null;
        _cts.Dispose();
        _cts = null;
        SetState(ConnectionState.Disconnected);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var delay = ReconnectDelayMs;
        while (!ct.IsCancellationRequested)
        {
            IRegisterReader? reader = null;
            try
            {
                reader = _factory.Create(_device);
                _edge.Reset();
                SetState(ConnectionState.Connected);
                delay = ReconnectDelayMs;
                await PollLoopAsync(reader, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ErrorOccurred?.Invoke(ex.Message);
                SetState(ConnectionState.Reconnecting);
            }
            finally
            {
                try { reader?.Dispose(); } catch { /* ignore */ }
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
            delay = Math.Min(delay * 2, ReconnectMaxDelayMs);
        }
    }

    private async Task PollLoopAsync(IRegisterReader reader, CancellationToken ct)
    {
        var start = checked((ushort)(_cfg.DRegisterBase + _cfg.StartRegister));
        var count = checked((ushort)_cfg.Count);
        var triggerIdx = _cfg.IndexOf(_cfg.TriggerRegister);
        var triggerAddr = checked((ushort)(_cfg.DRegisterBase + _cfg.TriggerRegister));

        while (!ct.IsCancellationRequested)
        {
            var snapshot = await reader.ReadAsync(start, count, ct);
            ReadCount++;
            LastSnapshot = snapshot;
            SnapshotUpdated?.Invoke(snapshot);

            if (triggerIdx >= 0 && triggerIdx < snapshot.Length && _edge.Feed(snapshot[triggerIdx]))
            {
                var copy = (ushort[])snapshot.Clone();
                var at = DateTime.Now;
                try { SaveTriggered?.Invoke(copy, at); }
                catch (Exception ex) { LastError = ex.Message; ErrorOccurred?.Invoke("保存失败: " + ex.Message); }
                if (_cfg.ResetTriggerAfterSave)
                    await reader.WriteAsync(triggerAddr, 0, ct);
            }

            await Task.Delay(Math.Max(1, _device.PollIntervalMs), ct);
        }
    }

    private void SetState(ConnectionState s)
    {
        if (_state == s) return;
        _state = s;
        StateChanged?.Invoke(s);
    }
}
