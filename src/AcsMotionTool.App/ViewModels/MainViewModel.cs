using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;

namespace AcsMotionTool.App.ViewModels;

/// <summary>
/// 主 ViewModel:组合各页 ViewModel，负责跨页协作(轴数量/IO 规模应用、命令异常统一记日志、状态栏)。
/// 视图(Window/UserControl)只通过绑定与它及各子 ViewModel 交互，没有任何事件处理器业务逻辑。
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IAcsService _acs;
    private bool _disposed;
    private readonly ILogService _log;

    public MainViewModel(
        IAcsService acs,
        ILogService log,
        ConnectionViewModel connection,
        AxisViewModel axis,
        IoViewModel io,
        VariableViewModel variables,
        TerminalViewModel terminal,
        ProgramViewModel program,
        LogViewModel logs)
    {
        _acs = acs;
        _log = log;
        Connection = connection;
        Axis = axis;
        Io = io;
        Variables = variables;
        Terminal = terminal;
        Program = program;
        Logs = logs;

        // 跨页协作:连接页“应用”按钮的结果转发到轴页 / IO 页
        Connection.AxisCountApplied += (_, count) => Axis.SetAxisCount(count);
        Connection.IoLayoutApplied += (_, layout) => Io.ApplyLayout(layout.Ports, layout.Bits);

        // 状态栏跟随连接状态与选中轴
        _acs.ConnectionStateChanged += (_, _) => OnPropertyChanged(nameof(ConnectionStatus));
        Axis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AxisViewModel.SelectedAxisName)) OnPropertyChanged(nameof(SelectedAxisName));
        };

        // 状态栏显示轮询健康度(耗时/间隔)，长时间运行是否变慢一眼可见
        _acs.PollMetricsUpdated += (_, m) =>
            PollHealthText = $"轮询: {m.DurationMs}ms / 间隔 {m.EffectiveIntervalMs}ms (第 {m.PollCount} 次)";

        // 各子 ViewModel 的异步命令异常统一记录到日志
        foreach (var vm in EnumerateChildViewModels())
        {
            vm.CommandFailed += (_, ex) => Logs.ReportCommandError(ex);
        }

        _log.Info("ACS 调试工具已启动", "UI");
    }

    public ConnectionViewModel Connection { get; }
    public AxisViewModel Axis { get; }
    public IoViewModel Io { get; }
    public VariableViewModel Variables { get; }
    public TerminalViewModel Terminal { get; }
    public ProgramViewModel Program { get; }
    public LogViewModel Logs { get; }

    /// <summary>状态栏:连接状态</summary>
    public string ConnectionStatus => _acs.IsConnected
        ? $"已连接 ({(string.IsNullOrEmpty(_acs.Info?.SerialNumber) ? (_acs.IsSimulator ? "模拟器" : "以太网") : _acs.Info!.SerialNumber)})"
        : "未连接";

    /// <summary>状态栏:选中轴</summary>
    public string SelectedAxisName => Axis.SelectedAxisName;

    private string _pollHealthText = "轮询: 未启动";

    /// <summary>状态栏:轮询耗时/间隔</summary>
    public string PollHealthText
    {
        get => _pollHealthText;
        private set => SetField(ref _pollHealthText, value);
    }

    private IEnumerable<ViewModelBase> EnumerateChildViewModels()
    {
        yield return Connection;
        yield return Axis;
        yield return Io;
        yield return Variables;
        yield return Terminal;
        yield return Program;
        yield return Logs;
    }

    /// <summary>释放服务(停止轮询、断开通讯)</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_acs is IDisposable disposable) disposable.Dispose();
    }
}
