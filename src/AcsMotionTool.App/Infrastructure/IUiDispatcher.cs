using System.Windows.Threading;

namespace AcsMotionTool.App.Infrastructure;

/// <summary>
/// UI 线程调度抽象。ViewModel 通过它切换线程，从而**不直接依赖 WPF 的 Dispatcher**，
/// 便于在没有界面的环境下做单元测试/自动化验证。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>当前是否在 UI 线程</summary>
    bool IsOnUiThread { get; }

    /// <summary>在 UI 线程上同步执行</summary>
    void Invoke(Action action);

    /// <summary>在 UI 线程上异步执行</summary>
    /// <param name="background">true 时以低优先级(Background)排队，避免周期性刷新抢占输入与渲染</param>
    Task InvokeAsync(Action action, bool background = false);
}

/// <summary>基于 WPF Dispatcher 的实现(仅界面进程使用)</summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public bool IsOnUiThread => _dispatcher.CheckAccess();

    public void Invoke(Action action)
    {
        if (IsOnUiThread) action();
        else _dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Action action, bool background = false)
    {
        if (IsOnUiThread)
        {
            action();
            return Task.CompletedTask;
        }

        // 周期性刷新用 Background 优先级:输入与渲染优先处理，长时间运行也不容易“发卡”
        var priority = background ? DispatcherPriority.Background : DispatcherPriority.Normal;
        return _dispatcher.InvokeAsync(action, priority).Task;
    }
}

/// <summary>
/// 直接在当前线程执行的实现(用于控制台测试:事件与轮询都在后台线程完成，
/// 不需要 WPF，也就能在没有界面的情况下验证 ViewModel 逻辑)。
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => true;

    public void Invoke(Action action) => action();

    public Task InvokeAsync(Action action, bool background = false)
    {
        action();
        return Task.CompletedTask;
    }
}
