using System.Collections.ObjectModel;
using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;

namespace AcsMotionTool.App.ViewModels;

/// <summary>命令终端页 ViewModel:ACSPL+ 命令下发与应答显示</summary>
public sealed class TerminalViewModel : ViewModelBase
{
    private readonly IAcsService _acs;
    private readonly ILogService _log;

    public TerminalViewModel(IAcsService acs, ILogService log)
    {
        _acs = acs;
        _log = log;

        ExecuteCommand = CreateAsyncCommand(ExecuteAsync, () => HasInput && IsConnected);
        SendCommand = CreateAsyncCommand(SendAsync, () => HasInput && IsConnected);
        QuickCommandCommand = CreateAsyncCommand(ExecuteQuickAsync, () => IsConnected);
        ClearCommand = CreateCommand(() => Output.Clear());
    }

    /// <summary>快捷查询按钮</summary>
    public IReadOnlyList<string> QuickCommands { get; } = new[]
    {
        "?FPOS(0)", "?RPOS(0)", "?APOS(0)", "?MST(0)", "?VEL(0)", "?ACC(0)", "?DEC(0)",
        "?JERK(0)", "?KDEC(0)", "?IN(0)", "?OUT(0)", "?SYSINFO(2)",
    };

    public ObservableCollection<string> Output { get; } = new();

    /// <summary>终端输出上限(长时间运行避免无限增长)</summary>
    private const int MaxOutputLines = 2000;

    /// <summary>追加一行输出，超过上限时批量裁剪</summary>
    private void AppendOutput(string line)
    {
        Output.Add(line);
        if (Output.Count > MaxOutputLines + 200)
        {
            for (var i = 0; i < 200 && Output.Count > 0; i++) Output.RemoveAt(0);
        }
    }

    private string _input = "?FPOS(0)";

    public string Input
    {
        get => _input;
        set
        {
            if (SetField(ref _input, value)) OnPropertyChanged(nameof(HasInput));
        }
    }

    public bool HasInput => !string.IsNullOrWhiteSpace(Input);

    public bool IsConnected => _acs.IsConnected;

    public AsyncRelayCommand ExecuteCommand { get; }
    public AsyncRelayCommand SendCommand { get; }
    public AsyncRelayCommand QuickCommandCommand { get; }
    public RelayCommand ClearCommand { get; }

    private Task ExecuteAsync() => ExecuteCommandTextAsync(Input?.Trim() ?? string.Empty);

    private Task SendAsync()
    {
        var command = Input?.Trim() ?? string.Empty;
        if (command.Length == 0) return Task.CompletedTask;
        AppendOutput($">> {command}");
        return _acs.SendCommandAsync(command);
    }

    private Task ExecuteQuickAsync(object? parameter)
    {
        var command = parameter as string ?? string.Empty;
        if (command.Length == 0) return Task.CompletedTask;
        Input = command;
        return ExecuteCommandTextAsync(command);
    }

    private async Task ExecuteCommandTextAsync(string command)
    {
        if (command.Length == 0) return;
        AppendOutput($"> {command}");
        var response = await _acs.ExecuteCommandAsync(command).ConfigureAwait(true);
        // 控制器应答常带 CR/LF 包裹，这里统一去掉首尾空白，避免显示成空行或错位
        AppendOutput(string.IsNullOrWhiteSpace(response) ? "(无响应)" : response.Trim());
    }

    public void NotifyConnectionChanged()
    {
        OnPropertyChanged(nameof(IsConnected));
        RaiseAllCanExecuteChanged();
    }
}
