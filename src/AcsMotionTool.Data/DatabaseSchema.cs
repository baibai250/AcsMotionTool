namespace AcsMotionTool.Data;

/// <summary>数据库建库建表 SQL(全部使用 IF NOT EXISTS，可重复执行)</summary>
internal static class DatabaseSchema
{
    /// <summary>创建数据库</summary>
    public static string CreateDatabase(string databaseName) =>
        $"CREATE DATABASE IF NOT EXISTS `{databaseName}` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;";

    /// <summary>当前版本的表结构</summary>
    public static readonly string[] CreateTables =
    {
        // 软限位主表:每个轴一行
        """
        CREATE TABLE IF NOT EXISTS `axis_soft_limit` (
            `axis_index`     INT           NOT NULL COMMENT '轴号(从 0 开始)',
            `axis_name`      VARCHAR(64)   NOT NULL DEFAULT '' COMMENT '轴名称，如 X/Y/Z/R',
            `soft_limit_min` DOUBLE        NULL COMMENT '软限位下限，NULL 表示该方向不检查',
            `soft_limit_max` DOUBLE        NULL COMMENT '软限位上限，NULL 表示该方向不检查',
            `updated_at`     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            `updated_by`     VARCHAR(64)   NOT NULL DEFAULT '' COMMENT '最后修改人(主机名\\用户名)',
            PRIMARY KEY (`axis_index`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='轴软限位配置';
        """,

        // 审计表:每次修改留痕，便于追溯是谁改了安全相关参数
        """
        CREATE TABLE IF NOT EXISTS `axis_soft_limit_history` (
            `id`             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
            `axis_index`     INT           NOT NULL,
            `axis_name`      VARCHAR(64)   NOT NULL DEFAULT '',
            `soft_limit_min` DOUBLE        NULL,
            `soft_limit_max` DOUBLE        NULL,
            `changed_at`     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
            `changed_by`     VARCHAR(64)   NOT NULL DEFAULT '',
            PRIMARY KEY (`id`),
            KEY `idx_axis_changed` (`axis_index`, `changed_at`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='轴软限位修改历史';
        """,
    };
}
