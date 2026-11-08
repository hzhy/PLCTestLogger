using System.IO;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Mapping;
using PlcTestLogger.Core.Polling;
using PlcTestLogger.Core.Storage;

namespace PlcTestLogger.App.Services;

public sealed record SavedRecord(string DeviceId, DateTime At, string FilePath, IReadOnlyList<FormattedCell> Cells);

/// <summary>管理全部设备的轮询器，把触发事件接到 Excel 写入，并维护 devices.json。</summary>
public sealed class DeviceManager
{
    private readonly RegisterConfig _registers;
    private readonly DevicesConfig _devices;
    private readonly IRegisterReaderFactory _factory;
    private readonly Dictionary<string, DevicePoller> _pollers = new();

    public DeviceManager(RegisterConfig registers, DevicesConfig devices, IRegisterReaderFactory factory, ExcelLogStore store)
    {
        _registers = registers;
        _devices = devices;
        _factory = factory;
        Store = store;
        Formatter = new SnapshotFormatter(registers);
    }

    public RegisterConfig Registers => _registers;
    public SnapshotFormatter Formatter { get; }
    public ExcelLogStore Store { get; }
    public IReadOnlyList<DeviceDefinition> Devices => _devices.Devices;

    public event Action<string, ConnectionState>? StateChanged;
    public event Action<string, ushort[]>? SnapshotUpdated;
    public event Action<SavedRecord>? RecordSaved;
    public event Action<string, string>? ErrorOccurred;

    public void StartAll()
    {
        foreach (var d in _devices.Devices.Where(d => d.Enabled)) StartPoller(d);
    }

    /// <summary>打开/关闭某台设备的轮询与保存，并写回 devices.json。</summary>
    public async Task SetEnabledAsync(string id, bool enabled)
    {
        var d = _devices.Devices.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException($"设备 {id} 不存在");
        if (d.Enabled == enabled && (enabled == _pollers.ContainsKey(id))) return;
        d.Enabled = enabled;
        ConfigLoader.SaveDevices(AppPaths.DevicesConfig, _devices);
        if (enabled) { if (!_pollers.ContainsKey(id)) StartPoller(d); }
        else if (_pollers.Remove(id, out var p)) await p.StopAsync();
        AppLog.Info($"{id} {(enabled ? "已启用" : "已停用")}");
    }

    public async Task StopAllAsync()
    {
        var pollers = _pollers.Values.ToList();
        _pollers.Clear();
        await Task.WhenAll(pollers.Select(p => p.StopAsync())).ConfigureAwait(false);
    }

    public DevicePoller? GetPoller(string id) => _pollers.TryGetValue(id, out var p) ? p : null;

    public void AddDevice(DeviceDefinition d)
    {
        if (ConfigLoader.ValidateDevice(d) is { } err) throw new ArgumentException(err);
        if (_devices.Devices.Any(x => string.Equals(x.Id, d.Id, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"设备编号 {d.Id} 已存在");
        _devices.Devices.Add(d);
        try { ConfigLoader.SaveDevices(AppPaths.DevicesConfig, _devices); }
        catch { _devices.Devices.Remove(d); throw; }
        if (d.Enabled) StartPoller(d);
        AppLog.Info($"新增设备 {d.Id} {d.Ip}:{d.Port}");
    }

    /// <summary>修改已有设备的连接参数。设备编号不可变，且设备必须处于停用状态。</summary>
    public void UpdateDevice(DeviceDefinition updated)
    {
        var d = _devices.Devices.FirstOrDefault(x => x.Id == updated.Id) ?? throw new ArgumentException($"设备 {updated.Id} 不存在");
        if (d.Enabled || _pollers.ContainsKey(d.Id)) throw new InvalidOperationException($"设备 {d.Id} 正在运行，编辑前请先停用");
        if (ConfigLoader.ValidateDevice(updated) is { } err) throw new ArgumentException(err);
        var backup = new DeviceDefinition { Id = d.Id, Ip = d.Ip, Port = d.Port, UnitId = d.UnitId, PollIntervalMs = d.PollIntervalMs, Enabled = d.Enabled };
        d.Ip = updated.Ip; d.Port = updated.Port; d.UnitId = updated.UnitId; d.PollIntervalMs = updated.PollIntervalMs;
        try { ConfigLoader.SaveDevices(AppPaths.DevicesConfig, _devices); }
        catch { d.Ip = backup.Ip; d.Port = backup.Port; d.UnitId = backup.UnitId; d.PollIntervalMs = backup.PollIntervalMs; throw; }
        AppLog.Info($"编辑设备 {d.Id} -> {d.Ip}:{d.Port} 站号 {d.UnitId} 周期 {d.PollIntervalMs}ms");
    }

    public async Task RemoveDeviceAsync(string id)
    {
        if (_pollers.Remove(id, out var p)) await p.StopAsync();
        _devices.Devices.RemoveAll(x => x.Id == id);
        ConfigLoader.SaveDevices(AppPaths.DevicesConfig, _devices);
        AppLog.Info($"删除设备 {id}");
    }

    private void StartPoller(DeviceDefinition d)
    {
        var poller = new DevicePoller(d, _registers, _factory);
        poller.StateChanged += s => { AppLog.Info($"{d.Id} 状态 {s}"); StateChanged?.Invoke(d.Id, s); };
        poller.SnapshotUpdated += snap => SnapshotUpdated?.Invoke(d.Id, snap);
        poller.ErrorOccurred += msg => { AppLog.Error($"{d.Id}: {msg}"); ErrorOccurred?.Invoke(d.Id, msg); };
        poller.SaveTriggered += (snap, at) => OnSaveTriggered(d.Id, snap, at);
        _pollers[d.Id] = poller;
        poller.Start();
    }

    private void OnSaveTriggered(string deviceId, ushort[] snapshot, DateTime at)
    {
        var cells = Formatter.Format(snapshot);
        var path = Store.GetFilePath(deviceId, at);
        var texts = cells.Select(c => c.Text).ToList();
        AppendResult result;
        try { result = Store.Append(deviceId, at, Formatter.Headers, texts); }
        catch (Exception ex)
        {
            AppLog.Error($"{deviceId} 记录丢失! {at:yyyy-MM-dd HH:mm:ss}	{string.Join("	", texts)}	{ex.Message}");
            throw;
        }
        if (result == AppendResult.Pending)
        {
            var msg = $"Excel 被占用（请关闭 {Path.GetFileName(path)}），记录已暂存，下次写入时自动合并";
            AppLog.Error($"{deviceId} {msg}: {at:yyyy-MM-dd HH:mm:ss}	{string.Join("	", texts)}");
            ErrorOccurred?.Invoke(deviceId, msg);
        }
        else AppLog.Info($"{deviceId} 保存记录 -> {path}");
        RecordSaved?.Invoke(new SavedRecord(deviceId, at, path, cells));
    }
}
