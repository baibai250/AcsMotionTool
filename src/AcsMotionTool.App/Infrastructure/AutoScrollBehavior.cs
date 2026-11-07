using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace AcsMotionTool.App.Infrastructure;

/// <summary>
/// 列表自动滚动到末尾的附加行为:
/// 视图里只需写 &lt;ListBox infra:AutoScrollBehavior.IsEnabled="True" /&gt;，
/// 不需要在 View 的代码后置里写 ScrollIntoView。
/// </summary>
public static class AutoScrollBehavior
{
    private static readonly DependencyProperty HandlerProperty =
        DependencyProperty.RegisterAttached("Handler", typeof(NotifyCollectionChangedEventHandler),
            typeof(AutoScrollBehavior), new PropertyMetadata(null));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;
        if (listBox.Items is not INotifyCollectionChanged collection) return;

        if ((bool)e.NewValue)
        {
            if (listBox.GetValue(HandlerProperty) is NotifyCollectionChangedEventHandler) return;

            // 滚动节流:日志/终端高频追加时，最多每 200ms 滚动一次，
            // 避免每条日志都触发一次布局(长时间运行会明显拖慢界面)
            long lastScrollAt = 0;
            NotifyCollectionChangedEventHandler handler = (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add) return;
                if (listBox.Items.Count == 0) return;

                var now = Environment.TickCount64;
                if (now - lastScrollAt < 200) return;
                lastScrollAt = now;

                try
                {
                    listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
                }
                catch
                {
                    // 布局尚未完成时忽略
                }
            };

            collection.CollectionChanged += handler;
            listBox.SetValue(HandlerProperty, handler);
        }
        else if (listBox.GetValue(HandlerProperty) is NotifyCollectionChangedEventHandler handler)
        {
            collection.CollectionChanged -= handler;
            listBox.ClearValue(HandlerProperty);
        }
    }
}
