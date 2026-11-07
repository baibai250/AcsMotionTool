using AcsMotionTool.Core;

namespace AcsMotionTool.App.Services;

/// <summary>应用设置服务:统一读写 acs-settings.json，各 ViewModel 共享同一个设置对象</summary>
public interface IAppSettingsService
{
    AcsAppSettings Current { get; }

    /// <summary>保存到磁盘</summary>
    bool Save();

    /// <summary>设置文件路径</summary>
    string Path { get; }

    /// <summary>最近一次保存失败的原因(成功时为 null)</summary>
    string? LastError { get; }
}

/// <inheritdoc />
public sealed class AppSettingsService : IAppSettingsService
{
    public AppSettingsService(AcsAppSettings? settings = null, string? path = null)
    {
        Path = path ?? AcsSettingsStore.DefaultPath;
        Current = settings ?? AcsSettingsStore.Load(Path);
    }

    public AcsAppSettings Current { get; }

    public string Path { get; }

    public string? LastError { get; private set; }

    public bool Save()
    {
        var ok = AcsSettingsStore.Save(Current, Path, out var error);
        LastError = error;
        return ok;
    }
}
