using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PlcTestLogger.App;
using PlcTestLogger.App.Services;
using PlcTestLogger.App.Views;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Polling;
using PlcTestLogger.Core.Storage;

// 离屏渲染窗口客户区为 PNG（锁屏时屏幕抓图不可用）。
//   ShotGen edit <out.png>              编辑设备对话框
//   ShotGen main <out.png> [waitMs]     主界面：读取本程序目录下 config\，连接设备后渲染（需先启动模拟器）
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "edit";
        var outPath = args.Length > 1 ? args[1] : mode + ".png";
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window w;
        DeviceManager? manager = null;
        var waitMs = 300;
        if (mode == "main")
        {
            var registers = RegisterConfigSource.Load();
            var devices = ConfigLoader.LoadDevices(AppPaths.DevicesConfig);
            manager = new DeviceManager(registers.Config, devices, new NModbusRegisterReaderFactory(), new ExcelLogStore(AppPaths.LoggerDir));
            w = new MainWindow(manager, registers.Label);
            manager.StartAll();
            waitMs = args.Length > 2 ? int.Parse(args[2]) : 4000;
        }
        else
        {
            var def = new DeviceDefinition { Id = "DEV01", Ip = "192.168.1.10", Port = 502, UnitId = 1, PollIntervalMs = 200, Enabled = false };
            w = new AddDeviceWindow(def);
        }
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = -20000; w.Top = -20000; w.ShowActivated = false; w.ShowInTaskbar = false;
        w.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(waitMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Render(w, outPath);
            if (manager is not null) manager.StopAllAsync().Wait(5000);
            w.Close();
            app.Shutdown();
        };
        timer.Start();
        app.Run();
        return 0;
    }

    static void Render(Window w, string outPath)
    {
        w.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var content = (FrameworkElement)w.Content;
        var size = content.RenderSize;
        var rtb = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(size));
        }
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(outPath)) enc.Save(fs);
        Console.WriteLine($"saved {outPath} {rtb.PixelWidth}x{rtb.PixelHeight}");
    }
}
