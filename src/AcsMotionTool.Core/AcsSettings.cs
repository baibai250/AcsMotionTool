using System.Text.Json;
using System.Text.Json.Serialization;

namespace AcsMotionTool.Core;

/// <summary>单轴配置</summary>
public sealed class AcsAxisSetting
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public double? SoftLimitMin { get; set; }
    public double? SoftLimitMax { get; set; }
    public double JogVelocity { get; set; } = 5;
    public double MoveVelocity { get; set; } = 20;
    public double Acceleration { get; set; } = 100;
    public double Deceleration { get; set; } = 100;
    public int HomeBufferIndex { get; set; }
}

/// <summary>变量监视项</summary>
public sealed class AcsVariableWatch
{
    public string Name { get; set; } = string.Empty;
    public int Buffer { get; set; } = -1;
}

/// <summary>程序设置(保存到 exe 同目录 acs-settings.json)</summary>
public sealed class AcsAppSettings
{
    public AcsConnectionOptions Connection { get; set; } = new();
    public AcsDatabaseOptions Database { get; set; } = new();
    public List<AcsAxisSetting> Axes { get; set; } = CreateDefaultAxes();
    public List<AcsVariableWatch> WatchedVariables { get; set; } = new()
    {
        new AcsVariableWatch { Name = "?FPOS(0)" },
        new AcsVariableWatch { Name = "?MST(0)" },
        new AcsVariableWatch { Name = "?IN(0)" },
        new AcsVariableWatch { Name = "?OUT(0)" },
    };

    public int PollIntervalMs { get; set; } = 200;
    public int IOPortCount { get; set; } = 2;
    public int IOBitsPerPort { get; set; } = 16;
    public bool AutoConnectOnStartup { get; set; } = true;
    public bool RememberLastConnection { get; set; } = true;

    /// <summary>
    /// 各视图的分割比例(键为视图名，例如 Axis / Connection / Io)，
    /// 用来记住用户拖动 GridSplitter 调好的界面占比。
    /// </summary>
    public Dictionary<string, AcsLayoutSnapshot> LayoutRatios { get; set; } = new();

    public static List<AcsAxisSetting> CreateDefaultAxes()
    {
        string[] names = { "X", "Y", "Z", "R" };
        var list = new List<AcsAxisSetting>();
        for (var i = 0; i < names.Length; i++)
        {
            list.Add(new AcsAxisSetting { Index = i, Name = names[i], HomeBufferIndex = i });
        }
        return list;
    }

    /// <summary>按轴号取配置(不存在则创建一个默认配置)</summary>
    public AcsAxisSetting GetAxis(int index)
    {
        var found = Axes.FirstOrDefault(a => a.Index == index);
        if (found != null) return found;
        found = new AcsAxisSetting { Index = index, Name = $"Axis{index}", HomeBufferIndex = index };
        Axes.Add(found);
        Axes.Sort((a, b) => a.Index.CompareTo(b.Index));
        return found;
    }
}

/// <summary>
/// MySQL 数据库连接参数(用于持久化软限位等配置)。
/// 默认值按现场常见本机 MySQL:127.0.0.1:3306，root / 123456，库名 acs_motion。
/// </summary>
public sealed class AcsDatabaseOptions
{
    /// <summary>是否启用数据库(关闭后软限位只保存在 acs-settings.json)</summary>
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string User { get; set; } = "root";
    public string Password { get; set; } = "123456";
    public string Database { get; set; } = "acs_motion";

    /// <summary>在界面点“应用/保存”时自动写入数据库</summary>
    public bool SaveSoftLimitsAutomatically { get; set; } = true;

    /// <summary>启动时从数据库加载软限位覆盖本地配置</summary>
    public bool LoadSoftLimitsOnStartup { get; set; } = true;

    public AcsDatabaseOptions Clone() => new()
    {
        Enabled = Enabled,
        Host = Host,
        Port = Port,
        User = User,
        Password = Password,
        Database = Database,
        SaveSoftLimitsAutomatically = SaveSoftLimitsAutomatically,
        LoadSoftLimitsOnStartup = LoadSoftLimitsOnStartup,
    };

    public string Describe() => $"{User}@{Host}:{Port}/{Database}";
}

/// <summary>
/// 某个视图的分割比例快照:只记录星号(*)/比例的尺寸定义，
/// 数值为“相对于最窄一栏的倍数”，用 GridSplitter 拖动后保存、下次启动还原。
/// </summary>
public sealed class AcsLayoutSnapshot
{
    /// <summary>各星号列的相对比例</summary>
    public double[] Columns { get; set; } = Array.Empty<double>();

    /// <summary>各星号行的相对比例</summary>
    public double[] Rows { get; set; } = Array.Empty<double>();
}

/// <summary>设置文件读写</summary>
public static class AcsSettingsStore
{    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>默认设置文件路径(exe 同目录)</summary>
    public static string DefaultPath =>
        Path.Combine(AppContext.BaseDirectory, "acs-settings.json");

    public static AcsAppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path)) return new AcsAppSettings();
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AcsAppSettings>(json, Options) ?? new AcsAppSettings();
            // 防止手工编辑过的文件造成空集合(反序列化时 json 中的 null 会覆盖默认值)
            if (settings.Connection is null) settings.Connection = new AcsConnectionOptions();
            if (settings.Database is null) settings.Database = new AcsDatabaseOptions();
            if (settings.Axes is null) settings.Axes = AcsAppSettings.CreateDefaultAxes();
            if (settings.WatchedVariables is null) settings.WatchedVariables = new List<AcsVariableWatch>();
            if (settings.LayoutRatios is null) settings.LayoutRatios = new Dictionary<string, AcsLayoutSnapshot>();
            return settings;
        }
        catch
        {
            return new AcsAppSettings();
        }
    }

    public static bool Save(AcsAppSettings settings, string? path = null)
        => Save(settings, path, out _);

    /// <summary>保存设置并返回失败原因(便于界面把真实原因写进日志)</summary>
    public static bool Save(AcsAppSettings settings, string? path, out string? error)
    {
        path ??= DefaultPath;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
