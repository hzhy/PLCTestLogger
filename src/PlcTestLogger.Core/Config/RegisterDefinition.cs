using System.Text.Json.Serialization;

namespace PlcTestLogger.Core.Config;

/// <summary>一个 D 寄存器的含义定义（来自 config/registers.json）。</summary>
public sealed class RegisterDefinition
{
    /// <summary>D 寄存器编号，例如 200 表示 D200。</summary>
    public int Address { get; set; }

    /// <summary>列名 / 显示名，例如 "产品编号"。</summary>
    public string Name { get; set; } = "";

    /// <summary>enum = 常量选项；number = 数值（乘 Scale、附 Unit）；raw = 原始整数。</summary>
    public string Type { get; set; } = "raw";

    /// <summary>enum 类型：寄存器数值(字符串键) -&gt; 语义化文本。</summary>
    public Dictionary<string, string>? Options { get; set; }

    /// <summary>enum 类型可选：当本寄存器取某选项时，引用它的结果列使用的单位。</summary>
    public Dictionary<string, string>? ResultUnits { get; set; }

    /// <summary>enum 类型可选：当本寄存器取某选项时，引用它的结果列使用的倍率。</summary>
    public Dictionary<string, double>? ResultScales { get; set; }

    /// <summary>enum 类型可选：当本寄存器取某选项时，引用它的结果列保留的小数位数。</summary>
    public Dictionary<string, int>? ResultDecimals { get; set; }

    /// <summary>number 类型：原始值 × Scale。</summary>
    public double Scale { get; set; } = 1;

    /// <summary>number 类型：保留小数位数。</summary>
    public int Decimals { get; set; } = 0;

    /// <summary>number 类型：固定单位后缀。</summary>
    public string Unit { get; set; } = "";

    /// <summary>number 类型可选：单位/倍率由另一寄存器（enum）的当前选项决定。</summary>
    public int? UnitFrom { get; set; }

    /// <summary>按 16 位有符号整数解释。</summary>
    public bool Signed { get; set; }

    [JsonIgnore] public bool IsEnum => string.Equals(Type, "enum", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsNumber => string.Equals(Type, "number", StringComparison.OrdinalIgnoreCase);
}

/// <summary>寄存器配置根对象。</summary>
public sealed class RegisterConfig
{
    /// <summary>D0 对应的 Modbus 保持寄存器地址（汇川 Easy 系列通常为 0）。</summary>
    public int DRegisterBase { get; set; } = 0;

    /// <summary>读取起始 D 寄存器编号。</summary>
    public int StartRegister { get; set; } = 200;

    /// <summary>连续读取的寄存器个数。</summary>
    public int Count { get; set; } = 31;

    /// <summary>标记“测试完成、可以保存”的寄存器编号；由 0 变 1 时触发保存。</summary>
    public int TriggerRegister { get; set; } = 230;

    /// <summary>保存成功后是否把触发寄存器写回 0。</summary>
    public bool ResetTriggerAfterSave { get; set; } = false;

    public List<RegisterDefinition> Registers { get; set; } = new();

    /// <summary>输出到 Excel 的寄存器编号及顺序（保存时间列固定为第一列）。</summary>
    public List<int> ExcelColumns { get; set; } = new();

    public RegisterDefinition? Find(int address) => Registers.FirstOrDefault(r => r.Address == address);

    /// <summary>D 寄存器编号 -&gt; 快照数组下标。</summary>
    public int IndexOf(int address) => address - StartRegister;

    public bool Contains(int address) => address >= StartRegister && address < StartRegister + Count;
}
