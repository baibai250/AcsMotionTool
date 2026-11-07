using System.Windows;

namespace AcsMotionTool.App;

/// <summary>
/// 主窗口(纯视图):DataContext 由 App 组合根注入(MainViewModel)，
/// 代码后置里没有任何业务逻辑与事件处理器。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();
}
