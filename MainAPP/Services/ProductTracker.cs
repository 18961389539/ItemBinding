using MainAPP.Models;
using System;
using System.Collections.Generic;

namespace MainAPP.Services
{
    /// <summary>
    /// 输送线场景下的产品去重跟踪器。
    /// <para>编码器模式（DedupByEncoder=true）：按编码器值的帧间差分预测匹配同一产品，
    /// 不受视觉质心抖动/角度 180° 歧义影响。启动阶段（Δ̂ 未建立）用位置匹配过渡。</para>
    /// <para>位置模式（默认）：有条码产品基于条码匹配去重；无条码产品基于位置+角度匹配去重。</para>
    /// <para>过期清理：超过时间窗口未被再次匹配的产品从跟踪列表移除。</para>
    /// </summary>
    public class ProductTracker
    {
        private readonly IAlgorithmSettings _settings;

        public ProductTracker(IAlgorithmSettings? settings = null)
        {
            _settings = settings ?? Settings.Instance;
        }

        private class TrackedItem
        {
            public string Barcode { get; set; } = string.Empty;
            public double MatchCoord { get; set; }
            public double Angle { get; set; }
            public DateTime LastSeen { get; set; }
            /// <summary>最近一次匹配的编码器计数值（编码器模式使用）。</summary>
            public uint Encoder { get; set; }
        }

        private readonly List<TrackedItem> _items = new();
        private readonly object _lock = new();

        /// <summary>
        /// 已跟踪产品的过期时间（秒），超过此时间未被再次匹配的产品从跟踪列表移除。
        /// 值来自设置（TrackerExpireSeconds，默认 60）——必须大于相邻两次触发的最大时间间隔，
        /// 否则慢速产线会重复计数（判据详见 AlgorithmSettings.TrackerExpireSeconds）。
        /// </summary>
        private int ExpireSeconds => Math.Max(1, _settings.TrackerExpireSeconds);

        /// <summary>编码器差分匹配的最小容差（counts），Δ̂ 自适应容差的下限。</summary>
        private const long MinEncoderTolerance = 20;

        /// <summary>
        /// 多假设匹配的最大帧间隔（k 上限）。k=1 为相邻触发帧，k≥2 表示中间有丢帧/跳帧。
        /// 上限不宜过大：产品间距恰为 k×Δ̂ 整数倍时存在误匹配风险（与既有 k=1 方案同类风险），
        /// 取 4 已覆盖视野内连续丢 3 帧的极端情况。
        /// </summary>
        private const int MaxEncoderFrameGap = 4;

        /// <summary>编码器匹配连续失败的重置阈值（语义同 AngleTracker）。</summary>
        private const int EncoderFailResetThreshold = 5;
        private int _encoderFailStreak;

        /// <summary>
        /// 帧间编码器位移的平滑估计（Δ̂，counts/帧）。null 表示尚未建立。
        /// </summary>
        private double? _deltaEstimator;

        /// <summary>
        /// 过滤已跟踪的产品，返回新增产品数量。
        /// </summary>
        /// <param name="models">本帧检测结果</param>
        /// <returns>新增（未匹配到已跟踪项）的产品数量</returns>
        public int FilterNewAndCount(IReadOnlyList<DbModel> models)
        {
            if (!_settings.DedupEnabled)
            {
                return models.Count;
            }

            var useEncoder = _settings.DedupByEncoder;
            var trackByX = string.Equals(_settings.DedupTrackAxis, "X", StringComparison.OrdinalIgnoreCase);
            var posThreshold = _settings.DedupPositionThreshold;
            var angleThreshold = _settings.DedupAngleThreshold;
            // P0-1: 有条码产品也需要位置校验阈值（使用 noread 阈值的3倍，
            // 因为跨帧位置变化可能更大，但仍需区分"同一产品移动"与"不同产品同条码"）
            var barcodePosThreshold = posThreshold * 3;
            var now = DateTime.Now;
            int newCount = 0;
            // P0-1: 记录本批次已匹配的 _items 索引，防止同一帧内同一条码被多个产品重复匹配
            var matchedInBatch = new HashSet<int>();

            lock (_lock)
            {
                // 清理过期项
                _items.RemoveAll(item => (now - item.LastSeen).TotalSeconds > ExpireSeconds);

                foreach (var model in models)
                {
                    bool matched = false;

                    if (useEncoder && model.Encode != 0)
                    {
                        // 编码器差分匹配（Δ̂ 未建立时先用位置匹配过渡并建立 Δ̂）
                        matched = TryMatchByEncoder(model, now, matchedInBatch,
                            posThreshold, angleThreshold, trackByX, barcodePosThreshold);
                        if (matched)
                        {
                            _encoderFailStreak = 0;
                        }
                        else
                        {
                            // 连续失败达到阈值：触发间隔可能已变（换产线/编码器当量变化），
                            // 重置 Δ̂ 让位置过渡重新自适应建立，避免编码器匹配永久退化
                            _encoderFailStreak++;
                            if (_encoderFailStreak >= EncoderFailResetThreshold && _deltaEstimator.HasValue)
                            {
                                LogService.Instance.Info(
                                    $"[ProductTracker] 编码器匹配连续失败 {_encoderFailStreak} 次，重置 Δ̂ 重新自适应（触发间隔可能已变化）");
                                _deltaEstimator = null;
                                _encoderFailStreak = 0;
                            }
                        }
                    }

                    if (!matched)
                    {
                        // 位置/条码匹配（编码器无效、未启用编码器、或编码器差分未命中）
                        matched = TryMatchByPosition(model, now, matchedInBatch,
                            trackByX, posThreshold, angleThreshold, barcodePosThreshold);
                    }

                    if (!matched)
                    {
                        _items.Add(new TrackedItem
                        {
                            Barcode = string.IsNullOrWhiteSpace(model.Barcode) || string.Equals(model.Barcode, "noread", StringComparison.OrdinalIgnoreCase)
                                ? "noread"
                                : model.Barcode,
                            MatchCoord = trackByX ? model.WorldX : model.WorldY,
                            Angle = model.Angle,
                            Encoder = model.Encode,
                            LastSeen = now
                        });
                        newCount++;
                        // 诊断日志：帮助排查识别数量与实际条码数不一致的问题
                        // 2026-09-05: DbModel.Angle 已统一 (-180,180] 域（AngleTracker 归一化），
                        // 未知哨兵 -9999 原样打印，无需再换算
                        LogService.Instance.Info(
                            $"[ProductTracker] 新增计数: Barcode={model.Barcode}, " +
                            $"Pos=({model.WorldX:F1},{model.WorldY:F1}), Angle={model.Angle:F1}, " +
                            $"Enc={model.Encode}, 已跟踪={_items.Count}, 本帧新增={newCount}");
                    }
                }
            }

            return newCount;
        }

