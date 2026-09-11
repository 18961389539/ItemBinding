using Serilog;
using System;
using System.Diagnostics;
using System.Threading;

namespace MainAPP.Services
{
    /// <summary>
    /// 内存诊断工具，用于追踪内存增长的根因。
    /// 
    /// 提供：
    /// 1. 周期性内存快照（GC 各代大小、LOH、工作集、私有内存）
    /// 2. 图像/大对象分配与释放的追踪日志
    /// 3. 队列深度和活跃任务数的追踪
    /// 
    /// 使用方式：
    ///   MemoryDiagnostics.LogSnapshot("label") → 输出当前内存状态
    ///   MemoryDiagnostics.LogAllocation("label", size) → 记录大对象分配
    ///   MemoryDiagnostics.LogDeallocation("label", size) → 记录大对象释放
    ///   MemoryDiagnostics.StartPeriodicSnapshot(intervalSec, cts.Token) → 启动定时快照
    /// </summary>
    public static class MemoryDiagnostics
    {
        private static readonly ILogger _logger = Log.ForContext("Tag", "MEM");

        // 用 Stopwatch 做高精度时间戳，避免每次调用 DateTime.Now
        private static readonly Stopwatch _uptime = Stopwatch.StartNew();

        // L277: 缓存当前进程的 Process 对象，避免每次快照都重新分配和释放 OS 句柄
        private static readonly Process s_currentProcess = Process.GetCurrentProcess();

        // L260: GenerationInfo 索引 3 对应 LOH（大对象堆），提取为命名常量
        private const int LohGenerationIndex = 3;

        // P2-1: 每帧诊断日志采样间隔，避免 INFO 级日志每帧刷屏导致日志膨胀（14h 累计 78 万行）
        // 取 100 时，按 10 FPS 计算约每 10s 输出一次，仍能反映内存趋势
        private const int FrameSummarySamplingInterval = 100;
        private const int ActiveTasksSamplingInterval = 100;
        private static int s_frameSummaryCounter;
        private static int s_activeTasksCounter;

        /// <summary>
        /// 获取当前内存使用的简短摘要，供 UI 日志展示。
        /// 返回格式：WorkingSet=xxxMB, Managed=yyyMB
        /// </summary>
        public static string GetCurrentMemoryUsage()
        {
            var managedBytes = GC.GetTotalMemory(forceFullCollection: false);
            s_currentProcess.Refresh();
            var workingSet = s_currentProcess.WorkingSet64;
            return $"WorkingSet={workingSet / (1024.0 * 1024.0):F1}MB, Managed={managedBytes / (1024.0 * 1024.0):F1}MB";
        }

        /// <summary>
        /// 记录一次内存快照，包含 GC 各代堆大小、LOH、工作集、私有内存等。
        /// 调用频率不宜过高（推荐每 30~60 秒一次），否则日志量过大。
        /// </summary>
        /// <param name="label">快照标签，用于区分不同调用点</param>
        public static void LogSnapshot(string label)
        {
            var uptimeSec = _uptime.Elapsed.TotalSeconds;
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);

            var totalManaged = GC.GetTotalMemory(forceFullCollection: false);
            // 合并为一次 GetGCMemoryInfo 调用，避免重复访问 GC 堆信息
            var gcInfo = GC.GetGCMemoryInfo();
            var gen0Size = gcInfo.GenerationInfo[0].SizeAfterBytes;
            var heapSize = gcInfo.HeapSizeBytes;
            var lohSize = GetLOHSize(gcInfo);

            // L277: 使用缓存的 Process 对象并刷新，替代每次创建新实例
            s_currentProcess.Refresh();
            var workingSet = s_currentProcess.WorkingSet64;
            var privateMemory = s_currentProcess.PrivateMemorySize64;
            var pagedMemory = s_currentProcess.PagedMemorySize64;
            var virtualMemory = s_currentProcess.VirtualMemorySize64;
            var threadCount = s_currentProcess.Threads.Count;

