using ACS.SPiiPlusNET;

namespace AcsMotionTool.Core;

/// <summary>连接状态</summary>
public enum AcsConnectionState
{
    Disconnected = 0,
    Ethernet = 1,
    Simulator = 2,
}

/// <summary>
/// ACS 控制器门面类。
/// 所有对 ACS.SPiiPlusNET.Api 的访问都被串行化(内部锁)，避免多线程并发访问同一条通讯链路。
/// </summary>
public sealed class AcsController : IDisposable
{
    private const string LogSource = "Controller";

    private readonly Api _api = new();
    private readonly object _sync = new();
    private readonly Dictionary<int, AcsAxis> _axes = new();
    private bool _disposed;

    /// <summary>
    /// 连接状态缓存。**UI 线程只读这个字段**，不再为了读状态去抢通讯锁 / 调 SDK，
    /// 避免后台轮询或长耗时命令持锁时把界面卡住。
    /// </summary>
    private volatile bool _isConnected;

    public AcsController()
    {
        Io = new AcsIo(this);
        Variables = new AcsVariables(this);
        Terminal = new AcsTerminal(this);
        Program = new AcsProgram(this);
    }

    /// <summary>日志事件，UI 订阅后写入日志窗口</summary>
    public event EventHandler<AcsLogEventArgs>? LogRaised;

    /// <summary>数字量输入输出</summary>
    public AcsIo Io { get; }

    /// <summary>ACSPL+ 变量读写</summary>
    public AcsVariables Variables { get; }

    /// <summary>ACSPL+ 命令终端</summary>
    public AcsTerminal Terminal { get; }

    /// <summary>控制器程序缓冲区(ACSPL+ 程序)</summary>
    public AcsProgram Program { get; }

    /// <summary>当前连接状态</summary>
    public AcsConnectionState State { get; private set; } = AcsConnectionState.Disconnected;

    /// <summary>当前连接参数</summary>
    public AcsConnectionOptions Options { get; private set; } = new();

    /// <summary>控制器信息(连接成功后读取)</summary>
    public AcsControllerInfo? Info { get; private set; }

    /// <summary>
    /// 是否已连接(读缓存，无锁、不访问 SDK；可在界面线程高频调用)。
    /// 缓存由连接/断开以及后台轮询更新(见 <see cref="RefreshConnectionState"/>)。
    /// </summary>
    public bool IsConnected => State != AcsConnectionState.Disconnected && _isConnected;

    /// <summary>
    /// 刷新连接状态缓存(只应在后台线程周期性调用；内部会短暂持锁读取 SDK 状态)。
    /// </summary>
    public void RefreshConnectionState()
    {
        if (State == AcsConnectionState.Disconnected)
        {
            _isConnected = false;
            return;
        }

        lock (_sync)
        {
            if (State == AcsConnectionState.Disconnected)
            {
                _isConnected = false;
                return;
            }
            _isConnected = IsConnectedLocked();
        }
    }

    /// <summary>是否连接的是模拟器</summary>
    public bool IsSimulator => State == AcsConnectionState.Simulator;

    #region 日志

    public void Log(AcsLogLevel level, string message, string? source = null, Exception? exception = null)
        => LogRaised?.Invoke(this, new AcsLogEventArgs(level, message, source, exception));

    public void LogInfo(string message, string? source = null) => Log(AcsLogLevel.Info, message, source);
    public void LogWarn(string message, string? source = null) => Log(AcsLogLevel.Warn, message, source);
    public void LogError(string message, string? source = null, Exception? ex = null) => Log(AcsLogLevel.Error, message, source, ex);
    public void LogDebug(string message, string? source = null) => Log(AcsLogLevel.Debug, message, source);

    #endregion

    #region 内部执行封装(串行化访问)

    /// <summary>
    /// 执行一次 API 调用；失败时记录日志并返回 fallback，不抛出异常(适合轮询/刷新)。
    /// </summary>
    internal T Execute<T>(Func<Api, T> action, string operation, T fallback, string source = LogSource)
    {
        lock (_sync)
        {
            try
            {
                return action(_api);
            }
            catch (Exception ex)
            {
                LogError($"{operation} 失败: {ex.Message}", source, ex);
                return fallback;
            }
        }
    }

    /// <summary>执行一次 API 调用，失败返回 false</summary>
    internal bool Execute(Action<Api> action, string operation, string source = LogSource)
        => Execute(api => { action(api); return true; }, operation, false, source);

    /// <summary>执行一次 API 调用，失败时静默返回 fallback(用于周期性轮询，避免日志刷屏)</summary>
    internal T ExecuteQuiet<T>(Func<Api, T> action, T fallback)
    {
        lock (_sync)
        {
            try
            {
                return action(_api);
            }
            catch
            {
                return fallback;
            }
        }
    }

