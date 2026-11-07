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
        if (id.Length == 0) { Warn("设备编号为必填项。"); IdBox.Focus(); return; }
        if (ip.Length == 0) { Warn("IP 地址为必填项。"); IpBox.Focus(); return; }
        if (PortBox.Text.Trim().Length == 0) { Warn("端口为必填项。"); PortBox.Focus(); return; }
        if (!int.TryParse(PortBox.Text, out var port)) { Warn("端口必须是 1~65535 的整数。"); PortBox.Focus(); return; }
        if (!byte.TryParse(UnitBox.Text, out var unit)) { Warn("站号必须是 0~255 的整数。"); UnitBox.Focus(); return; }
        if (!int.TryParse(IntervalBox.Text, out var interval)) { Warn($"轮询周期必须是不低于 {DeviceDefinition.MinPollIntervalMs} 的整数（毫秒）。"); IntervalBox.Focus(); return; }

        var def = new DeviceDefinition { Id = id, Ip = ip, Port = port, UnitId = unit, PollIntervalMs = interval };
        if (ConfigLoader.ValidateDevice(def) is { } err) { Warn(err + "。"); return; }
        Result = def;
        DialogResult = true;
    }

    private void Warn(string msg) => MessageBox.Show(this, msg, "输入有误", MessageBoxButton.OK, MessageBoxImage.Warning);
}
