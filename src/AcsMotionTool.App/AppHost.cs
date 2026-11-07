using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;
using AcsMotionTool.App.ViewModels;
using AcsMotionTool.Core;
namespace AcsMotionTool.App;

/// <summary>
/// 组合根:集中创建服务与 ViewModel(手工依赖注入，零第三方容器)。
/// 初始化界面与主界面共享同一套服务/ViewModel 实例，因此初始化阶段建立的连接、
/// 加载的软限位能直接出现在主界面上。
/// </summary>
public sealed class AppHost : IDisposable
{
    private AppHost(
        IUiDispatcher dispatcher,
        IDialogService dialog,
        ILogService log,
        IAppSettingsService settings,
        ISoftLimitStore softLimitStore,
        IAcsService acs,
        ConnectionViewModel connection,
        AxisViewModel axis,
        IoViewModel io,
        VariableViewModel variables,
        TerminalViewModel terminal,
        ProgramViewModel program,
        LogViewModel logs,
        MainViewModel main,
        InitializationViewModel initialization)
    {
        Dispatcher = dispatcher;
        Dialog = dialog;
        Log = log;
        Settings = settings;
        SoftLimitStore = softLimitStore;
        Acs = acs;
        Connection = connection;
        Axis = axis;
        Io = io;
        Variables = variables;
        Terminal = terminal;
        Program = program;
        Logs = logs;
        Main = main;
        Initialization = initialization;
    }

    public IUiDispatcher Dispatcher { get; }
    public IDialogService Dialog { get; }
    public ILogService Log { get; }
    public IAppSettingsService Settings { get; }
    public ISoftLimitStore SoftLimitStore { get; }
    public IAcsService Acs { get; }

    public ConnectionViewModel Connection { get; }
    public AxisViewModel Axis { get; }
    public IoViewModel Io { get; }
    public VariableViewModel Variables { get; }
    public TerminalViewModel Terminal { get; }
    public ProgramViewModel Program { get; }
    public LogViewModel Logs { get; }
    public MainViewModel Main { get; }
    public InitializationViewModel Initialization { get; }

    /// <summary>组装整个应用(界面进程与无界面测试共用)</summary>
    public static AppHost Create(IUiDispatcher dispatcher, IDialogService dialog, IAppSettingsService? settings = null,
        ISoftLimitStore? softLimitStore = null)
    {
        var log = new LogService(dispatcher);
        settings ??= new AppSettingsService();
        softLimitStore ??= new MySqlSoftLimitStore();

        var acs = new AcsService(dispatcher, log)
        {
            PollIntervalMs = Math.Max(50, settings.Current.PollIntervalMs),
        };

        var connection = new ConnectionViewModel(acs, settings, log, dialog, softLimitStore);
        var axis = new AxisViewModel(acs, settings, log, softLimitStore);
        var io = new IoViewModel(acs, settings, log);
        var variables = new VariableViewModel(acs, settings, log);
        var terminal = new TerminalViewModel(acs, log);
        var program = new ProgramViewModel(acs, log);
        var logs = new LogViewModel(log, dialog);

        var main = new MainViewModel(acs, log, connection, axis, io, variables, terminal, program, logs);
        var initialization = new InitializationViewModel(acs, settings, log, softLimitStore, connection, axis);

        // 界面分割比例持久化(记住用户拖动的界面占比)
        GridLayoutBehavior.Preferences = new LayoutPreferences(settings);

        return new AppHost(dispatcher, dialog, log, settings, softLimitStore, acs,
            connection, axis, io, variables, terminal, program, logs, main, initialization);
    }

    public void Dispose()
    {
        Main.Dispose();
        if (Acs is IDisposable disposableAcs && !ReferenceEquals(disposableAcs, Main)) disposableAcs.Dispose();
    }
}