    /// <summary>quiet 为 true 时静默执行，否则记录错误日志</summary>
    internal object? ExecuteQuietOrLog(Func<Api, object?> action, string operation, bool quiet)
        => quiet
            ? ExecuteQuiet(action, null)
            : Execute(action, operation, (object?)null, LogSource);

    /// <summary>执行一次返回 bool 的 API 调用(显式区分于 void 重载，避免 lambda 重载歧义)</summary>
    internal bool TryExecute(Func<Api, bool> action, string operation, string source = LogSource)
        => Execute(api => action(api), operation, false, source);

    /// <summary>执行一次 API 调用，失败时抛出 <see cref="AcsOperationException"/></summary>
    internal T ExecuteOrThrow<T>(Func<Api, T> action, string operation, string source = LogSource)
    {
        lock (_sync)
        {
            try
            {
                return action(_api);
            }
            catch (Exception ex)
            {
                var msg = $"{operation} 失败: {ex.Message}";
                LogError(msg, source, ex);
                throw new AcsOperationException(msg, ex);
            }
        }
    }

    /// <summary>执行一次 API 调用，失败时抛出 <see cref="AcsOperationException"/></summary>
    internal void ExecuteOrThrow(Action<Api> action, string operation, string source = LogSource)
        => ExecuteOrThrow(api => { action(api); return true; }, operation, source);

    private bool IsConnectedLocked()
    {
        try
        {
            return _api.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>尝试设置通讯超时(失败不影响连接)</summary>
    private void TrySetTimeout(int timeoutMs)
    {
        try
        {
            _api.SetTimeout(Math.Max(1000, timeoutMs));
        }
        catch (Exception ex)
        {
            LogWarn($"设置通讯超时失败(已忽略，不影响通讯): {ex.Message}", LogSource);
        }
    }

    #endregion

    #region 连接 / 断开

    /// <summary>
    /// 按指定参数连接控制器(以太网或模拟器)
    /// </summary>
    public bool Connect(AcsConnectionOptions? options = null)
    {
        if (options != null) Options = options.Clone();

        var opt = Options;
        lock (_sync)
        {
            if (IsConnectedLocked())
            {
                LogInfo($"已处于连接状态({State})，忽略本次连接请求", LogSource);
                return true;
            }

            try
            {
                // 注意:部分环境下(例如连接模拟器)SetTimeout 会因为 "Communication server can't be found"
                // 而失败，这并不影响后续通讯，因此这里只记录告警，不中断连接流程。
                TrySetTimeout(opt.TimeoutMs);

                if (opt.Mode == AcsConnectionMode.Simulator)
                {
                    LogInfo("正在打开 SPiiPlus 模拟器通讯(OpenCommSimulator)...", LogSource);
                    _api.OpenCommSimulator();
                }
                else
                {
                    LogInfo($"正在连接控制器 {opt.IpAddress} 协议={(int)opt.Protocol} ({opt.Protocol})...", LogSource);
                    _api.OpenCommEthernet(opt.IpAddress, (int)opt.Protocol);
                }

                if (!_api.IsConnected)
                {
                    LogError($"连接失败: {opt.Describe()}，请检查网线/IP/防火墙与控制器电源", LogSource);
                    State = AcsConnectionState.Disconnected;
                    _isConnected = false;
                    return false;
                }

                State = opt.Mode == AcsConnectionMode.Simulator
                    ? AcsConnectionState.Simulator
                    : AcsConnectionState.Ethernet;
                _isConnected = true;
            }
            catch (Exception ex)
            {
                State = AcsConnectionState.Disconnected;
                _isConnected = false;
                LogError($"连接异常: {ex.Message}", LogSource, ex);
                return false;
            }

            if (opt.RebootAfterConnect && State == AcsConnectionState.Ethernet)
            {
                try
                {
                    LogInfo("按配置重启控制器...", LogSource);
                    _api.ControllerReboot(opt.RebootTimeoutMs);
                    _api.CloseComm();
                    Thread.Sleep(200);
                    _api.OpenCommEthernet(opt.IpAddress, (int)opt.Protocol);
                }
                catch (Exception ex)
                {
                    LogError($"控制器重启/重连失败: {ex.Message}", LogSource, ex);
                }
            }
        }

        Info = ReadControllerInfo();
        LogInfo($"连接成功: {opt.Describe()}；{Info?.ToDisplayString()}", LogSource);
        return IsConnected;
    }

    /// <summary>断开连接</summary>
    public void Disconnect()
    {
        lock (_sync)
        {
            try
            {
                if (IsConnectedLocked())
                {
                    _api.CloseMessageBuffer();
                    _api.CloseComm();
                }
                if (State == AcsConnectionState.Simulator)
                {
                    try { _api.CloseSimulator(); } catch { /* 模拟器已关闭时忽略 */ }
                }
            }
            catch (Exception ex)
            {
                LogWarn($"断开连接时出现异常: {ex.Message}", LogSource);
            }
            finally
            {
                State = AcsConnectionState.Disconnected;
                _isConnected = false;
                Info = null;
            }
        }
        LogInfo("已断开连接", LogSource);
    }

    /// <summary>重启控制器(会中断连接，需重新 Connect)</summary>
    public bool Reboot(int timeoutMs = 120000)
    {
        LogWarn("正在重启控制器，请等待...", LogSource);
        return Execute(api => api.ControllerReboot(timeoutMs), "控制器重启");
    }

    /// <summary>把控制器参数/缓冲区保存到 Flash</summary>
    public bool SaveToFlash()
        => Execute(api => api.ControllerSaveToFlash(null!, null!, null!, null!), "保存到 Flash");

    /// <summary>读取控制器信息</summary>
    public AcsControllerInfo? ReadControllerInfo()
    {
        if (!IsConnected) return null;

        return Execute(api => new AcsControllerInfo
        {
            IsSimulator = State == AcsConnectionState.Simulator,
            FirmwareVersion = TryRead(() => api.GetFirmwareVersion()),
            SerialNumber = TryRead(() => api.GetSerialNumber()),
            LibraryVersion = FormatVersion(TryReadUInt(() => api.GetLibraryVersion())),
            NetLibraryVersion = FormatVersion(TryReadUInt(() => api.GetNETLibraryVersion())),
            UmdVersion = FormatVersion(TryReadUInt(() => api.GetUMDVersion())),
            AxisCount = TryReadDouble(() => api.GetAxesCount()),
            BufferCount = TryReadDouble(() => api.GetBuffersCount()),
        }, "读取控制器信息", null);
    }

    private static string? TryRead(Func<string> f)
    {
        try { return f(); } catch { return null; }
    }

    private static uint TryReadUInt(Func<uint> f)
    {
        try { return f(); } catch { return 0; }
    }

    private static double TryReadDouble(Func<double> f)
    {
        try { return f(); } catch { return 0; }
    }

    private static string? FormatVersion(uint value)
        => value == 0 ? null : $"{value} (0x{value:X8})";

    #endregion

    #region 轴对象

    /// <summary>获取指定轴(按轴号缓存)</summary>
    public AcsAxis GetAxis(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), "轴号不能为负数");
        lock (_sync)
        {
            if (!_axes.TryGetValue(index, out var axis))
            {
                axis = new AcsAxis(this, index);
                _axes[index] = axis;
            }
            return axis;
        }
    }

