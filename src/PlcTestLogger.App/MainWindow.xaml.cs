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
        Title = $"实验数据追溯系统 v{AppVersion}";
        _manager = manager;
        _vm = new MainViewModel(manager);
        DataContext = _vm;
    }

    /// <summary>来自 csproj 的 &lt;Version&gt;，窗口标题与用户手册共用同一版本号。</summary>
    public static string AppVersion
    {
        get
        {
            var v = typeof(MainWindow).Assembly.GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    private void AddDevice_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddDeviceWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        try { _vm.AddDevice(dlg.Result); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "新增设备失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void EditDevice_Click(object sender, RoutedEventArgs e) => EditSelectedDevice();

    private void DeviceGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 只响应双击数据行，双击表头或空白处不弹窗
        if (e.OriginalSource is DependencyObject src && FindParent<System.Windows.Controls.DataGridRow>(src) is not null) EditSelectedDevice();
    }

    private void EditSelectedDevice()
    {
        var item = _vm.SelectedDevice;
        if (_vm.CheckCanEdit(item) is { } why)
        {
            MessageBox.Show(this, why, "编辑设备", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new AddDeviceWindow(item!.Definition) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        try { _vm.EditDevice(item, dlg.Result); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "编辑设备失败", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d is not null and not T) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return d as T;
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
