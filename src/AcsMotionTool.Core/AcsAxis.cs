using ACS.SPiiPlusNET;

namespace AcsMotionTool.Core;

/// <summary>
/// 单轴控制对象。所有方法内部都通过 <see cref="AcsController"/> 串行访问 ACS 通讯链路。
/// </summary>
public sealed class AcsAxis
{
    private readonly AcsController _owner;

    internal AcsAxis(AcsController owner, int index)
    {
        _owner = owner;
        Index = index;
        Name = $"Axis{index}";
    }

    /// <summary>轴号</summary>
    public int Index { get; }

    /// <summary>轴名称(仅用于显示/日志)</summary>
    public string Name { get; set; }

    /// <summary>软限位下限(为 null 表示不检查)</summary>
    public double? SoftLimitMin { get; set; }

    /// <summary>软限位上限(为 null 表示不检查)</summary>
    public double? SoftLimitMax { get; set; }

    private Axis AcsAxisEnum => (Axis)Index;
    private string NameOrIndex => $"{Name}(轴{Index})";

    #region 状态读取

    /// <summary>读取轴状态快照</summary>
    /// <param name="readParameters">是否同时读取速度/加速度等参数(较慢，用于参数页)</param>
    /// <param name="quiet">true 时不写错误日志(用于周期性轮询)</param>
    public AcsAxisStatus ReadStatus(bool readParameters = false, bool quiet = false)
    {
        AcsAxisStatus Read(Api api)
        {
            var motorState = api.GetMotorState(AcsAxisEnum);
            var axisState = api.GetAxisState(AcsAxisEnum);
            var safety = api.GetSafetyInputPort(AcsAxisEnum);
            var motionError = SafeMotionError(api, AcsAxisEnum);

            return new AcsAxisStatus
            {
                Index = Index,
                Name = Name,
                EncoderPosition = SafeDouble(() => api.GetFPosition(AcsAxisEnum)),
                CommandPosition = SafeDouble(() => api.GetRPosition(AcsAxisEnum)),
                TargetPosition = SafeDouble(() => api.GetTargetPosition(AcsAxisEnum)),
                Velocity = readParameters ? SafeDouble(() => api.GetVelocity(AcsAxisEnum)) : 0,
                Acceleration = readParameters ? SafeDouble(() => api.GetAcceleration(AcsAxisEnum)) : 0,
                Deceleration = readParameters ? SafeDouble(() => api.GetDeceleration(AcsAxisEnum)) : 0,
                Jerk = readParameters ? SafeDouble(() => api.GetJerk(AcsAxisEnum)) : 0,
                KillDeceleration = readParameters ? SafeDouble(() => api.GetKillDeceleration(AcsAxisEnum)) : 0,
                IsEnabled = (motorState & MotorStates.ACSC_MST_ENABLE) != 0,
                IsInPosition = (motorState & MotorStates.ACSC_MST_INPOS) != 0,
                IsMoving = (motorState & MotorStates.ACSC_MST_MOVE) != 0 || (axisState & AxisStates.ACSC_AST_MOVE) != 0,
                IsAccelerating = (motorState & MotorStates.ACSC_MST_ACC) != 0,
                NegativeLimit = (safety & SafetyControlMasks.ACSC_SAFETY_LL) != 0,
                PositiveLimit = (safety & SafetyControlMasks.ACSC_SAFETY_RL) != 0,
                DriveAlarm = (safety & SafetyControlMasks.ACSC_SAFETY_DRIVE) != 0,
                SafetyFlags = (uint)safety,
                AxisStateFlags = (int)axisState,
                MotionError = motionError,
                HasFault = (safety & SafetyControlMasks.ACSC_SAFETY_DRIVE) != 0 || AcsMotionErrors.IsFault(motionError),
                IsValid = true,
            };
        }

        return quiet
            ? _owner.ExecuteQuiet(Read, AcsAxisStatus.Invalid(Index, Name, "读取失败"))
            : _owner.Execute(Read, $"读取 {NameOrIndex} 状态", AcsAxisStatus.Invalid(Index, Name, "读取失败"), "Axis");
    }

