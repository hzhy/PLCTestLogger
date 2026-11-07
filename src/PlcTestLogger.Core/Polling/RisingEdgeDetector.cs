namespace PlcTestLogger.Core.Polling;

/// <summary>检测 0 -&gt; 非 0 的上升沿。第一次喂入的值只作基线，不触发。</summary>
public sealed class RisingEdgeDetector
{
    private ushort? _last;

    public bool Feed(ushort value)
    {
        var rising = _last is ushort last && last == 0 && value != 0;
        _last = value;
        return rising;
    }

    /// <summary>通信中断后调用：下一次喂入的值重新作为基线。</summary>
    public void Reset() => _last = null;
}
