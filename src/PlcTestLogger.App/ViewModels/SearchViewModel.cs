using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcTestLogger.Core.Storage;

namespace PlcTestLogger.App.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly ExcelLogStore _store;

    public SearchViewModel(ExcelLogStore store, IEnumerable<string> deviceIds)
    {
        _store = store;
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
            var table = new DataTable();
            foreach (var h in rows[0]) table.Columns.Add(MakeUnique(table, h));
            foreach (var r in rows.Skip(1)) table.Rows.Add(r.Cast<object>().ToArray());
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
