using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AcsMotionTool.App.Infrastructure;
using AcsMotionTool.App.Services;
using AcsMotionTool.Core;

namespace AcsMotionTool.App.ViewModels;

/// <summary>
/// 日志页 ViewModel:展示日志服务的集合，提供级别/关键字过滤、清空、保存。
/// </summary>
public sealed class LogViewModel : ViewModelBase
{
    private readonly ILogService _log;
    private readonly IDialogService _dialog;

    public LogViewModel(ILogService log, IDialogService dialog)
    {
        _log = log;
        _dialog = dialog;

        ClearCommand = CreateCommand(() => _log.Clear());
        SaveCommand = CreateCommand(Save);

        ItemsView = CollectionViewSource.GetDefaultView(_log.Items);
        ItemsView.Filter = FilterItem;
        _log.Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TotalCount));
    }

    public ObservableCollection<LogItem> Items => _log.Items;

    /// <summary>过滤后的视图(级别 + 关键字)</summary>
    public ICollectionView ItemsView { get; }

    public int TotalCount => ItemsView.Cast<object>().Count();

    private int _levelFilter;

    /// <summary>0=全部, 1=信息, 2=警告, 3=错误</summary>
    public int LevelFilter
    {
        get => _levelFilter;
        set
        {
            if (!SetField(ref _levelFilter, value)) return;
            ItemsView.Refresh();
            OnPropertyChanged(nameof(TotalCount));
        }
    }

    private string _searchText = string.Empty;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value)) return;
            ItemsView.Refresh();
            OnPropertyChanged(nameof(TotalCount));
        }
    }

    private bool _autoScroll = true;

    public bool AutoScroll { get => _autoScroll; set => SetField(ref _autoScroll, value); }

    public RelayCommand ClearCommand { get; }
    public RelayCommand SaveCommand { get; }

    private bool FilterItem(object obj)
    {
        if (obj is not LogItem item) return false;

        var levelOk = _levelFilter switch
        {
            1 => item.Level == AcsLogLevel.Info,
            2 => item.Level == AcsLogLevel.Warn,
            3 => item.Level == AcsLogLevel.Error,
            _ => true,
        };
        if (!levelOk) return false;

        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        var keyword = _searchText.Trim();
        return item.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void Save()
    {
        var path = _dialog.PickSaveFile("文本文件 (*.log)|*.log|所有文件 (*.*)|*.*",
            $"acs-log-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        if (path == null) return;
        _log.Save(path);
    }

    public void ReportCommandError(Exception exception, string? source = null)
        => _log.Error($"操作异常: {exception.GetType().Name}: {exception.Message}", source ?? "UI");

    public void Info(string message, string? source = null) => _log.Info(message, source);
    public void Warn(string message, string? source = null) => _log.Warn(message, source);
    public void Error(string message, string? source = null) => _log.Error(message, source);
    public void Add(AcsLogLevel level, string message, string? source = null) => _log.Add(level, message, source);
}
