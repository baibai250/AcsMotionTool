using System.Windows;
using System.Windows.Controls;
using AcsMotionTool.App.Services;
using AcsMotionTool.Core;

namespace AcsMotionTool.App.Infrastructure;

/// <summary>
/// Grid 分割比例持久化附加行为。
///
/// 用法(视图里只写一行):
///   &lt;Grid infra:GridLayoutBehavior.PersistKey="Axis"&gt;
///
/// 行为:
///   1) 视图加载时把上次保存的“星号比例”应用到 ColumnDefinition/RowDefinition(Width/Height = 比例*);
///   2) 窗口关闭时读取当前各星号列/行的实际宽度，换算成相对比例保存(通过 ILayoutPreferences)。
/// 只有星号(*)大小的定义参与保存/还原，像素与 Auto 定义保持原样，因此不会破坏布局语义。
/// </summary>
public static class GridLayoutBehavior
{
    /// <summary>由组合根注入(未注入时行为退化为“仅本次会话可拖动”)</summary>
    public static ILayoutPreferences? Preferences { get; set; }

    public static readonly DependencyProperty PersistKeyProperty =
        DependencyProperty.RegisterAttached(
            "PersistKey",
            typeof(string),
            typeof(GridLayoutBehavior),
            new PropertyMetadata(null, OnPersistKeyChanged));

    public static void SetPersistKey(DependencyObject element, string value) => element.SetValue(PersistKeyProperty, value);

    public static string? GetPersistKey(DependencyObject element) => (string?)element.GetValue(PersistKeyProperty);

    /// <summary>保存窗口关闭钩子，避免重复订阅</summary>
    private static readonly Dictionary<Grid, Window> HookedWindows = new();

    private static void OnPersistKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid) return;
        if (string.IsNullOrWhiteSpace(e.NewValue as string)) return;

        grid.Loaded -= OnGridLoaded;
        grid.Loaded += OnGridLoaded;
        grid.Unloaded -= OnGridUnloaded;
        grid.Unloaded += OnGridUnloaded;
    }

    private static void OnGridLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid) return;

        var key = GetPersistKey(grid);
        if (string.IsNullOrWhiteSpace(key)) return;

        Restore(grid, key);
        HookWindow(grid, key);
    }

    private static void OnGridUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Grid grid) return;

        var key = GetPersistKey(grid);
        if (!string.IsNullOrWhiteSpace(key)) Save(grid, key);

        if (HookedWindows.Remove(grid, out var window))
        {
            window.Closing -= OnWindowClosing;
        }
    }

    private static void HookWindow(Grid grid, string key)
    {
        var window = Window.GetWindow(grid);
        if (window == null) return;
        if (HookedWindows.TryGetValue(grid, out var existing) && ReferenceEquals(existing, window)) return;

        if (HookedWindows.Remove(grid, out var oldWindow)) oldWindow.Closing -= OnWindowClosing;

        HookedWindows[grid] = window;
        window.Closing += OnWindowClosing;
    }

    private static void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 窗口关闭时把所有挂在该窗口上的 Grid 比例存下来
        foreach (var pair in HookedWindows.ToList())
        {
            var key = GetPersistKey(pair.Key);
            if (string.IsNullOrWhiteSpace(key)) continue;
            Save(pair.Key, key!);
        }
    }

    #region 还原 / 保存

    private static void Restore(Grid grid, string key)
    {
        var snapshot = Preferences?.Get(key);
        if (snapshot == null) return;

        Apply(grid.ColumnDefinitions, snapshot.Columns, horizontal: true);
        Apply(grid.RowDefinitions, snapshot.Rows, horizontal: false);
    }

    private static void Apply(System.Collections.IEnumerable definitions, double[]? ratios, bool horizontal)
    {
        if (ratios == null || ratios.Length == 0) return;

        var index = 0;
        foreach (var definition in definitions)
        {
            if (definition is not (ColumnDefinition or RowDefinition)) continue;

            var isStar = definition switch
            {
                ColumnDefinition c => c.Width.IsStar,
                RowDefinition r => r.Height.IsStar,
                _ => false,
            };
            if (!isStar) continue;
            if (index >= ratios.Length) break;

            var ratio = Math.Max(0.05, ratios[index++]);
            if (definition is ColumnDefinition column)
            {
                column.Width = new GridLength(ratio, GridUnitType.Star);
            }
            else if (definition is RowDefinition row)
            {
                row.Height = new GridLength(ratio, GridUnitType.Star);
            }
        }
    }

    private static void Save(Grid grid, string key)
    {
        var preferences = Preferences;
        if (preferences == null) return;

        var columns = Capture(grid.ColumnDefinitions.Select(d => (d.Width.IsStar, d.ActualWidth)).ToList());
        var rows = Capture(grid.RowDefinitions.Select(d => (d.Height.IsStar, d.ActualHeight)).ToList());

        if (columns.Length < 2 && rows.Length < 2) return;   // 没有可拖动分割，无需保存

        preferences.Set(key, new AcsLayoutSnapshot { Columns = columns, Rows = rows });
    }

    /// <summary>把实际像素宽度换算成“相对最窄一栏的倍数”，忽略 Auto/像素定义</summary>
    private static double[] Capture(IReadOnlyList<(bool IsStar, double Actual)> sizes)
    {
        var widths = sizes.Where(s => s.IsStar && s.Actual > 1).Select(s => s.Actual).ToList();
        if (widths.Count < 2) return Array.Empty<double>();

        var min = widths.Min();
        if (min <= 0) return Array.Empty<double>();

        return widths.Select(w => Math.Round(w / min, 2)).ToArray();
    }

    #endregion
}
