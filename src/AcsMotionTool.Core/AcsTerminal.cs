namespace AcsMotionTool.Core;

/// <summary>
/// ACSPL+ 命令终端:直接向控制器发送命令/查询并取回应答文本。
/// 常用查询示例:
///   ?VER            控制器版本
///   ?MST(0)         轴 0 的电机状态字
///   ?FPOS(0)        轴 0 反馈位置
///   ?RPOS(0)        轴 0 命令位置
///   ?APOS(0)        轴 0 实际(编码器)位置
///   ?VEL(0)         轴 0 速度
///   ?IN(0)          端口 0 的数字输入
///   ?OUT(0)         端口 0 的数字输出
///   ?ERROR(0)       轴 0 错误码
/// </summary>
public sealed class AcsTerminal
{
    private const string LogSource = "Terminal";
    private readonly AcsController _owner;

    internal AcsTerminal(AcsController owner) => _owner = owner;

    /// <summary>发送查询命令并返回应答(失败时返回错误描述文本)</summary>
    public string Transaction(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        string response;
        try
        {
            response = _owner.ExecuteOrThrow(api => api.Transaction(command), $"命令 {command}", LogSource);
        }
        catch (AcsOperationException ex)
        {
            return "错误: " + ex.Message;
        }

        _owner.LogInfo($"> {command}  响应: {Clean(response)}", LogSource);
        return response;
    }

    /// <summary>发送原始命令并返回原始应答(不做任何加工)</summary>
    public string RawTransaction(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        try
        {
            return _owner.ExecuteOrThrow(api => api.RawTransaction(command), $"原始命令 {command}", LogSource);
        }
        catch (AcsOperationException ex)
        {
            return "错误: " + ex.Message;
        }
    }

    /// <summary>发送命令(不等待应答)，适合下发动作类命令</summary>
    public bool Send(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var ok = _owner.Execute(api => api.Command(command), $"下发命令 {command}", LogSource);
        if (ok) _owner.LogInfo($">> {command}", LogSource);
        return ok;
    }

    /// <summary>读取控制器主动上报的消息</summary>
    public string Receive()
        => _owner.Execute(api => api.Receive(), "读取上报消息", string.Empty, LogSource);

    private static string Clean(string text)
        => text.Replace("\r", " ").Replace("\n", " ").Trim();

    /// <summary>执行多行命令(按行拆分依次下发，返回每行的应答)</summary>
    public IReadOnlyList<(string Command, string Response)> ExecuteLines(string script)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(script)) return result;

        foreach (var raw in script.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("//") || line.StartsWith("#")) continue;   // 支持注释行
            result.Add((line, Transaction(line)));
        }
        return result;
    }
}
