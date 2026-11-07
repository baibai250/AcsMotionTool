using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;
using AcsMotionTool.Core;
using AcsMotionTool.Data;

namespace AcsMotionTool.App.ViewModels;

/// <summary>
/// 轴控制页 ViewModel:轴状态列表、选中轴操作(使能/点动/定位/停止/回零/位置设定/参数)、
/// 轴名称与软限位(可持久化到 MySQL)、全部轴批量操作。
/// </summary>
public sealed class AxisViewModel : ViewModelBase, ISoftLimitSync
{
    private readonly IAcsService _acs;
    private readonly IAppSettingsService _settings;
    private readonly ILogService _log;
    private readonly ISoftLimitStore _softLimitStore;

    private readonly Dictionary<int, int> _lastMotionCodes = new();
    private bool _initialized;

    public AxisViewModel(IAcsService acs, IAppSettingsService settings, ILogService log, ISoftLimitStore softLimitStore)
    {
        _acs = acs;
        _settings = settings;
        _log = log;
        _softLimitStore = softLimitStore;

        EnableCommand = CreateAsyncCommand(() => AxisOpAsync(a => _acs.SetServoEnabledAsync(a.Index, true)), () => HasSelectedAxis);
        DisableCommand = CreateAsyncCommand(() => AxisOpAsync(a => _acs.SetServoEnabledAsync(a.Index, false)), () => HasSelectedAxis);
        ClearFaultCommand = CreateAsyncCommand(() => AxisOpAsync(a => _acs.ClearAxisFaultAsync(a.Index)), () => HasSelectedAxis);
        StopCommand = CreateAsyncCommand(() => AxisOpAsync(a => _acs.StopAxisAsync(a.Index, false)), () => HasSelectedAxis);
        EmergencyStopCommand = CreateAsyncCommand(() => AxisOpAsync(a => _acs.StopAxisAsync(a.Index, true)), () => HasSelectedAxis);
        SetPositionCommand = CreateAsyncCommand(SetPositionAsync, () => HasSelectedAxis);
        ReadParametersCommand = CreateAsyncCommand(ReadParametersAsync, () => HasSelectedAxis);
        WriteParametersCommand = CreateAsyncCommand(WriteParametersAsync, () => HasSelectedAxis);
        HomeCommand = CreateAsyncCommand(HomeAsync, () => HasSelectedAxis);
        StopHomeCommand = CreateAsyncCommand(StopHomeAsync, () => HasSelectedAxis);
        WriteHomeProgramCommand = CreateAsyncCommand(WriteHomeProgramAsync, () => HasSelectedAxis);
        ApplyAxisIdentityCommand = CreateAsyncCommand(ApplyAxisIdentityAsync, () => HasSelectedAxis);
        SaveSoftLimitsToDbCommand = CreateAsyncCommand(() => SaveAllToDatabaseAsync(), () => IsConnected || SoftLimitDbEnabled);
        LoadSoftLimitsFromDbCommand = CreateAsyncCommand(LoadFromDatabaseCommandAsync, () => SoftLimitDbEnabled);

        EnableAllCommand = CreateAsyncCommand(() => _acs.EnableAllAxesAsync(Axes.Count), () => IsConnected && Axes.Count > 0);
        DisableAllCommand = CreateAsyncCommand(() => _acs.DisableAllAxesAsync(), () => IsConnected && Axes.Count > 0);
        StopAllCommand = CreateAsyncCommand(() => _acs.StopAllAxesAsync(Axes.Count, false), () => IsConnected && Axes.Count > 0);
        EmergencyStopAllCommand = CreateAsyncCommand(() => _acs.StopAllAxesAsync(Axes.Count, true), () => IsConnected && Axes.Count > 0);
        ClearAllFaultsCommand = CreateAsyncCommand(() => _acs.ClearAllFaultsAsync(Axes.Count), () => IsConnected && Axes.Count > 0);

        PadPressCommand = CreateAsyncCommand(PadPressAsync, () => PadHasFourAxes);
        PadReleaseCommand = CreateCommand(parameter => _ = PadReleaseAsync(parameter));
        StopAllPadJogsCommand = CreateAsyncCommand(() => PadJogStopAsync("all"), () => PadHasFourAxes);
        MoveXyCommand = CreateAsyncCommand(MoveXyAsync, () => PadHasFourAxes);
        TakeCurrentXyCommand = CreateCommand(TakeCurrentXy);
        MoveSingleAxisCommand = CreateAsyncCommand(MoveSingleAxisAsync, () => HasSelectedAxis);
        TakeCurrentSingleAxisCommand = CreateCommand(TakeCurrentSingleAxis);

        _log.Items.CollectionChanged += OnLogCollectionChanged;

        _acs.AxisStatusesUpdated += OnAxisStatusesUpdated;
        _acs.ConnectionStateChanged += OnConnectionStateChanged;

        BuildAxes();

        SoftLimitDbStatus = _settings.Current.Database.Enabled
            ? $"数据库: {_settings.Current.Database.Describe()} (未同步)"
            : "数据库: 已禁用(软限位只保存在本地设置文件)";
    }

