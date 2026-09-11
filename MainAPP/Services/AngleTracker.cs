using System;
using System.Collections.Generic;
using MainAPP.Models;

namespace MainAPP.Services
{
    /// <summary>
    /// 跨帧角度锁定跟踪器（输送线场景）。
    /// 同一产品（按编码器差分预测 或 世界坐标 匹配）连续多次成像时：
    /// <list type="bullet">
    /// <item>某一帧拍到角度特征（角度模型成功）→ 锁定该角度，后续帧沿用；</item>
    /// <item>未拍到特征但有锁定 → 输出锁定角度，不被失败帧污染；</item>
    /// <item>未拍到特征且无锁定（首帧）→ 输出 <see cref="UnknownAngle"/>（-9999，表示未知）。</item>
    /// </list>
    /// 编码器有效时优先用编码器差分匹配（不受视觉抖动/角度歧义影响），
    /// 编码器无效（值为 0）或 Δ̂ 未建立时回退世界坐标欧氏距离匹配。
    /// 输出域：(-180,180]（2026-09-05 全系统统一为机器人发送域，DbModel.Angle 落库即此域）。
    /// </summary>
    public class AngleTracker
    {
        private readonly IAlgorithmSettings _settings;

        /// <summary>单例（向后兼容，与 ProductTracker 同生命周期，配方切换时 Clear）。</summary>
        public static AngleTracker Instance { get; } = new();

        public AngleTracker(IAlgorithmSettings? settings = null)
        {
            _settings = settings ?? Models.Settings.Instance;
        }

        /// <summary>
        /// 未知角度哨兵值。未识别到角度特征且无历史锁定时输出该值，
        /// 下游（VGT/DB）应识别并跳过，不得当作真实角度使用。
        /// </summary>
        public const double UnknownAngle = -9999.0;

        private sealed class TrackedProduct
        {
            public double WorldX;
            public double WorldY;
            /// <summary>最近一次匹配的编码器计数值。</summary>
            public uint Encoder;
            /// <summary>已锁定的角度模型输出；null 表示尚未拍到特征。</summary>
            public double? LockedAngle;
            public DateTime LastSeen;
        }

        private readonly List<TrackedProduct> _items = new();
        private readonly object _lock = new();

        /// <summary>跟踪项过期时间（秒），超过未再次匹配则移除（产品已离开视野）。</summary>
        private const int ExpireSeconds = 5;

        /// <summary>位置匹配阈值兜底（mm），Settings.DedupPositionThreshold 无效时使用。</summary>
        private const double DefaultPositionThreshold = 3.0;

        /// <summary>编码器差分匹配的最小容差（counts）。</summary>
        private const long MinEncoderTolerance = 20;

        /// <summary>帧间编码器位移的平滑估计（Δ̂，counts/帧）；null 表示尚未建立。</summary>
        private double? _deltaEstimator;

