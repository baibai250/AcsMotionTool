using System.Collections;
using ACS.SPiiPlusNET;

namespace AcsMotionTool.Core;

/// <summary>
/// ACSPL+ 变量读写。
/// 变量名支持以下写法(与 ACS 终端一致的直观写法):
///   FPOS            普通变量
///   MST(0)          带一个下标的变量(轴号 / 端口号)
///   IN(0) / OUT(0)  数字 IO 端口
///   ?FPOS(0)        带前导 ? 也可以(会被自动去掉)
/// buffer 为程序缓冲区号(-1 表示全局 ACSC_NONE)。
/// </summary>
public sealed class AcsVariables
{
    private const string LogSource = "Variable";
    private readonly AcsController _owner;

    internal AcsVariables(AcsController owner) => _owner = owner;

    private static ProgramBuffer ToBuffer(int buffer) => (ProgramBuffer)buffer;

    /// <summary>读取变量(自动返回 double / int / 数组；读取失败返回 null)</summary>
    /// <param name="quiet">true 时不写错误日志(用于周期性轮询)</param>
    public object? ReadVariable(string expression, int buffer = -1, bool quiet = false)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        var (name, i1, i2) = Parse(expression);

        object? Read(Api api)
        {
            object? raw = i1 == null
                ? api.ReadVariable(name, ToBuffer(buffer))
                : i2 == null
                    ? api.ReadVariable(name, ToBuffer(buffer), i1.Value, i1.Value)
                    : api.ReadVariable(name, ToBuffer(buffer), i1.Value, i1.Value, i2.Value, i2.Value);
            return Normalize(raw);
        }