    #region 轴列表

    public ObservableCollection<AxisRowViewModel> Axes { get; } = new();

    private AxisRowViewModel? _selectedAxis;

    public AxisRowViewModel? SelectedAxis
    {
        get => _selectedAxis;
        set
        {
            if (!SetField(ref _selectedAxis, value)) return;
            OnPropertyChanged(nameof(HasSelectedAxis));
            OnPropertyChanged(nameof(SelectedAxisName));
            OnPropertyChanged(nameof(SingleAxisTitle));
            LoadSelectedAxisSettings();
            RaiseAllCanExecuteChanged();
        }
    }

    public bool HasSelectedAxis => SelectedAxis != null;

    /// <summary>最近 5 条日志(轴页底部展示，最新在前)</summary>
    public ObservableCollection<LogItem> RecentLogs { get; } = new();

    public string SelectedAxisName => SelectedAxis == null ? "(未选择轴)" : $"{SelectedAxis.Name} (轴 {SelectedAxis.Index})";

    public bool IsConnected => _acs.IsConnected;

    /// <summary>重建轴列表</summary>
    public void SetAxisCount(int count)
    {
        count = Math.Max(1, count);
        while (Axes.Count > count) Axes.RemoveAt(Axes.Count - 1);
        for (var i = Axes.Count; i < count; i++) Axes.Add(CreateRow(i));

        SelectedAxis ??= Axes.FirstOrDefault();
        _acs.SetAxes(Axes.Select(a => a.Index).ToList());
        OnPropertyChanged(nameof(PadHasFourAxes));
        OnPropertyChanged(nameof(PadGridEnabled));
        RaiseAllCanExecuteChanged();
    }

    private void BuildAxes()
    {
        var settings = _settings.Current;
        var count = Math.Max(1, settings.Axes.Count == 0 ? 4 : settings.Axes.Max(a => a.Index) + 1);
        while (Axes.Count > count) Axes.RemoveAt(Axes.Count - 1);
        for (var i = Axes.Count; i < count; i++) Axes.Add(CreateRow(i));
        SelectedAxis ??= Axes.FirstOrDefault();
        _acs.SetAxes(Axes.Select(a => a.Index).ToList());
    }

    private AxisRowViewModel CreateRow(int index)
    {
        var setting = _settings.Current.GetAxis(index);
        _acs.ConfigureAxis(index, setting.Name, setting.SoftLimitMin, setting.SoftLimitMax);
        return new AxisRowViewModel(index, setting.Name);
    }

    #endregion

    #region 绑定属性(运动/回零参数)

    private double _jogVelocity = 5;
    private double _jogVelocityZ = 2;
    private double _jogVelocityR = 5;
    private int _padModeIndex;
    private double _padStep = 1;
    private double _targetX;
    private double _targetY;
    private double _singleAxisTarget;
    private double _moveAcceleration = 100;
    private double _moveDeceleration = 100;
    private double _setPositionValue;
    private int _setPositionMode;
    private int _homeBufferIndex;
    private int _homingModeIndex;
    private int _homingMethod = 17;
    private double _homingVelocity = 100;
    private double _homingMaxDistance = 100000;
    private string _homingOffsetText = string.Empty;
    private double _softZeroPosition;
    private double _paramVelocity = 20;
    private double _paramAcceleration = 100;
    private double _paramDeceleration = 100;
    private double _paramJerk = 1000;
    private double _paramKillDeceleration = 1000;
    private string _axisNameText = string.Empty;
    private string _softLimitMinText = string.Empty;
    private string _softLimitMaxText = string.Empty;

    public double JogVelocity { get => _jogVelocity; set => SetField(ref _jogVelocity, value); }

    /// <summary>手动面板：Z 轴点动速度</summary>
    public double JogVelocityZ { get => _jogVelocityZ; set => SetField(ref _jogVelocityZ, value); }

