using MySqlConnector;

namespace AcsMotionTool.Data;

/// <summary>
/// 基于 MySQL 的软限位仓储实现。
/// 三个原则:1) 全部使用参数化 SQL(防注入);2) 建库建表可重复执行;
/// 3) 修改时在同一事务里写审计表，便于追溯安全参数的变更。
/// </summary>
public sealed class MySqlSoftLimitRepository : ISoftLimitRepository
{
    private const string TableName = "axis_soft_limit";
    private const string HistoryTableName = "axis_soft_limit_history";
    private readonly string _connectionString;
    private readonly string _databaseName;
    private readonly string _operatorName;

    /// <summary>使用连接参数构造</summary>
    /// <param name="server">服务器，例如 127.0.0.1</param>
    /// <param name="port">端口，默认 3306</param>
    /// <param name="user">用户名，例如 root</param>
    /// <param name="password">密码</param>
    /// <param name="database">数据库名，例如 acs_motion</param>
    public MySqlSoftLimitRepository(string server, int port, string user, string password, string database,
        string? operatorName = null)
    {
        _databaseName = string.IsNullOrWhiteSpace(database) ? "acs_motion" : database.Trim();
        _operatorName = operatorName ?? $"{Environment.MachineName}\\{Environment.UserName}";
        _connectionString = BuildConnectionString(server, port, user, password, _databaseName, includeDatabase: true);
        _serverOnlyConnectionString = BuildConnectionString(server, port, user, password, _databaseName, includeDatabase: false);
    }

    private readonly string _serverOnlyConnectionString;

    /// <summary>数据库名</summary>
    public string DatabaseName => _databaseName;

