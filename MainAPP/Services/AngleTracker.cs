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

        /// <summary>跟踪项过期时间（秒），超过未再次匹配则移除（产品已离开视野）。
        /// 值来自设置（判据见 AlgorithmSettings.TrackerExpireSeconds——慢速产线须调大）。</summary>
        private int ExpireSeconds => Math.Max(1, _settings.TrackerExpireSeconds);

        /// <summary>位置匹配阈值兜底（mm），Settings.DedupPositionThreshold 无效时使用。</summary>
        private const double DefaultPositionThreshold = 3.0;

        /// <summary>编码器差分匹配的最小容差（counts）。</summary>
        private const long MinEncoderTolerance = 20;

        /// <summary>多假设匹配的最大帧间隔（k 上限，语义同 ProductTracker.MaxEncoderFrameGap）。
        /// 上限不宜过大：产品间距恰为 k×Δ̂ 整数倍时存在误匹配风险。</summary>
        private const int MaxEncoderFrameGap = 4;

        /// <summary>
        /// 编码器匹配连续失败的重置阈值：连续失败达到该次数时判定"触发间隔已变"
        /// （换产线/编码器当量变化），重置 Δ̂ 让位置过渡重新自适应建立。
        /// 误重置无害——位置兜底期间功能正常，重置后几帧内即可重新收敛。
        /// </summary>
        private const int EncoderFailResetThreshold = 5;
        private int _encoderFailStreak;

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

                // 编码器失配（丢帧/触发间隔变化导致 fwd 超出多假设空间）时回退位置匹配：
                // 横向/世界坐标稳定是强同品证据——否则锁定中断、输出 -9999、整条不发 VGT。
                // （与 ProductTracker 的 TryMatchByEncoder 失败后 TryMatchByPosition 兜底同构。）
                var item = encoder != 0
                    ? FindMatchByEncoder(encoder, worldX, worldY, threshold, now)
                      ?? FindMatchByPosition(worldX, worldY, threshold)
                    : FindMatchByPosition(worldX, worldY, threshold);

                if (item is null)
                {
                    // 编码器有效但差分匹配失败：计数并在连续失败达到阈值时重置 Δ̂——
                    // 否则换产线/编码器当量变化后，Δ̂ 卡死旧值，编码器匹配永久退化（只剩位置兜底）。
                    if (encoder != 0)
                    {
                        _encoderFailStreak++;
                        if (_encoderFailStreak >= EncoderFailResetThreshold && _deltaEstimator.HasValue)
                        {
                            LogService.Instance.Info(
                                $"[AngleTracker] 编码器匹配连续失败 {_encoderFailStreak} 次，重置 Δ̂ 重新自适应（触发间隔可能已变化）");
                            _deltaEstimator = null;
                            _encoderFailStreak = 0;
                        }
                    }

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

                _encoderFailStreak = 0;
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
        /// 编码器差分匹配：Δ̂ 已建立时按多假设（k×Δ̂，k=1 正常，k≥2 丢帧）预测匹配；
        /// 未建立时用位置匹配过渡并建立 Δ̂。
        /// <para>★ 背景：硬触发间隔在 200mm 附近波动（产线无法严格等距），且中间可能丢帧/跳帧
        /// → 实际帧间编码器差为 k×Δ̂ 的形态。旧的单一预测（k=1）会把丢帧帧（fwd=2Δ̂）用容差拒绝，
        /// 同一产品的角度锁定中断，后续帧输出未知哨兵 -9999、整条不发 VGT。</para>
        /// <para>★ Δ̂ 按命中间隔归一化（语义 = 单帧位移）：直接拿 fwd 平滑会把 Δ̂ 拉向 2Δ̂，
        /// 随后正常帧全部失配。</para>
        /// </summary>
        private TrackedProduct? FindMatchByEncoder(uint encoder, double worldX, double worldY, double threshold, DateTime now)
        {
            // Δ̂ 未建立：用位置匹配过渡（启动阶段），成功后用本次观测差建立 Δ̂。
            // 注：过渡期若恰逢丢帧，Δ̂ 初值会偏大；后续正常帧由多假设 k=1 失配后
            // 回退位置匹配兜底——去重/锁定仍有效，Δ̂ 待后续观测差自行修正。
            if (!_deltaEstimator.HasValue)
            {
                var posItem = FindMatchByPosition(worldX, worldY, threshold);
                if (posItem is not null)
                {
                    _deltaEstimator = (uint)(encoder - posItem.Encoder);
                    return posItem;
                }

                // 注：不做"唯一项编码器差建立 Δ̂"的兜底——同帧可能存在多个不同产品，
                // 唯一跟踪项未必与观测对应（单测 SameProduct 第 1 帧 Expected 2/Actual 1 即此场景）。
                return null;
            }

            int bestIdx = -1;
            long bestErr = long.MaxValue;
            int bestGap = 1;

            for (int i = 0; i < _items.Count; i++)
            {
                // 无符号前进距离（模 2^32，天然处理编码器回绕）
                long fwd = (uint)(encoder - _items[i].Encoder);
                if (fwd <= 0 || fwd > int.MaxValue)
                {
                    continue; // 编码器倒退或异常跳变
                }

                for (int gap = 1; gap <= MaxEncoderFrameGap; gap++)
                {
                    long err = Math.Abs(fwd - (long)Math.Round(_deltaEstimator.Value * gap));
                    if (err < bestErr)
                    {
                        bestErr = err;
                        bestIdx = i;
                        bestGap = gap;
                    }
                }
            }

            var tol = Math.Max(MinEncoderTolerance, (long)Math.Round(_deltaEstimator.Value * 0.5));
            if (bestIdx < 0 || bestErr > tol)
            {
                return null;
            }

            long obsPerFrame = (long)Math.Round((double)(uint)(encoder - _items[bestIdx].Encoder) / bestGap);
            _deltaEstimator = _deltaEstimator.Value * 0.7 + obsPerFrame * 0.3;
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
