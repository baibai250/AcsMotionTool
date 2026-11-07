namespace AcsMotionTool.App.Infrastructure;

/// <summary>
/// 对话框与文件选择抽象:让 ViewModel 不出现 MessageBox/SaveFileDialog 等视图代码，
/// 也让自动化测试可以替换成桩实现。
/// </summary>
public interface IDialogService
{
    void ShowInformation(string message, string title = "提示");
    void ShowWarning(string message, string title = "提示");
    void ShowError(string message, string title = "错误");

    /// <summary>确认对话框(确定/取消)</summary>
    bool Confirm(string message, string title = "确认");

    /// <summary>保存文件对话框，返回 null 表示用户取消</summary>
    string? PickSaveFile(string filter, string defaultFileName);
}

/// <summary>基于 WPF 的实现</summary>
public sealed class WpfDialogService : IDialogService
{
    public void ShowInformation(string message, string title = "提示")
        => System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    public void ShowWarning(string message, string title = "提示")
        => System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

    public void ShowError(string message, string title = "错误")
        => System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

    public bool Confirm(string message, string title = "确认")
        => System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    public string? PickSaveFile(string filter, string defaultFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
