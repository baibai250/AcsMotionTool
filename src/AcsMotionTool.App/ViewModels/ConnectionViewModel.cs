using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;
using AcsMotionTool.Core;

namespace AcsMotionTool.App.ViewModels;

/// <summary>
/// 连接页 ViewModel:通讯参数、连接/断开、控制器信息、重启/保存 Flash、轴数与 IO 规模设置。
/// </summary>
public sealed class ConnectionViewModel : ViewModelBase
{
    private readonly IAcsService _acs;
    private readonly IAppSettingsService _settings;
    private readonly ILogService _log;
    private readonly IDialogService _dialog;
    private readonly ISoftLimitStore _softLimitStore;

    public ConnectionViewModel(IAcsService acs, IAppSettingsService settings, ILogService log, IDialogService dialog,
        ISoftLimitStore softLimitStore)
    {
        _acs = acs;
        _settings = settings;
        _log = log;
        _dialog = dialog;
        _softLimitStore = softLimitStore;

        var connection = settings.Current.Connection;
        _useSimulator = connection.Mode == AcsConnectionMode.Simulator;
        _ipAddress = connection.IpAddress;
        _protocolIndex = connection.Protocol == AcsEthernetProtocol.UdpPointToPoint ? 0 : 1;
        _timeoutMs = connection.TimeoutMs;
        _rebootAfterConnect = connection.RebootAfterConnect;
        _pollIntervalMs = settings.Current.PollIntervalMs;
        _autoConnect = settings.Current.AutoConnectOnStartup;
        _axisCount = Math.Max(1, settings.Current.Axes.Count == 0 ? 4 : settings.Current.Axes.Max(a => a.Index) + 1);
        _ioPortCount = settings.Current.IOPortCount;
        _ioBitsPerPort = settings.Current.IOBitsPerPort;

        var database = settings.Current.Database;
        _dbEnabled = database.Enabled;
        _dbHost = database.Host;
        _dbPort = database.Port;
        _dbUser = database.User;
        _dbPassword = database.Password;
        _dbName = database.Database;
        _dbAutoSave = database.SaveSoftLimitsAutomatically;
        _dbLoadOnStartup = database.LoadSoftLimitsOnStartup;
        _dbStatusText = database.Enabled ? "未测试" : "已禁用";

        ConnectCommand = CreateAsyncCommand(ConnectAsync, () => !IsConnected);
        DisconnectCommand = CreateAsyncCommand(DisconnectAsync, () => IsConnected);
        RefreshInfoCommand = CreateAsyncCommand(RefreshInfoAsync, () => IsConnected);
        RebootCommand = CreateAsyncCommand(RebootAsync, () => IsConnected);
        SaveToFlashCommand = CreateAsyncCommand(SaveToFlashAsync, () => IsConnected);
        SaveSettingsCommand = CreateCommand(SaveSettings);
        ApplyAxisCountCommand = CreateCommand(ApplyAxisCount);
        ApplyIoLayoutCommand = CreateCommand(ApplyIoLayout);
        TestDatabaseCommand = CreateAsyncCommand(TestDatabaseAsync, () => DbEnabled);
        InitializeDatabaseCommand = CreateAsyncCommand(InitializeDatabaseAsync, () => DbEnabled);
        SaveDatabaseSettingsCommand = CreateCommand(SaveDatabaseSettings);

        _acs.ConnectionStateChanged += OnConnectionStateChanged;
    }

    /// <summary>轴数量被应用(由 MainViewModel 转发给轴页)</summary>
    public event EventHandler<int>? AxisCountApplied;

    /// <summary>IO 规模被应用(由 MainViewModel 转发给 IO 页)</summary>
    public event EventHandler<(int Ports, int Bits)>? IoLayoutApplied;

    #region 绑定属性

    private bool _useSimulator;
    private string _ipAddress = "192.168.0.10";
    private int _protocolIndex;
    private int _timeoutMs = 5000;
    private bool _rebootAfterConnect;
    private int _pollIntervalMs = 200;
    private bool _autoConnect;
    private int _axisCount = 4;
    private int _ioPortCount = 2;
    private int _ioBitsPerPort = 16;
    private string _controllerInfoText = "(未连接)";

    public bool UseSimulator { get => _useSimulator; set => SetField(ref _useSimulator, value); }
    public string IpAddress { get => _ipAddress; set => SetField(ref _ipAddress, value); }
    public int ProtocolIndex { get => _protocolIndex; set => SetField(ref _protocolIndex, value); }
    public int TimeoutMs { get => _timeoutMs; set => SetField(ref _timeoutMs, value); }
    public bool RebootAfterConnect { get => _rebootAfterConnect; set => SetField(ref _rebootAfterConnect, value); }
    public bool AutoConnectOnStartup { get => _autoConnect; set => SetField(ref _autoConnect, value); }

