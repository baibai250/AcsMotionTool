using System.Collections.ObjectModel;
using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;
using AcsMotionTool.Core;

namespace AcsMotionTool.App.ViewModels;

/// <summary>初始化步骤状态</summary>
public enum InitializationStepState
{
    Pending,
    Running,
    Success,
    Failed,
    Skipped,
}

/// <summary>初始化界面上的一个步骤</summary>
public sealed class InitializationStepViewModel : ViewModelBase
{
    public InitializationStepViewModel(string title)
    {
        Title = title;
        _detail = "等待中…";
    }

    public string Title { get; }

    private string _detail;
    public string Detail
    {
        get => _detail;
        private set => SetField(ref _detail, value);
    }

    private InitializationStepState _state = InitializationStepState.Pending;
    public InitializationStepState State
    {
        get => _state;
        private set
        {
            if (!SetField(ref _state, value)) return;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(StateText));
        }
    }

    /// <summary>状态图标</summary>
    public string Icon => State switch
    {
        InitializationStepState.Running => "◐",
        InitializationStepState.Success => "✓",
        InitializationStepState.Failed => "✗",
        InitializationStepState.Skipped => "→",
        _ => "○",
    };

    public string StateText => State switch
    {
        InitializationStepState.Running => "进行中",
        InitializationStepState.Success => "成功",
        InitializationStepState.Failed => "失败",
        InitializationStepState.Skipped => "已跳过",
        _ => "等待",
    };

    public void Run(string detail = "进行中…")
    {
        State = InitializationStepState.Running;
        Detail = detail;
    }

    public void Succeed(string detail)
    {
        State = InitializationStepState.Success;
        Detail = detail;
    }

    public void Fail(string detail)
    {
        State = InitializationStepState.Failed;
        Detail = detail;
    }

    public void Skip(string detail)
    {
        State = InitializationStepState.Skipped;
        Detail = detail;
    }

    public void Reset()
    {
        State = InitializationStepState.Pending;
        Detail = "等待中…";
    }
}

/// <summary>
/// 启动初始化 ViewModel:依次完成 加载配置 → 初始化数据库 → 连接 ACS → 加载软限位 → 完成，
/// 并在成功时明确显示“ACS 初始化成功”。
/// </summary>
public sealed class InitializationViewModel : ViewModelBase
{
    private readonly IAcsService _acs;
    private readonly IAppSettingsService _settings;
    private readonly ILogService _log;
    private readonly ISoftLimitStore _softLimitStore;
    private readonly ISoftLimitSync? _softLimitSync;
    private readonly ConnectionViewModel _connection;

    private bool _running;

    public InitializationViewModel(
        IAcsService acs,
        IAppSettingsService settings,
        ILogService log,
        ISoftLimitStore softLimitStore,
        ConnectionViewModel connection,
        ISoftLimitSync? softLimitSync = null)
    {
        _acs = acs;
        _settings = settings;
        _log = log;
        _softLimitStore = softLimitStore;
        _connection = connection;
        _softLimitSync = softLimitSync;

        LoadSettingsStep = new InitializationStepViewModel("1. 加载本地配置");
        DatabaseStep = new InitializationStepViewModel("2. 初始化数据库(MySQL)");
        AcsConnectionStep = new InitializationStepViewModel("3. 连接 ACS 控制器");
        SoftLimitStep = new InitializationStepViewModel("4. 加载轴软限位");
        FinishStep = new InitializationStepViewModel("5. 完成");

        Steps = new ObservableCollection<InitializationStepViewModel>
        {
            LoadSettingsStep, DatabaseStep, AcsConnectionStep, SoftLimitStep, FinishStep,
        };

        StartCommand = CreateAsyncCommand(InitializeAsync, () => !_running);
        RetryCommand = CreateAsyncCommand(InitializeAsync, () => !_running);
        RetryWithSimulatorCommand = CreateAsyncCommand(RetryWithSimulatorAsync, () => !_running && HasFailure);
        EnterMainCommand = CreateCommand(RequestEnterMain, () => IsCompleted);
    }

    #region 步骤与状态

    public ObservableCollection<InitializationStepViewModel> Steps { get; }

    public InitializationStepViewModel LoadSettingsStep { get; }
    public InitializationStepViewModel DatabaseStep { get; }
    public InitializationStepViewModel AcsConnectionStep { get; }
    public InitializationStepViewModel SoftLimitStep { get; }
    public InitializationStepViewModel FinishStep { get; }

