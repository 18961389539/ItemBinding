using MainAPP.Services;

namespace MainAPP.Application;

/// <summary>
/// 头尾特征池的自适应死区（2026-09-13）：按配方维护每特征的 |v| 滑动窗口，
/// 实际死区 = <c>k × median(|v|)</c>，自动适配不同产品的不对称量级。
/// <para><b>为什么需要</b>：固定死区 0.10 对强不对称产品形同虚设（特征值天生 0.5），
/// 对弱不对称产品永不裁决（特征值天生 0.05）——这是"兼容更多产品"的最大障碍。
/// 中位数是稳健统计量，20~30 帧即可标定，<b>不需要真值</b>（纯分布信息，
/// 回答"这个产品的特征值通常有多大"而非"判得对不对"）。</para>
/// <para><b>★ 全帧口径（防自锁的关键）</b>：<see cref="Record"/> 记录<b>每一帧</b>的 |v|，
/// 无论该特征是否出死区。若只记出死区的帧，会形成闭环：死区高 → 只有大值出死区 →
/// 中位数更高 → 死区更高……死区单调爬顶、级联全部落死区而系统静默失效。
/// 统计池必须外生于判决结果（配套不变式：deadband 不得影响 SignedValue，
/// 由 <c>Deadband_DoesNotAffectSignedValues</c> 测试焊死）。</para>
/// <para><b>冷启动与换配方</b>：按配方分桶维护窗口（换产品不互相污染）；
/// 窗口未满 <see cref="WindowSize"/> 帧时回退到固定死区——即冷启动期的行为与现状
/// 完全一致，是<b>安全降级</b>而非不可预测。窗口不持久化：重启即重新冷启动（30 帧过渡）。</para>
/// <para><b>与让位机制的关系</b>：让位的置信度按固定 <c>ConfReference</c> 归一化
/// （不随自适应死区漂移），故让位阈值 1.5/3.0 跨产品仍然通用；本类只改变
/// "特征是否发言"，不改变"发言有多强"。</para>
/// </summary>
public sealed class HeadTailAdaptiveDeadband
{
    /// <summary>
    /// 滑动窗口容量，兼作生效门槛：窗口填满才开始自适应。
    /// 与审计的 <c>MinTruthSamples=30</b> 同源——30 是"中位数够稳"的最低样本量。
    /// </summary>
    public const int WindowSize = 30;

    /// <summary>死区下限 = 固定死区 × 0.5。防中位数坍缩：对称产品某特征恒近 0，
    /// 无下限会把死区压到噪声量级、让大量噪声发言。</summary>
    public const double FloorFactor = 0.5;

    /// <summary>死区上限 = 固定死区 × 3.0。防极端值把死区抬到永不裁决。</summary>
    public const double CeilingFactor = 3.0;

    public static HeadTailAdaptiveDeadband Instance { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<(string Recipe, string Feature), Queue<double>> _windows = new();
    private readonly HashSet<string> _adaptiveRecipes = new(StringComparer.Ordinal);

    private HeadTailAdaptiveDeadband()
    {
    }

    /// <summary>
    /// 取某配方某特征当前应使用的基础死区（不含特征自身的量纲缩放系数）。
    /// </summary>
    /// <param name="recipe">配方键（按配方分桶，换产品不互相污染）。</param>
    /// <param name="feature">特征名。</param>
    /// <param name="fallbackDeadband">固定死区（窗口未满时的回退值，也是钳制的基准）。</param>
    /// <param name="k">自适应系数：raw = k × median(|v|)。k 的真实语义是"设定出死区率"。</param>
    /// <returns>钳制后的基础死区，或窗口未满时的 <paramref name="fallbackDeadband"/>。</returns>
    public double GetBaseDeadband(string recipe, string feature, double fallbackDeadband, double k)
    {
        bool becameAdaptive = false;
        double result;
        lock (_gate)
        {
            if (_windows.TryGetValue((recipe, feature), out var window) && window.Count >= WindowSize)
            {
                var raw = k * Median(window);
                // 钳制基准取固定死区（而非绝对值）：若用户把全局死区调到 0.2，下限/上限应随之平移
                result = Math.Clamp(raw, FloorFactor * fallbackDeadband, CeilingFactor * fallbackDeadband);
                // 首个特征跨过门槛时提示一次，让"在线标定何时生效"可见
                becameAdaptive = _adaptiveRecipes.Add(recipe);
            }
            else
            {
                result = fallbackDeadband;
            }
        }

        if (becameAdaptive)
        {
            LogService.Instance.Info(
                $"[特征池] 配方「{recipe}」的自适应死区已生效（窗口 {WindowSize} 帧，k={k:0.00}）");
        }

        return result;
    }

    /// <summary>
    /// 记录一帧的 |v|。<b>必须全帧记录</b>（无论是否出死区）——出死区与否依赖当前死区，
    /// 若只记出死区的帧，窗口内容就依赖于判决结果，构成自锁闭环（见类文档）。
    /// </summary>
    public void Record(string recipe, string feature, double absValue)
    {
        if (double.IsNaN(absValue) || double.IsInfinity(absValue))
        {
            return; // 脏值不入窗（NaN 会污染中位数使其永久失效）
        }

        lock (_gate)
        {
            var key = (recipe, feature);
            if (!_windows.TryGetValue(key, out var window))
            {
                window = new Queue<double>(WindowSize);
                _windows[key] = window;
            }

            window.Enqueue(absValue);
            while (window.Count > WindowSize)
            {
                window.Dequeue();
            }
        }
    }

    /// <summary>中位数（偶数个取中间两数均值）。窗口仅 30 元素，排序开销可忽略。</summary>
    private static double Median(Queue<double> window)
    {
        var sorted = window.ToArray();
        Array.Sort(sorted);
        var n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
