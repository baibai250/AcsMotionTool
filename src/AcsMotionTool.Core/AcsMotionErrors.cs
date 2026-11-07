namespace AcsMotionTool.Core;

/// <summary>
/// ACS 运动终止码(MERR / GetMotionError)的判定与解释。
///
/// 重要:按 ACS 官方文档(Motion Termination Error),
///   5000 ~ 5008 属于**正常运动终止**，不是故障，例如
///     5000 运动未完成 / 5001 运动正常结束 / 5002 被 KILL / 5003 被 HALT / 5004 被下使能 ...
///   5009 及以上才是真正的故障(限位、驱动器、编码器、超温、STO 等)。
/// 因此判断“轴是否报警”必须用 <see cref="IsFault"/>，不能简单用“错误码 != 0”。
/// </summary>
public static class AcsMotionErrors
{
    /// <summary>故障码起始值(5009 及以上为故障)</summary>
    public const int FirstFaultCode = 5009;

    /// <summary>是否为真正的故障码(需要在界面上报警)</summary>
    public static bool IsFault(int code) => code >= FirstFaultCode;

    /// <summary>是否为正常的运动终止码(5000~5008)</summary>
    public static bool IsNormalTermination(int code) => code is >= 5000 and < FirstFaultCode;

    /// <summary>终止码/故障码的中文说明</summary>
    public static string Describe(int code) => code switch
    {
        0 => "无",
        // ---- 5000~5008 正常终止 ----
        5000 => "运动未完成(尚未到达终点就被终止)",
        5001 => "运动正常结束(到达终点)",
        5002 => "运动被用户 KILL 急停",
        5003 => "运动被用户 HALT 停止",
        5004 => "电机被用户下使能(DISABLE)",
        5005 => "因电机被下使能而终止运动",
        5006 => "运动被 KILL 终止(未到达终点)",
        5007 => "因其它轴下使能而联动下使能",
        5008 => "因其它轴 KILL 而联动终止运动",
        // ---- 5009 及以上 故障 ----
        5009 => "通用运动失败",
        5010 => "硬件右限位",
        5011 => "硬件左限位",
        5012 => "网络错误(EtherCAT 通讯)",
        5014 => "电机过热",
        5015 => "软件右限位",
        5016 => "软件左限位",
        5017 => "编码器 1 未连接",
        5018 => "编码器 2 未连接",
        5019 => "驱动器报警",
        5020 => "编码器 1 错误",
        5021 => "编码器 2 错误",
        5022 => "位置误差(非致命)",
        5023 => "位置误差(致命)",
        5024 => "速度超限",
        5025 => "加速度超限",
        5026 => "驱动器/电机过流",
        5027 => "伺服处理器报警",
        5028 => "STO 安全转矩关断未连接",
        5030 => "HSSI 未连接",
        5031 => "断线",
        5032 => "存在故障时仍尝试运动",
        5033 => "向被禁止的方向运动(限位/软限位为 ON)",
        5034 => "MPU 过热",
        5035 => "程序错误",
        5036 => "内存溢出",
        5037 => "MPU 时间占用过高",
        5038 => "硬件急停",
        5039 => "伺服中断",
        5041 => "组件故障(电源/母线电压)",
        5042 => "霍尔传感器错误",
        5043 => "外部轨迹错误导致运动终止",
        5044 => "外部网络错误",
        5045 => "SYNC 丢失导致下使能",
        5046 => "GPRT 溢出",
        5047 => "轴处于 HOLD 状态",
        5048 => "电机过流",
        5049 => "驱动器过流",
        5050 => "STO 错误",
        5051 => "STO_1 24V 断开",
        5052 => "STO_2 24V 断开",
        5053 => "STO_1 与 STO_2 24V 均断开",
        5054 => "SS1_1 5V 断开",
        5055 => "SS1_2 5V 断开",
        5056 => "SS1_1 与 SS1_2 5V 均断开",
        5057 => "SS1 时序错误",
        5060 => "驱动器报警(无故障)",
        _ => code < FirstFaultCode ? "未知的正常终止码" : "未知故障码",
    };

    /// <summary>带码值的说明，例如 "5003 运动被用户 HALT 停止"</summary>
    public static string DescribeWithCode(int code) => code == 0 ? "无" : $"{code} {Describe(code)}";

    /// <summary>用于界面显示的简短文本(正常终止码会标注“正常”)</summary>
    public static string DescribeForDisplay(int code)
    {
        if (code == 0) return string.Empty;
        var text = DescribeWithCode(code);
        return IsFault(code) ? $"故障: {text}" : $"正常终止: {text}";
    }
}
