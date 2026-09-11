using MainAPP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// 条码数据服务（单例），提供检测记录的 CRUD、批量写入、分页查询、综合搜索和 CSV 导入导出功能。
    /// 使用 ConcurrentQueue + SemaphoreSlim 实现线程安全的批量写入：
    /// 记录先入队，当累计达到 BatchSize（500条）时自动刷新到 SQLite 数据库（WAL 模式）。
    /// </summary>
    public class BarcodeDataService : IDisposable, IBarcodeDataService
    {
        private static readonly Lazy<BarcodeDataService> _lazy = new(() => new BarcodeDataService());
        public static BarcodeDataService Instance => _lazy.Value;

        // M700: _pendingRecords 是 ConcurrentQueue + Interlocked 计数，去掉了 _pendingLock。
        // _flushLock 保留 SemaphoreSlim——async 方法中需要跨 await 串行化 SQLite 写入。
        private readonly ConcurrentQueue<DbModel> _pendingRecords = new();
        private readonly SemaphoreSlim _flushLock = new(1, 1);
        private int _pendingCount;
        // M224: _disposed 标记为 volatile，保证跨线程可见性
        private volatile bool _disposed;

        private const int BatchSize = 500;
        // L102: 每条 DbModel 记录的粗略内存估算字节数，用于内存诊断日志
        private const int EstimatedRecordSizeBytes = 512;
        // L383: CsvFieldCount 已移除，ImportFromCsvAsync 改为按表头名称解析字段
        // M501: SearchAsync 默认 Take 上限，防止无分页调用方在大数据量场景下 OOM
        private const int DefaultSearchTakeLimit = 10000;

        private BarcodeDataService()
        {
        }

        // M17: WAL 是数据库文件级别的持久设置，只需设置一次
        // M343a: _walInitialized 在 DB 文件重建后永不重置。若用户手动删除 .db 文件后重建，
        // 由于该标志已置 1，WAL 模式不会被重新设置。暂不在此处重置，因为重置会引入并发竞态
        // （多线程同时调用 CreateDbContext 时可能重复执行 PRAGMA），需配合更细粒度的锁才能安全处理。
        private static int _walInitialized;
        // L386: WAL 设置失败重试计数与上限，超过后接受默认日志模式避免日志刷屏
        private static int _walRetryCount;
        private const int MaxWalRetryCount = 3;

        // M502: DbContext 创建策略说明
        // 当前 CreateDbContext 每次操作 new 一个 AppDbContext，依赖 OnConfiguring 内部配置连接字符串。
        // EF Core 8 推荐在 DI 中注册 AddDbContextFactory<AppDbContext>() 并注入 IDbContextFactory<AppDbContext>，
        // 由框架池化 DbContext，可获得更好的创建性能与统一的生命周期管理。
        // 本服务为 Lazy<T> 单例（不经过 DI 构造），AppDbContext 也在 App.xaml.cs.InitializeDatabaseAsync
        // 中被直接 new 出来使用，迁移到 IDbContextFactory 需要：
        //   1) AppDbContext 增加 DbContextOptions<AppDbContext> 构造参数，移除 OnConfiguring 中的连接配置；
        //   2) App.ConfigureServices 中注册 services.AddDbContextFactory<AppDbContext>(opt => opt.UseSqlite(...))；
        //   3) BarcodeDataService 改为通过 DI 解析（放弃 Lazy<T> 单例模式）或采用 ServiceLocator 反模式获取工厂；
        //   4) 同步调整 App.xaml.cs.InitializeDatabaseAsync 等所有 new AppDbContext() 调用点。
        // 改动跨多文件且涉及单例生命周期变化，风险较高，暂不实施，留待后续统一重构 DI 时处理。
        private static AppDbContext CreateDbContext()
        {
            var dbContext = new AppDbContext();
            dbContext.ChangeTracker.AutoDetectChangesEnabled = false;
            // 仅首次创建时执行 PRAGMA，避免每次操作都发 SQL
            if (Interlocked.Exchange(ref _walInitialized, 1) == 0)
            {
                try
                {
                    dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                }
                catch (Exception ex)
                {
                    // WAL 设置失败不致命，回退为默认日志模式
                    LogService.Instance.Warning($"WAL 设置失败: {ex.Message}");
                    // L386: 超过最大重试次数后不再重置 _walInitialized，接受默认日志模式
                    if (Interlocked.Increment(ref _walRetryCount) < MaxWalRetryCount)
                    {
                        Interlocked.Exchange(ref _walInitialized, 0);
                    }
                }
            }
            return dbContext;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BarcodeDataService));
            }
        }

        private bool EnqueuePending(DbModel model)
        {
            EnsureNotDisposed();
            // REVIEW-FIX: 入队与 Drain/清库共用 _flushLock，保证"入队计数"与"Drain 出队"原子一致，
            // 避免 Drain 期间并发入队的计数被清零（计数低估、flush 触发延迟）以及清库期间新入队
            // 记录绕过 Drain 造成"清库后数据复现"。SemaphoreSlim 无竞争时走快速路径，开销可忽略。
            _flushLock.Wait();
            try
            {
                _pendingRecords.Enqueue(model);
                int count = Interlocked.Increment(ref _pendingCount);
                return count >= BatchSize;
            }
            finally
            {
                _flushLock.Release();
            }
        }

        private bool EnqueuePendingRange(IEnumerable<DbModel> models)
        {
            EnsureNotDisposed();
            int localCount = 0;
            _flushLock.Wait();
            try
            {
                foreach (var model in models)
                {
                    _pendingRecords.Enqueue(model);
                    localCount++;
                }
            }
            finally
            {
                _flushLock.Release();
            }

            int pendingCountSnapshot = Interlocked.Add(ref _pendingCount, localCount);
            bool shouldFlush = pendingCountSnapshot >= BatchSize;

            // L280: 出锁后再记录日志，避免持锁期间 IO
            if (shouldFlush || pendingCountSnapshot > 0 && pendingCountSnapshot % 100 == 0)
            {
                MemoryDiagnostics.LogQueueDepth("BarcodeData(Pending)", pendingCountSnapshot, BatchSize);
            }

            return shouldFlush;
        }

        // REVIEW-FIX: Drain 改为"计数快照 + 出队对应数量"。原实现（CompareExchange 检查 +
        // 全量出队 + Exchange(0)）在 Drain 期间并发入队时会清掉新入队计数。新实现先
        // Exchange 快照计数，再只出队该数量的记录：Drain 期间新入队的记录保留在队列且
        // 计数由其自身 Increment 维护，二者始终一致。
        // 注意：仅在持有 _flushLock 的上下文中调用（FlushPendingAsync / ClearAllAsync /
        // DeleteOlderThanAsync / TrimToMaxCountAsync）。
        private List<DbModel> DrainPending()
        {
            int count = Interlocked.Exchange(ref _pendingCount, 0);
            if (count == 0)
                return [];

            var pending = new List<DbModel>(count);
            for (int i = 0; i < count; i++)
            {
                if (!_pendingRecords.TryDequeue(out var model))
                    break;
                pending.Add(model);
            }
            return pending;
        }

        // L424a: allowWhenDisposed 参数为死代码（无任何调用方传入 true），已删除
        private async Task FlushPendingAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BarcodeDataService));
            }

            await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var pending = DrainPending();
                if (pending.Count == 0)
                    return;

                MemoryDiagnostics.LogQueueDepth("BarcodeData(PendingFlush)", pending.Count, BatchSize);

                try
                {
                    using var dbContext = CreateDbContext();
                    dbContext.BarcodeData.AddRange(pending);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // REVIEW-FIX: 写入失败（如 SQLITE_BUSY、磁盘满）时把已出队记录重新入队，
                    // 避免 Drain 后 SaveChanges 抛异常导致记录静默丢失。重入队后继续上抛，
                    // 由调用方决定是否重试；记录保底不再丢失（至少保留在内存队列等待下次 flush）。
                    RequeuePending(pending);
                    MemoryDiagnostics.LogQueueDepth("BarcodeData(Requeued)", pending.Count, BatchSize);
                    LogService.Instance.Error($"条码批量写入失败，{pending.Count} 条记录已重新入队: {ex.Message}");
                    throw;
                }

                MemoryDiagnostics.LogDeallocation("BarcodeData(Flushed)", pending.Count * EstimatedRecordSizeBytes);
            }
            finally
            {
                _flushLock.Release();
            }
        }

        /// <summary>
        /// 把一组记录重新放回待写队列（flush 失败回队用）。
        /// 仅在持有 _flushLock 的上下文中调用。
        /// </summary>
        private void RequeuePending(List<DbModel> records)
        {
            foreach (var model in records)
            {
                _pendingRecords.Enqueue(model);
            }
            Interlocked.Add(ref _pendingCount, records.Count);
        }

        /// <summary>
        /// 获取所有数据
        /// </summary>
        public async Task<List<DbModel>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            return await dbContext.BarcodeData
                .AsNoTracking()
                .OrderByDescending(d => d.DetectTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 根据ID获取数据
        /// </summary>
        public async Task<DbModel?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            return await dbContext.BarcodeData
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 添加新数据
        /// </summary>
        public async Task<DbModel> AddAsync(DbModel model, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            if (EnqueuePending(model))
            {
                await FlushPendingAsync(cancellationToken).ConfigureAwait(false);
            }

            return model;
        }

        /// <summary>
        /// 批量添加数据
        /// </summary>
        public async Task AddRangeAsync(IEnumerable<DbModel> models, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            if (EnqueuePendingRange(models))
            {
                await FlushPendingAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SavePendingAsync(CancellationToken cancellationToken = default)
        {
            await FlushPendingAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 更新数据
        /// </summary>
        public async Task UpdateAsync(DbModel model, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            dbContext.BarcodeData.Update(model);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 删除数据
        /// </summary>
        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            var model = await dbContext.BarcodeData.FirstOrDefaultAsync(d => d.Id == id, cancellationToken).ConfigureAwait(false);
            if (model != null)
            {
                dbContext.BarcodeData.Remove(model);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 根据二维码查询数据
        /// </summary>
        public async Task<List<DbModel>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
        {
            // M160: 校验 barcode 非空
            ArgumentNullException.ThrowIfNull(barcode);
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            // REVIEW-FIX: 增加 Take 上限，防止子串匹配命中海量记录时一次性加载 OOM
            return await dbContext.BarcodeData
                .AsNoTracking()
                .Where(d => d.Barcode.Contains(barcode))
                .OrderByDescending(d => d.DetectTime)
                .Take(DefaultSearchTakeLimit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 获取最近N条数据
        /// </summary>
        public async Task<List<DbModel>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            return await dbContext.BarcodeData
                .AsNoTracking()
                .OrderByDescending(d => d.DetectTime)
                .Take(count)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 获取某个时间范围的数据
        /// </summary>
        public async Task<List<DbModel>> GetByTimeRangeAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            // L264: 校验时间范围
            if (start > end)
            {
                throw new ArgumentException("开始时间不能晚于结束时间。", nameof(start));
            }
            using var dbContext = CreateDbContext();
            return await dbContext.BarcodeData
                .AsNoTracking()
                .Where(d => d.DetectTime >= start && d.DetectTime <= end)
                .OrderByDescending(d => d.DetectTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 获取数据总数
        /// </summary>
        public async Task<int> GetCountAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            return await dbContext.BarcodeData.CountAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 清空所有数据
        /// </summary>
        public async Task ClearAllAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            // M342a: 获取 _flushLock，持有锁期间完成 drain + delete，避免清库期间其它线程
            // 将待处理记录写入数据库导致清库后仍有残留数据
            await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // H79b: 清空待处理队列，避免清库后待写入记录再次入库
                DrainPending();
                using var dbContext = CreateDbContext();
                await dbContext.BarcodeData.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _flushLock.Release();
            }
        }

        /// <summary>
        /// 按时间清理：删除 DetectTime 早于 cutoff 的所有记录。
        /// 利用 IX_BarcodeData_DetectTime 索引高效定位。返回删除的行数。
        /// </summary>
        public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            if (cutoff >= DateTime.Now) return 0;
            await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DrainPending();
                using var dbContext = CreateDbContext();
                    // 使用 ExecuteDeleteAsync 直接生成 DELETE WHERE，避免加载实体到内存
                    return await dbContext.BarcodeData
                        .Where(d => d.DetectTime < cutoff)
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
            }
            finally
            {
                _flushLock.Release();
            }
        }

        /// <summary>
        /// 按数量清理：删除超出 maxCount 的最旧记录（按 DetectTime 升序）。
        /// 返回删除的行数。maxCount <= 0 表示不限制。
        /// </summary>
        public async Task<int> TrimToMaxCountAsync(int maxCount, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            if (maxCount <= 0) return 0;
            await _flushLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DrainPending();
                using var dbContext = CreateDbContext();
                var total = await dbContext.BarcodeData.CountAsync(cancellationToken).ConfigureAwait(false);
                if (total <= maxCount) return 0;

                var toDelete = total - maxCount;
                // 取最旧的 N 条记录的 Id，然后批量删除
                var idsToDelete = await dbContext.BarcodeData
                    .OrderBy(d => d.DetectTime)
                    .Take(toDelete)
                    .Select(d => d.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (idsToDelete.Count == 0) return 0;
                // REVIEW-FIX: 分批删除（每批 500 条），避免单条 SQL 的 IN 参数超过 SQLite
                // 变量数上限（32766），否则清理 3 万+ 条时抛 "too many SQL variables" 且被上层吞掉，
                // 数据库持续膨胀。使用子查询分批，边删边推进。
                const int DeleteBatchSize = 500;
                int deleted = 0;
                for (int i = 0; i < idsToDelete.Count; i += DeleteBatchSize)
                {
                    var batchIds = idsToDelete.Skip(i).Take(DeleteBatchSize).ToList();
                    deleted += await dbContext.BarcodeData
                        .Where(d => batchIds.Contains(d.Id))
                        .ExecuteDeleteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                return deleted;
            }
            finally
            {
                _flushLock.Release();
            }
        }

        /// <summary>
        /// 根据编码范围查询数据
        /// </summary>
        public async Task<List<DbModel>> GetByEncodeRangeAsync(long minEncode, long maxEncode, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            return await dbContext.BarcodeData
                .AsNoTracking()
                .Where(d => d.Encode >= minEncode && d.Encode <= maxEncode)
                .OrderByDescending(d => d.DetectTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 根据位置范围查询数据
        /// </summary>
        public async Task<List<DbModel>> GetByPositionRangeAsync(double minX, double maxX, double minY, double maxY, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            return await dbContext.BarcodeData
                .AsNoTracking()
                .Where(d => d.WorldX >= minX && d.WorldX <= maxX && d.WorldY >= minY && d.WorldY <= maxY)
                .OrderByDescending(d => d.DetectTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 根据角度范围查询数据
        /// </summary>
        public async Task<List<DbModel>> GetByAngelRangeAsync(double minAngel, double maxAngel, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            return await dbContext.BarcodeData
                .AsNoTracking()
                .Where(d => d.Angle >= minAngel && d.Angle <= maxAngel)
                .OrderByDescending(d => d.DetectTime)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 分页获取数据
        /// </summary>
        public async Task<(List<DbModel> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            // M158: 校验分页参数
            ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
            using var dbContext = CreateDbContext();
            var totalCount = await dbContext.BarcodeData.CountAsync(cancellationToken).ConfigureAwait(false);
            var items = await dbContext.BarcodeData
                .AsNoTracking()
                .OrderByDescending(d => d.DetectTime)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return (items, totalCount);
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        // L427a: 重构为单次 SQL 聚合查询。原实现执行 7 次独立 DB 往返（1 次 Count + 6 次 AverageAsync），
        // 现改为单次 Select + FirstOrDefaultAsync，由 EF Core 翻译为一条含子查询聚合的 SQL，
        // 将 7 次 DB 往返合并为 1 次。空表场景下 FirstOrDefaultAsync 返回 null，按原语义返回全 0。
        // 字段使用 (double?) 强转以匹配 SQL AVG 在空集返回 NULL 的语义，最终通过 ?? 0 兜底。
        public async Task<(double AvgCostTime, double AvgX, double AvgY, double AvgAngel, double AvgWidth, double AvgHeight, int TotalRecords)> GetStatisticsAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();

            var stats = await dbContext.BarcodeData
                .AsNoTracking()
                .Select(x => new
                {
                    Count = dbContext.BarcodeData.Count(),
                    AvgCostTime = dbContext.BarcodeData.Average(d => (double?)d.CostTime),
                    AvgX = dbContext.BarcodeData.Average(d => (double?)d.WorldX),
                    AvgY = dbContext.BarcodeData.Average(d => (double?)d.WorldY),
                    AvgAngel = dbContext.BarcodeData.Average(d => (double?)d.Angle),
                    AvgWidth = dbContext.BarcodeData.Average(d => (double?)d.Width),
                    AvgHeight = dbContext.BarcodeData.Average(d => (double?)d.Height),
                })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (stats == null || stats.Count == 0)
            {
                return (0, 0, 0, 0, 0, 0, 0);
            }

            return (
                stats.AvgCostTime ?? 0,
                stats.AvgX ?? 0,
                stats.AvgY ?? 0,
                stats.AvgAngel ?? 0,
                stats.AvgWidth ?? 0,
                stats.AvgHeight ?? 0,
                stats.Count
            );
        }

        /// <summary>
        /// 批量删除数据
        /// </summary>
        public async Task BulkDeleteAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            // M159: 校验 ids 非空
            ArgumentNullException.ThrowIfNull(ids);
            var idList = ids.Distinct().ToArray();
            if (idList.Length == 0)
            {
                return;
            }

            // REVIEW-FIX: 分批删除，避免单条 SQL 的 IN 参数超过 SQLite 变量上限（32766），
            // 大批量删除（如全选删除数万条）时不再抛 "too many SQL variables"。
            const int DeleteBatchSize = 500;
            using var dbContext = CreateDbContext();
            for (int i = 0; i < idList.Length; i += DeleteBatchSize)
            {
                var batchIds = idList.Skip(i).Take(DeleteBatchSize).ToArray();
                await dbContext.BarcodeData
                    .Where(d => batchIds.Contains(d.Id))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 综合搜索数据
        /// </summary>
        public async Task<List<DbModel>> SearchAsync(string? barcode = null, long? minEncode = null, long? maxEncode = null,
            double? minX = null, double? maxX = null, double? minY = null, double? maxY = null,
            double? minAngel = null, double? maxAngel = null, double? minScore = null, double? maxScore = null,
            DateTime? startTime = null, DateTime? endTime = null, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            using var dbContext = CreateDbContext();
            var query = dbContext.BarcodeData.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(barcode))
                query = query.Where(d => d.Barcode.Contains(barcode));

            if (minEncode.HasValue)
                query = query.Where(d => d.Encode >= minEncode.Value);
            if (maxEncode.HasValue)
                query = query.Where(d => d.Encode <= maxEncode.Value);

            if (minX.HasValue)
                query = query.Where(d => d.WorldX >= minX.Value);
            if (maxX.HasValue)
                query = query.Where(d => d.WorldX <= maxX.Value);

            if (minY.HasValue)
                query = query.Where(d => d.WorldY >= minY.Value);
            if (maxY.HasValue)
                query = query.Where(d => d.WorldY <= maxY.Value);

            if (minAngel.HasValue)
                query = query.Where(d => d.Angle >= minAngel.Value);
            if (maxAngel.HasValue)
                query = query.Where(d => d.Angle <= maxAngel.Value);

            if (minScore.HasValue)
                query = query.Where(d => d.Score >= minScore.Value);
            if (maxScore.HasValue)
                query = query.Where(d => d.Score <= maxScore.Value);

            if (startTime.HasValue)
                query = query.Where(d => d.DetectTime >= startTime.Value);
            if (endTime.HasValue)
                query = query.Where(d => d.DetectTime <= endTime.Value);

            return await query
                .OrderByDescending(d => d.DetectTime)
                .Take(DefaultSearchTakeLimit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 分页综合搜索数据
        /// </summary>
        public async Task<(List<DbModel> Items, int TotalCount)> SearchPagedAsync(int pageNumber, int pageSize,
            string? barcode = null, long? minEncode = null, long? maxEncode = null,
            double? minX = null, double? maxX = null, double? minY = null, double? maxY = null,
            double? minAngel = null, double? maxAngel = null, double? minScore = null, double? maxScore = null,
            DateTime? startTime = null, DateTime? endTime = null, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            // M158: 校验分页参数
            ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
            using var dbContext = CreateDbContext();
            var query = dbContext.BarcodeData.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(barcode))
                query = query.Where(d => d.Barcode.Contains(barcode));

            if (minEncode.HasValue)
                query = query.Where(d => d.Encode >= minEncode.Value);
            if (maxEncode.HasValue)
                query = query.Where(d => d.Encode <= maxEncode.Value);

            if (minX.HasValue)
                query = query.Where(d => d.WorldX >= minX.Value);
            if (maxX.HasValue)
                query = query.Where(d => d.WorldX <= maxX.Value);

            if (minY.HasValue)
                query = query.Where(d => d.WorldY >= minY.Value);
            if (maxY.HasValue)
                query = query.Where(d => d.WorldY <= maxY.Value);

            if (minAngel.HasValue)
                query = query.Where(d => d.Angle >= minAngel.Value);
            if (maxAngel.HasValue)
                query = query.Where(d => d.Angle <= maxAngel.Value);

            if (minScore.HasValue)
                query = query.Where(d => d.Score >= minScore.Value);
            if (maxScore.HasValue)
                query = query.Where(d => d.Score <= maxScore.Value);

            if (startTime.HasValue)
                query = query.Where(d => d.DetectTime >= startTime.Value);
            if (endTime.HasValue)
                query = query.Where(d => d.DetectTime <= endTime.Value);

            var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
            var items = await query
                .OrderByDescending(d => d.DetectTime)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return (items, totalCount);
        }

        /// <summary>
        /// 导出数据到CSV文件（全表导出）
        /// </summary>
        public async Task ExportToCsvAsync(string filePath, CancellationToken cancellationToken = default)
        {
            // M33: 流式查询避免大表 OOM，不一次性加载到内存
            EnsureNotDisposed();
            // L265: 校验文件路径
            ArgumentException.ThrowIfNullOrEmpty(filePath);
            using var dbContext = CreateDbContext();
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            await writer.WriteLineAsync(GetCsvHeader()).ConfigureAwait(false);
            await foreach (var record in dbContext.BarcodeData
                .AsNoTracking()
                .OrderByDescending(d => d.DetectTime)
                .AsAsyncEnumerable()
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                await writer.WriteLineAsync(FormatCsvRow(record)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 按筛选条件导出数据到CSV文件
        /// </summary>
        public async Task ExportToCsvAsync(string filePath,
            string? barcode = null,
            long? minEncode = null, long? maxEncode = null,
            double? minScore = null, double? maxScore = null,
            DateTime? startTime = null, DateTime? endTime = null,
            CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            ArgumentException.ThrowIfNullOrEmpty(filePath);
            using var dbContext = CreateDbContext();
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            await writer.WriteLineAsync(GetCsvHeader()).ConfigureAwait(false);

            // 复用 SearchAsync 的筛选逻辑
            var query = dbContext.BarcodeData.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(barcode))
                query = query.Where(d => d.Barcode.Contains(barcode));
            if (minEncode.HasValue)
                query = query.Where(d => d.Encode >= minEncode.Value);
            if (maxEncode.HasValue)
                query = query.Where(d => d.Encode <= maxEncode.Value);
            if (minScore.HasValue)
                query = query.Where(d => d.Score >= minScore.Value);
            if (maxScore.HasValue)
                query = query.Where(d => d.Score <= maxScore.Value);
            if (startTime.HasValue)
                query = query.Where(d => d.DetectTime >= startTime.Value);
            if (endTime.HasValue)
                query = query.Where(d => d.DetectTime <= endTime.Value);
            query = query.OrderByDescending(d => d.DetectTime);

            await foreach (var record in query.AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteLineAsync(FormatCsvRow(record)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 从CSV文件导入数据（分批独立事务）。
        /// L383: 按表头名称解析，同时兼容旧版 10 列格式（X/Y）和新版 18 列格式（WorldX/WorldY 等）
        /// #7: 每 ImportBatchSize=1000 条记录为一个独立事务，批次失败只回滚当前批次，不影响已提交批次。
        /// </summary>
        /// <param name="filePath">CSV 文件路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <param name="progress">可选进度回调，每批次完成后触发</param>
        /// <returns>导入结果摘要（总行数、成功数、失败数、失败明细）</returns>
        public async Task<CsvImportResult> ImportFromCsvAsync(string filePath, CancellationToken cancellationToken = default, IProgress<CsvImportProgress>? progress = null)
        {
            EnsureNotDisposed();
            // L265: 校验文件路径
            ArgumentException.ThrowIfNullOrEmpty(filePath);

            // M332a: 分批读取解析与写入，避免一次性将全部记录加载到内存（ImportBatchSize = 1000）
            const int ImportBatchSize = 1000;
            var batch = new List<DbModel>(ImportBatchSize);

            using var reader = new StreamReader(filePath, Encoding.UTF8);
            // L383: 读取并解析表头，构建列名 → 索引映射，按名称查找字段
            var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                LogService.Instance.Warning("CSV 导入: 表头为空，已跳过导入");
                return CsvImportResult.Empty;
            }
            var headerParts = ParseCsvLine(headerLine);
            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headerParts.Length; i++)
            {
                // 首次出现的列名生效，重复列名忽略
                if (!columnIndex.ContainsKey(headerParts[i]))
                    columnIndex[headerParts[i]] = i;
            }
            // 兼容旧格式：旧表头用 X/Y，新表头用 WorldX/WorldY
            var worldXIdx = columnIndex.TryGetValue("WorldX", out var wxi) ? wxi :
                            columnIndex.TryGetValue("X", out var xi) ? xi : -1;
            var worldYIdx = columnIndex.TryGetValue("WorldY", out var wyi) ? wyi :
                            columnIndex.TryGetValue("Y", out var yi) ? yi : -1;
            // 必需字段索引（按新表头名查找，缺失时跳过该行）
            int IdIdx = columnIndex.GetValueOrDefault("Id", -1),
                BarcodeIdx = columnIndex.GetValueOrDefault("Barcode", -1),
                EncodeIdx = columnIndex.GetValueOrDefault("Encode", -1),
                AngleIdx = columnIndex.GetValueOrDefault("Angle", -1),
                WidthIdx = columnIndex.GetValueOrDefault("Width", -1),
                HeightIdx = columnIndex.GetValueOrDefault("Height", -1),
                CostTimeIdx = columnIndex.GetValueOrDefault("CostTime", -1),
                DetectTimeIdx = columnIndex.GetValueOrDefault("DetectTime", -1);
            // 可选字段索引（缺失时使用默认值）
            int ImageXIdx = columnIndex.GetValueOrDefault("ImageX", -1),
                ImageYIdx = columnIndex.GetValueOrDefault("ImageY", -1),
                AreaIdx = columnIndex.GetValueOrDefault("Area", -1),
                ImageBarcodeXIdx = columnIndex.GetValueOrDefault("ImageBarcodeX", -1),
                ImageBarcodeYIdx = columnIndex.GetValueOrDefault("ImageBarcodeY", -1),
                BarcodeScoreIdx = columnIndex.GetValueOrDefault("BarcodeScore", -1),
                ScoreIdx = columnIndex.GetValueOrDefault("Score", -1),
                SpeedIdx = columnIndex.GetValueOrDefault("Speed", -1),
                // 2026-09-08: 灰度判向统计可选列（旧文件无此列时为 -1，读取 null）
                BrightMeanIdx = columnIndex.GetValueOrDefault("BrightMean", -1),
                DarkMeanIdx = columnIndex.GetValueOrDefault("DarkMean", -1),
                BrightnessDiffIdx = columnIndex.GetValueOrDefault("BrightnessDiff", -1);

            int lineNum = 1;
            int totalRows = 0;
            int importedCount = 0;
            int failedCount = 0;
            int batchCount = 0;
            int failedBatchCount = 0;
            var errors = new List<string>();

            // #7: 使用单一 DbContext，但每批使用独立事务，失败只回滚当前批次
            using var dbContext = CreateDbContext();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                lineNum++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = ParseCsvLine(line);
                // L383: 必需字段全部存在才处理，否则跳过
                if (IdIdx < 0 || BarcodeIdx < 0 || EncodeIdx < 0 ||
                    worldXIdx < 0 || worldYIdx < 0 ||
                    AngleIdx < 0 || WidthIdx < 0 || HeightIdx < 0 ||
                    CostTimeIdx < 0 || DetectTimeIdx < 0 ||
                    parts.Length <= new[] { IdIdx, BarcodeIdx, EncodeIdx, worldXIdx, worldYIdx,
                        AngleIdx, WidthIdx, HeightIdx, CostTimeIdx, DetectTimeIdx }.Max())
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行字段数不足或列缺失，已跳过");
                    continue;
                }

                // M38: 使用 TryParse 容错，损坏行跳过而非中断整个导入
                // M118: 仅校验 Id 字段为合法整数，不再使用其值（让数据库自增）
                if (!int.TryParse(parts[IdIdx], out _))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 Id 解析失败 '{parts[IdIdx]}'，已跳过");
                    continue;
                }
                if (!uint.TryParse(parts[EncodeIdx], out var encode))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 Encode 解析失败 '{parts[EncodeIdx]}'，已跳过");
                    continue;
                }
                if (!double.TryParse(parts[worldXIdx], CultureInfo.InvariantCulture, out var worldX))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 WorldX 解析失败 '{parts[worldXIdx]}'，已跳过");
                    continue;
                }
                if (!double.TryParse(parts[worldYIdx], CultureInfo.InvariantCulture, out var worldY))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 WorldY 解析失败 '{parts[worldYIdx]}'，已跳过");
                    continue;
                }
                if (!double.TryParse(parts[AngleIdx], CultureInfo.InvariantCulture, out var angle))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 Angle 解析失败 '{parts[AngleIdx]}'，已跳过");
                    continue;
                }
                if (!double.TryParse(parts[WidthIdx], CultureInfo.InvariantCulture, out var width))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 Width 解析失败 '{parts[WidthIdx]}'，已跳过");
                    continue;
                }
                if (!double.TryParse(parts[HeightIdx], CultureInfo.InvariantCulture, out var height))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 Height 解析失败 '{parts[HeightIdx]}'，已跳过");
                    continue;
                }
                if (!int.TryParse(parts[CostTimeIdx], out var costTime))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 CostTime 解析失败 '{parts[CostTimeIdx]}'，已跳过");
                    continue;
                }
                // H60: DateTimeStyles 改为 None，与导出端本地时间字符串语义一致
                if (!DateTime.TryParse(parts[DetectTimeIdx], CultureInfo.InvariantCulture, DateTimeStyles.None, out var detectTime))
                {
                    LogService.Instance.Warning($"CSV 导入: 第 {lineNum} 行 DetectTime 解析失败 '{parts[DetectTimeIdx]}'，已跳过");
                    continue;
                }

                // L383: 可选字段缺失时使用默认值，保证新旧格式都兼容
                double.TryParse(ImageXIdx >= 0 && parts.Length > ImageXIdx ? parts[ImageXIdx] : "0",
                    CultureInfo.InvariantCulture, out var imageX);
                double.TryParse(ImageYIdx >= 0 && parts.Length > ImageYIdx ? parts[ImageYIdx] : "0",
                    CultureInfo.InvariantCulture, out var imageY);
                double.TryParse(AreaIdx >= 0 && parts.Length > AreaIdx ? parts[AreaIdx] : "0",
                    CultureInfo.InvariantCulture, out var area);
                double.TryParse(ImageBarcodeXIdx >= 0 && parts.Length > ImageBarcodeXIdx ? parts[ImageBarcodeXIdx] : "0",
                    CultureInfo.InvariantCulture, out var imageBarcodeX);
                double.TryParse(ImageBarcodeYIdx >= 0 && parts.Length > ImageBarcodeYIdx ? parts[ImageBarcodeYIdx] : "0",
                    CultureInfo.InvariantCulture, out var imageBarcodeY);
                double.TryParse(BarcodeScoreIdx >= 0 && parts.Length > BarcodeScoreIdx ? parts[BarcodeScoreIdx] : "0",
                    CultureInfo.InvariantCulture, out var barcodeScore);
                double.TryParse(ScoreIdx >= 0 && parts.Length > ScoreIdx ? parts[ScoreIdx] : "0",
                    CultureInfo.InvariantCulture, out var score);
                long.TryParse(SpeedIdx >= 0 && parts.Length > SpeedIdx ? parts[SpeedIdx] : "0",
                    CultureInfo.InvariantCulture, out var speed);

                // 2026-09-08: 灰度判向统计——空单元格/列缺失 → null（TryParse 失败兜底 null，不阻断该行导入）
                double? brightMean = ParseNullableDouble(parts, BrightMeanIdx);
                double? darkMean = ParseNullableDouble(parts, DarkMeanIdx);
                double? brightnessDiff = ParseNullableDouble(parts, BrightnessDiffIdx);

                batch.Add(new DbModel
                {
                    // M118: Id 设为 0 让数据库自增，避免导入 CSV 时 Id 冲突
                    Id = 0,
                    Barcode = parts[BarcodeIdx],
                    Encode = encode,
                    WorldX = worldX,
                    WorldY = worldY,
                    Angle = angle,
                    Width = width,
                    Height = height,
                    ImageX = imageX,
                    ImageY = imageY,
                    Area = area,
                    ImageBarcodeX = imageBarcodeX,
                    ImageBarcodeY = imageBarcodeY,
                    BarcodeScore = barcodeScore,
                    Score = score,
                    Speed = speed,
                    CostTime = costTime,
                    DetectTime = detectTime,
                    BrightMean = brightMean,
                    DarkMean = darkMean,
                    BrightnessDiff = brightnessDiff
                });
                totalRows++;

                // #7: 达到批次大小时以独立事务写入，失败只回滚当前批次
                if (batch.Count >= ImportBatchSize)
                {
                    batchCount++;
                    bool success = await FlushBatchAsync(dbContext, batch, batchCount, cancellationToken).ConfigureAwait(false);
                    if (success)
                    {
                        importedCount += batch.Count;
                    }
                    else
                    {
                        failedCount += batch.Count;
                        failedBatchCount++;
                        errors.Add($"批次 {batchCount}（{batch.Count} 条）写入失败");
                    }
                    batch.Clear();
                    progress?.Report(new CsvImportProgress(importedCount, failedCount, batchCount));
                }
            }

            // 写入剩余不足一个批次的记录
            if (batch.Count > 0)
            {
                batchCount++;
                bool success = await FlushBatchAsync(dbContext, batch, batchCount, cancellationToken).ConfigureAwait(false);
                if (success)
                {
                    importedCount += batch.Count;
                }
                else
                {
                    failedCount += batch.Count;
                    failedBatchCount++;
                    errors.Add($"批次 {batchCount}（{batch.Count} 条）写入失败");
                }
                batch.Clear();
                progress?.Report(new CsvImportProgress(importedCount, failedCount, batchCount));
            }

            if (failedBatchCount > 0)
            {
                LogService.Instance.Warning($"CSV 导入完成: 共 {totalRows} 行，成功 {importedCount}，失败 {failedCount}（{failedBatchCount} 个批次失败）");
            }
            else
            {
                LogService.Instance.Info($"CSV 导入完成: 共 {totalRows} 行，全部成功");
            }

            return new CsvImportResult(totalRows, importedCount, failedCount, batchCount, failedBatchCount, errors);
        }

        /// <summary>
        /// #7: 以独立事务写入一个批次的记录。失败时回滚当前事务并返回 false，不影响后续批次。
        /// </summary>
        private async Task<bool> FlushBatchAsync(AppDbContext dbContext, List<DbModel> batch, int batchNumber, CancellationToken cancellationToken)
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                dbContext.BarcodeData.AddRange(batch);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                // L272: 显式捕获 Exception，回滚当前批次事务
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    System.Diagnostics.Trace.WriteLine($"CSV 导入: 批次 {batchNumber} 回滚失败: {rollbackEx}");
                }
                LogService.Instance.Error($"CSV 导入: 批次 {batchNumber}（{batch.Count} 条）写入失败: {ex.Message}");
                return false;
            }
            finally
            {
                // 释放已处理实体的跟踪，避免 ChangeTracker 无限累积占用内存
                dbContext.ChangeTracker.Clear();
            }
        }

        private static string CsvEscape(string? field)
        {
            // M346a: null 防护，避免 record.Barcode 为 null 时后续 Contains 抛 NullReferenceException
            field ??= string.Empty;
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }

            return field;
        }

        // 2026-09-08: CSV 可空 double 单元格解析——列缺失(idx<0)/空串/解析失败均返回 null，不阻断行导入
        private static double? ParseNullableDouble(string[] parts, int idx)
        {
            if (idx < 0 || parts.Length <= idx || string.IsNullOrWhiteSpace(parts[idx]))
            {
                return null;
            }
            return double.TryParse(parts[idx], CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        // P0-4: CSV 导出列与 DataGrid 显示列保持一致，统一表头与行格式，避免两处 ExportToCsvAsync 重载列定义漂移
        // 2026-09-08: 表尾追加灰度判向统计列（BrightMean/DarkMean/BrightnessDiff，null 导出为空串；
        // 导入按列名解析，缺列不影响旧文件回导）
        private static string GetCsvHeader() =>
            "Id,Barcode,Encode,WorldX,WorldY,Angle,Width,Height,ImageX,ImageY,Area,ImageBarcodeX,ImageBarcodeY,BarcodeScore,Score,Speed,CostTime,DetectTime,BrightMean,DarkMean,BrightnessDiff";

        private static string FormatCsvRow(DbModel record)
        {
            // H60: 去掉 DetectTime 的 U 后缀，导出/导入统一按本地时间字符串处理
            // M243: double 字段使用 InvariantCulture，避免在不同区域设置下产生不兼容的小数分隔符
            // 2026-09-08: 表尾追加灰度判向统计列（double? 无值导出空串，Invariant 数字格式）
            return FormattableString.Invariant(
                $"{record.Id},{CsvEscape(record.Barcode)},{record.Encode},{record.WorldX},{record.WorldY},{record.Angle},{record.Width},{record.Height},{record.ImageX},{record.ImageY},{record.Area},{record.ImageBarcodeX},{record.ImageBarcodeY},{record.BarcodeScore},{record.Score},{record.Speed},{record.CostTime},{record.DetectTime:yyyy-MM-dd HH:mm:ss},{record.BrightMean?.ToString(System.Globalization.CultureInfo.InvariantCulture)},{record.DarkMean?.ToString(System.Globalization.CultureInfo.InvariantCulture)},{record.BrightnessDiff?.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        private static string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            var inQuotes = false;
            var currentField = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }

            result.Add(currentField.ToString());
            return result.ToArray();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // M282: 冗余 fire-and-forget 已删除，flush 由 App.OnExit 中 SavePendingAsync 单独处理
            // H54: 不 Dispose _flushLock，避免后台 flush 完成后 Release 抛 ObjectDisposedException；交由 GC 处理
            // L434a: _flushLock 永不 Dispose。SemaphoreSlim 内部仅持有托管资源（无 OS 句柄），
            // GC 最终会回收，不会造成非托管资源泄漏。
        }
    }

    /// <summary>
    /// #7: CSV 导入结果摘要
    /// </summary>
    public sealed class CsvImportResult
    {
        public static CsvImportResult Empty { get; } = new(0, 0, 0, 0, 0, Array.Empty<string>());

        public CsvImportResult(int totalRows, int importedCount, int failedCount, int batchCount, int failedBatchCount, IReadOnlyList<string> errors)
        {
            TotalRows = totalRows;
            ImportedCount = importedCount;
            FailedCount = failedCount;
            BatchCount = batchCount;
            FailedBatchCount = failedBatchCount;
            Errors = errors;
        }

        /// <summary>成功解析的数据行总数</summary>
        public int TotalRows { get; }
        /// <summary>成功写入数据库的记录数</summary>
        public int ImportedCount { get; }
        /// <summary>因批次事务失败未能写入的记录数</summary>
        public int FailedCount { get; }
        /// <summary>处理的批次总数</summary>
        public int BatchCount { get; }
        /// <summary>失败的批次数</summary>
        public int FailedBatchCount { get; }
        /// <summary>失败明细（每批次的错误描述）</summary>
        public IReadOnlyList<string> Errors { get; }

        public bool IsAllSuccess => FailedBatchCount == 0;
    }

    /// <summary>
    /// #7: CSV 导入进度（用于 IProgress 回调）
    /// </summary>
    public sealed record CsvImportProgress(int ImportedCount, int FailedCount, int BatchCount);
}
