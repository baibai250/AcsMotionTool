using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using AcsMotionTool.Core;
using AcsMotionTool.App.Infrastructure;

namespace AcsMotionTool.App.Services;

/// <summary>一条日志</summary>
public sealed class LogItem
{
    public LogItem(AcsLogLevel level, string message, string? source)
    {
        Time = DateTime.Now;
        Level = level;
        Message = message;
        Source = source;
    }

    public DateTime Time { get; }
    public AcsLogLevel Level { get; }
    public string Message { get; }
    public string? Source { get; }

    public string Text => $"[{Time:HH:mm:ss.fff}] [{Level,-5}] [{Source ?? "ACS"}] {Message}";

    /// <summary>级别中文名(界面胶囊用)</summary>
    public string LevelText => Level switch
    {
        AcsLogLevel.Debug => "调试",
        AcsLogLevel.Warn => "警告",
        AcsLogLevel.Error => "错误",
        _ => "信息",
    };

    public override string ToString() => Text;
}

/// <summary>日志服务:集中收集日志，供日志视图展示(集合更新始终发生在 UI 线程)</summary>
public interface ILogService
{
    ObservableCollection<LogItem> Items { get; }

    void Add(AcsLogLevel level, string message, string? source = null);
    void Info(string message, string? source = null);
    void Warn(string message, string? source = null);
    void Error(string message, string? source = null);
    void Clear();

    /// <summary>保存日志到文件</summary>
    bool Save(string path);

    event EventHandler? Cleared;
}

/// <inheritdoc />
public sealed class LogService : ILogService
{
    private const int MaxItems = 3000;

    /// <summary>超出上限后一次裁剪的条数(避免每条都 RemoveAt(0) 引起 O(n) 抖动)</summary>
    private const int TrimChunk = 200;

    /// <summary>每秒最多写入的日志条数(防止异常刷屏把界面拖死)</summary>
    private const int MaxItemsPerSecond = 200;

    private readonly IUiDispatcher _dispatcher;
    private readonly object _rateLock = new();
    private long _windowStartedAt;
    private int _windowCount;
    private long _dropped;

    public LogService(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Items = new ObservableCollection<LogItem>();
    }

    public ObservableCollection<LogItem> Items { get; }

    public event EventHandler? Cleared;

    public void Add(AcsLogLevel level, string message, string? source = null)
    {
        if (IsRateLimited()) return;

        var item = new LogItem(level, message, source);
        _dispatcher.Invoke(() =>
        {
            Items.Add(item);

            // 批量裁剪:只在明显超限时一次性移除一批，减少集合变更次数
            if (Items.Count > MaxItems + TrimChunk)
            {
                for (var i = 0; i < TrimChunk && Items.Count > 0; i++) Items.RemoveAt(0);
            }
        });
    }

    /// <summary>限流:每秒超过上限后丢弃并累计，下一次补充一条统计</summary>
    private bool IsRateLimited()
    {
        lock (_rateLock)
        {
            var now = Environment.TickCount64;
            if (now - _windowStartedAt >= 1000)
            {
                _windowStartedAt = now;
                if (_dropped > 0)
                {
                    var dropped = _dropped;
                    _dropped = 0;
                    _windowCount = 0;
                    _dispatcher.Invoke(() => Items.Add(new LogItem(AcsLogLevel.Warn,
                        $"上一秒日志过多，已丢弃 {dropped} 条", "Log")));
                }
                _windowCount = 0;
            }

            if (_windowCount >= MaxItemsPerSecond)
            {
                _dropped++;
                return true;
            }

            _windowCount++;
            return false;
        }
    }

    public void Info(string message, string? source = null) => Add(AcsLogLevel.Info, message, source);
    public void Warn(string message, string? source = null) => Add(AcsLogLevel.Warn, message, source);
    public void Error(string message, string? source = null) => Add(AcsLogLevel.Error, message, source);

    public void Clear()
    {
        _dispatcher.Invoke(() =>
        {
            Items.Clear();
            Cleared?.Invoke(this, EventArgs.Empty);
        });
    }

    public bool Save(string path)
    {
        try
        {
            File.WriteAllLines(path, Items.Select(i => i.Text), System.Text.Encoding.UTF8);
            Info($"日志已保存到 {path}", "Log");
            return true;
        }
        catch (Exception ex)
        {
            Error($"保存日志失败: {ex.Message}", "Log");
            return false;
        }
    }
}
