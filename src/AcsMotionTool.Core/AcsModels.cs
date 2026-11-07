namespace AcsMotionTool.Core;

/// <summary>
/// 单轴状态快照(一次读取的结果)
/// </summary>
public sealed class AcsAxisStatus
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>编码器(反馈)位置</summary>
    public double EncoderPosition { get; init; }

    /// <summary>命令(参考)位置</summary>
    public double CommandPosition { get; init; }

    /// <summary>目标位置</summary>
    public double TargetPosition { get; init; }

    /// <summary>速度(单位/秒)</summary>
    public double Velocity { get; init; }

    /// <summary>加速度参数</summary>
    public double Acceleration { get; init; }

    /// <summary>减速度参数</summary>
    public double Deceleration { get; init; }

    /// <summary>加加速度(Jerk)参数</summary>
    public double Jerk { get; init; }

    /// <summary>急停减速度参数</summary>
    public double KillDeceleration { get; init; }

    /// <summary>伺服使能</summary>
    public bool IsEnabled { get; init; }

    /// <summary>定位到位</summary>
    public bool IsInPosition { get; init; }

    /// <summary>正在运动</summary>
    public bool IsMoving { get; init; }

    /// <summary>正在加速</summary>
    public bool IsAccelerating { get; init; }

    /// <summary>负限位</summary>
    public bool NegativeLimit { get; init; }

    /// <summary>正限位</summary>
    public bool PositiveLimit { get; init; }

    /// <summary>驱动器报警(DRIVE 安全输入)</summary>
    public bool DriveAlarm { get; init; }

    /// <summary>是否有故障(驱动器报警或 5009 及以上的运动故障码；5000~5008 是正常终止，不算故障)</summary>
    public bool HasFault { get; init; }

    /// <summary>运动终止码/故障码(MERR)</summary>
    public int MotionError { get; init; }

    /// <summary>运动码说明(自动区分“正常终止”与“故障”)</summary>
    public string MotionErrorText => AcsMotionErrors.DescribeWithCode(MotionError);

    /// <summary>运动码是否为真正的故障(5009 及以上)</summary>
    public bool IsMotionFault => AcsMotionErrors.IsFault(MotionError);

    /// <summary>原始安全输入标志</summary>
    public uint SafetyFlags { get; init; }

    /// <summary>原始轴状态标志</summary>
    public int AxisStateFlags { get; init; }

    /// <summary>本次读取是否成功</summary>
    public bool IsValid { get; init; } = true;

    /// <summary>读取失败时的错误信息</summary>
    public string? ErrorMessage { get; init; }

    public string PositionText => $"{EncoderPosition:F4}";

    /// <summary>
    /// 与另一份状态比较，判断界面是否需要刷新。
    /// 轮询时若数值没变就不触发属性变更，长时间运行时能明显减少 UI 负担。
    /// </summary>
    public bool HasSameValues(AcsAxisStatus other)
        => IsValid == other.IsValid
           && Math.Abs(EncoderPosition - other.EncoderPosition) < 1e-9
           && Math.Abs(CommandPosition - other.CommandPosition) < 1e-9
           && Math.Abs(TargetPosition - other.TargetPosition) < 1e-9
           && Math.Abs(Velocity - other.Velocity) < 1e-9
           && IsEnabled == other.IsEnabled
           && IsInPosition == other.IsInPosition
           && IsMoving == other.IsMoving
           && IsAccelerating == other.IsAccelerating
           && NegativeLimit == other.NegativeLimit
           && PositiveLimit == other.PositiveLimit
           && DriveAlarm == other.DriveAlarm
           && HasFault == other.HasFault
           && MotionError == other.MotionError;

    /// <summary>构造一个表示读取失败的状态对象</summary>
    public static AcsAxisStatus Invalid(int index, string name, string? error) => new()
    {
        Index = index,
        Name = name,
        IsValid = false,
        ErrorMessage = error,
    };

    public override string ToString() =>
        $"Axis{Index} {Name} pos={EncoderPosition:F4} cmd={CommandPosition:F4} vel={Velocity:F4} " +
        $"en={(IsEnabled ? 1 : 0)} inpos={(IsInPosition ? 1 : 0)} move={(IsMoving ? 1 : 0)}";
}

/// <summary>
/// 轴运动参数
/// </summary>
public sealed class AcsAxisParameters
{
    public double Velocity { get; set; } = 10;
    public double Acceleration { get; set; } = 100;
    public double Deceleration { get; set; } = 100;
    public double Jerk { get; set; } = 1000;
    public double KillDeceleration { get; set; } = 1000;

    public override string ToString() =>
        $"vel={Velocity}, acc={Acceleration}, dec={Deceleration}, jerk={Jerk}, kdec={KillDeceleration}";
}

/// <summary>
/// 控制器信息
/// </summary>
public sealed class AcsControllerInfo
{
    public bool IsSimulator { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? SerialNumber { get; init; }
    public string? LibraryVersion { get; init; }
    public string? NetLibraryVersion { get; init; }
    public string? UmdVersion { get; init; }
    public double AxisCount { get; init; }
    public double BufferCount { get; init; }

    public string ToDisplayString() =>
        $"固件:{FirmwareVersion ?? "?"} 序列号:{SerialNumber ?? "?"} 轴数:{AxisCount:F0} 缓冲区:{BufferCount:F0} " +
        $"{Environment.NewLine}上位机库:{NetLibraryVersion ?? "?"} C库:{LibraryVersion ?? "?"} UMD:{UmdVersion ?? "?"}";
}
