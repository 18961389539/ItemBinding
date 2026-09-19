using System;
using System.Collections.Generic;

namespace MainAPP.Services
{
    /// <summary>
    /// 编码器上报缓冲（2026-09-17 重构为<b>非消费式</b>）。
    ///
    /// <para><b>重构原因（用户决策）</b>：原实现绑定即消费（<c>RemoveRange(0, foundIdx+1)</c>），
    /// 编码器 UDP 包晚于图像到达时，帧会绑到**上一拍**的值并从此系统性错位。
    /// 现改为：历史**保留**（靠容量上限淘汰最旧），绑定改为「取 Time ≤ 拍照时刻的最近一条」，
    /// 迟到的包在后续帧解析时仍能被正确纳入——配合"延迟 2 帧绑定"（见 HomeViewModel.ReadImage），
    /// 给编码器 UDP 包留出充足的到达时间。</para>
    ///
    /// <para><b>线程安全</b>：Add（编码器接收线程）与 TryResolve（收图线程）并发安全。</para>
    /// </summary>
    internal sealed class EncoderBindingBuffer
    {
        private readonly object _lock = new();
        private readonly List<(uint Encoder, DateTime Time)> _records = new();
        private readonly int _capacity;

        /// <param name="capacity">容量上限：超过时淘汰最旧记录（与 ToVGTService.MaxEncoderHistory 对齐）。</param>
        public EncoderBindingBuffer(int capacity)
        {
            _capacity = Math.Max(16, capacity);
        }

        /// <summary>当前记录数（诊断/测试用）。</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _records.Count;
                }
            }
        }

        /// <summary>追加一条编码器上报；超容量时淘汰最旧记录。</summary>
        public void Add(uint encoder, DateTime time)
        {
            lock (_lock)
            {
                _records.Add((encoder, time));
                if (_records.Count > _capacity)
                {
                    _records.RemoveRange(0, _records.Count - _capacity);
                }
            }
        }

        /// <summary>
        /// 取「Time ≤ searchTime 的最近一条」（**不消费**，历史保留）。
        /// </summary>
        /// <param name="searchTime">绑定的基准时刻（帧的拍照/到达时刻）。</param>
        /// <param name="currentIntervalMs">当前编码器上报间隔（毫秒），用于新鲜度阈值。</param>
        /// <param name="encoder">命中的编码器值；无命中时为 0。</param>
        /// <param name="time">命中记录的上报时刻；无命中时为 <see cref="DateTime.MinValue"/>。</param>
        /// <param name="stale">
        /// true = 记录相对 searchTime 已超龄（滞后 > max(500ms, 2×上报间隔)）——
        /// 说明编码器包丢失/断流，本帧绑到的是上一次触发的编码器，XY 与 Encode 错位约一个触发间隔。
        /// 调用方应按配置拒发或标记。</param>
        /// <returns>false = 没有任何 Time ≤ searchTime 的记录（编码器报文尚未到达）。</returns>
        public bool TryResolve(DateTime searchTime, long currentIntervalMs, out uint encoder, out DateTime time, out bool stale)
        {
            lock (_lock)
            {
                // 二分：最后一条 Time ≤ searchTime（列表按追加时刻天然有序；
                // 主机时钟回跳可能破坏有序性——此时取到的可能不是"最近"一条，属已知边界，见 stale 判定）
                var lo = 0;
                var hi = _records.Count - 1;
                var found = -1;
                while (lo <= hi)
                {
                    var mid = lo + (hi - lo) / 2;
                    if (_records[mid].Time <= searchTime)
                    {
                        found = mid;
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid - 1;
                    }
                }

                if (found < 0)
                {
                    encoder = 0;
                    time = DateTime.MinValue;
                    stale = true;
                    return false;
                }

                var record = _records[found];
                var stalenessMs = (searchTime - record.Time).TotalMilliseconds;
                // 新鲜度阈值：max(500ms, 2×当前上报间隔)——上报间隔抖动时阈值随之放宽
                var thresholdMs = Math.Max(500.0, currentIntervalMs * 2.0);

                encoder = record.Encoder;
                time = record.Time;
                stale = stalenessMs > thresholdMs;
                return true;
            }
        }

        /// <summary>清空全部记录（停止/重置时调用）。</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _records.Clear();
            }
        }
    }
}
