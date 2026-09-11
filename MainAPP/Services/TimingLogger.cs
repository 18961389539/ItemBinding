using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MainAPP.Services
{
    #region TimingLogger
    /// <summary>
    /// 简单的计时记录器，用于跟踪处理流程中各阶段的耗时并在 Dispose 时记录日志。
    /// </summary>
    /// <remarks>
    /// 使用示例：using var timings = new TimingLogger("frame");
    /// </remarks>
    public sealed class TimingLogger : IDisposable
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly List<(string Label, long Ms)> _splits = new();
        private readonly string _name;
        private readonly string _device;

        // P1-12: 暴露最近一帧的耗时数据，供状态栏轮询读取
        // 使用 Interlocked 保证读/写线程可见性（volatile 不支持 long）
        private static long _lastInferenceMs;
        private static long _lastTotalMs;
        private static string _lastDevice = "CPU";

        // 耗时历史趋势：环形缓冲区存储最近 100 帧的推理耗时与总耗时，供 UI 展示统计趋势
        // REVIEW-FIX(耗时曲线): 容量 60→100（用户要求最多保留 100 个数据），并新增总耗时历史
        // REVIEW-FIX(耗时曲线多事件): 新增图像加载/绘制/保存三个分段历史，与推理/总耗时并行写入
        private const int HistoryCapacity = 100;
        private static readonly long[] _inferenceHistory = new long[HistoryCapacity];
        private static readonly long[] _totalHistory = new long[HistoryCapacity];
        private static readonly long[] _loadHistory = new long[HistoryCapacity];
        private static readonly long[] _drawHistory = new long[HistoryCapacity];
        private static readonly long[] _saveHistory = new long[HistoryCapacity];
        private static int _historyIndex;
        private static int _historyCount;
        private static readonly object _historyLock = new();

        /// <summary>最近一帧纯推理耗时（ms），= Inference split - PrepareFolder split</summary>
        public static long LastInferenceMs => Interlocked.Read(ref _lastInferenceMs);
        /// <summary>最近一帧总耗时（ms）</summary>
        public static long LastTotalMs => Interlocked.Read(ref _lastTotalMs);
        /// <summary>最近一帧使用的推理设备名</summary>
        public static string LastDevice => _lastDevice;

        /// <summary>最近 100 帧的推理耗时历史（按写入顺序，最早在前，无数据返回空列表）</summary>
        public static IReadOnlyList<long> RecentInferenceMs
        {
            get => SnapshotHistory(_inferenceHistory);
        }

        /// <summary>最近 100 帧的总耗时历史（按写入顺序，最早在前，无数据返回空列表）</summary>
        public static IReadOnlyList<long> RecentTotalMs
        {
            get => SnapshotHistory(_totalHistory);
        }

        /// <summary>最近 100 帧的图像加载耗时历史（LoadImage 段）</summary>
        public static IReadOnlyList<long> RecentLoadMs
        {
            get => SnapshotHistory(_loadHistory);
        }

        /// <summary>最近 100 帧的绘制耗时历史（MutateDraw 段）</summary>
        public static IReadOnlyList<long> RecentDrawMs
        {
            get => SnapshotHistory(_drawHistory);
        }

        /// <summary>最近 100 帧的保存耗时历史（SaveDraw 段）</summary>
        public static IReadOnlyList<long> RecentSaveMs
        {
            get => SnapshotHistory(_saveHistory);
        }

        /// <summary>从环形缓冲按写入顺序拷贝快照（无数据返回空数组）</summary>
        private static IReadOnlyList<long> SnapshotHistory(long[] history)
        {
            lock (_historyLock)
            {
                if (_historyCount == 0) return Array.Empty<long>();
                var snapshot = new long[_historyCount];
                if (_historyCount < HistoryCapacity)
                {
                    // 缓冲区未满，有效数据在 [0, _historyCount)
                    Array.Copy(history, snapshot, _historyCount);
                }
                else
                {
                    // 缓冲区已满，_historyIndex 指向最旧数据，按写入顺序拷贝
                    Array.Copy(history, _historyIndex, snapshot, 0, HistoryCapacity - _historyIndex);
                    Array.Copy(history, 0, snapshot, HistoryCapacity - _historyIndex, _historyIndex);
                }
                return snapshot;
            }
        }

        /// <summary>最近 100 帧推理耗时最大值（ms），无数据返回 0</summary>
        public static long MaxInferenceMs
        {
            get
            {
                lock (_historyLock)
                {
                    if (_historyCount == 0) return 0;
                    long max = 0;
                    int count = Math.Min(_historyCount, HistoryCapacity);
                    for (int i = 0; i < count; i++)
                    {
                        if (_inferenceHistory[i] > max) max = _inferenceHistory[i];
                    }
                    return max;
                }
            }
        }

        /// <summary>最近 100 帧推理耗时平均值（ms），无数据返回 0</summary>
        public static long AvgInferenceMs
        {
            get
            {
                lock (_historyLock)
                {
                    if (_historyCount == 0) return 0;
                    long sum = 0;
                    int count = Math.Min(_historyCount, HistoryCapacity);
                    for (int i = 0; i < count; i++)
                    {
                        sum += _inferenceHistory[i];
                    }
                    return count > 0 ? sum / count : 0;
                }
            }
        }

        /// <summary>最近 100 帧推理耗时 P95（ms），无数据返回 0</summary>
        public static long P95InferenceMs
        {
            get
            {
                var snapshot = RecentInferenceMs;
                if (snapshot.Count == 0) return 0;
                var sorted = snapshot.ToArray();
                Array.Sort(sorted);
                // P95 索引：向上取整后减 1，保证至少 1 个样本时也能取到
                int idx = (int)Math.Ceiling(sorted.Length * 0.95) - 1;
                if (idx < 0) idx = 0;
                if (idx >= sorted.Length) idx = sorted.Length - 1;
                return sorted[idx];
            }
        }

        public TimingLogger(string? name, string device = "CPU")
        {
            _name = name ?? string.Empty;
            _device = device;
        }

        /// <summary>
        /// 记录一个分段标签及当前耗时（ms）。
        /// </summary>
        public void Split(string label)
        {
            _splits.Add((label, _sw.ElapsedMilliseconds));
        }

        /// <summary>
        /// 总耗时（毫秒）。
        /// </summary>
        public int TotalTime => (int)_sw.ElapsedMilliseconds;

        /// <summary>
        /// 停止计时并在日志中输出每个分段的耗时信息及当前内存占用（若存在分段）。
        /// </summary>
        public void Dispose()
        {
            _sw.Stop();
            // REVIEW-FIX(日志刷屏): 不再每帧输出 Timing INFO 日志（30fps 会刷屏）。
            // 耗时统计仍写入静态字段与历史环形缓冲（状态栏/耗时曲线继续工作）；
            // 需要单帧明细时仍可通过 GetLog() 保存到 log_*.txt。

            // P1-12: 更新静态字段供 UI 状态栏读取
            // 纯推理耗时 = Inference split - PrepareFolder split（两者均为累计值，差值即为 Inference 段耗时）
            long inferenceMs = 0;
            int prepIdx = -1, infIdx = -1;
            for (int i = 0; i < _splits.Count; i++)
            {
                if (_splits[i].Label == "PrepareFolder") prepIdx = i;
                else if (_splits[i].Label == "Inference") infIdx = i;
            }
            if (infIdx >= 0)
            {
                long baseMs = (prepIdx >= 0 && prepIdx < infIdx) ? _splits[prepIdx].Ms : 0;
                inferenceMs = _splits[infIdx].Ms - baseMs;
            }
            Interlocked.Exchange(ref _lastInferenceMs, inferenceMs);
            Interlocked.Exchange(ref _lastTotalMs, _sw.ElapsedMilliseconds);
            _lastDevice = _device;

            // 分段耗时（累计值差值）：图像加载 LoadImage、绘制 MutateDraw、保存 SaveDraw
            long loadMs = GetSegmentMs("LoadImage");
            long drawMs = GetSegmentMs("MutateDraw");
            long saveMs = GetSegmentMs("SaveDraw");

            // 同步写入各耗时历史环形缓冲区，供 UI 统计趋势使用
            lock (_historyLock)
            {
                _inferenceHistory[_historyIndex] = inferenceMs;
                _totalHistory[_historyIndex] = _sw.ElapsedMilliseconds;
                _loadHistory[_historyIndex] = loadMs;
                _drawHistory[_historyIndex] = drawMs;
                _saveHistory[_historyIndex] = saveMs;
                _historyIndex = (_historyIndex + 1) % HistoryCapacity;
                if (_historyCount < HistoryCapacity) _historyCount++;
            }
        }

        /// <summary>
        /// 计算指定分段的耗时（ms）：该分段累计值 - 其前一分段累计值。
        /// 分段不存在返回 0；首个分段以 0 为基准。
        /// </summary>
        private long GetSegmentMs(string label)
        {
            int idx = -1;
            for (int i = 0; i < _splits.Count; i++)
            {
                if (_splits[i].Label == label)
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0) return 0;
            long prevMs = idx > 0 ? _splits[idx - 1].Ms : 0;
            long delta = _splits[idx].Ms - prevMs;
            return delta > 0 ? delta : 0;
        }

        /// <summary>
        /// 获取格式化的性能日志字符串（含设备信息）。
        /// </summary>
        public string GetLog()
        {
            var parts = string.Join(", ", _splits.Select(s => $"{s.Label}={s.Ms}ms"));
            // L379a: 无分段时不输出尾部多余的 "; "
            return _splits.Count > 0
                ? $"Timing({_name})[{_device}] Total={_sw.ElapsedMilliseconds}ms; {parts}"
                : $"Timing({_name})[{_device}] Total={_sw.ElapsedMilliseconds}ms";
        }
    }
    #endregion
}
