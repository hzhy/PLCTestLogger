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
        foreach (var d in _devices.Devices) StartPoller(d);
    }

    public async Task StopAllAsync()
    {
        foreach (var p in _pollers.Values) await p.StopAsync();
        _pollers.Clear();
    }

    public DevicePoller? GetPoller(string id) => _pollers.TryGetValue(id, out var p) ? p : null;

    public void AddDevice(DeviceDefinition d)
    {
        if (string.IsNullOrWhiteSpace(d.Id)) throw new ArgumentException("设备编号不能为空");
        if (_devices.Devices.Any(x => x.Id == d.Id)) throw new ArgumentException($"设备编号 {d.Id} 已存在");
        _devices.Devices.Add(d);
        ConfigLoader.SaveDevices(AppPaths.DevicesConfig, _devices);
        StartPoller(d);
        AppLog.Info($"新增设备 {d.Id} {d.Ip}:{d.Port}");
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
        Store.Append(deviceId, at, Formatter.Headers, cells.Select(c => c.Text).ToList());
        AppLog.Info($"{deviceId} 保存记录 -> {path}");
        RecordSaved?.Invoke(new SavedRecord(deviceId, at, path, cells));
    }
}