        /// <summary>
        /// 编码器差分匹配：候选项的预测位置 = item.Encoder + Δ̂，与当前编码器的偏差在容差内即视为同一产品。
        /// Δ̂ 未建立时回退位置匹配，并用首次成功匹配的观测差建立 Δ̂。
        /// </summary>
        private bool TryMatchByEncoder(DbModel model, DateTime now, HashSet<int> matchedInBatch,
            double posThreshold, double angleThreshold, bool trackByX, double barcodePosThreshold)
        {
            uint enc = model.Encode;

            // Δ̂ 未建立：用位置匹配过渡（启动阶段），成功后用本次观测差建立 Δ̂
            if (!_deltaEstimator.HasValue)
            {
                // 编码器模式过渡必须忽略角度：角度可能为未知哨兵 -9999，且矩形角有 180° 歧义
                var matchedIdx = FindPositionMatch(model, now, matchedInBatch,
                    trackByX, posThreshold, angleThreshold, barcodePosThreshold, ignoreAngle: true);
                if (matchedIdx < 0)
                {
                    // 注：不做"唯一项编码器差建立 Δ̂"的兜底——同帧可能存在多个不同产品，
                    // 唯一跟踪项未必与观测对应，贸然建立会误匹配（单测 SameProduct 第 1 帧
                    // Expected 2/Actual 1 即此场景）。Δ̂ 建立以位置过渡成功为准。
                    return false;
                }

                var item = _items[matchedIdx];
                _deltaEstimator = (uint)(enc - item.Encoder);
                item.Encoder = enc;
                item.LastSeen = now;
                item.MatchCoord = trackByX ? model.WorldX : model.WorldY;
                matchedInBatch.Add(matchedIdx);
                return true;
            }

            // 差分预测匹配：选偏差最小的候选
            int bestIdx = -1;
            long bestErr = long.MaxValue;
            int bestGap = 1;

            for (int i = 0; i < _items.Count; i++)
            {
                if (matchedInBatch.Contains(i))
                {
                    continue;
                }

                // 无符号前进距离（模 2^32，天然处理编码器回绕）
                long fwd = (uint)(enc - _items[i].Encoder);
                if (fwd <= 0 || fwd > int.MaxValue)
                {
                    continue; // 编码器倒退或异常跳变
                }

                // 多假设匹配：硬触发间隔在 200mm 附近波动（产线无法严格等距），
                // 且中间可能丢帧/跳帧 → 帧间隔假设为 k×Δ̂（k=1 正常，k≥2 丢帧）。
                // 取误差最小的 (候选, k) 组合，而不是只按 k=1 预测——
                // 否则丢帧时 fwd=2Δ̂ 会被容差拒绝，同一产品被误判为新品重复计数。
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

            // 容差覆盖触发波动（200mm 附近的 Δ 起伏），多假设已覆盖丢帧倍数
            var tol = Math.Max(MinEncoderTolerance, (long)Math.Round(_deltaEstimator.Value * 0.5));
            if (bestIdx < 0 || bestErr > tol)
            {
                return false;
            }

            var matched = _items[bestIdx];
            // ★ Δ̂ 按命中间隔归一化：Δ̂ 的语义是"单帧位移"，
            //   丢帧时 fwd/bestGap 才是单帧观测——直接拿 fwd 平滑会把 Δ̂ 拉向 2Δ̂，
            //   随后所有正常帧全部失配（形成隔帧匹配怪圈）。
            long obsPerFrame = (long)Math.Round((double)(uint)(enc - matched.Encoder) / bestGap);
            _deltaEstimator = _deltaEstimator.Value * 0.7 + obsPerFrame * 0.3;
            matched.Encoder = enc;
            matched.LastSeen = now;
            matched.MatchCoord = trackByX ? model.WorldX : model.WorldY;
            matchedInBatch.Add(bestIdx);
            return true;
        }

        /// <summary>
        /// 位置/条码匹配（原逻辑）：有条码用条码+位置，无条码用位置+角度。
        /// </summary>
        private bool TryMatchByPosition(DbModel model, DateTime now, HashSet<int> matchedInBatch,
            bool trackByX, double posThreshold, double angleThreshold, double barcodePosThreshold)
        {
            var hasBarcode = !string.IsNullOrWhiteSpace(model.Barcode)
                && !string.Equals(model.Barcode, "noread", StringComparison.OrdinalIgnoreCase);
            var coord = trackByX ? model.WorldX : model.WorldY;

            for (int i = 0; i < _items.Count; i++)
            {
                if (matchedInBatch.Contains(i))
                {
                    continue;
                }

                if (hasBarcode)
                {
                    // 有条码：条码字符串相同 + 位置在合理范围内
                    if (!string.Equals(_items[i].Barcode, model.Barcode, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var posDiff = Math.Abs(_items[i].MatchCoord - coord);
                    if (posDiff > barcodePosThreshold)
                    {
                        continue;
                    }

                    _items[i].LastSeen = now;
                    _items[i].MatchCoord = coord;
                    _items[i].Encoder = model.Encode;
                    matchedInBatch.Add(i);
                    return true;
                }

                // 无条码：位置+角度匹配；任一角度为未知哨兵（-9999，域 (-180,180] 之外）时放宽为纯位置匹配
                if (_items[i].Barcode != "noread")
                {
                    continue;
                }

                var posDiff2 = Math.Abs(_items[i].MatchCoord - coord);
                // 角度未知（如 -9999）时不做角度约束，避免无特征产品永远匹配不上。
                // 2026-09-05: 规范域改为 (-180,180] 后负角度合法（如 -32°），故仅 < -180 判为未知哨兵
                var angleOk = _items[i].Angle < -180 || model.Angle < -180
                    || AngleDifference(_items[i].Angle, model.Angle) < angleThreshold;
                if (posDiff2 < posThreshold && angleOk)
                {
                    _items[i].LastSeen = now;
                    _items[i].MatchCoord = coord;
                    _items[i].Angle = model.Angle;
                    _items[i].Encoder = model.Encode;
                    matchedInBatch.Add(i);
                    return true;
                }
            }

            return false;
        }

        private int FindPositionMatch(DbModel model, DateTime now, HashSet<int> matchedInBatch,
            bool trackByX, double posThreshold, double angleThreshold, double barcodePosThreshold,
            bool ignoreAngle = false)
        {
            var hasBarcode = !string.IsNullOrWhiteSpace(model.Barcode)
                && !string.Equals(model.Barcode, "noread", StringComparison.OrdinalIgnoreCase);
            var coord = trackByX ? model.WorldX : model.WorldY;

            for (int i = 0; i < _items.Count; i++)
            {
                if (matchedInBatch.Contains(i))
                {
                    continue;
                }

                if (hasBarcode)
                {
                    if (!string.Equals(_items[i].Barcode, model.Barcode, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (Math.Abs(_items[i].MatchCoord - coord) <= barcodePosThreshold)
                    {
                        return i;
                    }
                }
                else
                {
                    if (_items[i].Barcode != "noread")
                    {
                        continue;
                    }

                    // ignoreAngle=true 时只比位置（编码器模式过渡，角度可能为 -9999 或含 180° 歧义）
                    if (Math.Abs(_items[i].MatchCoord - coord) < posThreshold
                        && (ignoreAngle || AngleDifference(_items[i].Angle, model.Angle) < angleThreshold))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// 计算两个方向角之间的最小差值（度），考虑 180 度周期性（方向角周期）。
        /// 对 (-180,180] 域同样正确（如 175° 与 -175° → 10°）。
        /// 任一角度为未知哨兵（< -180，域外）时返回 180（视为不匹配）。
        /// </summary>
        private static double AngleDifference(double a1, double a2)
        {
            if (a1 < -180 || a2 < -180)
            {
                return 180;
            }

            var diff = Math.Abs(a1 - a2) % 180;
            return diff > 90 ? 180 - diff : diff;
        }

        /// <summary>
        /// 清空跟踪列表。
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
