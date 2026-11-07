namespace AcsMotionTool.Core;

/// <summary>
/// controller 程序(ACSPL+)文本工具。
/// 注意:控制器只接受 ASCII 文本，中文注释会导致 "Unrecognized command"，
/// 因此本类生成/清洗的程序一律只保留 ASCII 可打印字符 + 换行。
/// </summary>
public static class AcsProgramText
{
    /// <summary>
    /// 生成单轴回零程序(使用 ACSPL+ 的 HOME 命令)。
    /// 例:HOME 0, 17, 100, 5000
    ///   参数依次为: 轴号, 回零方式, 回零速度, 最大距离[, 原点偏移]
    /// 常用回零方式: 1=负限位+索引脉冲, 2=正限位+索引脉冲, 17=负限位开关, 18=正限位开关,
    ///               33/34=索引脉冲, 35/36=当前位置为零 …
    /// </summary>
    public static string HomeCommand(int axisIndex, int method = 17, double velocity = 100,
        double maxDistance = 100000, double? offset = null, bool addStop = true)
    {
        var line = $"HOME {axisIndex}, {method}, {Num(velocity)}, {Num(maxDistance)}";
        if (offset.HasValue) line += $", {Num(offset.Value)}";

        var text = line + "\r\n";
        if (addStop) text += "STOP\r\n";
        return text;
    }

    /// <summary>
    /// 生成“软件置零”程序(使用 ACSPL+ 的 SET 命令;FPOS 是只读变量，不能直接赋值)。
    /// SET RPOS 会同时更新 FPOS 与 APOS。
    /// </summary>
    public static string SoftZeroProgram(int axisIndex, double position = 0, bool addStop = true)
    {
        var text = $"SET RPOS({axisIndex})={Num(position)}\r\n";
        if (addStop) text += "STOP\r\n";
        return text;
    }

    /// <summary>
    /// 清洗为控制器可接受的文本:
    ///   1) 去掉非 ASCII 字符(控制器只接受 ASCII，中文注释会导致 Unrecognized command)
    ///   2) 把 C 风格注释(// 或 #)转换成 ACSPL+ 的注释符 "!"(ACSPL+ 用感叹号注释)
    ///   3) 统一换行、去掉空行
    /// 返回清洗后的文本与被改动的字符数。
    /// </summary>
    public static string Sanitize(string? text, out int removedChars)
    {
        removedChars = 0;
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var normalized = text!.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = new List<string>();

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = new string(rawLine.Where(c => c >= 32 && c <= 126 || c == '\t').ToArray());
            removedChars += rawLine.Length - line.Length;

            line = ConvertComment(line.Trim(), ref removedChars);
            if (line.Length == 0) continue;
            lines.Add(line);
        }

        return lines.Count == 0 ? string.Empty : string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>把 // 或 # 形式的注释转换成 ACSPL+ 的 "!" 注释</summary>
    private static string ConvertComment(string line, ref int changed)
    {
        if (line.Length == 0) return line;

        // 整行注释
        if (line.StartsWith("//", StringComparison.Ordinal))
        {
            changed++;
            return "!" + line[2..];
        }
        if (line.StartsWith("#", StringComparison.Ordinal))
        {
            changed++;
            return "!" + line[1..];
        }

        // 行内注释(简单处理:第一处 // 之后都当作注释)
        var index = line.IndexOf("//", StringComparison.Ordinal);
        if (index >= 0)
        {
            changed++;
            return (line[..index] + "!" + line[(index + 2)..]).TrimEnd();
        }

        return line;
    }

    private static string Num(double value)
        => value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
}
