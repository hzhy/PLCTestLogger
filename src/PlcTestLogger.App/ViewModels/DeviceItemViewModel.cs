using CommunityToolkit.Mvvm.ComponentModel;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Polling;

namespace PlcTestLogger.App.ViewModels;

public partial class DeviceItemViewModel : ObservableObject
{
    public DeviceItemViewModel(DeviceDefinition def) => Definition = def;

    public DeviceDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Ip => Definition.Ip;
    public int Port => Definition.Port;

    [ObservableProperty] private ConnectionState _state = ConnectionState.Disconnected;
    [ObservableProperty] private string _lastSavedAt = "";
    [ObservableProperty] private int _savedCount;
    [ObservableProperty] private string _lastError = "";

    public string StateText => State switch
    {
        ConnectionState.Connected => "已连接",
        ConnectionState.Reconnecting => "重连中",
        _ => "未连接",
    };

    partial void OnStateChanged(ConnectionState value) => OnPropertyChanged(nameof(StateText));
}

public partial class RegisterRowViewModel : ObservableObject
{
    public RegisterRowViewModel(int address, string name) { Address = address; Name = name; }
    public int Address { get; }
    public string Register => $"D{Address}";
    public string Name { get; }
    [ObservableProperty] private ushort _raw;
    [ObservableProperty] private string _text = "";
}
