using System.Windows.Input;

namespace AcsMotionTool.App.Infrastructure;

/// <summary>可主动刷新可用状态的命令</summary>
public interface IRelayCommand : ICommand
{
    void RaiseCanExecuteChanged();
}

/// <summary>同步命令</summary>
public sealed class RelayCommand : IRelayCommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute()) { }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// 异步命令:执行期间自动置为不可用(避免重复点击)，异常统一交给 onError 处理，
/// 不会像 async void 那样把异常抛到 UI 线程之外。
/// </summary>
public sealed class AsyncRelayCommand : IRelayCommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute(), onError) { }

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>是否正在执行</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    /// <summary>
    /// 执行命令(返回可等待的 Task，便于自动化测试/顺序调用)。
    /// 异常不会外抛，统一交给构造时传入的 onError 处理。
    /// </summary>
    public async Task ExecuteAsync(object? parameter = null)
    {
        if (_isRunning) return;
        if (_canExecute != null && !_canExecute(parameter)) return;

        IsRunning = true;
        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
