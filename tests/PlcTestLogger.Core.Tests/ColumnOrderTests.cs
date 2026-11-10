using PlcTestLogger.Core.Mapping;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class ColumnOrderTests
{
    [Fact]
    public void SortsByRegisterAddress_TimeFirst_UnknownLast()
    {
        var headers = new[] { "保存时间", "产品编号", "温度", "扭矩", "纯水流量", "接口1测试结果", "旧列名", "测试方式" };
        var order = ColumnOrder.ByRegisterAddress(headers, ConfigLoaderTests.BuiltIn());
        Assert.Equal(
            new[] { "保存时间", "产品编号", "测试方式", "纯水流量", "温度", "扭矩", "接口1测试结果", "旧列名" },
            order.Select(i => headers[i]));
    }

    [Fact]
    public void BuiltInExcelHeaders_ComeOutAscending()
    {
        var cfg = ConfigLoaderTests.BuiltIn();
        var headers = new[] { "保存时间" }.Concat(new SnapshotFormatter(cfg).Headers).ToArray();
        var order = ColumnOrder.ByRegisterAddress(headers, cfg);
        var addresses = order.Skip(1).Select(i => cfg.Registers.First(r => r.Name == headers[i]).Address).ToList();
        Assert.Equal(addresses.OrderBy(a => a), addresses);
        Assert.Equal(0, order[0]);
    }
}
