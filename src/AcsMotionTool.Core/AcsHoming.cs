namespace AcsMotionTool.Core;

/// <summary>回零方式</summary>
public enum AcsHomingMode
{
    /// <summary>运行控制器里已有的回零程序(现场常用:回零程序预先下载在某个缓冲区)</summary>
    RunBufferProgram = 0,

    /// <summary>按参数生成 HOME 指令程序，写入缓冲区后运行</summary>
    GenerateAndRun = 1,

    /// <summary>软件置零(不跑程序，直接把当前位置设为 0；模拟器/调试用)</summary>
    SoftZero = 2,
}

/// <summary>回零参数</summary>
public sealed class AcsHomingOptions
{
    /// <summary>回零方式</summary>
    public AcsHomingMode Mode { get; set; } = AcsHomingMode.RunBufferProgram;

    /// <summary>程序缓冲区号(RunBufferProgram / GenerateAndRun 使用)</summary>
    public int BufferIndex { get; set; }

    /// <summary>回零方式号(HOME 命令的 HomingMethod)，默认 17 = 负限位开关</summary>
    public int Method { get; set; } = 17;

    /// <summary>回零速度</summary>
    public double Velocity { get; set; } = 100;

    /// <summary>最大搜索距离(防止无限找原点)</summary>
    public double MaxDistance { get; set; } = 100000;

    /// <summary>原点偏移(可选；回零完成后零点相对原点的偏移)</summary>
    public double? Offset { get; set; }

    /// <summary>软件置零时设定的位置(默认 0)</summary>
    public double SoftZeroPosition { get; set; }

    public override string ToString() => Mode switch
    {
        AcsHomingMode.RunBufferProgram => $"运行回零程序(缓冲区 {BufferIndex})",
        AcsHomingMode.GenerateAndRun => $"生成 HOME 指令并运行(缓冲区 {BufferIndex}, 方式 {Method}, 速度 {Velocity}, 最大距离 {MaxDistance})",
        AcsHomingMode.SoftZero => $"软件置零(位置 = {SoftZeroPosition})",
        _ => Mode.ToString(),
    };
}