    public int PollIntervalMs
    {
        get => _pollIntervalMs;
        set
        {
            if (!SetField(ref _pollIntervalMs, value)) return;
            _acs.PollIntervalMs = Math.Max(50, value);
        }
    }

    public int AxisCount { get => _axisCount; set => SetField(ref _axisCount, value); }
    public int IoPortCount { get => _ioPortCount; set => SetField(ref _ioPortCount, value); }
    public int IoBitsPerPort { get => _ioBitsPerPort; set => SetField(ref _ioBitsPerPort, value); }

    public string ControllerInfoText { get => _controllerInfoText; set => SetField(ref _controllerInfoText, value); }

    public bool IsConnected => _acs.IsConnected;

    #endregion

    #region 命令

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand RefreshInfoCommand { get; }
    public AsyncRelayCommand RebootCommand { get; }
    public AsyncRelayCommand SaveToFlashCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ApplyAxisCountCommand { get; }
    public RelayCommand ApplyIoLayoutCommand { get; }

    /// <summary>测试数据库连接</summary>
    public AsyncRelayCommand TestDatabaseCommand { get; }

    /// <summary>初始化数据库(建库建表)</summary>
    public AsyncRelayCommand InitializeDatabaseCommand { get; }

    /// <summary>保存数据库连接参数到本地设置</summary>
    public RelayCommand SaveDatabaseSettingsCommand { get; }

    #endregion

    #region 数据库设置

    private bool _dbEnabled = true;
    private string _dbHost = "127.0.0.1";
    private int _dbPort = 3306;
    private string _dbUser = "root";
    private string _dbPassword = "123456";
    private string _dbName = "acs_motion";
    private bool _dbAutoSave = true;
    private bool _dbLoadOnStartup = true;
    private string _dbStatusText = "未测试";

    /// <summary>是否启用数据库(关闭后软限位只保存在本地设置文件)</summary>
    public bool DbEnabled
    {
        get => _dbEnabled;
        set
        {
            if (SetField(ref _dbEnabled, value)) RaiseAllCanExecuteChanged();
        }
    }

    public string DbHost { get => _dbHost; set => SetField(ref _dbHost, value); }
    public int DbPort { get => _dbPort; set => SetField(ref _dbPort, value); }
    public string DbUser { get => _dbUser; set => SetField(ref _dbUser, value); }
    public string DbPassword { get => _dbPassword; set => SetField(ref _dbPassword, value); }
    public string DbName { get => _dbName; set => SetField(ref _dbName, value); }

    /// <summary>界面点“应用软限位”时自动写入数据库</summary>
    public bool DbAutoSave { get => _dbAutoSave; set => SetField(ref _dbAutoSave, value); }

    /// <summary>启动时从数据库加载软限位</summary>
    public bool DbLoadOnStartup { get => _dbLoadOnStartup; set => SetField(ref _dbLoadOnStartup, value); }

    /// <summary>数据库状态提示</summary>
    public string DbStatusText { get => _dbStatusText; set => SetField(ref _dbStatusText, value); }

    /// <summary>把界面上的数据库参数写入设置对象(不落盘)</summary>
    public void CaptureDatabaseOptions()
    {
        var database = _settings.Current.Database;
        database.Enabled = DbEnabled;
        database.Host = DbHost?.Trim() ?? string.Empty;
        database.Port = DbPort <= 0 ? 3306 : DbPort;
        database.User = DbUser?.Trim() ?? string.Empty;
        database.Password = DbPassword ?? string.Empty;
        database.Database = string.IsNullOrWhiteSpace(DbName) ? "acs_motion" : DbName.Trim();
        database.SaveSoftLimitsAutomatically = DbAutoSave;
        database.LoadSoftLimitsOnStartup = DbLoadOnStartup;
    }

    private async Task TestDatabaseAsync()
    {
        CaptureDatabaseOptions();
        DbStatusText = "正在测试...";
        var result = await _softLimitStore.TestConnectionAsync(_settings.Current.Database).ConfigureAwait(true);
        DbStatusText = result.Message;
        if (result.Success) _log.Info($"数据库测试: {result.Message}", "数据库");
        else _log.Error($"数据库测试: {result.Message}", "数据库");
        RaiseAllCanExecuteChanged();
    }

    private async Task InitializeDatabaseAsync()
    {
        CaptureDatabaseOptions();
        DbStatusText = "正在初始化...";
        var result = await _softLimitStore.InitializeAsync(_settings.Current.Database).ConfigureAwait(true);
        DbStatusText = result.Message;
        if (result.Success)
        {
            _log.Info($"数据库初始化: {result.Message}", "数据库");
            SaveDatabaseSettings();
        }
        else
        {
            _log.Error($"数据库初始化失败: {result.Message}", "数据库");
        }
    }

