using System.Net;
using System.Windows;
using PlcTestLogger.Core.Config;

namespace PlcTestLogger.App.Views;

public partial class AddDeviceWindow : Window
{
    public AddDeviceWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => IdBox.Focus();
    }

    public DeviceDefinition? Result { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var id = IdBox.Text.Trim();
        var ip = IpBox.Text.Trim();
        if (id.Length == 0) { Warn("请输入设备编号。"); return; }
        if (id.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) { Warn("设备编号不能包含 \\ / : * ? \" < > | 等字符。"); return; }
        if (!IPAddress.TryParse(ip, out _)) { Warn("IP 地址格式不正确。"); return; }
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535) { Warn("端口必须是 1~65535 的整数。"); return; }
        if (!byte.TryParse(UnitBox.Text, out var unit)) { Warn("站号必须是 0~255 的整数。"); return; }
        if (!int.TryParse(IntervalBox.Text, out var interval) || interval < 20) { Warn("轮询周期必须是不小于 20 的整数（毫秒）。"); return; }

        Result = new DeviceDefinition { Id = id, Ip = ip, Port = port, UnitId = unit, PollIntervalMs = interval };
        DialogResult = true;
    }

    private void Warn(string msg) => MessageBox.Show(this, msg, "输入有误", MessageBoxButton.OK, MessageBoxImage.Warning);
}