    /// <summary>手动面板：R 轴点动速度</summary>
    public double JogVelocityR { get => _jogVelocityR; set => SetField(ref _jogVelocityR, value); }

    /// <summary>手动方向板是否可用(需要至少 4 根轴: X/Y/Z/R)</summary>
    public bool PadHasFourAxes => Axes.Count >= 4;

    /// <summary>手动页模式: 0=步进, 1=连续, 2=绝对</summary>
    public int PadModeIndex
    {
        get => _padModeIndex;
        set
        {
            if (!SetField(ref _padModeIndex, value)) return;
            OnPropertyChanged(nameof(IsPadAbsMode));
            OnPropertyChanged(nameof(PadGridEnabled));
            if (value == 2) _ = PadJogStopAsync("all");
        }
    }

    public bool IsPadAbsMode => _padModeIndex == 2;

    /// <summary>方向板是否可点(未连接 4 轴或处于绝对模式时禁用)</summary>
    public bool PadGridEnabled => PadHasFourAxes && _padModeIndex != 2;

    /// <summary>步进模式:一步的步长(各轴共用, 单位 mm/°)</summary>
    public double PadStep { get => _padStep; set => SetField(ref _padStep, value); }

    /// <summary>绝对模式:目标 X/Y</summary>
    public double TargetX { get => _targetX; set => SetField(ref _targetX, value); }
    public double TargetY { get => _targetY; set => SetField(ref _targetY, value); }

    /// <summary>单轴定位目标(跟随当前选中轴)</summary>
    public double SingleAxisTarget { get => _singleAxisTarget; set => SetField(ref _singleAxisTarget, value); }

    public string SingleAxisTitle => $"单轴定位 · 当前 {SelectedAxis?.Name ?? "-"}";

    public string PadXPosition => PadPositionText(0);
    public string PadYPosition => PadPositionText(1);
    public string PadZPosition => PadPositionText(2);
    public string PadRPosition => PadPositionText(3);

    private string PadPositionText(int index) => Axes.FirstOrDefault(a => a.Index == index)?.PositionText ?? "--";

    public double MoveAcceleration { get => _moveAcceleration; set => SetField(ref _moveAcceleration, value); }
    public double MoveDeceleration { get => _moveDeceleration; set => SetField(ref _moveDeceleration, value); }
    public double SetPositionValue { get => _setPositionValue; set => SetField(ref _setPositionValue, value); }
    public int SetPositionMode { get => _setPositionMode; set => SetField(ref _setPositionMode, value); }
    public int HomeBufferIndex { get => _homeBufferIndex; set => SetField(ref _homeBufferIndex, value); }

    /// <summary>0=运行控制器回零程序, 1=生成 HOME 指令并运行, 2=软件置零</summary>
    public int HomingModeIndex { get => _homingModeIndex; set => SetField(ref _homingModeIndex, value); }

    public int HomingMethod { get => _homingMethod; set => SetField(ref _homingMethod, value); }
    public double HomingVelocity { get => _homingVelocity; set => SetField(ref _homingVelocity, value); }
    public double HomingMaxDistance { get => _homingMaxDistance; set => SetField(ref _homingMaxDistance, value); }
    public string HomingOffsetText { get => _homingOffsetText; set => SetField(ref _homingOffsetText, value); }
    public double SoftZeroPosition { get => _softZeroPosition; set => SetField(ref _softZeroPosition, value); }
    public double ParamVelocity { get => _paramVelocity; set => SetField(ref _paramVelocity, value); }
    public double ParamAcceleration { get => _paramAcceleration; set => SetField(ref _paramAcceleration, value); }
    public double ParamDeceleration { get => _paramDeceleration; set => SetField(ref _paramDeceleration, value); }
    public double ParamJerk { get => _paramJerk; set => SetField(ref _paramJerk, value); }
    public double ParamKillDeceleration { get => _paramKillDeceleration; set => SetField(ref _paramKillDeceleration, value); }
    public string AxisNameText { get => _axisNameText; set => SetField(ref _axisNameText, value); }
    public string SoftLimitMinText { get => _softLimitMinText; set => SetField(ref _softLimitMinText, value); }
    public string SoftLimitMaxText { get => _softLimitMaxText; set => SetField(ref _softLimitMaxText, value); }

    #endregion

    #region 命令

    public AsyncRelayCommand EnableCommand { get; }
    public AsyncRelayCommand DisableCommand { get; }
    public AsyncRelayCommand ClearFaultCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand EmergencyStopCommand { get; }

