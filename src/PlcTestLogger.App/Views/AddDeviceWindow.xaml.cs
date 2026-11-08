using System.Windows;
using PlcTestLogger.Core.Config;

namespace PlcTestLogger.App.Views;

public partial class AddDeviceWindow : Window
{
    private readonly DeviceDefinition? _existing;

    /// <summary>新增模式。</summary>
    public AddDeviceWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => IdBox.Focus();
    }

    /// <summary>编辑模式：设备编号只读，其余字段预填。</summary>
    public AddDeviceWindow(DeviceDefinition existing) : this()
    {
        _existing = existing;
        Title = "编辑设备";
        IdBox.Text = existing.Id;
        IdBox.IsReadOnly = true;
        IdBox.Background = System.Windows.Media.Brushes.WhiteSmoke;
        IdBox.Foreground = System.Windows.Media.Brushes.Gray;
        IdBox.ToolTip = "设备编号不可修改（记录文件夹按编号命名）";
        IpBox.Text = existing.Ip;
        PortBox.Text = existing.Port.ToString();
        UnitBox.Text = existing.UnitId.ToString();
        IntervalBox.Text = existing.PollIntervalMs.ToString();
        HintText.Text = "设备编号不可修改；带 * 的为必填项；轮询周期最小 200 毫秒。";
        Loaded += (_, _) => { IpBox.Focus(); IpBox.SelectAll(); };
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

        var def = new DeviceDefinition { Id = _existing?.Id ?? id, Ip = ip, Port = port, UnitId = unit, PollIntervalMs = interval, Enabled = _existing?.Enabled ?? true };
        if (ConfigLoader.ValidateDevice(def) is { } err) { Warn(err + "。"); return; }
        Result = def;
        DialogResult = true;
    }

    private void Warn(string msg) => MessageBox.Show(this, msg, "输入有误", MessageBoxButton.OK, MessageBoxImage.Warning);
}