            _logger.Information(
                "[MemSnapshot] Label={Label}, UptimeSec={Uptime:F0}, " +
                "TotalManaged={TotalManaged:F2}MB, HeapSize={HeapSize:F2}MB, LOH={LOH:F2}MB, " +
                "WorkingSet={WS:F2}MB, PrivateMem={Private:F2}MB, PagedMem={Paged:F2}MB, VirtualMem={VM:F2}MB, " +
                "GC_Gen0={GC0}, GC_Gen1={GC1}, GC_Gen2={GC2}, " +
                "Threads={Threads}",
                label, uptimeSec,
                totalManaged / (1024.0 * 1024.0),
                heapSize / (1024.0 * 1024.0),
                lohSize / (1024.0 * 1024.0),
                workingSet / (1024.0 * 1024.0),
                privateMemory / (1024.0 * 1024.0),
                pagedMemory / (1024.0 * 1024.0),
                virtualMemory / (1024.0 * 1024.0),
                gen0, gen1, gen2,
                threadCount);
        }

        /// <summary>
        /// 记录大对象分配（图像、大型 byte[] 等）。
        /// </summary>
        /// <param name="label">分配描述</param>
        /// <param name="sizeBytes">分配大小（字节）</param>
        /// <param name="extra">额外信息（如尺寸、格式等）</param>
        public static void LogAllocation(string label, long sizeBytes, string? extra = null)
        {
            var uptimeSec = _uptime.Elapsed.TotalSeconds;
            var totalManaged = GC.GetTotalMemory(forceFullCollection: false);

            _logger.Information(
                "[MemAlloc] Label={Label}, Size={Size:F2}MB, TotalManaged={Total:F2}MB, UptimeSec={Uptime:F0}{Extra}",
                label,
                sizeBytes / (1024.0 * 1024.0),
                totalManaged / (1024.0 * 1024.0),
                uptimeSec,
                extra is null ? "" : $", Extra={extra}");
        }

        /// <summary>
        /// 记录大对象释放。
        /// </summary>
        public static void LogDeallocation(string label, long sizeBytes)
        {
            var uptimeSec = _uptime.Elapsed.TotalSeconds;
            var totalManaged = GC.GetTotalMemory(forceFullCollection: false);

            _logger.Information(
                "[MemFree] Label={Label}, Size={Size:F2}MB, TotalManaged={Total:F2}MB, UptimeSec={Uptime:F0}",
                label,
                sizeBytes / (1024.0 * 1024.0),
                totalManaged / (1024.0 * 1024.0),
                uptimeSec);
        }

        /// <summary>
        /// 记录队列深度（用于追踪 ConcurrentQueue 等是否持续增长）。
        /// </summary>
        public static void LogQueueDepth(string queueName, int count, int capacity = -1)
        {
            if (capacity > 0)
            {
                _logger.Information(
                    "[MemQueue] Queue={Queue}, Count={Count}, Capacity={Capacity}, Usage={Usage:P1}",
                    queueName, count, capacity, (double)count / capacity);
            }
            else
            {
                _logger.Information(
                    "[MemQueue] Queue={Queue}, Count={Count}",
                    queueName, count);
            }
        }

        /// <summary>
        /// 记录活跃任务数。
        /// P2-1: 默认按 <see cref="ActiveTasksSamplingInterval"/> 采样，避免每帧 INFO 刷屏。
        /// 设置 <paramref name="forceLog"/> = true 可强制输出（用于关键节点或退出摘要）。
        /// </summary>
        public static void LogActiveTasks(string poolName, int activeCount, bool forceLog = false)
        {
            if (!forceLog && Interlocked.Increment(ref s_activeTasksCounter) % ActiveTasksSamplingInterval != 1)
            {
                return;
            }
            _logger.Information(
                "[MemTask] Pool={Pool}, ActiveTasks={Count}",
                poolName, activeCount);
        }

        /// <summary>
        /// 记录集合大小（ObservableCollection、List 等）。
        /// </summary>
        public static void LogCollectionSize(string collectionName, int count)
        {
            _logger.Information(
                "[MemCollection] Collection={Collection}, Count={Count}",
                collectionName, count);
        }