    private static double SafeDouble(Func<double> f)
    {
        try { return f(); } catch { return 0; }
    }

    private static int SafeMotionError(Api api, Axis axis)
    {
        try { return api.GetMotionError(axis); } catch { return 0; }
    }

    /// <summary>读取速度/加速度等参数</summary>
    public AcsAxisParameters ReadParameters()
        => _owner.Execute(api => new AcsAxisParameters
        {
            Velocity = SafeDouble(() => api.GetVelocity(AcsAxisEnum)),
            Acceleration = SafeDouble(() => api.GetAcceleration(AcsAxisEnum)),
            Deceleration = SafeDouble(() => api.GetDeceleration(AcsAxisEnum)),
            Jerk = SafeDouble(() => api.GetJerk(AcsAxisEnum)),
            KillDeceleration = SafeDouble(() => api.GetKillDeceleration(AcsAxisEnum)),
        }, $"读取 {NameOrIndex} 参数", new AcsAxisParameters(), "Axis");

    #endregion

    #region 使能 / 故障

    /// <summary>伺服使能(可选等待使能完成)</summary>
    public bool Enable(bool wait = true, int timeoutMs = 10000)
    {
        var ok = _owner.TryExecute(api =>
        {
            var state = api.GetMotorState(AcsAxisEnum);
            if ((state & MotorStates.ACSC_MST_ENABLE) == 0) api.Enable(AcsAxisEnum);
            if (wait) api.WaitMotorEnabled(AcsAxisEnum, 1, timeoutMs);
            return true;
        }, $"使能 {NameOrIndex}", "Axis");

        _owner.LogInfo($"{NameOrIndex} 使能{(ok ? "成功" : "失败")}", "Axis");
        return ok;
    }

    /// <summary>断开伺服使能</summary>
    public bool Disable()
    {
        var ok = _owner.Execute(api => api.Disable(AcsAxisEnum), $"下使能 {NameOrIndex}", "Axis");
        _owner.LogInfo($"{NameOrIndex} 下使能{(ok ? "成功" : "失败")}", "Axis");
        return ok;
    }

    /// <summary>清除该轴故障</summary>
    public bool ClearFault()
    {
        var ok = _owner.Execute(api => api.FaultClear(AcsAxisEnum), $"清除 {NameOrIndex} 故障", "Axis");
        _owner.LogInfo($"{NameOrIndex} 清除故障{(ok ? "成功" : "失败")}", "Axis");
        return ok;
    }

    /// <summary>读取运动错误码</summary>
    public int GetMotionError()
        => _owner.Execute(api => SafeMotionError(api, AcsAxisEnum), $"读取 {NameOrIndex} 错误码", 0, "Axis");

    #endregion

    #region 运动

    /// <summary>
    /// 绝对运动(先检查软限位，必要时自动使能)
    /// </summary>
    public bool MoveAbsolute(double position, double? velocity = null, double? acceleration = null,
        double? deceleration = null, bool wait = false, int timeoutMs = 60000)
    {
        if (!CheckSoftLimit(position, "绝对运动")) return false;

        var ok = _owner.TryExecute(api =>
        {
            EnsureEnabled(api, AcsAxisEnum);
            if (acceleration.HasValue) api.SetAcceleration(AcsAxisEnum, acceleration.Value);
            if (deceleration.HasValue) api.SetDeceleration(AcsAxisEnum, deceleration.Value);

            if (velocity.HasValue)
            {
                api.ExtToPoint(MotionFlags.ACSC_NONE, AcsAxisEnum, position, velocity.Value, 0);
            }
            else
            {
                api.ToPoint(MotionFlags.ACSC_NONE, AcsAxisEnum, position);
            }
            return true;
        }, $"{NameOrIndex} 绝对运动到 {position}", "Axis");

        if (ok)
        {
            _owner.LogInfo($"{NameOrIndex} 绝对运动 -> {position}" + (velocity.HasValue ? $" @{velocity.Value}" : ""), "Axis");
            if (wait) ok = WaitMotionEnd(timeoutMs);
        }
        return ok;
    }

