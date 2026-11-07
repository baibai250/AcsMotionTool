using System.Windows;

namespace AcsMotionTool.App.Views;

/// <summary>
/// 启动初始化界面(纯视图):DataContext 由组合根注入 InitializationViewModel，
/// 代码后置不含业务逻辑。
/// </summary>
public partial class InitializationWindow : Window
{
    public InitializationWindow() => InitializeComponent();
}
