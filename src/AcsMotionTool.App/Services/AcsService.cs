using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.Core;

namespace AcsMotionTool.App.Services;

/// <summary>
/// IAcsService 的实现:包装 Core 的 <see cref="AcsController"/>，
/// 负责 1) 把阻塞调用放到后台线程 2) 后台轮询 3) 把所有事件切回 UI 线程。
/// </summary>
public sealed class AcsService : IAcsService, IDisposable
{
    private readonly AcsController _controller = new();
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogService _log;

    private readonly object _sync = new();
    private int[] _axisIndexes = Array.Empty<int>();
    private int[] _ioPorts = Array.Empty<int>();
    private int _ioBitsPerPort = 16;
    private VariableWatch[] _watches = Array.Empty<VariableWatch>();

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private bool _disposed;

    public AcsService(IUiDispatcher dispatcher, ILogService log)
    {
        _dispatcher = dispatcher;
        _log = log;
        _controller.LogRaised += OnControllerLog;
    }

    #region 状态与事件

    public bool IsConnected => _controller.IsConnected;

    public bool IsSimulator => _controller.IsSimulator;

    public AcsControllerInfo? Info => _controller.Info;

    public int PollIntervalMs { get; set; } = 200;

    /// <summary>最近一次轮询耗时(ms)</summary>
    public int LastPollDurationMs { get; private set; }

    /// <summary>自适应后的实际轮询间隔(ms)</summary>
    public int EffectivePollIntervalMs { get; private set; }

    /// <summary>累计轮询次数</summary>
    public long PollCount { get; private set; }

    /// <summary>轮询指标更新(用于状态栏显示，便于观察长时间运行是否变慢)</summary>
    public event EventHandler<PollMetrics>? PollMetricsUpdated;

    public event EventHandler<AcsLogEventArgs>? LogReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<IReadOnlyList<AcsAxisStatus>>? AxisStatusesUpdated;
    public event EventHandler<IReadOnlyList<IoPortState>>? IoUpdated;
    public event EventHandler<IReadOnlyList<VariableValue>>? VariablesUpdated;

    private void OnControllerLog(object? sender, AcsLogEventArgs e)
    {
        _dispatcher.Invoke(() => LogReceived?.Invoke(this, e));
    }

    private void RaiseConnectionStateChanged(bool connected)
        => _dispatcher.Invoke(() => ConnectionStateChanged?.Invoke(this, connected));

    #endregion

    #region 连接

    public async Task<bool> ConnectAsync(AcsConnectionOptions options)
    {
        var result = await Task.Run(() => _controller.Connect(options)).ConfigureAwait(false);
        RaiseConnectionStateChanged(result);

        if (result) StartPolling();
        return result;
    }

    public async Task DisconnectAsync()
    {
        StopPolling();
        await Task.Run(() => _controller.Disconnect()).ConfigureAwait(false);
        RaiseConnectionStateChanged(false);
    }

    public Task<bool> RebootAsync(int timeoutMs) => Task.Run(() => _controller.Reboot(timeoutMs));

    public Task<bool> SaveToFlashAsync() => Task.Run(() => _controller.SaveToFlash());

    public Task<AcsControllerInfo?> ReadControllerInfoAsync() => Task.Run(() => _controller.ReadControllerInfo());

    #endregion

    #region 周期采集

