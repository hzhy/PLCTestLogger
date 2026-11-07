using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcTestLogger.App.Services;
using PlcTestLogger.Core.Config;

namespace PlcTestLogger.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DeviceManager _manager;
    private readonly Dictionary<string, ushort[]> _latest = new();

    public MainViewModel(DeviceManager manager)
    {
        _manager = manager;
        foreach (var d in manager.Devices) Devices.Add(new DeviceItemViewModel(d));
        var cfg = manager.Registers;
        for (var a = cfg.StartRegister; a < cfg.StartRegister + cfg.Count; a++)
            Registers.Add(new RegisterRowViewModel(a, cfg.Find(a)?.Name ?? ""));

        manager.StateChanged += (id, s) => OnUi(() => { var d = Find(id); if (d is not null) d.State = s; });
        manager.SnapshotUpdated += (id, snap) =>
        {
            lock (_latest) _latest[id] = snap;
            if (id == SelectedDevice?.Id) OnUi(() => RefreshRegisters(snap));
        };
        manager.RecordSaved += r => OnUi(() =>
        {
            var d = Find(r.DeviceId);
            if (d is not null) { d.SavedCount++; d.LastSavedAt = r.At.ToString("yyyy-MM-dd HH:mm:ss"); }
            AddLog($"[{r.DeviceId}] 已保存 -> {r.FilePath}");
        });
        manager.ErrorOccurred += (id, msg) => OnUi(() =>
        {
            var d = Find(id);
            if (d is not null) d.LastError = msg;
            AddLog($"[{id}] {msg}");
        });
        SelectedDevice = Devices.FirstOrDefault();
    }

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();
    public ObservableCollection<RegisterRowViewModel> Registers { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveDeviceCommand))]
    private DeviceItemViewModel? _selectedDevice;

    public string TriggerInfo => $"触发寄存器 D{_manager.Registers.TriggerRegister}（0→1 时保存）    读取范围 D{_manager.Registers.StartRegister}~D{_manager.Registers.StartRegister + _manager.Registers.Count - 1}";
    public string LoggerDir => _manager.Store.Root;

    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value)
    {
        ushort[]? snap = null;
        if (value is not null) lock (_latest) _latest.TryGetValue(value.Id, out snap);
        RefreshRegisters(snap);
    }

    public void AddDevice(DeviceDefinition def)
    {
        _manager.AddDevice(def);
        var vm = new DeviceItemViewModel(def);
        Devices.Add(vm);
        SelectedDevice = vm;
        AddLog($"新增设备 {def.Id} ({def.Ip}:{def.Port})");
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveDevice()
    {
        var d = SelectedDevice;
        if (d is null) return;
        var answer = MessageBox.Show($"确定删除设备 {d.Id}（{d.Ip}）？已保存的 Excel 不会删除。", "删除设备", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        await _manager.RemoveDeviceAsync(d.Id);
        Devices.Remove(d);
        lock (_latest) _latest.Remove(d.Id);
        SelectedDevice = Devices.FirstOrDefault();
        AddLog($"已删除设备 {d.Id}");
    }

    private bool CanRemove() => SelectedDevice is not null;

    [RelayCommand]
    private void OpenLoggerDir()
    {
        try
        {
            System.IO.Directory.CreateDirectory(LoggerDir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", LoggerDir) { UseShellExecute = true });
        }
        catch (Exception ex) { AddLog("打开文件夹失败: " + ex.Message); }
    }

    private void RefreshRegisters(ushort[]? snap)
    {
        if (snap is null)
        {
            foreach (var r in Registers) { r.Raw = 0; r.Text = ""; }
            return;
        }
        var cells = _manager.Formatter.FormatAll(snap);
        for (var i = 0; i < Registers.Count && i < cells.Count; i++)
        {
            Registers[i].Raw = cells[i].Raw;
            Registers[i].Text = cells[i].Text;
        }
    }

    private DeviceItemViewModel? Find(string id) => Devices.FirstOrDefault(d => d.Id == id);

    private void AddLog(string msg)
    {
        Logs.Insert(0, $"{DateTime.Now:HH:mm:ss} {msg}");
        while (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
    }

    private static void OnUi(Action a)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp is null || disp.CheckAccess()) a();
        else disp.BeginInvoke(a);
    }
}
