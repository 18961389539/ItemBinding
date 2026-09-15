using System;
using System.Threading;
using System.Threading.Tasks;
using Extensions;
using MainAPP.Models;

namespace MainAPP.Services
{
    /// <summary>
    /// 数据/日志保留期清理服务。
    /// 按 Settings 中的 BarcodeDataRetentionDays / BarcodeDataMaxCount /
    /// LogRetentionDays / LogMaxCount 配置周期性清理过期数据，防止数据库无限膨胀。
    /// 设计参考 MemoryDiagnostics.RunPeriodicSnapshotAsync：Task + CancellationTokenSource + Task.Delay 循环。
    /// </summary>
    public static class DataRetentionService
    {
        /// <summary>
        /// 执行一次清理。读取 Settings 当前配置，对条码数据库和日志数据库分别应用时间/数量保留策略。
        /// </summary>
        public static async Task<CleanupResult> CleanupNowAsync(CancellationToken cancellationToken = default)
        {
            var result = new CleanupResult();
            var settings = Settings.Instance;

            // 清理前确保日志时间索引存在（幂等）——按时间删除在无索引时是全表扫描，
            // 日志量大后周期清理会明显变慢（2026-09-12 随保留期调大一并加固）
            try
            {
                await new LogDbContext().EnsureTimestampIndexAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"确保 Logs.Timestamp 索引失败（不影响本轮清理）: {ex.Message}");
            }

            // 条码数据库清理
            try
            {
                // 时间保留
                if (settings.BarcodeDataRetentionDays > 0)
                {
                    var cutoff = DateTime.Now.AddDays(-settings.BarcodeDataRetentionDays);
                    result.BarcodeDeletedByTime = await BarcodeDataService.Instance
                        .DeleteOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);
                }

                // 数量保留
                if (settings.BarcodeDataMaxCount > 0)
                {
                    result.BarcodeDeletedByCount = await BarcodeDataService.Instance
                        .TrimToMaxCountAsync(settings.BarcodeDataMaxCount, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"清理条码数据库失败: {ex}");
            }

            // 日志数据库清理
            try
            {
                // 时间保留
                if (settings.LogRetentionDays > 0)
                {
                    var cutoff = DateTime.Now.AddDays(-settings.LogRetentionDays);
                    result.LogDeletedByTime = await LogDatabaseService.Instance
                        .DeleteOlderThanAsync(cutoff, cancellationToken).ConfigureAwait(false);
                }

                // 数量保留
                if (settings.LogMaxCount > 0)
                {
                    result.LogDeletedByCount = await LogDatabaseService.Instance
                        .TrimToMaxCountAsync(settings.LogMaxCount, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"清理日志数据库失败: {ex}");
            }

            // 数据库文件 VACUUM 压缩（仅在删除量较大时）
            try
            {
                var totalDeleted = result.TotalDeleted;
                if (totalDeleted > 1000)
                {
                    LogService.Instance.Info($"数据清理完成，共删除 {totalDeleted} 条记录，执行 VACUUM 压缩");
                    // VACUUM 会在数据库内部重建，释放磁盘空间
                    await VacuumLogDatabaseAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (totalDeleted > 0)
                {
                    LogService.Instance.Info($"数据清理完成，共删除 {totalDeleted} 条记录");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"VACUUM 压缩失败: {ex}");
            }

            // 图片文件清理（2026-09-12 补）：此前仅在启动时执行一次（App fire-and-forget），
            // 7×24 长运行的产线机图片持续累积（IsSaveDraw 开启时可达数十 GB/天），
            // 直到下次重启才清理。挂入周期任务后与数据库清理同节奏，消除该缺口。
            try
            {
                if (settings.MinRecentDays > 0)
                {
                    FileHelper.DeleteOldFiles(settings.MinRecentDays, settings.PicturesSaveFolder);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"清理旧图片失败: {ex}");
            }

            return result;
        }

        /// <summary>
        /// 周期性执行清理任务。参考 MemoryDiagnostics.RunPeriodicSnapshotAsync 的模式。
        /// 启动时立即执行一次，然后按 intervalHours 间隔周期执行。
        /// </summary>
        public static async Task RunPeriodicCleanupAsync(int intervalHours, CancellationToken cancellationToken)
        {
            // 启动时立即执行一次清理
            try
            {
                await CleanupNowAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动时数据清理失败: {ex}");
            }

            // intervalHours <= 0 时只执行启动清理，不周期执行
            if (intervalHours <= 0) return;

            var interval = TimeSpan.FromHours(intervalHours);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    await CleanupNowAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"周期性数据清理失败: {ex}");
                }
            }
        }

        /// <summary>
        /// 对日志数据库执行 VACUUM，重建数据库文件释放磁盘空间。
        /// 注意：VACUUM 会锁定数据库，执行期间无法写入，仅在删除量较大时调用。
        /// </summary>
        private static async Task VacuumLogDatabaseAsync(CancellationToken cancellationToken)
        {
            // 2026-09-15: 跟随统一数据根，不再写死在 exe 目录
            var dbPath = DataPaths.LogDatabase;
            if (!System.IO.File.Exists(dbPath)) return;

            using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public sealed class CleanupResult
        {
            public int BarcodeDeletedByTime { get; set; }
            public int BarcodeDeletedByCount { get; set; }
            public int LogDeletedByTime { get; set; }
            public int LogDeletedByCount { get; set; }

            public int TotalDeleted => BarcodeDeletedByTime + BarcodeDeletedByCount
                                     + LogDeletedByTime + LogDeletedByCount;
        }
    }
}