    /// <summary>手动方向板：按下(步进走一步 / 连续开始点动)，参数为 "轴号:+"/"轴号:-"</summary>
    public AsyncRelayCommand PadPressCommand { get; }

    /// <summary>手动方向板：松开(连续模式停止点动)，参数为轴号</summary>
    public RelayCommand PadReleaseCommand { get; }

    /// <summary>绝对模式：X/Y 两轴一起移动到目标</summary>
    public AsyncRelayCommand MoveXyCommand { get; }

    /// <summary>绝对模式：把 X/Y 目标填成当前位置</summary>
    public RelayCommand TakeCurrentXyCommand { get; }

    /// <summary>单轴定位：把当前选中的轴移到目标位置</summary>
    public AsyncRelayCommand MoveSingleAxisCommand { get; }

    /// <summary>单轴定位：把目标填成当前位置</summary>
    public RelayCommand TakeCurrentSingleAxisCommand { get; }

    /// <summary>停止手动面板上所有正在点动的轴</summary>
    public AsyncRelayCommand StopAllPadJogsCommand { get; }

    public AsyncRelayCommand SetPositionCommand { get; }
    public AsyncRelayCommand ReadParametersCommand { get; }
    public AsyncRelayCommand WriteParametersCommand { get; }
    public AsyncRelayCommand HomeCommand { get; }
    public AsyncRelayCommand StopHomeCommand { get; }
    public AsyncRelayCommand WriteHomeProgramCommand { get; }
    public AsyncRelayCommand ApplyAxisIdentityCommand { get; }

    /// <summary>把当前各轴软限位写入数据库</summary>
    public AsyncRelayCommand SaveSoftLimitsToDbCommand { get; }

    /// <summary>从数据库加载软限位覆盖界面配置</summary>
    public AsyncRelayCommand LoadSoftLimitsFromDbCommand { get; }

    public AsyncRelayCommand EnableAllCommand { get; }
    public AsyncRelayCommand DisableAllCommand { get; }
    public AsyncRelayCommand StopAllCommand { get; }
    public AsyncRelayCommand EmergencyStopAllCommand { get; }
    public AsyncRelayCommand ClearAllFaultsCommand { get; }

    #endregion

    #region 命令实现

    private async Task AxisOpAsync(Func<AxisRowViewModel, Task<bool>> operation)
    {
        var axis = SelectedAxis;
        if (axis == null) return;
        await operation(axis).ConfigureAwait(true);
    }

    #region 手动方向板

    /// <summary>正在执行的点动任务:停止时要等它真正下发完成，避免“停止先于启动”导致轴继续走</summary>
    private readonly Dictionary<int, Task<bool>> _padJogTasks = new();

