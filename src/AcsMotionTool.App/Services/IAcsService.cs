using AcsMotionTool.Core;

namespace AcsMotionTool.App.Services;

/// <summary>IO 端口一次采集的结果</summary>
public sealed record IoPortState(int Port, int Input, int Output);

/// <summary>被监视变量一次采集的结果</summary>
public sealed record VariableValue(string Name, int Buffer, string Text, bool HasError);

/// <summary>需要周期采集的变量</summary>
public sealed record VariableWatch(string Name, int Buffer);

/// <summary>轮询健康指标(状态栏展示，便于长时间运行时观察是否变慢)</summary>
public sealed record PollMetrics(int DurationMs, int ConfiguredIntervalMs, int EffectiveIntervalMs, long PollCount);

/// <summary>
/// ACS 业务服务:ViewModel 只依赖这个接口，不直接接触 Core 的通讯细节。
/// 所有耗时操作都是异步的；所有事件都在 UI 线程上触发，ViewModel 无需关心线程切换。
/// </summary>
public interface IAcsService
{
    bool IsConnected { get; }
    bool IsSimulator { get; }
    AcsControllerInfo? Info { get; }

    /// <summary>状态轮询周期(ms)</summary>
    int PollIntervalMs { get; set; }

    /// <summary>最近一次轮询耗时(ms)</summary>
    int LastPollDurationMs { get; }

    /// <summary>自适应后的实际轮询间隔(ms)</summary>
    int EffectivePollIntervalMs { get; }

    /// <summary>累计轮询次数</summary>
    long PollCount { get; }

    /// <summary>轮询指标更新</summary>
    event EventHandler<PollMetrics>? PollMetricsUpdated;

    /// <summary>日志(来自 Core)</summary>
    event EventHandler<AcsLogEventArgs>? LogReceived;

    /// <summary>连接状态变化</summary>
    event EventHandler<bool>? ConnectionStateChanged;

    /// <summary>轴状态周期刷新</summary>
    event EventHandler<IReadOnlyList<AcsAxisStatus>>? AxisStatusesUpdated;

    /// <summary>IO 周期刷新</summary>
    event EventHandler<IReadOnlyList<IoPortState>>? IoUpdated;

    /// <summary>被监视变量周期刷新</summary>
    event EventHandler<IReadOnlyList<VariableValue>>? VariablesUpdated;

    #region 连接

    Task<bool> ConnectAsync(AcsConnectionOptions options);
    Task DisconnectAsync();
    Task<bool> RebootAsync(int timeoutMs);
    Task<bool> SaveToFlashAsync();
    Task<AcsControllerInfo?> ReadControllerInfoAsync();

    #endregion

    #region 周期采集

    void StartPolling();
    void StopPolling();

    /// <summary>设置需要采集的轴集合</summary>
    void SetAxes(IReadOnlyCollection<int> axisIndexes);

    /// <summary>设置需要采集的 IO 端口与位宽</summary>
    void SetIoLayout(IReadOnlyCollection<int> ports, int bitsPerPort);

    /// <summary>设置需要采集的变量</summary>
    void SetVariableWatches(IReadOnlyList<VariableWatch> watches);

    #endregion

    #region 轴

    Task<AcsAxisStatus> ReadAxisStatusAsync(int axisIndex, bool readParameters = false);
    Task<AcsAxisParameters> ReadAxisParametersAsync(int axisIndex);
    Task<bool> SetAxisParametersAsync(int axisIndex, AcsAxisParameters parameters);
    Task<bool> SetServoEnabledAsync(int axisIndex, bool enabled, bool wait = true, int timeoutMs = 15000);
    Task<bool> ClearAxisFaultAsync(int axisIndex);
    Task<bool> StopAxisAsync(int axisIndex, bool emergency);
    Task<bool> JogAsync(int axisIndex, double velocity);
    Task<bool> MoveAbsoluteAsync(int axisIndex, double position, double? velocity, double? acceleration,
        double? deceleration, bool wait, int timeoutMs);
    Task<bool> MoveRelativeAsync(int axisIndex, double delta, double? velocity, double? acceleration,
        double? deceleration, bool wait, int timeoutMs);
    Task<bool> SetAxisPositionAsync(int axisIndex, double position, bool encoderPosition);
    Task<bool> HomeAsync(int axisIndex, AcsHomingOptions options, int timeoutMs);
    Task<bool> StopHomeAsync(int axisIndex, int bufferIndex);
    Task<bool> WriteHomeProgramAsync(int axisIndex, AcsHomingOptions options);

    Task<bool> EnableAllAxesAsync(int axisCount);
    Task<bool> DisableAllAxesAsync();
    Task<bool> StopAllAxesAsync(int axisCount, bool emergency);
    Task<bool> ClearAllFaultsAsync(int axisCount);

    /// <summary>设置轴的名称与软限位(本地配置，立即用于运动拦截)</summary>
    void ConfigureAxis(int axisIndex, string? name, double? softLimitMin, double? softLimitMax);

    #endregion

    #region IO

    Task<bool> SetOutputBitAsync(int port, int bit, bool value);
    Task<bool> SetOutputPortAsync(int port, int value);
    Task<(int Raw, double Scaled)> ReadAnalogInputAsync(int port);
    Task<bool> WriteAnalogOutputAsync(int port, double value);

    #endregion

    #region 变量 / 终端 / 程序

    Task<string> ReadVariableTextAsync(string name, int buffer = -1);
    Task<bool> WriteVariableFromTextAsync(string name, string text, int buffer = -1);
    Task<bool> DeclareVariableAsync(string name, bool integer);

    Task<string> ExecuteCommandAsync(string command);
    Task<bool> SendCommandAsync(string command);

    Task<string> UploadProgramAsync(int bufferIndex);
    Task<bool> DownloadProgramAsync(int bufferIndex, string text);
    Task<bool> CompileProgramAsync(int bufferIndex);
    Task<bool> RunProgramAsync(int bufferIndex);
    Task<bool> StopProgramAsync(int bufferIndex);

    #endregion
}
