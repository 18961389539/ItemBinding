using Serilog;
using System.IO;

namespace MainAPP.Services
{
    /// <summary>
    /// 临时文件日志服务（独立于 <see cref="LogService"/>）。
    ///
    /// 职责边界：
    /// - <see cref="LogService"/>：管理 UI 可见的 <see cref="Models.LogEntry"/> 集合，并写入主 Serilog Log（静态）。
    ///   对高频诊断日志（[MemSnapshot]、编码器超时等）做过滤，不进入 UI 集合。
    /// - <see cref="TemporaryLog"/>：独立的 Serilog Logger 实例，写入 Saves/temp-logs/temp-log-{date}.txt，
    ///   Debug 级别、每日滚动、保留 7 天。用于高频临时诊断数据（如帧处理异常堆栈），
    ///   避免大量调试输出污染主日志文件。
    ///
    /// 两者不合并的原因：LogService 面向用户展示与主持久化日志，需控制 UI 集合大小与写入频率；
    /// TemporaryLog 面向开发调试，需保留完整 Debug 级别输出且独立滚动清理。
    /// 合并会导致职责混杂且主日志文件被调试输出撑大，故保持独立。
    /// </summary>
    public static class TemporaryLog
    {
        public static readonly Serilog.Core.Logger? Log;

        // L352: 标记是否已 CloseAndFlush，避免重复 Dispose；CloseAndFlush 后 Log 字段虽非 null 但不再可用
        private static volatile bool _closed;

        static TemporaryLog()
        {
            // M230: 静态构造函数体包入 try-catch，失败时 Log 保持 null，
            // 避免 TypeInitializationException 使类型永久不可用
            try
            {
                // 2026-09-15: 跟随统一数据根，不再写死在 exe 目录
                var tempLogDir = DataPaths.TempLogsDir;
                Directory.CreateDirectory(tempLogDir);

                Log = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                    .WriteTo.File(Path.Combine(tempLogDir, "temp-log-.txt"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                    .CreateLogger();

                // 2026-09-15 兜底：App.OnExit 中的 CloseAndFlush 位于多个 await 之后，
                // 而 WPF 不 await async void OnExit —— 那段代码可能来不及执行。
                // 这里在"本类型确实被使用过"的前提下注册进程退出兜底，保证临时日志不丢；
                // 未使用过 TemporaryLog 时不会走到静态构造函数，因而也不会多生成空日志文件。
                AppDomain.CurrentDomain.ProcessExit += static (_, _) => CloseAndFlush();
            }
            catch (Exception ex)
            {
                // 初始化失败时 Log 保持 null，调用方通过 Log?. 安全跳过
                System.Diagnostics.Trace.WriteLine($"TemporaryLog 初始化失败: {ex}");
            }
        }
        // 显式关闭并刷新这个 logger 实例
        // L258: Serilog.Core.Logger 实现了 IDisposable，直接调用 Dispose 无需 as 转换
        public static void CloseAndFlush()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            Log?.Dispose();
        }
    }
}