    private static string BuildConnectionString(string server, int port, string user, string password,
        string database, bool includeDatabase)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(server) ? "127.0.0.1" : server.Trim(),
            Port = (uint)(port <= 0 ? 3306 : port),
            UserID = string.IsNullOrWhiteSpace(user) ? "root" : user.Trim(),
            Password = password ?? string.Empty,
            SslMode = MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 15,
            CharacterSet = "utf8mb4",
        };
        if (includeDatabase) builder.Database = database;
        return builder.ConnectionString;
    }

    #region 连接与建表

    public async Task<DatabaseTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 注意:这里用“不指定数据库”的连接串，这样在目标库尚未创建时也能测试服务器与账号是否可用
            await using var connection = new MySqlConnection(_serverOnlyConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var (databaseExists, tableCount, rowCount) = await CheckSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            var version = connection.ServerVersion;
            var message = databaseExists
                ? $"连接成功: MySQL {version}，数据库 {_databaseName} 已存在(软限位表 {tableCount} 张，{rowCount} 条记录)"
                : $"连接成功: MySQL {version}，数据库 {_databaseName} 尚未创建(初始化时会自动创建)";
            return new DatabaseTestResult(true, message, version, databaseExists ? _databaseName : null);
        }
        catch (Exception ex)
        {
            return new DatabaseTestResult(false, TranslateError(ex), null, null);
        }
    }

    public async Task<DatabaseInitResult> EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 第一步:不指定数据库，确保数据库存在
            await using (var serverConnection = new MySqlConnection(_serverOnlyConnectionString))
            {
                await serverConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var createDatabase = new MySqlCommand(DatabaseSchema.CreateDatabase(_databaseName), serverConnection);
                await createDatabase.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                // 第二步:在该库里建表
                await using var useDatabase = new MySqlCommand($"USE `{_databaseName}`;", serverConnection);
                await useDatabase.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                foreach (var sql in DatabaseSchema.CreateTables)
                {
                    await using var createTable = new MySqlCommand(sql, serverConnection);
                    await createTable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                var count = await CountRowsAsync(serverConnection, cancellationToken).ConfigureAwait(false);
                return new DatabaseInitResult(true,
                    $"数据库初始化完成: {_databaseName}，MySQL {serverConnection.ServerVersion}，已有 {count} 条轴软限位记录",
                    serverConnection.ServerVersion, count);
            }
        }
        catch (Exception ex)
        {
            return new DatabaseInitResult(false, TranslateError(ex), null, 0);
        }
    }

    private async Task<(bool DatabaseExists, int TableCount, int RowCount)> CheckSchemaAsync(MySqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new MySqlCommand(
            """
            SELECT
                (SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @db) AS db_exists,
                (SELECT COUNT(*) FROM information_schema.TABLES
                  WHERE TABLE_SCHEMA = @db
                    AND TABLE_NAME IN ('axis_soft_limit', 'axis_soft_limit_history')) AS table_count
            """, connection);
        command.Parameters.AddWithValue("@db", _databaseName);

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return (false, 0, 0);
            var exists = reader.GetInt32(0) > 0;
            var tables = reader.GetInt32(1);
            if (!exists || tables == 0) return (exists, tables, 0);
        }

        // 表已存在时顺带统计记录数(失败不影响整体测试结果)
        try
        {
            await using var countCommand = new MySqlCommand(
                $"SELECT COUNT(*) FROM `{_databaseName}`.`{TableName}`", connection);
            var rows = await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return (true, 2, Convert.ToInt32(rows));
        }
        catch
        {
            return (true, 2, 0);
        }
    }

    private static async Task<int> CountRowsAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = new MySqlCommand($"SELECT COUNT(*) FROM `{TableName}`", connection);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt32(result);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>把 MySQL 异常翻译成中文提示</summary>
    private static string TranslateError(Exception ex)
    {
        if (ex is MySqlException mySql)
        {
            return mySql.ErrorCode switch
            {
                MySqlErrorCode.AccessDenied => $"访问被拒绝(1045):用户名或密码错误，或该用户不允许从本机连接。原始信息:{mySql.Message}",
                MySqlErrorCode.UnknownDatabase => $"数据库不存在(1049):{mySql.Message}",
                MySqlErrorCode.DatabaseAccessDenied => $"没有该数据库的权限(1044):{mySql.Message}",
                MySqlErrorCode.UnableToConnectToHost => $"无法连接服务器:请确认 MySQL 已启动、端口可达。原始信息:{mySql.Message}",
                MySqlErrorCode.TableAccessDenied => $"没有该表的权限:{mySql.Message}",
                _ => $"数据库错误({mySql.ErrorCode}):{mySql.Message}",
            };
        }
        if (ex is TimeoutException) return "连接数据库超时:请确认 MySQL 服务与端口(默认 3306)可达";
        return $"数据库操作失败:{ex.Message}";
    }

    #endregion

    #region 读写

    public async Task<IReadOnlyList<AxisSoftLimitRecord>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<AxisSoftLimitRecord>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new MySqlCommand(
            $"SELECT axis_index, axis_name, soft_limit_min, soft_limit_max, updated_at, updated_by FROM `{TableName}` ORDER BY axis_index",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AxisSoftLimitRecord(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return result;
    }

    public async Task<AxisSoftLimitRecord?> LoadAsync(int axisIndex, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new MySqlCommand(
            $"SELECT axis_index, axis_name, soft_limit_min, soft_limit_max, updated_at, updated_by FROM `{TableName}` WHERE axis_index = @axis",
            connection);
        command.Parameters.AddWithValue("@axis", axisIndex);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return new AxisSoftLimitRecord(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    public async Task<bool> UpsertAsync(AxisSoftLimitRecord record, string? operatorName = null,
        CancellationToken cancellationToken = default)
        => await UpsertManyAsync(new[] { record }, operatorName, cancellationToken).ConfigureAwait(false) > 0;

    public async Task<int> UpsertManyAsync(IEnumerable<AxisSoftLimitRecord> records, string? operatorName = null,
        CancellationToken cancellationToken = default)
    {
        var list = records?.ToList() ?? new List<AxisSoftLimitRecord>();
        if (list.Count == 0) return 0;

        var who = operatorName ?? _operatorName;
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var affected = 0;
            foreach (var record in list)
            {
                await using (var upsert = new MySqlCommand(
                    $"""
                     INSERT INTO `{TableName}` (axis_index, axis_name, soft_limit_min, soft_limit_max, updated_by)
                     VALUES (@axis, @name, @min, @max, @by)
                     ON DUPLICATE KEY UPDATE
                         axis_name = VALUES(axis_name),
                         soft_limit_min = VALUES(soft_limit_min),
                         soft_limit_max = VALUES(soft_limit_max),
                         updated_by = VALUES(updated_by),
                         updated_at = CURRENT_TIMESTAMP
                     """, connection, transaction))
                {
                    AddRecordParameters(upsert, record, who);
                    affected += await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await using (var history = new MySqlCommand(
                    $"INSERT INTO `{HistoryTableName}` (axis_index, axis_name, soft_limit_min, soft_limit_max, changed_by) " +
                    "VALUES (@axis, @name, @min, @max, @by)", connection, transaction))
                {
                    AddRecordParameters(history, record, who);
                    await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return affected;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(int axisIndex, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new MySqlCommand($"DELETE FROM `{TableName}` WHERE axis_index = @axis", connection);
        command.Parameters.AddWithValue("@axis", axisIndex);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<IReadOnlyList<AxisSoftLimitRecord>> LoadHistoryAsync(int axisIndex, int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = new List<AxisSoftLimitRecord>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new MySqlCommand(
            $"SELECT axis_index, axis_name, soft_limit_min, soft_limit_max, changed_at, changed_by " +
            $"FROM `{HistoryTableName}` WHERE axis_index = @axis ORDER BY changed_at DESC, id DESC LIMIT @limit",
            connection);
        command.Parameters.AddWithValue("@axis", axisIndex);
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 500));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new AxisSoftLimitRecord(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return result;
    }

    private static void AddRecordParameters(MySqlCommand command, AxisSoftLimitRecord record, string who)
    {
        command.Parameters.AddWithValue("@axis", record.AxisIndex);
        command.Parameters.AddWithValue("@name", record.AxisName ?? string.Empty);
        command.Parameters.AddWithValue("@min", (object?)record.SoftLimitMin ?? DBNull.Value);
        command.Parameters.AddWithValue("@max", (object?)record.SoftLimitMax ?? DBNull.Value);
        command.Parameters.AddWithValue("@by", who);
    }

    #endregion
}