    public void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _pollTask = Task.Run(() => PollLoopAsync(token), token);
        _log.Info($"已启动状态轮询(周期 {PollIntervalMs} ms)", "Poll");
    }

    public void StopPolling()
    {
        try
        {
            _pollCts?.Cancel();
            _pollTask?.Wait(1000);
        }
        catch
        {
            // 停止轮询时的异常忽略
        }
        finally
        {
            _pollCts?.Dispose();
            _pollCts = null;
            _pollTask = null;
        }
    }

    public void SetAxes(IReadOnlyCollection<int> axisIndexes)
    {
        lock (_sync) _axisIndexes = axisIndexes.Distinct().OrderBy(i => i).ToArray();
    }

    public void SetIoLayout(IReadOnlyCollection<int> ports, int bitsPerPort)
    {
        lock (_sync)
        {
            _ioPorts = ports.Distinct().OrderBy(p => p).ToArray();
            _ioBitsPerPort = Math.Clamp(bitsPerPort, 1, 32);
        }
    }

    public void SetVariableWatches(IReadOnlyList<VariableWatch> watches)
    {
        // 监视变量每个都要一次 SDK 往返，数量过多会拖慢轮询，这里做上限保护
        const int maxWatches = 50;
        var list = watches?.Take(maxWatches).ToArray() ?? Array.Empty<VariableWatch>();
        if (watches != null && watches.Count > maxWatches)
        {
            _log.Warn($"监视变量超过 {maxWatches} 个，仅前 {maxWatches} 个会被周期刷新", "Poll");
        }
        lock (_sync) _watches = list;
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var interval = Math.Max(50, PollIntervalMs);
            var started = Environment.TickCount64;

            try
            {
                // 用缓存的连接状态判断(不持锁)，随后在后台刷新一次真实状态
                _controller.RefreshConnectionState();
                if (_controller.IsConnected)
                {
                    await PollOnceAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"轮询异常: {ex.Message}", "Poll");
            }

            var elapsed = (int)(Environment.TickCount64 - started);
            LastPollDurationMs = elapsed;
            PollCount++;

            // 自适应:一次轮询耗时接近/超过设定周期时自动拉长间隔，避免长时间运行把通讯链路打满
            var effective = Math.Max(interval, elapsed * 2);
            EffectivePollIntervalMs = effective;
            var metrics = new PollMetrics(elapsed, interval, effective, PollCount);
            await _dispatcher.InvokeAsync(() => PollMetricsUpdated?.Invoke(this, metrics), background: true)
                .ConfigureAwait(false);

            try
            {
                await Task.Delay(effective, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollOnceAsync()
    {
        int[] axes;
        int[] ports;
        VariableWatch[] watches;
        lock (_sync)
        {
            axes = _axisIndexes;
            ports = _ioPorts;
            watches = _watches;
        }

        var statuses = new List<AcsAxisStatus>(axes.Length);
        foreach (var index in axes)
        {
            statuses.Add(_controller.GetAxis(index).ReadStatus(quiet: true));
        }

        var ioStates = new List<IoPortState>(ports.Length);
        foreach (var port in ports)
        {
            ioStates.Add(new IoPortState(port,
                _controller.Io.ReadInputPort(port, quiet: true),
                _controller.Io.ReadOutputPort(port, quiet: true)));
        }

        var variableValues = new List<VariableValue>(watches.Length);
        foreach (var watch in watches)
        {
            var text = _controller.Variables.ReadAsText(watch.Name, watch.Buffer, quiet: true);
            variableValues.Add(new VariableValue(watch.Name, watch.Buffer, text, string.IsNullOrEmpty(text)));
        }

        // 统一切回 UI 线程后再通知 ViewModel(低优先级，避免抢占输入/渲染)
        await _dispatcher.InvokeAsync(() =>
        {
            AxisStatusesUpdated?.Invoke(this, statuses);
            IoUpdated?.Invoke(this, ioStates);
            VariablesUpdated?.Invoke(this, variableValues);
        }, background: true).ConfigureAwait(false);
    }

    #endregion

    #region 轴

    private AcsAxis Axis(int index) => _controller.GetAxis(index);

    public Task<AcsAxisStatus> ReadAxisStatusAsync(int axisIndex, bool readParameters = false)
        => Task.Run(() => Axis(axisIndex).ReadStatus(readParameters));

    public Task<AcsAxisParameters> ReadAxisParametersAsync(int axisIndex)
        => Task.Run(() => Axis(axisIndex).ReadParameters());

    public Task<bool> SetAxisParametersAsync(int axisIndex, AcsAxisParameters parameters)
        => Task.Run(() => Axis(axisIndex).SetParameters(parameters));

    public Task<bool> SetServoEnabledAsync(int axisIndex, bool enabled, bool wait = true, int timeoutMs = 15000)
        => Task.Run(() => enabled ? Axis(axisIndex).Enable(wait, timeoutMs) : Axis(axisIndex).Disable());

    public Task<bool> ClearAxisFaultAsync(int axisIndex) => Task.Run(() => Axis(axisIndex).ClearFault());

    public Task<bool> StopAxisAsync(int axisIndex, bool emergency)
        => Task.Run(() => emergency ? Axis(axisIndex).EmergencyStop() : Axis(axisIndex).Stop());

    public Task<bool> JogAsync(int axisIndex, double velocity) => Task.Run(() => Axis(axisIndex).StartJog(velocity));

    public Task<bool> MoveAbsoluteAsync(int axisIndex, double position, double? velocity, double? acceleration,
        double? deceleration, bool wait, int timeoutMs)
        => Task.Run(() => Axis(axisIndex).MoveAbsolute(position, velocity, acceleration, deceleration, wait, timeoutMs));

    public Task<bool> MoveRelativeAsync(int axisIndex, double delta, double? velocity, double? acceleration,
        double? deceleration, bool wait, int timeoutMs)
        => Task.Run(() => Axis(axisIndex).MoveRelative(delta, velocity, acceleration, deceleration, wait, timeoutMs));

    public Task<bool> SetAxisPositionAsync(int axisIndex, double position, bool encoderPosition)
        => Task.Run(() => encoderPosition
            ? Axis(axisIndex).SetEncoderPosition(position)
            : Axis(axisIndex).SetCommandPosition(position));

    public Task<bool> HomeAsync(int axisIndex, AcsHomingOptions options, int timeoutMs)
        => Task.Run(() => Axis(axisIndex).Home(options, timeoutMs));

    public Task<bool> StopHomeAsync(int axisIndex, int bufferIndex)
        => Task.Run(() => Axis(axisIndex).StopHome(bufferIndex));

    public Task<bool> WriteHomeProgramAsync(int axisIndex, AcsHomingOptions options)
        => Task.Run(() =>
        {
            var text = AcsProgramText.HomeCommand(axisIndex, options.Method, options.Velocity, options.MaxDistance, options.Offset);
            if (!_controller.Program.Download(options.BufferIndex, text)) return false;
            _controller.Program.Compile(options.BufferIndex);
            return true;
        });

    public Task<bool> EnableAllAxesAsync(int axisCount) => Task.Run(() => _controller.EnableAll(axisCount));

    public Task<bool> DisableAllAxesAsync() => Task.Run(() => _controller.DisableAll());

    public Task<bool> StopAllAxesAsync(int axisCount, bool emergency)
        => Task.Run(() => emergency ? _controller.EmergencyStopAll(axisCount) : _controller.StopAll(axisCount));

    public Task<bool> ClearAllFaultsAsync(int axisCount) => Task.Run(() => _controller.ClearFaultsAll(axisCount));

    public void ConfigureAxis(int axisIndex, string? name, double? softLimitMin, double? softLimitMax)
    {
        var axis = Axis(axisIndex);
        if (!string.IsNullOrWhiteSpace(name)) axis.Name = name!.Trim();
        axis.SoftLimitMin = softLimitMin;
        axis.SoftLimitMax = softLimitMax;
    }

    #endregion

    #region IO

    public Task<bool> SetOutputBitAsync(int port, int bit, bool value)
        => Task.Run(() => _controller.Io.SetOutputBit(port, bit, value));

    public Task<bool> SetOutputPortAsync(int port, int value)
        => Task.Run(() => _controller.Io.SetOutputPort(port, value));

    public Task<(int Raw, double Scaled)> ReadAnalogInputAsync(int port)
        => Task.Run(() => (_controller.Io.ReadAnalogInput(port), _controller.Io.ReadAnalogInputNT(port)));

    public Task<bool> WriteAnalogOutputAsync(int port, double value)
        => Task.Run(() => _controller.Io.SetAnalogOutputNT(port, value));

    #endregion

    #region 变量 / 终端 / 程序

    public Task<string> ReadVariableTextAsync(string name, int buffer = -1)
        => Task.Run(() => _controller.Variables.ReadAsText(name, buffer));

    public Task<bool> WriteVariableFromTextAsync(string name, string text, int buffer = -1)
        => Task.Run(() => _controller.Variables.WriteFromText(name, text, buffer));

    public Task<bool> DeclareVariableAsync(string name, bool integer)
        => Task.Run(() => integer ? _controller.Variables.DeclareInteger(name) : _controller.Variables.DeclareReal(name));

    public Task<string> ExecuteCommandAsync(string command)
        => Task.Run(() => _controller.Terminal.Transaction(command));

    public Task<bool> SendCommandAsync(string command)
        => Task.Run(() => _controller.Terminal.Send(command));

    public Task<string> UploadProgramAsync(int bufferIndex) => Task.Run(() => _controller.Program.Upload(bufferIndex));

    public Task<bool> DownloadProgramAsync(int bufferIndex, string text)
        => Task.Run(() => _controller.Program.Download(bufferIndex, text));

    public Task<bool> CompileProgramAsync(int bufferIndex) => Task.Run(() => _controller.Program.Compile(bufferIndex));

    public Task<bool> RunProgramAsync(int bufferIndex) => Task.Run(() => _controller.Program.Run(bufferIndex));

    public Task<bool> StopProgramAsync(int bufferIndex) => Task.Run(() => _controller.Program.Stop(bufferIndex));

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        _controller.LogRaised -= OnControllerLog;
        _controller.Dispose();
    }
}
