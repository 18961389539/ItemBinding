using MainAPP.Models;
using System.Collections.ObjectModel;
using Serilog;
using System.Windows;
using System.Text.RegularExpressions;

// VSTHRD001: 使用 Dispatcher.BeginInvoke 切换到 UI 线程是 WPF 标准模式，无需 JoinableTaskFactory
#pragma warning disable VSTHRD001

namespace MainAPP.Services
{
    public sealed class LogService : ILogService
    {
        private static readonly Regex ResultPayloadRegex = new(
            @"^[^,\s]+,-?\d+(?:\.\d+)?,-?\d+(?:\.\d+)?,-?\d+(?:\.\d+)?,?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Lazy<LogService> _lazy = new(() => new LogService());
        public static LogService Instance => _lazy.Value;

        // L253: Logs 改为只读集合属性，避免外部重新赋值
        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();

        // L103: 独立计数器，避免 Logs.Count % 500 在 trim 后反复落在同一值触发
        // L278: 改为 long 防止长时间运行溢出
        private long _logAddCount;

        private LogService()
        {
            Log.Information("LogService initialized");
        }

        public void Info(string message)
        {
            AddLog("INFO", message);
            WritePersistentLog(Log.Information, message);
        }

        public void Warning(string message)
        {
            AddLog("WARNING", message);
            WritePersistentLog(Log.Warning, message);
        }

        public void Error(string message)
        {
            AddLog("ERROR", message);
            WritePersistentLog(Log.Error, message);
        }

        public void Debug(string message)
        {
            AddLog("DEBUG", message);
            WritePersistentLog(Log.Debug, message);
        }

        private static void WritePersistentLog(Action<string> writeAction, string message)
        {
            if (ShouldSkipPersistentLog(message))
            {
                return;
            }

            // M328a: 捕获 Serilog 写入异常，避免日志后端故障（磁盘满/文件占用等）导致调用方崩溃，
            // 使用 Trace.WriteLine 记录失败原因，避免再次触发 Serilog 形成递归
            try
            {
                writeAction(message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"WritePersistentLog 写入失败: {ex}");
            }
        }

        private static bool ShouldSkipPersistentLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var trimmed = message.Trim();
            // 2026-09-08: [UDP→ 发送摘要已放行持久化（ToVGTService.SendTo 成功日志，内容含
            // 条码/X/Y/角度，落 logs/log-*.txt 滚动文件与 Saves/DataBase/Logs.db 的 Logs 表供事后追溯）。
            // 该消息仍被 ShouldSkipUiLog 排除，不进入主页日志列表（防刷屏）。
            // Timing( / 编码器超时 / 结果载荷等高频或瞬态消息维持不落盘。
            return trimmed.StartsWith("Timing(", StringComparison.Ordinal)
                || trimmed.StartsWith("编码器监听接收超时", StringComparison.Ordinal)
                || trimmed.Equals("#Error#", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Error", StringComparison.OrdinalIgnoreCase)
                || ResultPayloadRegex.IsMatch(trimmed);
        }

        private void InsertLog(string level, string message)
        {
            var logEntry = new LogEntry(level, message);
            // L31: 改为尾部追加 + 超限时删除头部多余元素
            // HomeView DataGrid 通过 CollectionView SortDescriptions 实现倒序显示（见 HomeView.xaml.cs）
            Logs.Add(logEntry);
            var maxCount = Settings.Instance.LogsCount;
            // M328b: 当 LogsCount 配置缩减导致大量超出时，逐个 RemoveAt(0) 为 O(n²)，
            // 改为批量移除：removeCount > 1 时保存保留项、Clear 后重新添加，整体 O(n)
            int removeCount = Logs.Count - maxCount;
            if (removeCount > 0)
            {
                if (removeCount == 1)
                {
                    // M290: 稳态下仅删除 1 个超出元素，避免 Clear + Add 产生过多 UI 通知
                    Logs.RemoveAt(0);
                }
                else
                {
                    // 配置大幅缩减时批量清理，避免 O(n²) 性能问题
                    var keep = new List<LogEntry>(maxCount);
                    for (int i = removeCount; i < Logs.Count; i++)
                    {
                        keep.Add(Logs[i]);
                    }
                    Logs.Clear();
                    foreach (var item in keep)
                    {
                        Logs.Add(item);
                    }
                }
            }

            // L103: 使用独立计数器，每 500 次实际添加触发一次集合大小记录
            // L359: 使用 Interlocked.Increment 返回值保证原子读取
            // L415b: InsertLog 实际始终在 UI 线程执行（见 AddLog 中 Dispatcher.CheckAccess/BeginInvoke），
            // 此处用 Interlocked.Increment 并非必需，保留作为防御性编程，避免未来从非 UI 线程调用时计数错乱
            var newCount = Interlocked.Increment(ref _logAddCount);
            if (newCount % 500 == 0)
            {
                MemoryDiagnostics.LogCollectionSize("LogService.Logs", Logs.Count);
            }
        }

        // 高频诊断日志不进入 UI 集合（避免 ObservableCollection 通知导致 UI 卡顿）；
        // 是否持久化另由 ShouldSkipPersistentLog 决定（如 [UDP→ 发送摘要仅排除 UI、已放行落盘）。
        private static bool ShouldSkipUiLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var trimmed = message.Trim();
            return trimmed.StartsWith("[MemSnapshot]", StringComparison.Ordinal)
                || trimmed.StartsWith("[MemAlloc]", StringComparison.Ordinal)
                || trimmed.StartsWith("[MemFree]", StringComparison.Ordinal)
                || trimmed.StartsWith("[MemQueue]", StringComparison.Ordinal)
                || trimmed.StartsWith("[MemFrame]", StringComparison.Ordinal)
                || trimmed.StartsWith("[MemGC]", StringComparison.Ordinal)
                || trimmed.StartsWith("[MemTasks]", StringComparison.Ordinal)
                // 编码器监听超时为预期周期性行为，不进入 UI 集合（且不持久化，见 ShouldSkipPersistentLog）
                || trimmed.StartsWith("编码器监听接收超时", StringComparison.Ordinal)
                // [UDP→xxx] 发送摘要不进入 UI 日志列表避免刷屏（已放行持久化，供事后追溯）
                || trimmed.StartsWith("[UDP→", StringComparison.Ordinal);
        }

