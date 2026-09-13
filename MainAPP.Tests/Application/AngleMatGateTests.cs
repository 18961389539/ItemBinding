using MainAPP.Application;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// angleMat 创建 gate 的契约测试（2026-09-13）。
/// <para><b>背景</b>：该 gate 曾因只看 angleEnabled || grayDirectionEnabled 而漏掉特征池，
/// 造成「开特征池 + 关亮度判向」时 angleMat=null → 特征池每帧静默失效
/// （7 个特征全不算、头尾退化为掩码主轴角且无日志）。本组测试锁住三个消费者的契约：
/// <b>任一消费者开启就必须创建 Mat</b> —— 缺一即缺一个能力。</para>
/// <para>注意：这里测的是 gate 决策本身；HomeViewModel 必须经由 AngleMatGate 做判断
/// （勿内联展开），否则本测试仍绿但缺陷可能回归 —— 故 DetectionRecordService 侧另设
/// Warning 哨兵日志兜底。</para>
/// </summary>
public class AngleMatGateTests
{
    /// <summary>★ 缺陷场景：特征池开、其余全关 → 必须创建 Mat（此前返回 false 导致特征池静默失效）。</summary>
    [Fact]
    public void FeaturePoolOnly_RequiresMat()
    {
        Assert.True(AngleMatGate.ShouldCreate(
            angleEnabled: false, grayDirectionEnabled: false, featurePoolEnabled: true));
    }

    [Fact]
    public void AngleOnly_RequiresMat()
    {
        Assert.True(AngleMatGate.ShouldCreate(true, false, false));
    }

    [Fact]
    public void GrayDirectionOnly_RequiresMat()
    {
        Assert.True(AngleMatGate.ShouldCreate(false, true, false));
    }

    /// <summary>三者全关才不创建 —— 此时确实没有任何消费者需要 Mat，省掉转换是对的。</summary>
    [Fact]
    public void AllOff_SkipsMat()
    {
        Assert.False(AngleMatGate.ShouldCreate(false, false, false));
    }

    /// <summary>多消费者并存同样为 true（穷举剩余组合，锁死"任一即真"语义）。</summary>
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void AnyCombination_WithAtLeastOneOn_RequiresMat(
        bool angle, bool gray, bool pool)
    {
        Assert.True(AngleMatGate.ShouldCreate(angle, gray, pool));
    }
}