    /// <summary>方向键按下:步进模式走一步，连续模式开始点动(轴号 0=X,1=Y,2=Z,3=R)</summary>
    private async Task PadPressAsync(object? parameter)
    {
        if (!PadHasFourAxes || !TryParsePadJog(parameter, out var axis, out var direction)) return;

        switch (_padModeIndex)
        {
            case 0:
                await StepMoveAsync(axis, direction).ConfigureAwait(true);
                break;
            case 1:
                await StartPadJogAsync(axis, direction).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>方向键松开:只有连续模式需要停(步进模式让这一次相对移动走完)</summary>
    private async Task PadReleaseAsync(object? parameter)
    {
        if (_padModeIndex != 1) return;
        await PadJogStopAsync(parameter).ConfigureAwait(true);
    }

    /// <summary>步进模式:按共用步长走一步</summary>
    private async Task StepMoveAsync(int axis, int direction)
    {
        var step = Math.Abs(PadStep) * Math.Sign(direction);
        if (Math.Abs(step) < 1e-9) return;

        await _acs.MoveRelativeAsync(axis, step, SpeedForAxis(axis), MoveAcceleration, MoveDeceleration,
            wait: true, timeoutMs: 30000).ConfigureAwait(true);
    }

    /// <summary>连续模式:开始点动</summary>
    private async Task StartPadJogAsync(int axis, int direction)
    {
        var velocity = SpeedForAxis(axis);
        if (velocity < 1e-9) return;

        var task = _acs.JogAsync(axis, direction * velocity);
        _padJogTasks[axis] = task;
        await task.ConfigureAwait(true);
    }

    /// <summary>松开方向键停止点动，参数为轴号或 "all"</summary>
    private async Task PadJogStopAsync(object? parameter)
    {
        foreach (var axis in ResolvePadStopTargets(parameter))
        {
            await StopPadAxisAsync(axis).ConfigureAwait(true);
        }
    }

    private async Task StopPadAxisAsync(int axis)
    {
        if (_padJogTasks.TryGetValue(axis, out var start))
        {
            // 等启动任务落地再停，保证最终状态一定是“停”
            try { await start.ConfigureAwait(true); }
            catch { /* 启动失败时仍然要发停止 */ }
            _padJogTasks.Remove(axis);
        }

        await _acs.StopAxisAsync(axis, emergency: false).ConfigureAwait(true);
    }

    /// <summary>绝对模式:X/Y 两轴并行移动到目标(不做直线插补)</summary>
    private async Task MoveXyAsync()
    {
        if (!PadHasFourAxes) return;
        if (!CheckSoftLimit(0, TargetX, "X") || !CheckSoftLimit(1, TargetY, "Y")) return;

        var velocity = Math.Abs(JogVelocity);
        var x = _acs.MoveAbsoluteAsync(0, TargetX, velocity, MoveAcceleration, MoveDeceleration, wait: false, timeoutMs: 120000);
        var y = _acs.MoveAbsoluteAsync(1, TargetY, velocity, MoveAcceleration, MoveDeceleration, wait: false, timeoutMs: 120000);
        await Task.WhenAll(x, y).ConfigureAwait(true);

        _log.Info($"绝对移动: X→{TargetX:0.####}, Y→{TargetY:0.####}", "手动");
    }

    /// <summary>把 X/Y 目标填成当前位置</summary>
    private void TakeCurrentXy()
    {
        TargetX = AxisPosition(0);
        TargetY = AxisPosition(1);
    }

    /// <summary>单轴定位:把当前选中的轴移到指定位置</summary>
    private async Task MoveSingleAxisAsync()
    {
        var axis = SelectedAxis;
        if (axis == null) return;
        if (!CheckSoftLimit(axis.Index, SingleAxisTarget, axis.Name)) return;

        await _acs.MoveAbsoluteAsync(axis.Index, SingleAxisTarget, SpeedForAxis(axis.Index),
            MoveAcceleration, MoveDeceleration, wait: true, timeoutMs: 120000).ConfigureAwait(true);
    }

    private void TakeCurrentSingleAxis()
    {
        if (SelectedAxis is { } axis) SingleAxisTarget = axis.Position;
    }

    /// <summary>软限位预检(最终仍由 Core/控制器拦截，这里只是提前提示)</summary>
    private bool CheckSoftLimit(int axisIndex, double target, string axisName)
    {
        var setting = _settings.Current.GetAxis(axisIndex);

        if (setting.SoftLimitMin is { } min && target < min)
        {
            _log.Warn($"{axisName} 目标 {target:0.####} 低于软限位下限 {min:0.####}，已拦截", "手动");
            return false;
        }

        if (setting.SoftLimitMax is { } max && target > max)
        {
            _log.Warn($"{axisName} 目标 {target:0.####} 高于软限位上限 {max:0.####}，已拦截", "手动");
            return false;
        }

        return true;
    }

    /// <summary>按轴取点动速度(2=Z, 3=R, 其余按 XY)</summary>
    private double SpeedForAxis(int axis) => axis switch
    {
        2 => Math.Abs(JogVelocityZ),
        3 => Math.Abs(JogVelocityR),
        _ => Math.Abs(JogVelocity),
    };

    private double AxisPosition(int index) => Axes.FirstOrDefault(a => a.Index == index)?.Position ?? 0;

    private static bool TryParsePadJog(object? parameter, out int axis, out int direction)
    {
        axis = -1;
        direction = 0;
        if (parameter is not string text) return false;

        var parts = text.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out axis)) return false;

        direction = parts[1] switch
        {
            "+" => 1,
            "-" => -1,
            _ => 0,
        };
        return direction != 0 && axis is >= 0 and <= 3;
    }

    private static IEnumerable<int> ResolvePadStopTargets(object? parameter)
    {
        if (parameter is string text && text == "all") return new[] { 0, 1, 2, 3 };
        if (parameter is string single && int.TryParse(single, out var axis)) return new[] { axis };
        return new[] { 0, 1, 2, 3 };
    }

    #endregion

    private Task SetPositionAsync()
    {
        var axis = SelectedAxis;
        if (axis == null) return Task.CompletedTask;
        return _acs.SetAxisPositionAsync(axis.Index, SetPositionValue, encoderPosition: SetPositionMode == 0);
    }

