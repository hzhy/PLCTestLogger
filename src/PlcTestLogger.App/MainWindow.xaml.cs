using System.Windows;
using PlcTestLogger.App.Services;
using PlcTestLogger.App.ViewModels;
using PlcTestLogger.App.Views;

namespace PlcTestLogger.App;

public partial class MainWindow : Window
{
    private readonly DeviceManager _manager;
    private readonly MainViewModel _vm;

    public MainWindow(DeviceManager manager)
    {
        InitializeComponent();
        _manager = manager;
        _vm = new MainViewModel(manager);
        DataContext = _vm;
    }

    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddDeviceWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        try { _vm.AddDevice(dlg.Result); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "新增设备失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        var win = new SearchWindow(new SearchViewModel(_manager.Store, _manager.Devices.Select(d => d.Id)))
        {
            Owner = this,
        };
        if (_vm.SelectedDevice is { } sel) ((SearchViewModel)win.DataContext).SelectedDeviceId = sel.Id;
        win.Show();
    }
}
