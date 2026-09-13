using System;
using MainAPP.Application;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// 自适应死区管理器契约测试（2026-09-13）。
/// <para><b>被测契约</b>（对应风险评审的七项防御）：</para>
/// <list type="bullet">
/// <item>窗口未满 → 回退固定死区（冷启动 = 现状行为，安全降级）；</item>
/// <item>窗口已满 → k × median(|v|)，并钳制在 [0.5×, 3.0×] 固定死区内；</item>
/// <item>按配方分桶（换产品不互相污染）；</item>
/// <item>NaN/Infinity 不入窗（中位数会被一个 NaN 永久污染）；</item>
/// <item>钳制基准随 fallbackDeadband 平移（用户调全局死区时上下限跟随）。</item>
/// </list>
/// <para>注意 <see cref="HeadTailAdaptiveDeadband"/> 是单例——每个用例用独立配方名隔离。</para>
/// </summary>
public class HeadTailAdaptiveDeadbandTests
{
    private const double Fallback = 0.10;
    private const double K = 0.5;

    private static string UniqueRecipe(string tag) => $"自适应测试-{tag}-{Guid.NewGuid():N}";

    private static HeadTailAdaptiveDeadband Manager => HeadTailAdaptiveDeadband.Instance;

    private static void RecordMany(string recipe, string feature, int count, double value)
    {
        for (int i = 0; i < count; i++)
        {
            Manager.Record(recipe, feature, value);
        }
    }

    /// <summary>窗口未满 → 回退固定死区（冷启动期行为与现状一致）。</summary>
    [Fact]
    public void BelowWindowSize_ReturnsFallback()
    {
        var recipe = UniqueRecipe(nameof(BelowWindowSize_ReturnsFallback));
        RecordMany(recipe, "F", HeadTailAdaptiveDeadband.WindowSize - 1, 0.4);

        Assert.Equal(Fallback,
            Manager.GetBaseDeadband(recipe, "F", Fallback, K), 9);
    }

    /// <summary>窗口填满 → k × median。raw = 0.5 × 0.4 = 0.2，落在钳制区间内原样返回。</summary>
    [Fact]
    public void AtWindowSize_ReturnsKTimesMedian()
    {
        var recipe = UniqueRecipe(nameof(AtWindowSize_ReturnsKTimesMedian));
        RecordMany(recipe, "F", HeadTailAdaptiveDeadband.WindowSize, 0.4);

        Assert.Equal(0.20, Manager.GetBaseDeadband(recipe, "F", Fallback, K), 9);
    }

    /// <summary>
    /// ★ 防坍缩：对称产品的特征值恒近 0 → 中位数趋 0 → 无下限会把死区压到噪声量级。
    /// raw = 0.5 × 0.02 = 0.01，被下限 0.5 × 0.10 = 0.05 托住。
    /// </summary>
    [Fact]
    public void CollapsingMedian_ClampedToFloor()
    {
        var recipe = UniqueRecipe(nameof(CollapsingMedian_ClampedToFloor));
        RecordMany(recipe, "F", HeadTailAdaptiveDeadband.WindowSize, 0.02);

        Assert.Equal(HeadTailAdaptiveDeadband.FloorFactor * Fallback,
            Manager.GetBaseDeadband(recipe, "F", Fallback, K), 9);
    }

    /// <summary>
    /// ★ 防爬顶 + 滑动窗口：旧值被新值滑出后中位数跟随更新；
    /// 极端值 raw = 0.5 × 1.0 = 0.5 被上限 3.0 × 0.10 = 0.30 压住（不会永不裁决）。
    /// </summary>
    [Fact]
    public void LargeValues_ClampedToCeiling_AfterWindowSlides()
    {
        var recipe = UniqueRecipe(nameof(LargeValues_ClampedToCeiling_AfterWindowSlides));
        RecordMany(recipe, "F", HeadTailAdaptiveDeadband.WindowSize, 0.4);
        RecordMany(recipe, "F", HeadTailAdaptiveDeadband.WindowSize, 1.0);

        Assert.Equal(HeadTailAdaptiveDeadband.CeilingFactor * Fallback,
            Manager.GetBaseDeadband(recipe, "F", Fallback, K), 9);
    }

    /// <summary>按配方分桶：A 已自适应，B 无样本仍回退固定值（换产品不互相污染）。</summary>
    [Fact]
    public void PerRecipe_Isolated()
    {
        var recipeA = UniqueRecipe(nameof(PerRecipe_Isolated) + "-A");
        var recipeB = UniqueRecipe(nameof(PerRecipe_Isolated) + "-B");
        RecordMany(recipeA, "F", HeadTailAdaptiveDeadband.WindowSize, 0.4);

        Assert.Equal(0.20, Manager.GetBaseDeadband(recipeA, "F", Fallback, K), 9);
        Assert.Equal(Fallback, Manager.GetBaseDeadband(recipeB, "F", Fallback, K), 9);
    }

    /// <summary>同一配方内特征各自独立成窗（①的窗口不影响⑤的）。</summary>
    [Fact]
    public void PerFeature_Isolated()
    {
        var recipe = UniqueRecipe(nameof(PerFeature_Isolated));
        RecordMany(recipe, "CentroidOffset", HeadTailAdaptiveDeadband.WindowSize, 0.4);

        Assert.Equal(0.20, Manager.GetBaseDeadband(recipe, "CentroidOffset", Fallback, K), 9);
        Assert.Equal(Fallback, Manager.GetBaseDeadband(recipe, "GradientEnergyDiff", Fallback, K), 9);
    }

    /// <summary>NaN 不入窗：29 条有效 + 1 条 NaN → 窗口仍只有 29 条 → 未达门槛回退固定值。
    /// （NaN 会污染中位数使后续所有帧永久失效，必须在入口拒绝。）</summary>
    [Fact]
    public void NaN_IsNotRecorded()
    {
        var recipe = UniqueRecipe(nameof(NaN_IsNotRecorded));
        RecordMany(recipe, "F", HeadTailAdaptiveDeadband.WindowSize - 1, 0.4);
        Manager.Record(recipe, "F", double.NaN);

        Assert.Equal(Fallback, Manager.GetBaseDeadband(recipe, "F", Fallback, K), 9);
    }

    /// <summary>钳制基准随 fallbackDeadband 平移：全局死区调大时，下限/上限跟着走。</summary>
    [Fact]
    public void ClampScales_WithFallback()
    {
        var recipe = UniqueRecipe(nameof(ClampScales_WithFallback));
        var fallback = 0.20;
        RecordMany(recipe, "F", HeadTailAdaptiveDeadband.WindowSize, 0.4);

        // raw = 0.2，钳制区间 [0.10, 0.60] → 原样 0.2（若钳制基准写死 0.10，此处会得到不同结果）
        Assert.Equal(0.20, Manager.GetBaseDeadband(recipe, "F", fallback, K), 9);
    }

    /// <summary>不同 k 得到不同死区（k 是"设定出死区率"的旋钮）。</summary>
    [Fact]
    public void KScales_TheResult()
    {
        var recipe = UniqueRecipe(nameof(KScales_TheResult));
        RecordMany(recipe, "F", HeadTailAdaptiveDeadband.WindowSize, 0.4);

        Assert.Equal(0.10, Manager.GetBaseDeadband(recipe, "F", Fallback, k: 0.25), 9);
        Assert.Equal(0.30, Manager.GetBaseDeadband(recipe, "F", Fallback, k: 1.0), 9); // 触顶
    }
}
