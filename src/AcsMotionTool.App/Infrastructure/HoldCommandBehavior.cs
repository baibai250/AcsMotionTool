using System.Windows;
using System.Windows.Input;

namespace AcsMotionTool.App.Infrastructure;

/// <summary>
/// “按住执行、松开停止”的按钮行为（用于方向点动）。
/// 鼠标松开、鼠标捕获丢失、窗口失焦、控件卸载都会触发 ReleaseCommand，
/// 保证“松手一定停”，避免轴在后台一直走。
/// </summary>
public static class HoldCommandBehavior
{
    public static readonly DependencyProperty PressCommandProperty = DependencyProperty.RegisterAttached(
        "PressCommand", typeof(ICommand), typeof(HoldCommandBehavior), new PropertyMetadata(null, OnPressCommandChanged));

    public static readonly DependencyProperty PressParameterProperty = DependencyProperty.RegisterAttached(
        "PressParameter", typeof(object), typeof(HoldCommandBehavior), new PropertyMetadata(null));

    public static readonly DependencyProperty ReleaseCommandProperty = DependencyProperty.RegisterAttached(
        "ReleaseCommand", typeof(ICommand), typeof(HoldCommandBehavior), new PropertyMetadata(null));

    public static readonly DependencyProperty ReleaseParameterProperty = DependencyProperty.RegisterAttached(
        "ReleaseParameter", typeof(object), typeof(HoldCommandBehavior), new PropertyMetadata(null));

    private static readonly DependencyProperty IsPressedProperty = DependencyProperty.RegisterAttached(
        "IsPressed", typeof(bool), typeof(HoldCommandBehavior), new PropertyMetadata(false));

    public static void SetPressCommand(DependencyObject element, ICommand? value) => element.SetValue(PressCommandProperty, value);
    public static ICommand? GetPressCommand(DependencyObject element) => (ICommand?)element.GetValue(PressCommandProperty);

    public static void SetPressParameter(DependencyObject element, object? value) => element.SetValue(PressParameterProperty, value);
    public static object? GetPressParameter(DependencyObject element) => element.GetValue(PressParameterProperty);

    public static void SetReleaseCommand(DependencyObject element, ICommand? value) => element.SetValue(ReleaseCommandProperty, value);
    public static ICommand? GetReleaseCommand(DependencyObject element) => (ICommand?)element.GetValue(ReleaseCommandProperty);

    public static void SetReleaseParameter(DependencyObject element, object? value) => element.SetValue(ReleaseParameterProperty, value);
    public static object? GetReleaseParameter(DependencyObject element) => element.GetValue(ReleaseParameterProperty);

    private static void OnPressCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        element.PreviewMouseLeftButtonDown -= OnMouseDown;
        element.PreviewMouseLeftButtonUp -= OnMouseUp;
        element.LostMouseCapture -= OnLostMouseCapture;
        element.Unloaded -= OnUnloaded;

        if (e.NewValue is ICommand)
        {
            element.PreviewMouseLeftButtonDown += OnMouseDown;
            element.PreviewMouseLeftButtonUp += OnMouseUp;
            element.LostMouseCapture += OnLostMouseCapture;
            element.Unloaded += OnUnloaded;
        }
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || (bool)element.GetValue(IsPressedProperty)) return;

        var command = GetPressCommand(element);
        var parameter = GetPressParameter(element);
        if (command == null || !command.CanExecute(parameter)) return;

        element.SetValue(IsPressedProperty, true);
        element.CaptureMouse();
        command.Execute(parameter);
        e.Handled = true;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element) Release(element);
        e.Handled = true;
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element) Release(element);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element) Release(element);
    }

    private static void Release(FrameworkElement element)
    {
        if (!(bool)element.GetValue(IsPressedProperty)) return;

        // 先清状态再释放鼠标，避免 LostMouseCapture 回调递归
        element.SetValue(IsPressedProperty, false);
        if (element.IsMouseCaptured) element.ReleaseMouseCapture();

        var command = GetReleaseCommand(element);
        var parameter = GetReleaseParameter(element);
        if (command != null && command.CanExecute(parameter)) command.Execute(parameter);
    }
}
