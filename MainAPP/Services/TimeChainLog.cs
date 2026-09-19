using Serilog;
using System.IO;

namespace MainAPP.Services
{
    /// <summary>
    /// 2026-09-17: 时间链专用日志——[时间] 帧记录单独成文件，不进主日志（log-.txt / Logs.db）。
    /// 动机：每帧一条（@1fps ≈ 8.6 万行/天），混入主日志会淹没有效信息；
    /// 单独成文件后可直接整文件查看/导出，分析「采集→绑定→组装」全链时间语义。
    /// 文件：{DataPaths.LogsDir}/timechain-YYYYMMDD.txt，按天滚动，保留 32 天（与主日志 txt 一致）。
    /// 注意：本类自建 Serilog ILogger（独立文件 sink），与全局 Log.Logger 互不影响；
    /// 主日志的 ProcessExit 兜底只 CloseAndFlush 全局实例，本类自行挂 ProcessExit 做 Dispose。
    /// </summary>
    public static class TimeChainLog
    {
        private static readonly Lazy<ILogger> _lazy = new(CreateLogger);
        private static int _disposed;

        /// <summary>写入一条时间链记录。失败静默降级（Trace 留痕），绝不影响检测主流程。</summary>
        public static void Info(string message)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            // M328a 同款思路：捕获写入异常，避免日志后端故障（磁盘满/文件占用）导致调用方崩溃
            try
            {
                _lazy.Value.Information("{Message}", message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"TimeChainLog 写入失败: {ex}");
            }
        }

        private static ILogger CreateLogger()
        {
            try
            {
                var logDirectory = DataPaths.LogsDir;
                Directory.CreateDirectory(logDirectory);
                var logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(Path.Combine(logDirectory, "timechain-.txt"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 32,
                        // 共享模式：文件可被外部读取（现场可边运行边 tail/复制该文件做分析）
                        shared: true)
                    .CreateLogger();

                AppDomain.CurrentDomain.ProcessExit += static (_, _) => Shutdown();
                return logger;
            }
            catch (Exception ex)
            {
                // 日志后端不可用（磁盘满/权限）：退化为无 sink 的空 logger（写不进去也不抛），主日志留痕
                System.Diagnostics.Trace.WriteLine($"TimeChainLog 初始化失败: {ex}");
                try { Log.Warning("TimeChainLog 初始化失败，时间链记录将不可用: {Error}", ex.Message); } catch { }
                return new LoggerConfiguration().CreateLogger();
            }
        }

        /// <summary>进程退出时刷新并关闭独立文件 sink；可重复调用。</summary>
        public static void Shutdown()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || !_lazy.IsValueCreated)
            {
                return;
            }

            try
            {
                (_lazy.Value as IDisposable)?.Dispose();
            }
            catch
            {
                // 进程已退出，放弃
            }
        }
    }
}