        return quiet
            ? _owner.ExecuteQuiet(Read, null)
            : _owner.Execute(Read, $"读取变量 {expression}", null, LogSource);
    }

    /// <summary>读取变量的文本形式</summary>
    public string ReadAsText(string expression, int buffer = -1, bool quiet = false)
        => Format(ReadVariable(expression, buffer, quiet));

    /// <summary>按实数读取</summary>
    public double ReadReal(string expression, int buffer = -1, bool quiet = false)
        => ToDouble(ReadVariable(expression, buffer, quiet));

    /// <summary>按整数读取</summary>
    public int ReadInteger(string expression, int buffer = -1, bool quiet = false)
        => (int)ToDouble(ReadVariable(expression, buffer, quiet));

    /// <summary>读取一维实数数组</summary>
    public double[] ReadRealVector(string name, int from, int to, int buffer = -1, bool quiet = false)
    {
        var value = _owner.ExecuteQuietOrLog(api => api.ReadVariable(name, ToBuffer(buffer), from, to),
            $"读取实数数组 {name}[{from}..{to}]", quiet);
        return ToDoubleArray(value);
    }

    /// <summary>读取一维整数数组</summary>
    public int[] ReadIntegerVector(string name, int from, int to, int buffer = -1, bool quiet = false)
        => ToDoubleArray(_owner.ExecuteQuietOrLog(api => api.ReadVariable(name, ToBuffer(buffer), from, to),
            $"读取整数数组 {name}[{from}..{to}]", quiet)).Select(d => (int)d).ToArray();

    /// <summary>写入变量(自动按 double / int / 数组分派)</summary>
    public bool Write(string expression, object value, int buffer = -1)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        var (name, i1, i2) = Parse(expression);
        var length = (value as Array)?.Length ?? 0;

        var ok = _owner.Execute(api =>
        {
            if (length > 0 && i1 != null && i2 == null)
            {
                // 数组写入:把给出的下标当作起始下标，自动推导范围，例如 Write("TestArr(0)", new[]{1.0,2.0,3.0})
                api.WriteVariable(value, name, ToBuffer(buffer), i1.Value, i1.Value + length - 1);
            }
            else if (length > 0 && i1 != null && i2 != null)
            {
                // 二维数组写入:按行写入，起始行为 i1
                api.WriteVariable(value, name, ToBuffer(buffer), i1.Value, i1.Value + length - 1, i2.Value, i2.Value);
            }
            else if (i1 == null)
            {
                api.WriteVariable(value, name, ToBuffer(buffer));
            }
            else if (i2 == null)
            {
                api.WriteVariable(value, name, ToBuffer(buffer), i1.Value, i1.Value);
            }
            else
            {
                api.WriteVariable(value, name, ToBuffer(buffer), i1.Value, i1.Value, i2.Value, i2.Value);
            }
        }, $"写入变量 {expression}={Format(value)}", LogSource);

        if (ok) _owner.LogInfo($"{name} <- {Format(value)}", LogSource);
        return ok;
    }

    /// <summary>写入实数变量</summary>
    public bool WriteReal(string expression, double value, int buffer = -1) => Write(expression, value, buffer);

    /// <summary>写入整数变量</summary>
    public bool WriteInteger(string expression, int value, int buffer = -1) => Write(expression, value, buffer);

    /// <summary>把文本自动解析为 double / int / 数组后写入</summary>
    public bool WriteFromText(string expression, string text, int buffer = -1)
    {
        if (!TryParseValue(text, out var value))
        {
            _owner.LogWarn($"无法解析写入值: {text}", LogSource);
            return false;
        }
        return Write(expression, value!, buffer);
    }

    #region 变量声明

    /// <summary>
    /// 声明持久化全局变量。数组需要在名称中带上维数，例如:
    ///   "MyVar"           标量
    ///   "MyArr(10)"       一维数组
    ///   "MyMatrix(3)(4)"  二维数组
    /// </summary>
    public bool Declare(string name, bool integer = false)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var type = integer ? AcsplVariableType.ACSC_INT_TYPE : AcsplVariableType.ACSC_REAL_TYPE;
        var ok = _owner.Execute(api => api.DeclareVariable(type, name), $"声明变量 {name}", LogSource);
        if (ok) _owner.LogInfo($"已声明{(integer ? "整数" : "实数")}变量 {name}", LogSource);
        return ok;
    }

    /// <summary>声明实数变量(数组在名称里带维数，如 "MyArr(10)")</summary>
    public bool DeclareReal(string name) => Declare(name, integer: false);

    /// <summary>声明整数变量(数组在名称里带维数，如 "MyArr(10)")</summary>
    public bool DeclareInteger(string name) => Declare(name, integer: true);

    /// <summary>清除全部持久化变量</summary>
    public bool ClearAllVariables()
        => _owner.Execute(api => api.ClearVariables(), "清除全部变量", LogSource);

    #endregion

    #region 解析与格式化

    /// <summary>解析 "?MST(0)" / "IN(0)" / "FPOS" / "ARR(1,2)" 形式的变量表达式</summary>
    public static (string Name, int? Index1, int? Index2) Parse(string expression)
    {
        var text = (expression ?? string.Empty).Trim();
        if (text.StartsWith('?')) text = text[1..].Trim();

        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open <= 0 || close < open) return (text, null, null);

        var name = text[..open].Trim();
        var parts = text[(open + 1)..close]
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        int? i1 = null;
        int? i2 = null;
        if (parts.Length >= 1 && int.TryParse(parts[0], out var a)) i1 = a;
        if (parts.Length >= 2 && int.TryParse(parts[1], out var b)) i2 = b;
        return (name, i1, i2);
    }

    /// <summary>解析 "1.5" / "10" / "1,2,3" 形式的写入文本</summary>
    public static bool TryParseValue(string text, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        if (text.Contains(',') || text.Contains(';'))
        {
            var parts = text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .Where(p => p.Length > 0)
                            .ToArray();

            if (parts.Length > 0 && parts.All(p => int.TryParse(p, out _)))
            {
                value = parts.Select(int.Parse).ToArray();
                return true;
            }
            if (parts.Length > 0 && parts.All(p => double.TryParse(p, out _)))
            {
                value = parts.Select(double.Parse).ToArray();
                return true;
            }
            return false;
        }

        if (int.TryParse(text, out var i)) { value = i; return true; }
        if (double.TryParse(text, out var d)) { value = d; return true; }
        return false;
    }

    /// <summary>单元素数组解包</summary>
    private static object? Normalize(object? raw)
    {
        if (raw is Array array && array.Length == 1) return array.GetValue(0);
        return raw;
    }

    private static double ToDouble(object? value) => value switch
    {
        null => 0d,
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        short s => s,
        byte b => b,
        string s => double.TryParse(s, out var parsed) ? parsed : 0d,
        _ => TryConvert(value),
    };

    private static double TryConvert(object value)
    {
        try { return Convert.ToDouble(value); } catch { return 0d; }
    }

    private static double[] ToDoubleArray(object? value)
    {
        switch (value)
        {
            case null:
                return Array.Empty<double>();
            case double[] doubles:
                return doubles;
            case int[] ints:
                return ints.Select(i => (double)i).ToArray();
            case Array array:
                var result = new double[array.Length];
                for (var i = 0; i < array.Length; i++) result[i] = TryConvert(array.GetValue(i) ?? 0d);
                return result;
            default:
                return new[] { ToDouble(value) };
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        double d => d.ToString("G"),
        float f => f.ToString("G"),
        double[] doubles => "[" + string.Join(", ", doubles.Select(d => d.ToString("G"))) + "]",
        int[] ints => "[" + string.Join(", ", ints) + "]",
        string s => s.Trim(),
        IEnumerable enumerable => "[" + string.Join(", ", enumerable.Cast<object>()) + "]",
        _ => value.ToString() ?? string.Empty,
    };

    #endregion
}
