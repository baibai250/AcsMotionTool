using AcsMotionTool.Core;

namespace AcsMotionTool.App.Services;

/// <summary>
/// 界面布局比例存储:记住用户拖动 GridSplitter 后的占比，下次启动还原。
/// </summary>
public interface ILayoutPreferences
{
    /// <summary>读取某个视图保存的比例(没有则返回 null)</summary>
    AcsLayoutSnapshot? Get(string key);

    /// <summary>保存某个视图的比例</summary>
    void Set(string key, AcsLayoutSnapshot snapshot);
}

/// <inheritdoc />
public sealed class LayoutPreferences : ILayoutPreferences
{
    private readonly IAppSettingsService _settings;

    public LayoutPreferences(IAppSettingsService settings) => _settings = settings;

    public AcsLayoutSnapshot? Get(string key)
        => _settings.Current.LayoutRatios.TryGetValue(key, out var snapshot) ? snapshot : null;

    public void Set(string key, AcsLayoutSnapshot snapshot)
    {
        _settings.Current.LayoutRatios[key] = snapshot;
        _settings.Save();
    }
}
