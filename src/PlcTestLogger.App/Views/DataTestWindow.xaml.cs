using System.IO;
using System.Windows;
using PlcTestLogger.App.ViewModels;

namespace PlcTestLogger.App.Views;

public partial class DataTestWindow : Window
{
    private readonly DataTestViewModel _vm;

    public DataTestWindow(DataTestViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) => vm.RunCommand.Execute(null);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_vm.FilePath);
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择测试文件",
            Filter = "测试文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            InitialDirectory = dir is not null && Directory.Exists(dir) ? dir : AppContext.BaseDirectory,
        };
        if (dlg.ShowDialog(this) != true) return;
        _vm.FilePath = dlg.FileName;
        _vm.RunCommand.Execute(null);
    }
}