    private async Task ReadParametersAsync()
    {
        var axis = SelectedAxis;
        if (axis == null) return;
        var parameters = await _acs.ReadAxisParametersAsync(axis.Index).ConfigureAwait(true);
        ParamVelocity = parameters.Velocity;
        ParamAcceleration = parameters.Acceleration;
        ParamDeceleration = parameters.Deceleration;
        ParamJerk = parameters.Jerk;
        ParamKillDeceleration = parameters.KillDeceleration;
    }

    private Task WriteParametersAsync()
    {
        var axis = SelectedAxis;
        if (axis == null) return Task.CompletedTask;
        return _acs.SetAxisParametersAsync(axis.Index, new AcsAxisParameters
        {
            Velocity = ParamVelocity,
            Acceleration = ParamAcceleration,
            Deceleration = ParamDeceleration,
            Jerk = ParamJerk,
            KillDeceleration = ParamKillDeceleration,
        });
    }

    private Task HomeAsync()
    {
        var axis = SelectedAxis;
        if (axis == null) return Task.CompletedTask;

        var options = BuildHomingOptions();
        if (options.Mode == AcsHomingMode.SoftZero && !_acs.IsSimulator)
        {
            _log.Warn("“软件置零”只是把当前位置当作零点，真机上请确认不会影响坐标基准", "回零");
        }
        return _acs.HomeAsync(axis.Index, options, 180000);
    }

    private Task StopHomeAsync()
    {
        var axis = SelectedAxis;
        return axis == null ? Task.CompletedTask : _acs.StopHomeAsync(axis.Index, HomeBufferIndex);
    }

    private Task WriteHomeProgramAsync()
    {
        var axis = SelectedAxis;
        if (axis == null) return Task.CompletedTask;
        _log.Info($"写入回零程序到缓冲区 {HomeBufferIndex}", "回零");
        return _acs.WriteHomeProgramAsync(axis.Index, BuildHomingOptions());
    }

    private AcsHomingOptions BuildHomingOptions() => new()
    {
        BufferIndex = HomeBufferIndex,
        Method = HomingMethod,
        Velocity = HomingVelocity,
        MaxDistance = HomingMaxDistance,
        Offset = ParseOptionalDouble(HomingOffsetText, "回零原点偏移"),
        SoftZeroPosition = SoftZeroPosition,
        Mode = HomingModeIndex switch
        {
            1 => AcsHomingMode.GenerateAndRun,
            2 => AcsHomingMode.SoftZero,
            _ => AcsHomingMode.RunBufferProgram,
        },
    };

    /// <summary>
    /// 应用选中轴的名称与软限位:先应用到运行时(运动拦截)与本地设置，
    /// 再按设置决定是否同步写入 MySQL。
    /// </summary>
    private async Task ApplyAxisIdentityAsync()
    {
        var axis = SelectedAxis;
        if (axis == null) return;

        var name = string.IsNullOrWhiteSpace(AxisNameText) ? axis.Name : AxisNameText.Trim();
        var softMin = ParseOptionalDouble(SoftLimitMinText, "软限位下限");
        var softMax = ParseOptionalDouble(SoftLimitMaxText, "软限位上限");
        if (softMin.HasValue && softMax.HasValue && softMin > softMax)
        {
            (softMin, softMax) = (softMax, softMin);
            _log.Warn("软限位上下限写反了，已自动交换", "轴");
        }

        _acs.ConfigureAxis(axis.Index, name, softMin, softMax);
        axis.Name = name;

        var setting = _settings.Current.GetAxis(axis.Index);
        setting.Name = name;
        setting.SoftLimitMin = softMin;
        setting.SoftLimitMax = softMax;
        SaveSettingsOrLog();

        OnPropertyChanged(nameof(SelectedAxisName));
        _log.Info($"已应用: 轴{axis.Index} 名称={name}, 软限位=[{Format(softMin)}, {Format(softMax)}]", "轴");

        if (SoftLimitDbEnabled && _settings.Current.Database.SaveSoftLimitsAutomatically)
        {
            var record = new AxisSoftLimitRecord(axis.Index, name, softMin, softMax);
            var saved = await _softLimitStore.SaveAllAsync(_settings.Current.Database, new[] { record }).ConfigureAwait(true);
            if (saved)
            {
                SoftLimitDbStatus = $"数据库: 轴{axis.Index} 已保存 {DateTime.Now:HH:mm:ss}";
                _log.Info($"软限位已写入数据库: 轴{axis.Index} {record.RangeText}", "数据库");
            }
            else
            {
                SoftLimitDbStatus = $"数据库: 轴{axis.Index} 保存失败(请检查数据库连接)";
                _log.Warn($"软限位写入数据库失败: 轴{axis.Index}，参数 {_settings.Current.Database.Describe()}", "数据库");
            }
        }
    }