    /// <summary>相对运动</summary>
    public bool MoveRelative(double delta, double? velocity = null, double? acceleration = null,
        double? deceleration = null, bool wait = false, int timeoutMs = 60000)
    {
        var current = _owner.Execute(api => SafeDouble(() => api.GetFPosition(AcsAxisEnum)), $"读取 {NameOrIndex} 位置", 0.0, "Axis");
        if (!CheckSoftLimit(current + delta, "相对运动")) return false;

        var ok = _owner.TryExecute(api =>
        {
            EnsureEnabled(api, AcsAxisEnum);
            if (acceleration.HasValue) api.SetAcceleration(AcsAxisEnum, acceleration.Value);
            if (deceleration.HasValue) api.SetDeceleration(AcsAxisEnum, deceleration.Value);

            if (velocity.HasValue)
            {
                api.ExtToPoint(MotionFlags.ACSC_AMF_RELATIVE, AcsAxisEnum, delta, velocity.Value, 0);
            }
            else
            {
                api.ToPoint(MotionFlags.ACSC_AMF_RELATIVE, AcsAxisEnum, delta);
            }
            return true;
        }, $"{NameOrIndex} 相对运动 {delta}", "Axis");

        if (ok)
        {
            _owner.LogInfo($"{NameOrIndex} 相对运动 {delta}", "Axis");
            if (wait) ok = WaitMotionEnd(timeoutMs);
        }
        return ok;
    }

    /// <summary>点动(Jog)：以给定速度连续运动，直到调用 <see cref="Stop"/> 或 <see cref="EmergencyStop"/></summary>
    public bool StartJog(double velocity, double? acceleration = null)
    {
        var ok = _owner.TryExecute(api =>
        {
            EnsureEnabled(api, AcsAxisEnum);
            if (acceleration.HasValue) api.SetAcceleration(AcsAxisEnum, acceleration.Value);
            api.Jog(MotionFlags.ACSC_AMF_VELOCITY, AcsAxisEnum, velocity);
            return true;
        }, $"{NameOrIndex} Jog({velocity})", "Axis");

        if (ok) _owner.LogInfo($"{NameOrIndex} Jog 速度 {velocity}", "Axis");
        return ok;
    }

    /// <summary>平滑停止(按减速度)</summary>
    public bool Stop()
        => _owner.Execute(api => api.Halt(AcsAxisEnum), $"停止 {NameOrIndex}", "Axis");

    /// <summary>紧急停止(按急停减速度 Kill)</summary>
    public bool EmergencyStop()
        => _owner.Execute(api => api.Kill(AcsAxisEnum), $"急停 {NameOrIndex}", "Axis");

    /// <summary>结束该轴当前运动序列</summary>
    public bool EndSequence()
        => _owner.Execute(api => api.EndSequence(AcsAxisEnum), $"结束 {NameOrIndex} 运动序列", "Axis");

    /// <summary>等待运动结束(轮询方式，不会长时间占用通讯链路)</summary>
    public bool WaitMotionEnd(int timeoutMs = 60000, int pollMs = 20)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var moving = _owner.Execute(api =>
            {
                var state = api.GetMotorState(AcsAxisEnum);
                return (state & MotorStates.ACSC_MST_MOVE) != 0;
            }, $"{NameOrIndex} 运动状态", true, "Axis");

