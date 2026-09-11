using MainAPP.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace MainAPP.Services
{
    public sealed class LogDatabaseService
    {
        private static readonly Lazy<LogDatabaseService> InstanceLazy = new(() => new LogDatabaseService());

        public static LogDatabaseService Instance => InstanceLazy.Value;

        private readonly string _databasePath;

        private LogDatabaseService()
        {
            _databasePath = Path.Combine(AppContext.BaseDirectory, "Saves", "DataBase", "Logs.db");
        }

        /// <summary>
        /// 创建新的 LogDbContext 实例，与 BarcodeDataService.CreateDbContext 模式一致：
        /// 每次操作创建独立 context，关闭 AutoDetectChangesEnabled 以提升只读查询性能。
        /// </summary>
        private static LogDbContext CreateDbContext()
        {
            var dbContext = new LogDbContext();
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
            return dbContext;
        }

        /// <summary>
        /// 异步加载日志记录，按 id 倒序返回最新的 limit 条。
        /// </summary>
        public async Task<IReadOnlyList<LogEntry>> LoadLogsAsync(int limit = 10000, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_databasePath))
            {
                return [];
            }

            try
            {
                using var dbContext = CreateDbContext();
                var logs = await dbContext.Logs
                    .AsNoTracking()
                    .OrderByDescending(l => l.Id)
                    .Take(limit)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var log in logs)
                {
                    NormalizeLogEntry(log);
                }
                return logs;
            }
            catch (SqliteException ex)
            {
                LogService.Instance.Error($"异步加载日志失败: {ex}");
                return [];
            }
        }

        /// <summary>
        /// 按时间范围与级别筛选异步加载日志记录，支持分页。
        /// startTime/endTime 均为本地时间，endTime 含当天整天（加 1 天）。
        /// level 为空表示不按级别筛选。返回结果按 id 倒序排列。
        /// </summary>
        public async Task<IReadOnlyList<LogEntry>> LoadLogsFilteredAsync(
            DateTime? startTime,
            DateTime? endTime,
            string? level,
            int limit = 10000,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_databasePath)) return [];

            try
            {
                using var dbContext = CreateDbContext();
                var query = dbContext.Logs.AsNoTracking().AsQueryable();

                if (startTime.HasValue)
                {
                    query = query.Where(l => l.Timestamp >= startTime.Value);
                }
                if (endTime.HasValue)
                {
                    // 结束日期包含整天，加 1 天
                    var endInclusive = endTime.Value.Date.AddDays(1);
                    query = query.Where(l => l.Timestamp < endInclusive);
                }
                if (!string.IsNullOrEmpty(level))
                {
                    query = query.Where(l => l.Level == level);
                }

                var logs = await query
                    .OrderByDescending(l => l.Id)
                    .Skip(offset)
                    .Take(limit)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var log in logs)
                {
                    NormalizeLogEntry(log);
                }
                return logs;
            }
            catch (SqliteException ex)
            {
                LogService.Instance.Error($"按条件筛选日志失败: {ex}");
                return [];
            }
        }

        /// <summary>
        /// 获取符合筛选条件的日志总数。失败返回 0。
        /// 参数语义与 LoadLogsFilteredAsync 一致。
        /// </summary>
        public async Task<int> GetFilteredLogCountAsync(
            DateTime? startTime,
            DateTime? endTime,
            string? level,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_databasePath)) return 0;

            try
            {
                using var dbContext = CreateDbContext();
                var query = dbContext.Logs.AsNoTracking().AsQueryable();

                if (startTime.HasValue)
                {
                    query = query.Where(l => l.Timestamp >= startTime.Value);
                }
                if (endTime.HasValue)
                {
                    var endInclusive = endTime.Value.Date.AddDays(1);
                    query = query.Where(l => l.Timestamp < endInclusive);
                }
                if (!string.IsNullOrEmpty(level))
                {
                    query = query.Where(l => l.Level == level);
                }

                return await query.CountAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogService.Instance.Error($"获取筛选日志总数失败: {ex}");
                return 0;
            }
        }

        /// <summary>
        /// 异步清空所有日志记录。
        /// </summary>
        public async Task ClearLogsAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_databasePath))
            {
                return;
            }

            // M288: 捕获 SqliteException，避免数据库损坏时抛出未处理异常
            try
            {
                using var dbContext = CreateDbContext();
                await dbContext.Logs.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogService.Instance.Error($"异步清空日志失败: {ex}");
            }
        }

        // L408a: GetLogCount() 和 GetLogCountAsync() 为死代码（无任何调用方），已删除。

        /// <summary>
        /// 按时间清理：删除 Timestamp 早于 cutoff 的所有日志。返回删除的行数。
        /// Serilog SQLite Sink 表中 Timestamp 字段存储为 "yyyy-MM-dd HH:mm:ss.fff" 格式字符串（本地时间），
        /// EF Core 值转换器保证 DateTime 与字符串之间的比较语义一致。
        /// </summary>
        public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_databasePath)) return 0;
            if (cutoff >= DateTime.Now) return 0;

            try
            {
                using var dbContext = CreateDbContext();
                return await dbContext.Logs
                    .Where(l => l.Timestamp < cutoff)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogService.Instance.Error($"按时间清理日志失败: {ex}");
                return 0;
            }
        }

        /// <summary>
        /// 按数量清理：删除超出 maxCount 的最旧日志（按 id 升序）。返回删除的行数。
        /// maxCount <= 0 表示不限制。
        /// </summary>
        public async Task<int> TrimToMaxCountAsync(int maxCount, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_databasePath)) return 0;
            if (maxCount <= 0) return 0;

            try
            {
                using var dbContext = CreateDbContext();

                // 先查询总数，避免无谓的 DELETE
                var total = await dbContext.Logs.CountAsync(cancellationToken).ConfigureAwait(false);
                if (total <= maxCount) return 0;

                var toDelete = total - maxCount;
                // 删除最旧的 N 条（id 升序），先取出 Id 再批量删除
                var idsToDelete = await dbContext.Logs
                    .OrderBy(l => l.Id)
                    .Take(toDelete)
                    .Select(l => l.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (idsToDelete.Count == 0) return 0;

                return await dbContext.Logs
                    .Where(l => idsToDelete.Contains(l.Id))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogService.Instance.Error($"按数量清理日志失败: {ex}");
                return 0;
            }
        }

        /// <summary>
        /// 获取日志总数。失败返回 0。
        /// </summary>
        public async Task<int> GetLogCountAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_databasePath)) return 0;

            try
            {
                using var dbContext = CreateDbContext();
                return await dbContext.Logs.CountAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogService.Instance.Error($"获取日志总数失败: {ex}");
                return 0;
            }
        }

        /// <summary>
        /// 获取最早一条日志的时间戳。表为空或失败时返回 null。
        /// 按 id 升序取首条，配合保留策略可视化使用。
        /// </summary>
        public async Task<DateTime?> GetOldestLogTimeAsync(CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_databasePath)) return null;
            try
            {
                using var dbContext = CreateDbContext();
                // 投影为 DateTime?，表为空时 FirstOrDefaultAsync 返回 null
                return await dbContext.Logs
                    .OrderBy(l => l.Id)
                    .Select(l => (DateTime?)l.Timestamp)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SqliteException ex)
            {
                LogService.Instance.Error($"获取最早日志时间失败: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 对从数据库加载的 LogEntry 进行后处理：
        /// - 防御性处理可能为 NULL 的列（Serilog schema 中 Exception/Properties 允许 NULL）
        /// - 规范化 Level 名称（如 "Information" → "INFO"）
        /// - 用 RenderedMessage 填充 Message（供 UI 显示）
        /// - 根据规范化后的 Level 设置 Color
        /// </summary>
        private static void NormalizeLogEntry(LogEntry logEntry)
        {
            // 防御性处理：DB 列可能为 NULL，统一转换为空字符串（与原 IsDBNull 判断语义一致）
            logEntry.Level ??= string.Empty;
            logEntry.Exception ??= string.Empty;
            logEntry.RenderedMessage ??= string.Empty;
            logEntry.Properties ??= string.Empty;

            logEntry.Level = NormalizeLevel(logEntry.Level);
            logEntry.Message = string.IsNullOrWhiteSpace(logEntry.RenderedMessage)
                ? logEntry.Message
                : logEntry.RenderedMessage;
            logEntry.Color = LogEntry.GetColorByLevel(logEntry.Level);
        }

        private static string NormalizeLevel(string level)
        {
            // L105: 提取局部变量避免在 switch 表达式与默认分支中重复 Trim/ToUpperInvariant
            var n = level.Trim().ToUpperInvariant();
            return n switch
            {
                "INFORMATION" => "INFO",
                "VERBOSE" => "DEBUG",
                "WARN" => "WARNING",
                "WARNING" => "WARNING",
                "ERROR" => "ERROR",
                "DEBUG" => "DEBUG",
                "INFO" => "INFO",
                _ => n
            };
        }
    }
}