    #region 软限位数据库同步

    private bool SoftLimitDbEnabled => _settings.Current.Database.Enabled;

    private string _softLimitDbStatus = "数据库: 未同步";

    /// <summary>软限位数据库状态(显示在轴页)</summary>
    public string SoftLimitDbStatus
    {
        get => _softLimitDbStatus;
        private set => SetField(ref _softLimitDbStatus, value);
    }

    /// <summary>把界面上所有轴的软限位写入数据库</summary>
    public async Task<bool> SaveAllToDatabaseAsync()
    {
        if (!SoftLimitDbEnabled)
        {
            SoftLimitDbStatus = "数据库: 已禁用(不能保存)";
            _log.Warn("数据库已在设置中禁用，软限位仅保存在本地设置文件", "数据库");
            return false;
        }

        var records = CollectSoftLimitRecords();
        var saved = await _softLimitStore.SaveAllAsync(_settings.Current.Database, records).ConfigureAwait(true);
        if (saved)
        {
            SoftLimitDbStatus = $"数据库: 已保存 {records.Count} 轴 {DateTime.Now:HH:mm:ss}";
            _log.Info($"软限位已写入数据库({_settings.Current.Database.Describe()}): {string.Join(", ", records)}", "数据库");
        }
        else
        {
            SoftLimitDbStatus = "数据库: 保存失败(请检查连接与权限)";
            _log.Error($"软限位写入数据库失败: {_settings.Current.Database.Describe()}", "数据库");
        }
        return saved;
    }

    /// <summary>从数据库加载软限位并应用(界面 + 运动拦截 + 本地设置)</summary>
    public async Task<(int Count, string Message)> LoadFromDatabaseAsync()
    {
        if (!SoftLimitDbEnabled)
        {
            SoftLimitDbStatus = "数据库: 已禁用(未加载)";
            return (0, "数据库已禁用，使用本地设置中的软限位");
        }

        var records = await _softLimitStore.LoadAllAsync(_settings.Current.Database).ConfigureAwait(true);
        if (records.Count == 0)
        {
            SoftLimitDbStatus = "数据库: 无软限位记录(使用本地设置)";
            _log.Info("数据库中暂无软限位记录，继续使用本地设置", "数据库");
            return (0, "数据库中暂无软限位记录");
        }

        ApplyDatabaseRecords(records);
        SoftLimitDbStatus = $"数据库: 已加载 {records.Count} 轴 {DateTime.Now:HH:mm:ss}";
        _log.Info($"已从数据库加载软限位 {records.Count} 条: {string.Join(", ", records)}", "数据库");
        return (records.Count, $"从数据库加载 {records.Count} 轴软限位");
    }

    /// <summary>把数据库记录应用到运行时、界面与本地设置</summary>
    public void ApplyDatabaseRecords(IEnumerable<AxisSoftLimitRecord> records)
    {
        foreach (var record in records)
        {
            _acs.ConfigureAxis(record.AxisIndex, string.IsNullOrWhiteSpace(record.AxisName) ? null : record.AxisName,
                record.SoftLimitMin, record.SoftLimitMax);

            var row = Axes.FirstOrDefault(a => a.Index == record.AxisIndex);
            if (row != null && !string.IsNullOrWhiteSpace(record.AxisName)) row.Name = record.AxisName;

            var setting = _settings.Current.GetAxis(record.AxisIndex);
            if (!string.IsNullOrWhiteSpace(record.AxisName)) setting.Name = record.AxisName;
            setting.SoftLimitMin = record.SoftLimitMin;
            setting.SoftLimitMax = record.SoftLimitMax;
        }

        SaveSettingsOrLog();
        LoadSelectedAxisSettings();
        OnPropertyChanged(nameof(SelectedAxisName));
    }

    /// <summary>保存本地设置并把失败原因写进日志(不抛异常，避免影响主流程)</summary>
    private void SaveSettingsOrLog()
    {
        if (!_settings.Save())
        {
            _log.Warn($"设置保存失败: {_settings.Path}；原因: {_settings.LastError ?? "未知"}", "设置");
        }
    }

    /// <summary>收集当前各轴软限位(用于写库)</summary>
    public List<AxisSoftLimitRecord> CollectSoftLimitRecords()
        => Axes.Select(row =>
        {
            var setting = _settings.Current.GetAxis(row.Index);
            return new AxisSoftLimitRecord(row.Index, row.Name, setting.SoftLimitMin, setting.SoftLimitMax);
        }).ToList();

