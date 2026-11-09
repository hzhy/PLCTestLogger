using System.Windows;
using PlcTestLogger.App.Services;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Polling;
using PlcTestLogger.Core.Storage;

namespace PlcTestLogger.App;

public partial class App : Application
{
    private DeviceManager? _manager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("未处理异常: " + args.Exception);
            MessageBox.Show(args.Exception.Message, "程序错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => AppLog.Error("致命异常: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) => { AppLog.Error("后台任务异常: " + args.Exception); args.SetObserved(); };

        RegisterConfigSource.Result registers;
        DevicesConfig devices;
        try
        {
            registers = RegisterConfigSource.Load();
            devices = ConfigLoader.LoadDevices(AppPaths.DevicesConfig);
        }
        catch (Exception ex)
        {
            AppLog.Error("配置加载失败: " + ex);
            MessageBox.Show($"配置加载失败：\n{ex.Message}\n\n请检查 {AppPaths.ConfigDir} 下的 devices.json；如果放了 registers.json 覆盖文件，请检查其格式，或删除它以使用内置定义。", "配置错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        AppLog.Info($"启动，设备 {devices.Devices.Count} 台，触发寄存器 D{registers.Config.TriggerRegister}，寄存器定义：{registers.Label}");
        _manager = new DeviceManager(registers.Config, devices, new NModbusRegisterReaderFactory(), new ExcelLogStore(AppPaths.LoggerDir));
        var window = new MainWindow(_manager, registers.Label);
        MainWindow = window;
        window.Show();
        _manager.StartAll();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info("正在退出，停止轮询...");
        try { _manager?.StopAllAsync().Wait(10000); } catch { }
        AppLog.Info("退出");
        base.OnExit(e);
    }
}