        private void AddLog(string level, string message)
        {
            try
            {
                // 高频诊断日志只写持久化，不进入 UI 集合
                if (ShouldSkipUiLog(message))
                {
                    return;
                }

                var application = System.Windows.Application.Current;
                if (application?.Dispatcher is null || application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                if (application.Dispatcher.CheckAccess())
                {
                    InsertLog(level, message);
                    return;
                }

                // M151: 委托内包 try-catch，避免 InsertLog 抛出异常时静默吞掉或影响调度器
                _ = application.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        InsertLog(level, message);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"InsertLog 回调异常: {ex}");
                    }
                });
            }
            catch (TaskCanceledException)
            {
                // 应用关闭时调度器取消 BeginInvoke，属预期行为
            }
            catch (InvalidOperationException)
            {
                // 调度器已关闭或正在关闭时 BeginInvoke 抛出，属预期行为
            }
        }

        public void ClearLogs()
        {
            // M150: 检查 Dispatcher 访问权限，非 UI 线程时切回调度器执行
            var application = System.Windows.Application.Current;
            if (application?.Dispatcher is null || application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (application.Dispatcher.CheckAccess())
            {
                Logs.Clear();
                return;
            }

            // L428a: 委托内包 try-catch，与 AddLog 中的异常处理保持一致，避免 Logs.Clear() 抛出异常时静默吞掉或影响调度器
            _ = application.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Logs.Clear();
                }
                catch (Exception ex)
                {
                    Log.Error($"ClearLogs 回调异常: {ex}");
                }
            });
        }
    }
}