    public string Title => "ACS 运动控制调试工具";
    public string SubTitle => "正在初始化";

    private string _statusText = "准备初始化…";
    /// <summary>主状态文字(成功时为“ACS 初始化成功”)</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    private string _statusDetail = string.Empty;
    /// <summary>状态补充说明(控制器/数据库信息或失败原因)</summary>
    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetField(ref _statusDetail, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    public bool IsNotBusy => !IsBusy;

    private bool _isCompleted;
    /// <summary>是否可以进入主界面(成功或允许离线进入)</summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        private set
        {
            if (SetField(ref _isCompleted, value))
            {
                OnPropertyChanged(nameof(EnterMainText));
                RaiseAllCanExecuteChanged();
            }
        }
    }

    private bool _isSuccess;
    /// <summary>ACS 是否初始化成功</summary>
    public bool IsSuccess
    {
        get => _isSuccess;
        private set
        {
            if (SetField(ref _isSuccess, value)) OnPropertyChanged(nameof(StatusColor));
        }
    }

    private bool _hasFailure;
    /// <summary>是否出现失败(显示“重试”按钮)</summary>
    public bool HasFailure
    {
        get => _hasFailure;
        private set
        {
            if (SetField(ref _hasFailure, value)) RaiseAllCanExecuteChanged();
        }
    }

    /// <summary>进入主界面的按钮文字</summary>
    public string EnterMainText => IsSuccess ? "进入主界面" : "跳过并进入(离线)";

    /// <summary>状态文字颜色(供视图绑定，避免在 XAML 里写逻辑)</summary>
    public string StatusColor => IsSuccess ? "#1B7F32" : (HasFailure ? "#B3261E" : "#444444");

    #endregion

