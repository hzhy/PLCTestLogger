using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PlcTestLogger.App.Views;
using PlcTestLogger.Core.Config;

// 把“编辑设备”窗口的客户区渲染成 PNG（不依赖屏幕可见）。
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var outPath = args.Length > 0 ? args[0] : "edit-device-client.png";
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var def = new DeviceDefinition { Id = "DEV01", Ip = "192.168.1.10", Port = 502, UnitId = 1, PollIntervalMs = 200, Enabled = false };
        var w = new AddDeviceWindow(def) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        w.Show();
        w.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
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
        w.Close();
        app.Shutdown();
        return 0;
    }
}
