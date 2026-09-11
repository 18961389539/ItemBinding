using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace HikScanner
{
    /// <summary>
    /// 扫码枪参数优化器，使用模拟退火（Simulated Annealing）算法在曝光时间和增益值的连续空间中搜索
    /// 一个使扫码置信度分数最大的参数组合。
    /// </summary>
    /// <remarks>
    /// <para>算法目标：最大化扫码枪返回的原始置信度分数（score）。亮度（brightness）用于检测过曝/欠曝
    /// 并指导搜索方向，但不会直接替换作为比较的分数。</para>
    /// <para>设计思想：</para>
    /// <list type="bullet">
    /// <item>在参数空间中使用对数空间（曝光）和线性空间（增益）采样以保证对曝光尺度的平滑探索。</item>
    /// <item>使用动态步长和温度调度在粗搜索和精细搜索之间平衡：高温阶段倾向于探索，低温阶段倾向于利用。</item>
    /// <item>当图像明显过曝或欠曝时，算法对候选解施加偏置（bias）以更快地移动到有效亮度范围。</item>
    /// <item>使用评估缓存避免重复调用外部评估函数（通常为硬件接口或图像处理流程），提高效率。</item>
    /// </list>
    /// <para>注：所有数值阈值和权重均为经验设置，可通过配置记录进行调整以适配不同型号的扫码枪或拍摄环境。</para>
    /// </remarks>
    public class ScannerOptimizer
    {
        /// <summary>
        /// 评估函数委托，用于获取指定参数下的扫码分数和图像亮度。
        /// 调用此委托通常会触发摄像/抓图与条码识别流程，开销可能较大。
        /// </summary>
        /// <param name="exposure">曝光时间（单位与设备相关）。增大曝光通常会提高图像亮度，低光场景下有利于识别；
        /// 但过大曝光会导致高光饱和（过曝）与运动模糊，反而降低识别分数。</param>
        /// <param name="gain">增益值（放大信号）。增大增益会提高暗部可见性，但同时放大噪声，过高增益可能导致识别失败。</param>
        /// <returns>返回一个二元组：第一个元素为条码置信度分数（score），第二个元素为图像平均亮度（averageBrightness）。
        /// 注意：优化器以score作为比较基准，brightness仅用于检测过曝/欠曝并调整搜索方向。</returns>
        public delegate (float score, float averageBrightness) ScoreEvaluator(float exposure, float gain);

        /// <summary>
        /// 优化结果记录，包含找到的最好曝光/增益组合以及对应的最高分数。
        /// </summary>
        /// <param name="BestExposure">最优曝光时间。该值越大通常意味图像越亮（在未过曝的前提下），但也可能带来运动模糊或饱和风险。</param>
        /// <param name="BestGain">最优增益值。该值越大可提升弱光下的可见度，但也增加噪声。</param>
        /// <param name="BestScore">对应的最高置信度分数（由评估函数返回）。</param>
        public record OptimizationResult(float BestExposure, float BestGain, float BestScore);

        /// <summary>
        /// 曝光惩罚配置，用于控制在出现过曝或欠曝时对搜索方向施加的偏置。
        /// 这些参数不会直接改变评分逻辑，而是影响候选解生成时的偏置(bias)。
        /// </summary>
        /// <param name="IdealBrightness">理想亮度值（用于初始曝光网格搜索的参考）。
        /// 增大该值会让初始曝光搜索倾向更亮的设置；减小则倾向更暗的初始曝光。</param>
        /// <param name="OverexposureThreshold">过曝判定阈值（brightness > 阈值视为过曝）。
        /// 降低阈值会更早认为图像过曝，从而更频繁触发向降低曝光/增益的偏置；提高阈值则容忍更亮图像。</param>
        /// <param name="UnderexposureThreshold">欠曝判定阈值（brightness &lt; 阈值视为欠曝）。
        /// 提高该值会更早判定为欠曝并偏向增加曝光/增益；降低则更容忍暗图像。</param>
        /// <param name="OverexposurePenaltyWeight">过曝时偏置的权重（0-1）。
        /// 增大该权重会使算法在检测到过曝时更激进地降低曝光/增益；减小则反应更温和。</param>
        /// <param name="UnderexposurePenaltyWeight">欠曝时偏置的权重（0-1）。
        /// 增大该权重会使算法在检测到欠曝时更积极地增大曝光/增益；减小则更保守。</param>
        public record ExposurePenaltyConfig(
            float IdealBrightness = 100f,
            float OverexposureThreshold = 210f,
            float UnderexposureThreshold = 30f,
            float OverexposurePenaltyWeight = 0.8f,
            float UnderexposurePenaltyWeight = 0.6f);

        /// <summary>
        /// 模拟退火算法配置。通过调节下面的参数可以控制探索/利用的平衡、收敛速度和早退条件。
        /// </summary>
        /// <param name="InitialTemperature">初始温度。增大该值会让算法在早期更容易接受较差解，增强全局探索能力；
        /// 过大可能导致收敛缓慢。减小该值则更快进入局部搜索，风险是陷入局部最优。</param>
        /// <param name="MinTemperature">最低温度（终止条件）。增大该值会提前停止收敛，可能较早结束搜索；减小会更彻底地搜索但耗时更久。</param>
        /// <param name="CoolingRate">冷却速率，取值在 (0,1)。越接近1降温越慢，搜索更充分但耗时更长；越小则快速降温，提前收敛。</param>
        /// <param name="IterationsPerTemp">每个温度下的内部迭代次数。增大此值可以在同一温度下尝试更多候选，提升稳定性但增加计算量。</param>
        /// <param name="TargetScore">目标分数阈值，达到后可以提前退出。设置为null禁用该早退策略。</param>
        /// <param name="MaxNoImprovementRounds">在多少个温度轮内无全局改进时提前退出（0表示禁用）。减小会更快结束，增大则更耐心地继续搜索。</param>
        /// <param name="PreferBrighterOnTie">当分数相同时是否偏好更亮的图像（true：更亮，false：更暗）。该选项用于同分tie-break。</param>
        public record SimulatedAnnealingConfig(
            float InitialTemperature = 10.0f,
            float MinTemperature = 0.001f,
            float CoolingRate = 0.69f,
            int IterationsPerTemp = 8,
            float? TargetScore = 90f,
            int MaxNoImprovementRounds = 10,
            bool PreferBrighterOnTie = true);

        /// <summary>
        /// 粗粒度网格采样找到合适的初始曝光值
        /// </summary>
        private static float FindInitialExposure(
            ScoreEvaluator evaluator,
            float minExposure, float maxExposure,
            float gain, float targetBrightness)
        {
            // 粗粒度网格采样
            int gridCount = 8;
            float bestExp = minExposure, bestDiff = float.MaxValue;
            var cache = new Dictionary<(float, float), (float, float)>();
            for (int i = 0; i < gridCount; i++)
            {
                float exp = minExposure + (maxExposure - minExposure) * i / (gridCount - 1);
                (float score, float brightness) result;
                if (!cache.TryGetValue((exp, gain), out result))
                {
                    result = evaluator(exp, gain);
                    cache[(exp, gain)] = result;
                }
                float diff = MathF.Abs(result.brightness - targetBrightness);
                if (diff < bestDiff) { bestDiff = diff; bestExp = exp; }
            }
            return bestExp;
        }

        /// <summary>
        /// 使用模拟退火算法寻找最优扫码参数
        /// </summary>
        /// <remarks>
        /// <para>算法特点：</para>
        /// <para>1. 使用扫码枪返回的原始置信度分数作为优化目标</para>
        /// <para>2. 曝光惩罚机制仅用于调整搜索方向，不影响分数比较</para>
        /// <para>3. 过曝时自动偏向降低曝光和增益</para>
        /// <para>4. 欠曝时自动偏向增加曝光和增益</para>
        /// <para>5. 连续过曝或长期无改进时重置到最优解附近</para>
        /// </remarks>
        /// <param name="evaluator">评估函数，返回 (分数, 亮度)</param>
        /// <param name="minExposure">最小曝光时间</param>
        /// <param name="maxExposure">最大曝光时间</param>
        /// <param name="minGain">最小增益</param>
        /// <param name="maxGain">最大增益</param>
        /// <param name="penaltyConfig">曝光惩罚配置</param>
        /// <param name="saConfig">模拟退火配置</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>包含最优参数和最高分数的优化结果</returns>
#nullable enable
        public static OptimizationResult RunSimulatedAnnealing(
            ScoreEvaluator evaluator,
            float minExposure = 100, float maxExposure = 5000,
            float minGain = 0, float maxGain = 20,
            ExposurePenaltyConfig? penaltyConfig = null,
            SimulatedAnnealingConfig? saConfig = null,
            CancellationToken cancellationToken = default)
#nullable disable
        {
            penaltyConfig ??= new();
            saConfig ??= new();

            float T = saConfig.InitialTemperature;
            // 使用对数空间处理曝光可以使在大范围曝光值下的步长变化更加平滑，
            // 避免直接在线性空间中增减曝光造成在大曝光值范围上步长过大或过小的问题。
            float logMaxExp = MathF.Log(maxExposure + 1);
            float logMinExp = MathF.Log(minExposure + 1);
            float logExpRange = logMaxExp - logMinExp;
            // 增益在多数设备上是线性的，因此按线性区间处理
            float gainRange = maxGain - minGain;

            // 评估缓存
            var evalCache = new Dictionary<(float, float), (float, float)>();

            float currentGain = minGain;
            float currentExp = FindInitialExposure(
                (exp, gain) =>
                {
                    if (!evalCache.TryGetValue((exp, gain), out var result))
                    {
                        result = evaluator(exp, gain);
                        evalCache[(exp, gain)] = result;
                    }
                    return result;
                },
                minExposure, maxExposure, currentGain, penaltyConfig.IdealBrightness);

            (float currentScore, float brightness) = evalCache.TryGetValue((currentExp, currentGain), out var v)
                ? v : (evalCache[(currentExp, currentGain)] = evaluator(currentExp, currentGain));

            var best = new OptimizationResult(currentExp, currentGain, currentScore);
            float bestBrightness = brightness;
            int noImprovementCount = 0, overexposureCount = 0;
            int noImprovementRounds = 0;
            float lastBestScore = best.BestScore;

            float lastBestScoreForStep = best.BestScore;
            int stepAdjustWindow = 5;
            // 初始步长：曝光步长按对数范围的百分比，增益步长按线性范围的百分比。
            // 增大这些步长会让每次候选变化更大（更激进的探索），但可能错过局部精细调整；
            // 减小步长会使搜索更平滑、更保守，但可能需要更多迭代找到全局最优。
            float logExpStep = logExpRange * 0.02f;
            float gainStep = gainRange * 0.03f;

            while (T > saConfig.MinTemperature)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float tempFactor = MathF.Sqrt(T / saConfig.InitialTemperature);
                // 动态步长调整策略：
                // 1. 连续stepAdjustWindow轮（默认5轮）无全局改进 -> 增大步长20%，扩大搜索范围
                // 2. 本轮温度循环中发现新的全局最优解 -> 减小步长15%，精细搜索当前方向
                // 3. 步长限制：最小为初始步长的10%，最大为初始步长的500%，防止步长过小或过大

                // 记录初始步长以便对步长施加上下限
                float initialLogExpStep = logExpRange * 0.02f;
                float initialGainStep = gainRange * 0.03f;
                float minLogExpStep = initialLogExpStep * 0.1f;
                float maxLogExpStep = initialLogExpStep * 5.0f;
                float minGainStep = initialGainStep * 0.1f;
                float maxGainStep = initialGainStep * 5.0f;

                // 当多轮无改进时增大步长以尝试跳出当前搜索区域；当找到新全局最优时减小步长以精细搜索。
                if (noImprovementRounds >= stepAdjustWindow)
                {
                    // 长期无改进，增大步长以探索新区域
                    logExpStep *= 1.2f;
                    gainStep *= 1.2f;
                }
                else if (best.BestScore > lastBestScoreForStep)
                {
                    // 发现新的全局最优解，减小步长进行精细搜索
                    logExpStep *= 0.85f;
                    gainStep *= 0.85f;
                    lastBestScoreForStep = best.BestScore;
                }

                // 应用步长限制
                logExpStep = Math.Clamp(logExpStep, minLogExpStep, maxLogExpStep);
                gainStep = Math.Clamp(gainStep, minGainStep, maxGainStep);

                float logExpStepT = logExpStep * tempFactor;
                float gainStepT = gainStep * tempFactor;

                for (int i = 0; i < saConfig.IterationsPerTemp; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // 计算调整偏置
                    (float expBias, float gainBias) bias;
                    if (brightness >= 254.9f)
                        bias = (-2.0f, -1.5f);
                    else if (brightness > penaltyConfig.OverexposureThreshold)
                    {
                        float intensity = MathF.Min((brightness - penaltyConfig.OverexposureThreshold) / 5f, 1f);
                        bias = (-1.8f * intensity * penaltyConfig.OverexposurePenaltyWeight, -1.2f * intensity * penaltyConfig.OverexposurePenaltyWeight);
                    }
                    else if (brightness < penaltyConfig.UnderexposureThreshold)
                    {
                        float intensity = MathF.Min((penaltyConfig.UnderexposureThreshold - brightness) / 10f, 1f);
                        bias = (1.5f * intensity * penaltyConfig.UnderexposurePenaltyWeight, 1.0f * intensity * penaltyConfig.UnderexposurePenaltyWeight);
                    }
                    else
                        bias = (0f, 0f);

                    if (brightness >= 254.9f && ++overexposureCount > 3)
                    {
                        (currentExp, currentGain) = ResetToBestNeighbor(best, minExposure, maxExposure, minGain, maxGain, 0.8f, 0.4f);
                        overexposureCount = 0;
                        continue;
                    }
                    else if (brightness < 254.9f) overexposureCount = 0;

                    // 生成新解：曝光在对数空间上随机扰动，增益在原始线性空间上扰动。
                    // bias（偏置）控制在检测到过曝/欠曝时优先朝向降低或增加曝光/增益的方向移动。
                    // 更大的bias会更强烈地斜向移动搜索方向。
                    float logCurrentExp = MathF.Log(currentExp + 1);
                    float nextExp = Math.Clamp(MathF.Exp(logCurrentExp + (Random.Shared.NextSingle() * 2 - 1 + bias.expBias) * logExpStepT) - 1, minExposure, maxExposure);
                    float nextGain = Math.Clamp(currentGain + (Random.Shared.NextSingle() * 2 - 1 + bias.gainBias) * gainStepT, minGain, maxGain);

                    (float nextScore, float nextBrightness) = evalCache.TryGetValue((nextExp, nextGain), out var nv)
                        ? nv : (evalCache[(nextExp, nextGain)] = evaluator(nextExp, nextGain));

                    // 全局最优更新：无论是否接受该解，都记录历史最佳分数（同分时按配置选择亮/暗）
                    bool tiePreferBrighter = saConfig.PreferBrighterOnTie;
                    bool isBetterOnTie = tiePreferBrighter
                        ? nextBrightness > bestBrightness
                        : nextBrightness < bestBrightness;

                    if (nextScore > best.BestScore ||
                        (nextScore == best.BestScore && isBetterOnTie))
                    {
                        best = new(nextExp, nextGain, nextScore);
                        bestBrightness = nextBrightness;
                    }

                    // Metropolis准则（使用扫码枪原始分数）
                    // 接受条件优先级：
                    // 1. 当前图像接近饱和（亮度≥254.9）且新解更暗，同时新解分数不低于当前分数的一半 -> 避免过曝
                    // 2. 当前图像过暗（亮度＜欠曝阈值）且新解更亮，同时新解分数不低于当前分数的一半 -> 避免欠曝
                    // 3. 新解分数更高 -> 直接接受改进
                    // 4. 按概率接受较差解（模拟退火核心）
                    bool accept = (brightness >= 254.9f && nextBrightness < brightness && nextScore >= currentScore * 0.5f) ||
                                  (brightness < penaltyConfig.UnderexposureThreshold && nextBrightness > brightness && nextScore >= currentScore * 0.5f) ||
                                  nextScore > currentScore ||
                                  Random.Shared.NextDouble() < Math.Exp((nextScore - currentScore) / T);

                    if (accept)
                    {
                        (currentExp, currentGain, currentScore, brightness) = (nextExp, nextGain, nextScore, nextBrightness);
                        noImprovementCount = 0;
                    }
                    else if (++noImprovementCount > 20)
                    {
                        (currentExp, currentGain) = ResetToBestNeighbor(best, minExposure, maxExposure, minGain, maxGain, 0.9f, 0.2f);
                        (currentScore, brightness) = evalCache.TryGetValue((currentExp, currentGain), out var rv)
                            ? rv : (evalCache[(currentExp, currentGain)] = evaluator(currentExp, currentGain));
                        noImprovementCount = 0;
                    }
                }
                T *= saConfig.CoolingRate;

                // 早退机制：检查本轮是否有改进
                if (best.BestScore > lastBestScore)
                {
                    lastBestScore = best.BestScore;
                    noImprovementRounds = 0;
                }
                else
                {
                    noImprovementRounds++;
                }

                // 早退：达到目标分数
                if (saConfig.TargetScore.HasValue && best.BestScore >= saConfig.TargetScore.Value)
                {
                    Debug.WriteLine("达到分数退出");
                    break;
                }

                // 早退：连续多轮无改进
                if (saConfig.MaxNoImprovementRounds > 0 && noImprovementRounds >= saConfig.MaxNoImprovementRounds && lastBestScore > 0.1)
                {
                    Debug.WriteLine("多轮没有改进退出");
                    break;
                }

            }
            Debug.WriteLine("温度达标退出");
            return best;
        }

        /// <summary>
        /// 重置到最优解的邻域，用于跳出局部最优或过曝区域
        /// </summary>
        /// <param name="best">当前最优解</param>
        /// <param name="minExp">最小曝光</param>
        /// <param name="maxExp">最大曝光</param>
        /// <param name="minGain">最小增益</param>
        /// <param name="maxGain">最大增益</param>
        /// <param name="baseRatio">基础比例系数</param>
        /// <param name="randomRange">随机范围</param>
        /// <returns>新的曝光和增益值</returns>
        private static (float exp, float gain) ResetToBestNeighbor(
            OptimizationResult best, float minExp, float maxExp, float minGain, float maxGain,
            float baseRatio, float randomRange)
        {
            float exp = Math.Clamp(best.BestExposure * (baseRatio + Random.Shared.NextSingle() * randomRange), minExp, maxExp);
            float gain = Math.Clamp(best.BestGain * (baseRatio + Random.Shared.NextSingle() * randomRange), minGain, maxGain);
            return (exp, gain);
        }
    }
}