        /// <summary>
        /// 记录图像帧处理的完整生命周期摘要（输入大小、峰值内存等）。
        /// 在每帧处理结束时调用一次。
        /// P2-1: 默认按 <see cref="FrameSummarySamplingInterval"/> 采样，避免每帧 INFO 刷屏。
        /// 设置 <paramref name="forceLog"/> = true 可强制输出（用于关键节点或退出摘要）。
        /// </summary>
        public static void LogFrameSummary(
            uint frameNumber,
            long imageDataSize,
            long sourceImageSize,
            long drawImageSize,
            int queueDepth,
            int activeInferenceCount,
            int timingTotalMs,
            bool forceLog = false)
        {
            // P2-1: 默认按采样间隔输出，避免每帧刷屏（实测 14h 累计 38 万行）
            if (!forceLog && Interlocked.Increment(ref s_frameSummaryCounter) % FrameSummarySamplingInterval != 1)
            {
                return;
            }
            var totalManaged = GC.GetTotalMemory(forceFullCollection: false);
            _logger.Information(
                "[MemFrame] Frame={Frame}, ImgData={ImgData:F2}MB, SrcImg={Src:F2}MB, DrawImg={Draw:F2}MB, " +
                "TotalManaged={Total:F2}MB, QueueDepth={QD}, ActiveInf={AI}, TimingMs={Time}",
                frameNumber,
                imageDataSize / (1024.0 * 1024.0),
                sourceImageSize / (1024.0 * 1024.0),
                drawImageSize / (1024.0 * 1024.0),
                totalManaged / (1024.0 * 1024.0),
                queueDepth,
                activeInferenceCount,
                timingTotalMs);
        }

        /// <summary>
        /// 估算 ImageSharp Image&lt;Rgb24&gt; 对象占用的内存（仅像素缓冲区，不含对象头）。
        /// </summary>
        public static long EstimateImageSize(int width, int height)
        {
            // Rgb24 = 3 bytes per pixel
            return (long)width * height * 3;
        }

        /// <summary>
        /// 启动周期性内存快照（推荐 30~60 秒一次）。
        /// </summary>
        /// <param name="intervalSeconds">快照间隔（秒）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task RunPeriodicSnapshotAsync(int intervalSeconds, CancellationToken cancellationToken)
        {
            // L351: 校验 intervalSeconds 为正数，避免传入 0 或负数导致 Task.Delay 立即返回形成忙循环
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(intervalSeconds, 0);

            _logger.Information("[MemDiagnostics] 启动周期性内存快照，间隔 {Interval}s", intervalSeconds);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken).ConfigureAwait(false);
                    LogSnapshot("Periodic");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[MemDiagnostics] 周期性快照异常");
                }
            }

            _logger.Information("[MemDiagnostics] 周期性内存快照已停止");
        }

        /// <summary>
        /// 获取 LOH（大对象堆）大小（近似值）。
        /// </summary>
        /// <param name="gcInfo">已获取的 GC 内存信息，避免重复调用 GetGCMemoryInfo</param>
        private static long GetLOHSize(GCMemoryInfo gcInfo)
        {
            // 通过 GC 代获取 LOH 信息
            // GenerationInfo 索引: 0=Gen0, 1=Gen1, 2=Gen2, 3=LOH, 4=POH (.NET 5+)
            var gens = gcInfo.GenerationInfo;
            // L260: 使用命名常量替代魔法数字 3
            if (gens.Length > LohGenerationIndex)
            {
                return gens[LohGenerationIndex].SizeAfterBytes;
            }
            return 0;
        }

        /// <summary>
        /// 强制一次完整的 GC 回收，记录回收前后的内存状态。
        /// 谨慎使用：仅在怀疑内存泄漏时手动触发。
        /// </summary>
        public static void LogForcedGC(string reason)
        {
            var before = GC.GetTotalMemory(forceFullCollection: false);
            _logger.Information("[MemGC] 强制 GC 前，Reason={Reason}, Before={Before:F2}MB", reason, before / (1024.0 * 1024.0));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var after = GC.GetTotalMemory(forceFullCollection: false);
            var freed = before - after;
            _logger.Information("[MemGC] 强制 GC 后，Reason={Reason}, After={After:F2}MB, Freed={Freed:F2}MB",
                reason, after / (1024.0 * 1024.0), freed / (1024.0 * 1024.0));
        }

        /// <summary>
        /// 释放缓存的 Process 对象等资源，在应用退出时调用。
        /// M231: s_currentProcess 持有 OS 句柄，需显式 Dispose 避免句柄泄漏。
        /// </summary>
        public static void Shutdown()
        {
            // 强制释放 ImageSharp 内部缓冲池
            try { SixLabors.ImageSharp.Configuration.Default.MemoryAllocator.ReleaseRetainedResources(); }
            catch { }
            s_currentProcess.Dispose();
        }
    }
}
