using AcsMotionTool.Core;
using AcsMotionTool.Data;

namespace AcsMotionTool.App.Services;

/// <summary>
/// 软限位持久化服务(ViewModel 只依赖这个接口，看不到 MySql 细节)。
/// 实现类按当前设置里的数据库参数即时建立连接，因此界面上改了参数立刻生效。
/// </summary>
public interface ISoftLimitStore
{
    /// <summary>测试数据库连接(目标库不存在时也能测)</summary>
    Task<DatabaseTestResult> TestConnectionAsync(AcsDatabaseOptions options);

    /// <summary>初始化数据库(建库建表)</summary>
    Task<DatabaseInitResult> InitializeAsync(AcsDatabaseOptions options);

    /// <summary>读取全部轴的软限位</summary>
    Task<IReadOnlyList<AxisSoftLimitRecord>> LoadAllAsync(AcsDatabaseOptions options);

    /// <summary>批量写入(存在则更新)</summary>
    Task<bool> SaveAllAsync(AcsDatabaseOptions options, IEnumerable<AxisSoftLimitRecord> records, string? operatorName = null);
}

/// <summary>基于 MySQL 的实现</summary>
public sealed class MySqlSoftLimitStore : ISoftLimitStore
{
    private static MySqlSoftLimitRepository Create(AcsDatabaseOptions options)
        => new(options.Host, options.Port, options.User, options.Password, options.Database);

    public async Task<DatabaseTestResult> TestConnectionAsync(AcsDatabaseOptions options)
    {
        try
        {
            return await Create(options).TestConnectionAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new DatabaseTestResult(false, $"数据库操作失败:{ex.Message}");
        }
    }

    public async Task<DatabaseInitResult> InitializeAsync(AcsDatabaseOptions options)
    {
        try
        {
            return await Create(options).EnsureSchemaAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new DatabaseInitResult(false, $"数据库初始化失败:{ex.Message}");
        }
    }

    public async Task<IReadOnlyList<AxisSoftLimitRecord>> LoadAllAsync(AcsDatabaseOptions options)
    {
        try
        {
            return await Create(options).LoadAllAsync().ConfigureAwait(false);
        }
        catch
        {
            return Array.Empty<AxisSoftLimitRecord>();
        }
    }

    public async Task<bool> SaveAllAsync(AcsDatabaseOptions options, IEnumerable<AxisSoftLimitRecord> records,
        string? operatorName = null)
    {
        var list = records.ToList();
        if (list.Count == 0) return true;
        try
        {
            var count = await Create(options).UpsertManyAsync(list, operatorName).ConfigureAwait(false);
            return count > 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 软限位“同步”能力:由轴页 ViewModel 实现，供初始化界面在启动时把数据库里的
/// 软限位加载到界面与运动拦截逻辑中。
/// </summary>
public interface ISoftLimitSync
{
    /// <summary>从数据库加载软限位并应用到界面/运动拦截</summary>
    Task<(int Count, string Message)> LoadFromDatabaseAsync();

    /// <summary>把当前界面上的软限位写入数据库</summary>
    Task<bool> SaveAllToDatabaseAsync();
}
