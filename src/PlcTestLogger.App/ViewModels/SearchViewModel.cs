using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Mapping;
using PlcTestLogger.Core.Storage;

namespace PlcTestLogger.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly ExcelLogStore _store;
    private readonly RegisterConfig _registers;

    public SearchViewModel(ExcelLogStore store, IEnumerable<string> deviceIds, RegisterConfig registers)
    {
        _store = store;
        _registers = registers;
        foreach (var id in deviceIds) DeviceIds.Add(id);
        SelectedDeviceId = DeviceIds.FirstOrDefault();
    }

    public ObservableCollection<string> DeviceIds { get; } = new();
    [ObservableProperty] private string? _selectedDeviceId;
    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private DataView? _results;
    [ObservableProperty] private string _status = "选择设备和日期后点击「查询」。";

    [RelayCommand]
    private void Search()
    {
        if (string.IsNullOrEmpty(SelectedDeviceId)) { Status = "请选择设备。"; return; }
        try
        {
            var rows = _store.QueryDay(SelectedDeviceId, DateOnly.FromDateTime(Date));
            if (rows.Count == 0)
            {
                Results = null;
                Status = $"{SelectedDeviceId} 在 {Date:yyyy-MM-dd} 没有记录。";
                return;
            }
            // 列按寄存器地址从低到高显示（Excel 文件本身的列顺序不变）
            var order = ColumnOrder.ByRegisterAddress(rows[0], _registers);
            var table = new DataTable();
            foreach (var i in order) table.Columns.Add(MakeUnique(table, rows[0][i]));
            foreach (var r in rows.Skip(1)) table.Rows.Add(order.Select(i => (object)(i < r.Length ? r[i] : "")).ToArray());
            Results = table.DefaultView;
            Status = $"{SelectedDeviceId} {Date:yyyy-MM-dd} 共 {rows.Count - 1} 条记录。文件：{_store.GetFilePath(SelectedDeviceId, Date)}";
        }
        catch (Exception ex)
        {
            Results = null;
            Status = "查询失败: " + ex.Message;
        }
    }

    private static string MakeUnique(DataTable t, string name)
    {
        var n = string.IsNullOrWhiteSpace(name) ? "列" : name;
        var candidate = n;
        var i = 2;
        while (t.Columns.Contains(candidate)) candidate = $"{n}({i++})";
        return candidate;
    }
}