    #region 命令与事件

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }

    /// <summary>连接失败时“改用 SPiiPlus 模拟器重试”(无硬件环境也能完成初始化)</summary>
    public AsyncRelayCommand RetryWithSimulatorCommand { get; }

    public RelayCommand EnterMainCommand { get; }

    /// <summary>请求进入主界面</summary>
    public event EventHandler? EnterMainRequested;

    /// <summary>切换到模拟器并重新初始化</summary>
    private async Task RetryWithSimulatorAsync()
    {
        _log.Info("连接失败，改用 SPiiPlus 模拟器重试初始化", "初始化");
        _connection.UseSimulator = true;
        _connection.SaveSettingsCommand.Execute(null);
        await InitializeAsync().ConfigureAwait(true);
    }

    private void RequestEnterMain()
    {
        if (!IsCompleted) return;
        EnterMainRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    /// <summary>执行初始化(可重复调用:重试)</summary>
    public async Task InitializeAsync()
    {
        if (_running) return;
        _running = true;
        IsBusy = true;
        IsCompleted = false;
        HasFailure = false;
        IsSuccess = false;
        StatusText = "正在初始化…";
        StatusDetail = string.Empty;
        foreach (var step in Steps) step.Reset();
        RaiseAllCanExecuteChanged();

        var databaseReady = false;
        string? databaseMessage = null;

        try
        {
            // ---------- 1. 本地配置 ----------
            LoadSettingsStep.Run("读取 acs-settings.json…");
            await Task.Yield();
            var settings = _settings.Current;
            LoadSettingsStep.Succeed(
                $"配置: {_settings.Path}；通讯: {settings.Connection.Describe()}；轴数: {settings.Axes.Count}");

            // ---------- 2. 数据库 ----------
            if (!settings.Database.Enabled)
            {
                DatabaseStep.Skip("数据库中已禁用，软限位仅保存在本地设置文件");
            }
            else
            {
                DatabaseStep.Run($"连接 {settings.Database.Describe()} 并建库建表…");
                var init = await _softLimitStore.InitializeAsync(settings.Database).ConfigureAwait(true);
                databaseMessage = init.Message;
                if (init.Success)
                {
                    databaseReady = true;
                    DatabaseStep.Succeed(init.Message);
                }
                else
                {
                    DatabaseStep.Fail(init.Message);
                    _log.Warn($"数据库初始化失败: {init.Message}", "初始化");
                }
            }

            // ---------- 3. ACS 控制器 ----------
            if (!_settings.Current.AutoConnectOnStartup)
            {
                AcsConnectionStep.Skip("设置里关闭了“初始化时自动连接控制器”，可在主界面手动连接");
            }
            else
            {
                AcsConnectionStep.Run("打开通讯并读取控制器信息…");
                await _connection.ConnectCommand.ExecuteAsync().ConfigureAwait(true);
                if (_acs.IsConnected)
                {
                    var info = _acs.Info;
                    var target = _acs.IsSimulator ? "SPiiPlus 模拟器" : info?.SerialNumber ?? "以太网";
                    AcsConnectionStep.Succeed(
                        $"已连接: {target}；固件 {info?.FirmwareVersion ?? "?"}；轴数 {info?.AxisCount:F0}");
                }
                else
                {
                    AcsConnectionStep.Fail("连接失败:请检查控制器电源、网线、IP、防火墙，或在主界面手动连接");
                    _log.Error("ACS 控制器初始化连接失败", "初始化");
                }
            }

            // ---------- 4. 软限位 ----------
            if (_softLimitSync == null)
            {
                SoftLimitStep.Skip("未启用软限位同步");
            }
            else if (!databaseReady)
            {
                SoftLimitStep.Skip("数据库不可用，使用本地设置中的软限位");
            }
            else if (!_settings.Current.Database.LoadSoftLimitsOnStartup)
            {
                SoftLimitStep.Skip("已按设置跳过从数据库加载");
            }
            else
            {
                SoftLimitStep.Run("从数据库读取各轴软限位…");
                var (count, message) = await _softLimitSync.LoadFromDatabaseAsync().ConfigureAwait(true);
                if (count > 0) SoftLimitStep.Succeed(message);
                else SoftLimitStep.Succeed("数据库暂无软限位记录，使用本地设置");
            }

            // ---------- 5. 完成 ----------
            if (_acs.IsConnected)
            {
                FinishStep.Succeed("初始化完成");
                IsSuccess = true;
                StatusText = "ACS 初始化成功";
                StatusDetail = BuildSuccessDetail(databaseReady, databaseMessage);
                _log.Info($"ACS 初始化成功: {StatusDetail}", "初始化");
            }
            else if (AcsConnectionStep.State == InitializationStepState.Skipped)
            {
                // 用户主动关闭了自动连接:不算失败，但也不能说“初始化成功”
                FinishStep.Skip("未连接控制器(已按设置跳过)");
                IsSuccess = false;
                HasFailure = false;
                StatusText = "初始化完成(未连接控制器)";
                StatusDetail = databaseReady
                    ? $"数据库 {_settings.Current.Database.Database} 已就绪；进入主界面后可在“连接”页手动连接控制器。"
                    : "数据库未就绪；进入主界面后可在“连接”页手动连接控制器。";
                _log.Info($"初始化完成(未连接控制器): {StatusDetail}", "初始化");
            }
            else
            {
                FinishStep.Fail("ACS 未连接，初始化未完成");
                HasFailure = true;
                StatusText = "ACS 初始化失败";
                StatusDetail = "控制器未连接。可以点击“重试”，或“跳过并进入”到主界面手动连接。";
                _log.Error("ACS 初始化失败: 控制器未连接", "初始化");
            }
        }
        catch (Exception ex)
        {
            FinishStep.Fail($"初始化异常: {ex.Message}");
            HasFailure = true;
            StatusText = "ACS 初始化失败";
            StatusDetail = $"初始化过程发生异常: {ex.Message}";
            _log.Error($"初始化异常: {ex.Message}", "初始化");
        }
        finally
        {
            _running = false;
            IsBusy = false;
            IsCompleted = true;
            RaiseAllCanExecuteChanged();
        }
    }

    private string BuildSuccessDetail(bool databaseReady, string? databaseMessage)
    {
        var acsPart = _acs.IsSimulator
            ? "SPiiPlus 模拟器"
            : $"{_acs.Info?.SerialNumber ?? "以太网"} (固件 {_acs.Info?.FirmwareVersion ?? "?"})";
        var dbPart = databaseReady
            ? $"数据库 {_settings.Current.Database.Database} 就绪"
            : "数据库未就绪(软限位仅本地保存)";
        return $"控制器: {acsPart}；{dbPart}" + (string.IsNullOrWhiteSpace(databaseMessage) ? "" : $"\n{databaseMessage}");
    }
}
