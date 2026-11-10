using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcTestLogger.Core.Config;
using PlcTestLogger.Core.Testing;

namespace PlcTestLogger.App.ViewModels;

/// <summary>数据测试窗口：用测试文件里的 PLC 原始字检查换算和显示（不连接 PLC）。</summary>
public partial class DataTestViewModel : ObservableObject
{
    private readonly RegisterConfig _registers;
    private List<TestCheck> _all = new();

    public DataTestViewModel(RegisterConfig registers, string filePath)
    {
        _registers = registers;
        FilePath = filePath;
    }

    public ObservableCollection<TestCheck> Results { get; } = new();

    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private bool _onlyFailures;
    [ObservableProperty] private string _status = "";
    /// <summary>有失败项或文件错误时为 true，状态栏显示为红色。</summary>
    [ObservableProperty] private bool _isBad;

    partial void OnOnlyFailuresChanged(bool value) => Refill();

    [RelayCommand]
    private void Run()
    {
        _all = new();
        Refill();
        if (!File.Exists(FilePath))
        {
            IsBad = true;
            Status = $"找不到测试文件：{FilePath}。请点“选择文件…”。";
            return;
        }
        try
        {
            var cases = TestCaseRunner.Load(FilePath);
            _all = TestCaseRunner.Run(cases, _registers);
            Refill();
            var failed = _all.Count(r => !r.Passed);
            IsBad = failed > 0;
            Status = failed == 0
                ? $"全部通过：{cases.Count} 条用例，{_all.Count} 项检查。"
                : $"{failed} 项失败，共 {_all.Count} 项检查（{cases.Count} 条用例）。失败项的“显示值”列为程序当前的显示。";
        }
        catch (Exception ex)
        {
            IsBad = true;
            Status = "测试文件读取失败：" + ex.Message;
        }
    }

    private void Refill()
    {
        Results.Clear();
        foreach (var r in _all.Where(r => !OnlyFailures || !r.Passed)) Results.Add(r);
    }
}
