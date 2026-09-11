namespace MainAPP.Models
{
    /// <summary>
    /// 数据库配置（由 Settings 持有实例）。
    /// </summary>
    public class DatabaseSettings
    {
        /// <summary>
        /// 条码数据库记录保留天数（按 DetectTime 清理）。0=永不清理。
        /// 默认 90 天，超过此天数的记录将在周期清理时删除。
        /// </summary>
        public int BarcodeDataRetentionDays { get; set; } = 90;

        /// <summary>
        /// 条码数据库最大保留记录数。0=不限制。
        /// 默认 500000 条，超出时按 DetectTime 从最旧开始删除。
        /// </summary>
        public int BarcodeDataMaxCount { get; set; } = 500_000;

        /// <summary>
        /// 日志数据库记录保留天数（按 Timestamp 清理）。0=永不清理。
        /// 默认 30 天，超过此天数的日志将在周期清理时删除。
        /// 注意：此设置独立于 Serilog Sink 自身的保留期，两者取较小值生效。
        /// </summary>
        public int LogRetentionDays { get; set; } = 30;

        /// <summary>
        /// 日志数据库最大保留记录数。0=不限制。
        /// 默认 200000 条，超出时按 Timestamp 从最旧开始删除。
        /// </summary>
        public int LogMaxCount { get; set; } = 200_000;

        /// <summary>
        /// 数据/日志清理周期（小时）。每次周期到达时执行一次清理。
        /// 默认 6 小时。启动时也会立即执行一次清理。
        /// </summary>
        public int DataCleanupIntervalHours { get; set; } = 6;

        /// <summary>
        /// 数据库备份过期天数
        /// </summary>
        public int BackupExpireDays { get; set; } = 14;

        /// <summary>
        /// 条码列表中保留的最近N条记录数
        /// </summary>
        public int BarcodeRemoveCount { get; set; } = 5;

        /// <summary>
        /// UI日志列表最大保留条数
        /// </summary>
        public int LogsCount { get; set; } = 1000;
    }
}