    /// <summary>获取指定轴并命名(命名会显示在界面/日志中)</summary>
    public AcsAxis GetAxis(int index, string name)
    {
        var axis = GetAxis(index);
        axis.Name = name;
        return axis;
    }

    /// <summary>按控制器报告出的轴数返回全部轴对象</summary>
    public IReadOnlyList<AcsAxis> GetAxes(int? axisCount = null)
    {
        var count = axisCount ?? (int)Math.Max(1, Math.Min(64, Info?.AxisCount ?? 0));
        if (count <= 0) count = 1;
        var list = new List<AcsAxis>(count);
        for (var i = 0; i < count; i++) list.Add(GetAxis(i));
        return list;
    }

    #endregion

    #region 全局操作

    /// <summary>使能全部轴(0 ~ count-1)</summary>
    public bool EnableAll(int count)
    {
        var ok = true;
        for (var i = 0; i < count; i++)
        {
            ok &= GetAxis(i).Enable();
        }
        LogInfo($"全部使能({count} 轴) 结果: {(ok ? "成功" : "存在失败")}", LogSource);
        return ok;
    }

    /// <summary>断开全部轴的伺服</summary>
    public bool DisableAll()
        => Execute(api => api.DisableAll(), "全部下使能");

    /// <summary>全部轴停止(按各轴减速度平滑停止)</summary>
    public bool StopAll(int count)
    {
        var ok = true;
        for (var i = 0; i < count; i++) ok &= GetAxis(i).Stop();
        LogWarn($"全部轴停止({count} 轴)", LogSource);
        return ok;
    }

    /// <summary>全部轴紧急停止(按急停减速度 Kill 停止)</summary>
    public bool EmergencyStopAll(int count)
    {
        var ok = true;
        for (var i = 0; i < count; i++) ok &= GetAxis(i).EmergencyStop();
        LogWarn($"全部轴紧急停止({count} 轴)", LogSource);
        return ok;
    }

    /// <summary>清除全部轴故障</summary>
    public bool ClearFaultsAll(int count)
    {
        var ok = true;
        for (var i = 0; i < count; i++) ok &= GetAxis(i).ClearFault();
        LogInfo($"清除全部轴故障({count} 轴)", LogSource);
        return ok;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Disconnect();
        }
        catch
        {
            // 释放阶段的异常忽略
        }
    }
}

/// <summary>ACS 操作异常</summary>
public sealed class AcsOperationException : Exception
{
    public AcsOperationException(string message, Exception? inner = null) : base(message, inner) { }
}
