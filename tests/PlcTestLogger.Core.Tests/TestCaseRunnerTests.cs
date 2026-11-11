using PlcTestLogger.Core.Testing;
using Xunit;

namespace PlcTestLogger.Core.Tests;

public class TestCaseRunnerTests
{
    private const string Json = """
        {
          "cases": [
            {
              "name": "ok",
              "words": { "200": 1, "D204": "0x0001", "212": "0x0000", "213": "0x41A4", "210": 365 },
              "expect": { "200": "J0321干式", "204": "压力20PsiG", "D212": "20.50PsiG", "210": "36.5℃" }
            },
            {
              "name": "wrong",
              "words": { "200": 37 },
              "expect": { "200": "J0321干式", "240": 0 }
            }
          ]
        }
        """;

    [Fact]
    public void SampleTestCases_AllPassWithBuiltIn()
    {
        // samples/test/test-cases.json 随程序发布，必须与内置寄存器定义保持一致
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "test-cases.json");
        var results = TestCaseRunner.Run(TestCaseRunner.Load(path), ConfigLoaderTests.BuiltIn());
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.True(r.Passed, $"{r.Case} {r.Register}: 显示 {r.Display}，期望 {r.Expected}"));
    }

    [Fact]
    public void Parse_ReadsDecimalHexAndDPrefix()
    {
        var cases = TestCaseRunner.Parse(Json);
        Assert.Equal(2, cases.Count);
        Assert.Equal((ushort)0x41A4, cases[0].Words[213]);
        Assert.Equal((ushort)1, cases[0].Words[204]);
        Assert.Equal("20.50PsiG", cases[0].Expect[212]);
        Assert.Equal("0", cases[1].Expect[240]);
    }

    [Fact]
    public void Run_ReportsPassAndFailWithActualText()
    {
        var results = TestCaseRunner.Run(TestCaseRunner.Parse(Json), ConfigLoaderTests.BuiltIn());
        Assert.All(results.Where(r => r.Case == "ok"), r => Assert.True(r.Passed, $"{r.Register}: {r.Display}"));
        var bad = results.Single(r => r.Case == "wrong" && r.Register == "D200");
        Assert.False(bad.Passed);
        Assert.Equal("K37", bad.Display);
        Assert.Equal("37", bad.Value);
        Assert.Equal("20.5（浮点）", results.Single(r => r.Register == "D212").Value);
        Assert.Equal("365", results.Single(r => r.Case == "ok" && r.Register == "D210").Value);
        Assert.Equal("失败", bad.ResultText);
        Assert.True(results.Single(r => r.Case == "wrong" && r.Register == "D240").Passed);
        Assert.Equal("0x0000 0x41A4", results.Single(r => r.Register == "D212").Raw);
    }

    [Fact]
    public void Run_FlagsWordsOutsideRange_AndUndefinedExpectAddress()
    {
        var cases = TestCaseRunner.Parse("""
            { "cases": [
              { "name": "range", "words": { "199": 1 }, "expect": { "200": "x" } },
              { "name": "second-word", "words": {}, "expect": { "213": "0" } }
            ] }
            """);
        var results = TestCaseRunner.Run(cases, ConfigLoaderTests.BuiltIn());
        Assert.Contains("D199", results.Single(r => r.Case == "range").Display);
        Assert.False(results.Single(r => r.Case == "second-word").Passed);
    }

    [Theory]
    [InlineData("""{ "cases": [ { "name": "a", "words": { "200": 70000 }, "expect": { "200": "x" } } ] }""")]
    [InlineData("""{ "cases": [ { "name": "a", "words": { "200": "0xZZ" }, "expect": { "200": "x" } } ] }""")]
    [InlineData("""{ "cases": [ { "name": "a", "words": { "abc": 1 }, "expect": { "200": "x" } } ] }""")]
    [InlineData("""{ "cases": [ { "name": "a", "words": { "200": 1 } } ] }""")]
    [InlineData("""{ "items": [] }""")]
    public void Parse_RejectsBadInput(string json)
        => Assert.Throws<InvalidDataException>(() => TestCaseRunner.Parse(json));
}