        /// <summary>
        /// 解析本帧角度：匹配同一产品，应用锁定策略。
        /// </summary>
        /// <param name="encoder">编码器计数值（无效时为 0）。</param>
        /// <param name="worldX">产品世界坐标 X（mm）。</param>
        /// <param name="worldY">产品世界坐标 Y（mm）。</param>
        /// <param name="modelAngle">角度模型输出角度；角度模型失败/未启用时为 null。</param>
        /// <returns>本帧最终角度 (-180,180]（2026-09-05 起全系统角度规范域由 [0,360) 迁移至机器人发送域）；
        /// 无锁定且模型失败时返回 <see cref="UnknownAngle"/>（-9999）。</returns>
        public double Resolve(uint encoder, double worldX, double worldY, double? modelAngle)
        {
            var now = DateTime.Now;
            var threshold = _settings.DedupPositionThreshold;
            if (threshold <= 0)
            {
                threshold = DefaultPositionThreshold;
            }

            lock (_lock)
            {
                // 清理过期项（产品离开视野）
                _items.RemoveAll(item => (now - item.LastSeen).TotalSeconds > ExpireSeconds);

                var item = encoder != 0
                    ? FindMatchByEncoder(encoder, worldX, worldY, threshold, now)
                    : FindMatchByPosition(worldX, worldY, threshold);

                if (item is null)
                {
                    // 新产品：新建跟踪项；首帧即成功则直接锁定
                    item = new TrackedProduct
                    {
                        WorldX = worldX,
                        WorldY = worldY,
                        Encoder = encoder,
                        LockedAngle = modelAngle,
                        LastSeen = now,
                    };
                    _items.Add(item);
                    return modelAngle.HasValue ? Normalize(modelAngle.Value) : UnknownAngle;
                }

                item.LastSeen = now;
                item.WorldX = worldX;
                item.WorldY = worldY;
                item.Encoder = encoder;

                if (modelAngle.HasValue)
                {
                    // 拍到特征：更新锁定并输出
                    item.LockedAngle = modelAngle;
                    return Normalize(modelAngle.Value);
                }

                // 未拍到特征：沿用锁定角度；从未锁定过则输出未知哨兵值
                return item.LockedAngle.HasValue ? Normalize(item.LockedAngle.Value) : UnknownAngle;
            }
        }

        /// <summary>
        /// 编码器差分匹配：Δ̂ 已建立时按预测位置匹配；未建立时用位置匹配过渡并建立 Δ̂。
        /// </summary>
        private TrackedProduct? FindMatchByEncoder(uint encoder, double worldX, double worldY, double threshold, DateTime now)
        {
            // Δ̂ 未建立：用位置匹配过渡（启动阶段），成功后用本次观测差建立 Δ̂
            if (!_deltaEstimator.HasValue)
            {
                var posItem = FindMatchByPosition(worldX, worldY, threshold);
                if (posItem is not null)
                {
                    _deltaEstimator = (uint)(encoder - posItem.Encoder);
                }

                return posItem;
            }

            int bestIdx = -1;
            long bestErr = long.MaxValue;
            long predicted = (long)Math.Round(_deltaEstimator.Value);
            var tol = Math.Max(MinEncoderTolerance, (long)Math.Round(_deltaEstimator.Value * 0.5));

            for (int i = 0; i < _items.Count; i++)
            {
                // 无符号前进距离（模 2^32，天然处理编码器回绕）
                long fwd = (uint)(encoder - _items[i].Encoder);
                if (fwd <= 0 || fwd > int.MaxValue)
                {
                    continue; // 编码器倒退或异常跳变
                }

                long err = Math.Abs(fwd - predicted);
                if (err < bestErr)
                {
                    bestErr = err;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0 || bestErr > tol)
            {
                return null;
            }

            // 指数平滑更新 Δ̂：新观测占 30%
            long obs = (uint)(encoder - _items[bestIdx].Encoder);
            _deltaEstimator = _deltaEstimator.Value * 0.7 + obs * 0.3;
            return _items[bestIdx];
        }

        private TrackedProduct? FindMatchByPosition(double worldX, double worldY, double threshold)
        {
            var thresholdSq = threshold * threshold;
            foreach (var item in _items)
            {
                var dx = item.WorldX - worldX;
                var dy = item.WorldY - worldY;
                if (dx * dx + dy * dy <= thresholdSq)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 将任意角度值归一化到系统规范域 (-180,180]（与机器人发送域一致）。
        /// 内部实现委托 <see cref="ToVGT.ToRobotAngle"/>：取模 360 并对负值归一，保证
        /// 任意来源值（模型输出 [0,360)+Offset 越界、OpenCV 矩形角 [-90,0]、配方负偏移）都正确落域。
        /// NaN 视为 0（历史语义保留）。
        /// </summary>
        private static double Normalize(double angle)
        {
            if (double.IsNaN(angle))
            {
                return 0;
            }

            return ToVGT.ToRobotAngle(angle);
        }

        /// <summary>
        /// 清空跟踪列表（配方切换/换班时调用）。
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
                _deltaEstimator = null;
            }
        }
    }
}