    private void SaveDatabaseSettings()
    {
        CaptureDatabaseOptions();
        if (_settings.Save()) _log.Info($"数据库设置已保存: {_settings.Current.Database.Describe()}", "数据库");
        else _log.Warn($"数据库设置保存失败: {_settings.Path}；原因: {_settings.LastError ?? "未知"}", "数据库");
    }

    #endregion

    private async Task ConnectAsync()
    {
        var options = BuildOptions();
        _log.Info($"开始连接 {options.Describe()}", "连接");
        var ok = await _acs.ConnectAsync(options).ConfigureAwait(true);

        if (ok)
        {
            var info = _acs.Info;
            ControllerInfoText = info?.ToDisplayString() ?? "(无控制器信息)";
            _log.Info($"连接成功: {options.Describe()}", "连接");
            SaveSettings(silent: true);
        }
        else
        {
            ControllerInfoText = "(连接失败)";
            _log.Error($"连接失败: {options.Describe()}，请检查网线/IP/防火墙与控制器电源", "连接");
        }

        OnPropertyChanged(nameof(IsConnected));
        RaiseAllCanExecuteChanged();
    }

    private async Task DisconnectAsync()
    {
        await _acs.DisconnectAsync().ConfigureAwait(true);
        ControllerInfoText = "(未连接)";
        OnPropertyChanged(nameof(IsConnected));
        RaiseAllCanExecuteChanged();
    }

    private async Task RefreshInfoAsync()
    {
        var info = await _acs.ReadControllerInfoAsync().ConfigureAwait(true);
        ControllerInfoText = info?.ToDisplayString() ?? "(读取失败)";
    }

    private async Task RebootAsync()
    {
        if (!_dialog.Confirm("确定要重启 ACS 控制器吗?重启期间通讯会中断。")) return;
        await _acs.RebootAsync(_settings.Current.Connection.RebootTimeoutMs).ConfigureAwait(true);
        await _acs.DisconnectAsync().ConfigureAwait(true);
        ControllerInfoText = "(控制器重启中，请稍后重新连接)";
        OnPropertyChanged(nameof(IsConnected));
        RaiseAllCanExecuteChanged();
    }

    private async Task SaveToFlashAsync()
    {
        if (!_dialog.Confirm("确定要把控制器参数/程序保存到 Flash 吗?")) return;
        await _acs.SaveToFlashAsync().ConfigureAwait(true);
    }

    /// <summary>应用轴数量</summary>
    private void ApplyAxisCount()
    {
        AxisCountApplied?.Invoke(this, Math.Max(1, AxisCount));
        SaveSettings(silent: true);
    }

    /// <summary>应用 IO 规模</summary>
    private void ApplyIoLayout()
    {
        IoLayoutApplied?.Invoke(this, (Math.Max(1, IoPortCount), Math.Clamp(IoBitsPerPort, 1, 32)));
        SaveSettings(silent: true);
    }

    public void SaveSettings() => SaveSettings(silent: false);

    private void SaveSettings(bool silent)
    {
        var settings = _settings.Current;
        settings.Connection.Mode = UseSimulator ? AcsConnectionMode.Simulator : AcsConnectionMode.Ethernet;
        settings.Connection.IpAddress = IpAddress?.Trim() ?? string.Empty;
        settings.Connection.Protocol = ProtocolIndex == 0 ? AcsEthernetProtocol.UdpPointToPoint : AcsEthernetProtocol.TcpStream;
        settings.Connection.TimeoutMs = Math.Max(1000, TimeoutMs);
        settings.Connection.RebootAfterConnect = RebootAfterConnect;
        settings.PollIntervalMs = Math.Max(50, PollIntervalMs);
        settings.AutoConnectOnStartup = AutoConnectOnStartup;
        settings.IOPortCount = Math.Max(1, IoPortCount);
        settings.IOBitsPerPort = Math.Clamp(IoBitsPerPort, 1, 32);
        CaptureDatabaseOptions();

        if (_settings.Save())
        {
            if (!silent) _log.Info($"设置已保存到 {_settings.Path}", "设置");
        }
        else
        {
            // 把真实失败原因(权限/磁盘/序列化)写进日志，便于现场排查
            _log.Warn($"设置保存失败: {_settings.Path}；原因: {_settings.LastError ?? "未知"}", "设置");
        }
    }

    private AcsConnectionOptions BuildOptions() => new()
    {
        Mode = UseSimulator ? AcsConnectionMode.Simulator : AcsConnectionMode.Ethernet,
        IpAddress = IpAddress?.Trim() ?? string.Empty,
        Protocol = ProtocolIndex == 0 ? AcsEthernetProtocol.UdpPointToPoint : AcsEthernetProtocol.TcpStream,
        TimeoutMs = Math.Max(1000, TimeoutMs),
        RebootAfterConnect = RebootAfterConnect,
        RebootTimeoutMs = _settings.Current.Connection.RebootTimeoutMs,
    };

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        OnPropertyChanged(nameof(IsConnected));
        RaiseAllCanExecuteChanged();
    }
}
