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
        foreach (var d in manager.Devices) Devices.Add(CreateItem(d));
        foreach (var def in manager.Registers.Registers.Where(r => r.Enabled).OrderBy(r => r.Address))
            Registers.Add(new RegisterRowViewModel(def.Address, def.Name));

        manager.StateChanged += (id, s) => OnUi(() => { var d = Find(id); if (d is not null) d.State = s; });
        manager.SnapshotUpdated += (id, snap) =>
        {
            lock (_latest) _latest[id] = snap;
            if (id == SelectedDevice?.Id) OnUi(() => { if (id == SelectedDevice?.Id) RefreshRegisters(snap); });
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

    /// <summary>寄存器定义来源（内置 / config\registers.json），显示在状态栏。</summary>
    public string RegisterSource { get; init; } = "";

    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value)
    {
        ushort[]? snap = null;
        if (value is not null) lock (_latest) _latest.TryGetValue(value.Id, out snap);
        RefreshRegisters(snap);
    }

    private DeviceItemViewModel CreateItem(DeviceDefinition def)
    {
        var vm = new DeviceItemViewModel(def);
        vm.EnabledChanged += async (item, enabled) =>
        {
            try
            {
                await _manager.SetEnabledAsync(item.Id, enabled);
                if (!enabled) { item.State = Core.Polling.ConnectionState.Disconnected; item.LastError = ""; lock (_latest) _latest.Remove(item.Id); if (item == SelectedDevice) RefreshRegisters(null); }
                AddLog($"{item.Id} {(enabled ? "已启用轮询" : "已停用轮询")}");
            }
            catch (Exception ex) { AddLog($"[{item.Id}] 切换失败: {ex.Message}"); }
        };
        return vm;
    }

    public void AddDevice(DeviceDefinition def)
    {
        _manager.AddDevice(def);
        var vm = CreateItem(def);
        Devices.Add(vm);
        SelectedDevice = vm;
        AddLog($"新增设备 {def.Id} ({def.Ip}:{def.Port})");
    }

    /// <summary>编辑前的检查：返回 null 表示可以编辑，否则为提示文字。</summary>
    public string? CheckCanEdit(DeviceItemViewModel? d)
    {
        if (d is null) return "请先在设备列表中选中要编辑的设备。";
        if (d.Enabled) return $"设备 {d.Id} 正在运行，编辑前请先取消该行的“启用”勾选。";
        return null;
    }

    public void EditDevice(DeviceItemViewModel item, DeviceDefinition updated)
    {
        _manager.UpdateDevice(updated);
        item.RefreshDefinition();
        AddLog($"已修改设备 {item.Id} ({item.Ip}:{item.Port})");
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
            foreach (var r in Registers) { r.Raw = ""; r.Text = ""; }
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
