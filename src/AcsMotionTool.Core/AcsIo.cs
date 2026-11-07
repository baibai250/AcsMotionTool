using ACS.SPiiPlusNET;

namespace AcsMotionTool.Core;

/// <summary>
/// 数字量/模拟量 IO 访问。
/// 说明:端口号(port)对应控制器上的 IO 端口，位号(bit)为端口内的位。
/// 通用输出使用 SetOutput / GetOutput;扩展输出使用 SetExtOutput / GetExtInput。
/// </summary>
public sealed class AcsIo
{
    private const string LogSource = "IO";
    private readonly AcsController _owner;

    internal AcsIo(AcsController owner) => _owner = owner;

    #region 输入

    /// <summary>读取单个数字输入位(0/1，读取失败返回 -1)</summary>
    public int ReadInputBit(int port, int bit, bool quiet = false)
        => quiet
            ? _owner.ExecuteQuiet(api => api.GetInput(port, bit), -1)
            : _owner.Execute(api => api.GetInput(port, bit), $"读取输入 {port}.{bit}", -1, LogSource);

    /// <summary>读取整个数字输入端口(按位组合的整数)</summary>
    public int ReadInputPort(int port, bool quiet = false)
        => quiet
            ? _owner.ExecuteQuiet(api => api.GetInputPort(port), 0)
            : _owner.Execute(api => api.GetInputPort(port), $"读取输入端口 {port}", 0, LogSource);

    /// <summary>读取扩展数字输入位</summary>
    public int ReadExtInputBit(int port, int bit)
        => _owner.Execute(api => api.GetExtInput(port, bit), $"读取扩展输入 {port}.{bit}", -1, LogSource);

    /// <summary>读取扩展数字输入端口</summary>
    public int ReadExtInputPort(int port)
        => _owner.Execute(api => api.GetExtInputPort(port), $"读取扩展输入端口 {port}", 0, LogSource);

    /// <summary>批量读取一个端口的若干输入位</summary>
    public bool[] ReadInputBits(int port, int count = 16)
    {
        var result = new bool[count];
        var value = ReadInputPort(port);
        for (var i = 0; i < count; i++) result[i] = ((value >> i) & 0x01) != 0;
        return result;
    }

    #endregion

    #region 输出

    /// <summary>读取单个数字输出位</summary>
    public int ReadOutputBit(int port, int bit, bool quiet = false)
        => quiet
            ? _owner.ExecuteQuiet(api => api.GetOutput(port, bit), -1)
            : _owner.Execute(api => api.GetOutput(port, bit), $"读取输出 {port}.{bit}", -1, LogSource);

    /// <summary>读取整个数字输出端口</summary>
    public int ReadOutputPort(int port, bool quiet = false)
        => quiet
            ? _owner.ExecuteQuiet(api => api.GetOutputPort(port), 0)
            : _owner.Execute(api => api.GetOutputPort(port), $"读取输出端口 {port}", 0, LogSource);

    /// <summary>设置单个数字输出位</summary>
    public bool SetOutputBit(int port, int bit, bool value)
    {
        var ok = _owner.Execute(api => api.SetOutput(port, bit, value ? 1 : 0), $"设置输出 {port}.{bit}={value}", LogSource);
        if (ok) _owner.LogInfo($"输出 {port}.{bit} = {(value ? 1 : 0)}", LogSource);
        return ok;
    }

    /// <summary>设置整个数字输出端口</summary>
    public bool SetOutputPort(int port, int value)
    {
        var ok = _owner.Execute(api => api.SetOutputPort(port, value), $"设置输出端口 {port}=0x{value:X}", LogSource);
        if (ok) _owner.LogInfo($"输出端口 {port} = 0x{value:X} ({value})", LogSource);
        return ok;
    }

    /// <summary>读取扩展数字输出位</summary>
    public int ReadExtOutputPort(int port)
        => _owner.Execute(api => api.GetExtOutputPort(port), $"读取扩展输出端口 {port}", 0, LogSource);

    /// <summary>设置扩展数字输出位</summary>
    public bool SetExtOutputBit(int port, int bit, bool value)
        => _owner.Execute(api => api.SetExtOutput(port, bit, value ? 1 : 0), $"设置扩展输出 {port}.{bit}", LogSource);

    /// <summary>设置整个扩展数字输出端口</summary>
    public bool SetExtOutputPort(int port, int value)
        => _owner.Execute(api => api.SetExtOutputPort(port, value), $"设置扩展输出端口 {port}", LogSource);

    #endregion

    #region 模拟量

    /// <summary>读取模拟量输入(整数原始值)</summary>
    public int ReadAnalogInput(int port)
        => _owner.Execute(api => api.GetAnalogInput(port), $"读取模拟输入 {port}", 0, LogSource);

    /// <summary>读取模拟量输入(浮点)</summary>
    public double ReadAnalogInputNT(int port)
        => _owner.Execute(api => api.GetAnalogInputNT(port), $"读取模拟输入 {port}", 0.0, LogSource);

    /// <summary>设置模拟量输出(整数原始值)</summary>
    public bool SetAnalogOutput(int port, int value)
        => _owner.Execute(api => api.SetAnalogOutput(port, value), $"设置模拟输出 {port}", LogSource);

    /// <summary>设置模拟量输出(浮点)</summary>
    public bool SetAnalogOutputNT(int port, double value)
        => _owner.Execute(api => api.SetAnalogOutputNT(port, value), $"设置模拟输出 {port}", LogSource);

    #endregion
}