            if (!moving) return true;
            Thread.Sleep(pollMs);
        }
        _owner.LogWarn($"{NameOrIndex} 等待运动结束超时({timeoutMs}ms)", "Axis");
        return false;
    }

    #endregion

    #region 回零 / 位置设定

    /// <summary>
    /// 运行回零程序(控制器内的 ACSPL+ 程序缓冲区)，并等待回零结束。
    /// 若需要更强的控制(生成 HOME 指令、软件置零)，请用 <see cref="Home(AcsHomingOptions,int)"/>。
    /// </summary>
    public bool Home(int bufferIndex, int timeoutMs = 120000, string? label = null)
        => Home(new AcsHomingOptions { Mode = AcsHomingMode.RunBufferProgram, BufferIndex = bufferIndex }, timeoutMs, label);

    /// <summary>
    /// 回零(支持三种方式:运行已有回零程序 / 生成 HOME 指令程序 / 软件置零)
    /// </summary>
    public bool Home(AcsHomingOptions options, int timeoutMs = 180000, string? label = null)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        var bufferIndex = options.BufferIndex;
        var buffer = (ProgramBuffer)bufferIndex;

        // ---- 方式 3:软件置零(不跑程序) ----
        if (options.Mode == AcsHomingMode.SoftZero)
        {
            // 注意:FPOS 是只读变量，不能写成程序里的赋值语句，必须用 SET 或 SDK 接口
            var ok = SetEncoderPosition(options.SoftZeroPosition) & SetCommandPosition(options.SoftZeroPosition);
            if (ok) _owner.LogInfo($"{NameOrIndex} 软件置零完成: FPOS/RPOS = {options.SoftZeroPosition}", "Axis");
            return ok;
        }

        // ---- 方式 2:按参数生成 HOME 指令程序并写入缓冲区 ----
        if (options.Mode == AcsHomingMode.GenerateAndRun)
        {
            var text = AcsProgramText.HomeCommand(Index, options.Method, options.Velocity, options.MaxDistance, options.Offset);
            _owner.LogInfo($"{NameOrIndex} 生成回零程序: {text.Replace("\r\n", " | ").Trim()}", "Axis");
            if (!_owner.Program.Download(bufferIndex, text)) return false;
        }
        else
        {
            // ---- 方式 1:运行控制器里已有的回零程序，先确认缓冲区不为空 ----
            var content = _owner.Program.Upload(bufferIndex);
            if (string.IsNullOrWhiteSpace(content))
            {
                _owner.LogError($"{NameOrIndex} 回零失败: 缓冲区 {bufferIndex} 是空的(控制器里没有回零程序)。" +
                                "请改用“生成 HOME 指令程序”或“软件置零”，或先用 MMI Application Studio 把回零程序下载到该缓冲区。", "Axis");
                return false;
            }
        }

        if (!_owner.Program.Compile(bufferIndex))
        {
            _owner.LogError($"{NameOrIndex} 回零失败: 缓冲区 {bufferIndex} 编译不通过(检查 ACSPL+ 指令拼写，控制器只接受 ASCII 文本)", "Axis");
            return false;
        }

        var started = _owner.TryExecute(api =>
        {
            EnsureEnabled(api, AcsAxisEnum);
            api.StopBuffer(buffer);
            api.RunBuffer(buffer, label!);
            return true;
        }, $"{NameOrIndex} 启动回零程序(缓冲区 {bufferIndex})", "Axis");

        if (!started)
        {
            _owner.LogError($"{NameOrIndex} 回零失败: 程序无法启动(缓冲区为空 / 指令无法识别)", "Axis");
            return false;
        }

        _owner.LogInfo($"{NameOrIndex} 回零开始({options})", "Axis");

        // ---- 等待回零结束 ----
        // 判定依据(实测控制器/模拟器行为):
        //   * HOME 指令启动回零后程序本身可能立刻回到 COMPILED(运动是异步进行的)，
        //     所以不能只用“程序状态”判断结束，必须看轴是否还在运动；
        //   * 开始阶段轴尚未动起来，因此设置一个最小等待时间 + 连续若干次“静止”采样才算结束；
        //   * 一旦出现过运动，则以“运动停止”为结束条件(再等一小段确认)。
        const int pollMs = 200;
        const int requiredIdleSamples = 3;
        const int minWaitMs = 2000;
        const int confirmMs = 300;

        var start = Environment.TickCount64;
        var deadline = start + timeoutMs;
        var idleSamples = 0;
        var sawMoving = false;
        var faultCode = 0;
        var lastLog = start;
        var timedOut = false;
        var completed = false;

        while (true)
        {
            if (Environment.TickCount64 > deadline)
            {
                timedOut = true;
                break;
            }

            var moving = _owner.Execute(api =>
            {
                var state = api.GetMotorState(AcsAxisEnum);
                return (state & MotorStates.ACSC_MST_MOVE) != 0;
            }, $"{NameOrIndex} 运动状态", true, "Axis");

            var motionError = GetMotionError();
            if (AcsMotionErrors.IsFault(motionError))
            {
                faultCode = motionError;
                break;
            }

            if (moving)
            {
                sawMoving = true;
                idleSamples = 0;
            }
            else if (Environment.TickCount64 - start >= minWaitMs)
            {
                idleSamples++;
                if (idleSamples >= requiredIdleSamples)
                {
                    completed = true;
                    break;
                }
            }

            if (Environment.TickCount64 - lastLog > 2000)
            {
                lastLog = Environment.TickCount64;
                var status = ReadStatus();
                _owner.LogInfo($"{NameOrIndex} 回零中… 位置={status.EncoderPosition:F4} 运动={moving} 码={motionError}", "Axis");
            }

            Thread.Sleep(pollMs);
        }

        if (faultCode != 0)
        {
            _owner.LogError($"{NameOrIndex} 回零失败: 控制器报故障 {AcsMotionErrors.DescribeWithCode(faultCode)}", "Axis");
            StopHome(bufferIndex);
            return false;
        }

        if (timedOut)
        {
            _owner.LogWarn($"{NameOrIndex} 回零超时({timeoutMs}ms): 轴一直在找原点但没停下来。" +
                           "模拟器没有真实原点开关/索引脉冲，请改用“软件置零”；" +
                           "真机请检查回零方式号、限位/原点开关接线、回零速度与最大距离。", "Axis");
            StopHome(bufferIndex);
            return false;
        }

        if (!completed)
        {
            _owner.LogWarn($"{NameOrIndex} 回零未正常结束", "Axis");
            return false;
        }

        // 再确认一次确实已停止(避免刚启动就误判结束)
        Thread.Sleep(confirmMs);
        var finalStatus = ReadStatus();
        if (finalStatus.IsMoving)
        {
            _owner.LogWarn($"{NameOrIndex} 回零仍在运动，按未完成处理", "Axis");
            StopHome(bufferIndex);
            return false;
        }

        var code = finalStatus.MotionError;
        if (!sawMoving)
        {
            _owner.LogWarn($"{NameOrIndex} 回零过程中没有检测到运动(程序可能只是做了置零/未真正执行回零动作)", "Axis");
        }
        _owner.LogInfo($"{NameOrIndex} 回零结束: 位置={finalStatus.EncoderPosition:F4} 到位={finalStatus.IsInPosition} " +
                       $"运动码={AcsMotionErrors.DescribeWithCode(code)}", "Axis");

        // 正常终止码(5000~5008)不算故障；真正的故障已在上面拦截
        return true;
    }

    /// <summary>停止回零(停止程序缓冲区并停止该轴运动)</summary>
    public bool StopHome(int bufferIndex)
    {
        var okStop = _owner.Execute(api => api.StopBuffer((ProgramBuffer)bufferIndex),
            $"停止回零程序(缓冲区 {bufferIndex})", "Axis");
        var okHalt = Stop();   // 部分固件下 StopBuffer 不会中止 HOME 运动，需要再 Halt 一次
        return okStop || okHalt;
    }

    /// <summary>设置编码器(反馈)位置</summary>
    public bool SetEncoderPosition(double position)
        => _owner.Execute(api => api.SetFPosition(AcsAxisEnum, position), $"设置 {NameOrIndex} 编码器位置", "Axis");

    /// <summary>设置命令(参考)位置</summary>
    public bool SetCommandPosition(double position)
        => _owner.Execute(api => api.SetRPosition(AcsAxisEnum, position), $"设置 {NameOrIndex} 命令位置", "Axis");

    /// <summary>设置目标位置</summary>
    public bool SetTargetPosition(double position)
        => _owner.Execute(api => api.SetTargetPosition(AcsAxisEnum, position), $"设置 {NameOrIndex} 目标位置", "Axis");

    #endregion

    #region 参数设置

    /// <summary>写入速度/加速度等运动参数</summary>
    public bool SetParameters(AcsAxisParameters parameters)
    {
        var ok = _owner.TryExecute(api =>
        {
            api.SetVelocity(AcsAxisEnum, parameters.Velocity);
            api.SetAcceleration(AcsAxisEnum, parameters.Acceleration);
            api.SetDeceleration(AcsAxisEnum, parameters.Deceleration);
            api.SetJerk(AcsAxisEnum, parameters.Jerk);
            api.SetKillDeceleration(AcsAxisEnum, parameters.KillDeceleration);
            return true;
        }, $"设置 {NameOrIndex} 参数", "Axis");

        if (ok) _owner.LogInfo($"{NameOrIndex} 参数已写入: {parameters}", "Axis");
        return ok;
    }

    public bool SetVelocity(double velocity)
        => _owner.Execute(api => api.SetVelocity(AcsAxisEnum, velocity), $"设置 {NameOrIndex} 速度", "Axis");

    public bool SetAcceleration(double acceleration)
        => _owner.Execute(api => api.SetAcceleration(AcsAxisEnum, acceleration), $"设置 {NameOrIndex} 加速度", "Axis");

    public bool SetDeceleration(double deceleration)
        => _owner.Execute(api => api.SetDeceleration(AcsAxisEnum, deceleration), $"设置 {NameOrIndex} 减速度", "Axis");

    public bool SetJerk(double jerk)
        => _owner.Execute(api => api.SetJerk(AcsAxisEnum, jerk), $"设置 {NameOrIndex} Jerk", "Axis");

    public bool SetKillDeceleration(double killDeceleration)
        => _owner.Execute(api => api.SetKillDeceleration(AcsAxisEnum, killDeceleration), $"设置 {NameOrIndex} 急停减速度", "Axis");

    #endregion

    #region 内部辅助

    private static void EnsureEnabled(Api api, Axis axis)
    {
        var state = api.GetMotorState(axis);
        if ((state & MotorStates.ACSC_MST_ENABLE) == 0)
        {
            api.Enable(axis);
            api.WaitMotorEnabled(axis, 1, 10000);
        }
    }

    private bool CheckSoftLimit(double targetPosition, string action)
    {
        if (SoftLimitMin.HasValue && targetPosition < SoftLimitMin.Value)
        {
            _owner.LogWarn($"{NameOrIndex} {action}目标 {targetPosition} 低于软限位下限 {SoftLimitMin.Value}，已阻止", "Axis");
            return false;
        }
        if (SoftLimitMax.HasValue && targetPosition > SoftLimitMax.Value)
        {
            _owner.LogWarn($"{NameOrIndex} {action}目标 {targetPosition} 高于软限位上限 {SoftLimitMax.Value}，已阻止", "Axis");
            return false;
        }
        return true;
    }

    #endregion

    public override string ToString() => $"{Name}(轴{Index})";
}
