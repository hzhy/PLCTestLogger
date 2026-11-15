using System.Globalization;
using System.Text.Json;

namespace PlcTestLogger.Core.Config;

/// <summary>
/// 选项名称覆盖（config/register-options.json）：只改 D200、D202、D203、D204、D209 的 options 显示文字，
/// 其余定义仍用内置。按选项值逐项合并：文件里写了的值替换（或新增）显示文字，没写的值保持内置名称。
/// 文件格式：{ "D200": { "1": "J0321干式", ... }, "D204": { ... } }；以 "_" 开头的键（如 _comment）忽略。
/// </summary>
public static class RegisterOptionsOverride
{
    /// <summary>允许覆盖选项名称的寄存器。</summary>
    public static readonly int[] AllowedAddresses = { 200, 202, 203, 204, 209 };

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>地址 → (选项值 → 显示文字)。</summary>
    public static Dictionary<int, Dictionary<string, string>> Load(string path) => Parse(File.ReadAllText(path), path);

    public static Dictionary<int, Dictionary<string, string>> Parse(string json, string source)
    {
        using var doc = JsonDocument.Parse(json, DocOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{source}: 根节点必须是 {{ }} 对象");

        var allowed = string.Join("、", AllowedAddresses.Select(a => "D" + a));
        var result = new Dictionary<int, Dictionary<string, string>>();
        foreach (var reg in doc.RootElement.EnumerateObject())
        {
            if (reg.Name.StartsWith('_')) continue;
            var k = reg.Name.Trim();
            if (k.StartsWith("D", StringComparison.OrdinalIgnoreCase)) k = k[1..];
            if (!int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out var address))
                throw new InvalidDataException($"{source}: 寄存器 \"{reg.Name}\" 写法不正确，应写成 D200 或 200");
            if (!AllowedAddresses.Contains(address))
                throw new InvalidDataException($"{source}: D{address} 不能覆盖选项名称，只支持 {allowed}");
            if (result.ContainsKey(address))
                throw new InvalidDataException($"{source}: D{address} 重复出现");
            if (reg.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{source}: D{address} 的值必须是 {{ \"选项值\": \"显示文字\" }} 对象");

            var options = new Dictionary<string, string>();
            foreach (var opt in reg.Value.EnumerateObject())
            {
                if (opt.Name.StartsWith('_')) continue;
                if (!long.TryParse(opt.Name.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                    throw new InvalidDataException($"{source}: D{address} 的选项值 \"{opt.Name}\" 必须是整数");
                if (opt.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(opt.Value.GetString()))
                    throw new InvalidDataException($"{source}: D{address} 选项 {value} 的显示文字必须是非空字符串");
                var key = value.ToString(CultureInfo.InvariantCulture);
                if (options.ContainsKey(key))
                    throw new InvalidDataException($"{source}: D{address} 的选项值 {key} 重复");
                options[key] = opt.Value.GetString()!.Trim();
            }
            result[address] = options;
        }
        return result;
    }

    /// <summary>把覆盖合并到寄存器定义中（原地修改），返回实际改动的选项个数。</summary>
    public static int Apply(RegisterConfig cfg, IReadOnlyDictionary<int, Dictionary<string, string>> overrides, string source)
    {
        var changed = 0;
        foreach (var (address, options) in overrides)
        {
            var def = cfg.Find(address);
            if (def is null || !def.IsEnum)
                throw new InvalidDataException($"{source}: 内置定义中 D{address} 不是选项类型（enum），不能覆盖选项名称");
            def.Options ??= new Dictionary<string, string>();
            foreach (var (key, label) in options)
            {
                if (def.Options.TryGetValue(key, out var old) && old == label) continue;
                def.Options[key] = label;
                changed++;
            }
        }
        return changed;
    }
}
