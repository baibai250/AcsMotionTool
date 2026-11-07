namespace AcsMotionTool.Core;

/// <summary>日志级别</summary>
public enum AcsLogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>日志事件参数</summary>
public sealed class AcsLogEventArgs : EventArgs
{
    public AcsLogEventArgs(AcsLogLevel level, string message, string? source = null, Exception? exception = null)
    {
        Time = DateTime.Now;
        Level = level;
        Message = message;
        Source = source;
        Exception = exception;
    }

    public DateTime Time { get; }
    public AcsLogLevel Level { get; }
    public string Message { get; }
    public string? Source { get; }
    public Exception? Exception { get; }

    public string ToDisplayString()
    {
        var src = string.IsNullOrEmpty(Source) ? "ACS" : Source;
        var text = $"[{Time:HH:mm:ss.fff}] [{Level,-5}] [{src}] {Message}";
        if (Exception != null)
        {
            text += $" | {Exception.GetType().Name}: {Exception.Message}";
        }
        return text;
    }

    public override string ToString() => ToDisplayString();
}
