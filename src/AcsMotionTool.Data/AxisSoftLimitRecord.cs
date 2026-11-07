namespace AcsMotionTool.Data;

/// <summary>
/// 一条轴软限位记录(与数据库表 axis_soft_limit 一一对应)。
/// SoftLimitMin/Max 为 null 表示该方向不检查。
/// </summary>
public sealed record AxisSoftLimitRecord(
    int AxisIndex,
    string AxisName,
    double? SoftLimitMin,
    double? SoftLimitMax,
    DateTime? UpdatedAt = null,
    string? UpdatedBy = null)
{
    public string RangeText => $"[{Format(SoftLimitMin)}, {Format(SoftLimitMax)}]";

    private static string Format(double? value) => value?.ToString("G") ?? "不限";

    public override string ToString() => $"轴{AxisIndex} {AxisName} {RangeText}";
}

/// <summary>数据库连接测试结果</summary>
public sealed record DatabaseTestResult(bool Success, string Message, string? ServerVersion = null, string? DatabaseName = null);

/// <summary>数据库初始化(建库建表)结果</summary>
public sealed record DatabaseInitResult(bool Success, string Message, string? ServerVersion = null, int RecordCount = 0);

/// <summary>软限位数据访问接口</summary>
public interface ISoftLimitRepository
{
    /// <summary>测试连接(不需要目标库已存在)</summary>
    Task<DatabaseTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>确保数据库与数据表存在，返回当前记录数</summary>
    Task<DatabaseInitResult> EnsureSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>读取全部轴的软限位</summary>
    Task<IReadOnlyList<AxisSoftLimitRecord>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>读取单个轴</summary>
    Task<AxisSoftLimitRecord?> LoadAsync(int axisIndex, CancellationToken cancellationToken = default);

    /// <summary>写入(存在则更新)单个轴，同时写一条审计记录</summary>
    Task<bool> UpsertAsync(AxisSoftLimitRecord record, string? operatorName = null, CancellationToken cancellationToken = default);

    /// <summary>批量写入，返回写入条数</summary>
    Task<int> UpsertManyAsync(IEnumerable<AxisSoftLimitRecord> records, string? operatorName = null, CancellationToken cancellationToken = default);

    /// <summary>删除某个轴的软限位</summary>
    Task<bool> DeleteAsync(int axisIndex, CancellationToken cancellationToken = default);

    /// <summary>读取某轴的修改历史(最近的在前)</summary>
    Task<IReadOnlyList<AxisSoftLimitRecord>> LoadHistoryAsync(int axisIndex, int limit = 20, CancellationToken cancellationToken = default);
}