    private async Task LoadFromDatabaseCommandAsync()
    {
        var axis = SelectedAxis;
        var (count, message) = await LoadFromDatabaseAsync().ConfigureAwait(true);
        _log.Info($"{message}（当前选中: {axis?.Name ?? "-"}）", "数据库");
    }

    #endregion

    #endregion

    #region 状态刷新

    /// <summary>日志变化时维护最近 5 条(日志集合始终在 UI 线程更新)</summary>
    private void OnLogCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { Count: > 0 })
        {
            foreach (LogItem item in e.NewItems) RecentLogs.Insert(0, item);
            while (RecentLogs.Count > 5) RecentLogs.RemoveAt(RecentLogs.Count - 1);
            return;
        }

        // 裁剪/清空时整体重建，最多 5 条，开销可忽略
        RecentLogs.Clear();
        for (var i = _log.Items.Count - 1; i >= 0 && RecentLogs.Count < 5; i--) RecentLogs.Add(_log.Items[i]);
    }

    private void OnAxisStatusesUpdated(object? sender, IReadOnlyList<AcsAxisStatus> statuses)
    {
        foreach (var status in statuses)
        {
            var row = Axes.FirstOrDefault(a => a.Index == status.Index);
            if (row == null) continue;

            row.Apply(status);
            ReportMotionCodeChange(row, status);
        }

        OnPropertyChanged(nameof(PadXPosition));
        OnPropertyChanged(nameof(PadYPosition));
        OnPropertyChanged(nameof(PadZPosition));
        OnPropertyChanged(nameof(PadRPosition));
    }

    /// <summary>运动终止码变化时解释一次(5000~5008 正常终止，5009+ 才是故障)</summary>
    private void ReportMotionCodeChange(AxisRowViewModel row, AcsAxisStatus status)
    {
        if (!status.IsValid) return;
        _lastMotionCodes.TryGetValue(row.Index, out var previous);
        if (previous == status.MotionError) return;

        _lastMotionCodes[row.Index] = status.MotionError;
        if (status.IsMotionFault)
        {
            _log.Error($"{row.Name} 出现运动故障: {AcsMotionErrors.DescribeForDisplay(status.MotionError)}", "轴");
        }
        else if (status.MotionError != 0)
        {
            _log.Info($"{row.Name} 运动终止码: {AcsMotionErrors.DescribeForDisplay(status.MotionError)}", "轴");
        }
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        OnPropertyChanged(nameof(IsConnected));
        RaiseAllCanExecuteChanged();

        if (connected && !_initialized)
        {
            // 连接后把设置里的轴名/软限位与控制器轴数同步一次
            _initialized = true;
            foreach (var row in Axes)
            {
                var setting = _settings.Current.GetAxis(row.Index);
                _acs.ConfigureAxis(row.Index, setting.Name, setting.SoftLimitMin, setting.SoftLimitMax);
                row.Name = setting.Name;
            }

            var reported = (int)(_acs.Info?.AxisCount ?? 0);
            if (reported > 0)
            {
                _log.Info($"控制器报告轴数 = {Math.Min(reported, 64)}，界面当前 {Axes.Count} 轴", "轴");
                if (reported < Axes.Count) SetAxisCount(reported);
            }
        }
        else if (!connected)
        {
            _initialized = false;
        }
    }

    private void LoadSelectedAxisSettings()
    {
        var axis = SelectedAxis;
        if (axis == null) return;

        var setting = _settings.Current.GetAxis(axis.Index);
        JogVelocity = setting.JogVelocity;
        MoveAcceleration = setting.Acceleration;
        MoveDeceleration = setting.Deceleration;
        HomeBufferIndex = setting.HomeBufferIndex;
        SetPositionValue = 0;
        AxisNameText = axis.Name;
        SoftLimitMinText = setting.SoftLimitMin?.ToString("G") ?? string.Empty;
        SoftLimitMaxText = setting.SoftLimitMax?.ToString("G") ?? string.Empty;
        SingleAxisTarget = axis.Position;
    }

    private double? ParseOptionalDouble(string? text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text.Trim(), out var value)) return value;
        _log.Warn($"{fieldName} 数值非法: {text}，该项按“不检查”处理", "轴");
        return null;
    }

    private static string Format(double? value) => value?.ToString("G") ?? "不限";

    #endregion
}
