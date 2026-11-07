using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AcsMotionTool.App.Infrastructure;

/// <summary>
/// 所有 ViewModel 的基类:实现 INotifyPropertyChanged 并提供命令注册表，
/// 便于在状态变化(例如连接状态改变)时统一刷新所有命令的可用状态。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    private readonly List<IRelayCommand> _commands = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>通知所有属性变化(用于整行刷新)</summary>
    protected void OnAllPropertiesChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>创建一个同步命令(自动纳入命令注册表)</summary>
    protected RelayCommand CreateCommand(Action execute, Func<bool>? canExecute = null)
    {
        var command = new RelayCommand(_ => execute(), _ => canExecute?.Invoke() ?? true);
        _commands.Add(command);
        return command;
    }

    /// <summary>创建一个带参数命令</summary>
    protected RelayCommand CreateCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        var command = new RelayCommand(execute, canExecute);
        _commands.Add(command);
        return command;
    }

    /// <summary>创建一个异步命令(自动纳入命令注册表)</summary>
    protected AsyncRelayCommand CreateAsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        var command = new AsyncRelayCommand(_ => execute(), _ => canExecute?.Invoke() ?? true, ReportCommandError);
        _commands.Add(command);
        return command;
    }

    /// <summary>创建一个带参数的异步命令</summary>
    protected AsyncRelayCommand CreateAsyncCommand(Func<object?, Task> execute, Func<bool>? canExecute = null)
    {
        var command = new AsyncRelayCommand(execute, _ => canExecute?.Invoke() ?? true, ReportCommandError);
        _commands.Add(command);
        return command;
    }

    /// <summary>刷新所有命令的可用状态(CanExecute)</summary>
    protected void RaiseAllCanExecuteChanged()
    {
        foreach (var command in _commands) command.RaiseCanExecuteChanged();
    }

    /// <summary>异步命令内部未捕获异常的处理入口(默认转给日志服务)</summary>
    protected virtual void ReportCommandError(Exception exception)
        => CommandFailed?.Invoke(this, exception);

    /// <summary>命令执行异常事件(由 MainViewModel/日志服务统一记录)</summary>
    public event EventHandler<Exception>? CommandFailed;
}
