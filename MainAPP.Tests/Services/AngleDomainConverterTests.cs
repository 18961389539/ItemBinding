using MainAPP.Services;
using System;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// 发送角度域换算测试（2026-09-17）。
///
/// <para><b>背景</b>：部分机械手腕关节只能旋转 ±90°，而产品朝向是整圈量。
/// 对 180° 对称的产品/夹爪可把角度折叠 180° 再发（(-90,90] 域）。
/// 本文件把三件容易出错的事钉住：① 域不变量；② 折叠与"原角"物理等价（相差 180° 的整数倍）；
/// ③ <b>未知哨兵 -9999 绝不能被换算</b>——它按数值参与运算会变成 81°，看起来完全正常。</para>
/// </summary>
public class AngleDomainConverterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("junk")]
    [InlineData("Signed181")]
    public void Parse_UnknownOrEmpty_FallsBackToSigned180(string? raw)
    {
        // 发送域这类参数：配置损坏时退回"与历史行为一致"比退回"新行为"安全
        Assert.Equal(AngleDomain.Signed180, AngleDomainConverter.Parse(raw));
    }

    [Theory]
    [InlineData("Folded90")]
    [InlineData("folded90")]
    [InlineData("FOLDED90")]
    [InlineData(" Folded90 ")]
    public void Parse_RecognizesFolded90_CaseInsensitively(string raw)
    {
        Assert.Equal(AngleDomain.Folded90, AngleDomainConverter.Parse(raw));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 90)]          // 边界含 90
    [InlineData(90.5, -89.5)]
    [InlineData(-90, 90)]        // -90 折到 +90（对 180° 对称件同一朝向）
    [InlineData(-90.5, 89.5)]
    [InlineData(150, -30)]
    [InlineData(-150, 30)]
    [InlineData(180, 0)]
    [InlineData(-179.9, 0.1)]
    [InlineData(359, -1)]
    public void Folded90_MapsToExpectedRepresentative(double input, double expected)
    {
        Assert.Equal(expected, AngleDomainConverter.ToDomain(input, AngleDomain.Folded90), 6);
    }

    /// <summary>
    /// 域不变量：Folded90 的输出必须落在 (-90, 90]。
    /// 用扫描覆盖整数与半整点，避免只测几个样例漏掉边界。
    /// </summary>
    [Fact]
    public void Folded90_OutputAlwaysWithinHalfTurn()
    {
        for (var x = -1000.0; x <= 1000.0; x += 0.5)
        {
            var v = AngleDomainConverter.ToDomain(x, AngleDomain.Folded90);
            Assert.True(v > -90.0 && v <= 90.0, $"输入 {x} → 输出 {v} 越界");
        }
    }

    /// <summary>域不变量：Signed180 的输出必须落在 (-180, 180]（与系统规范域一致）。</summary>
    [Fact]
    public void Signed180_OutputAlwaysWithinFullTurn()
    {
        for (var x = -1000.0; x <= 1000.0; x += 0.5)
        {
            var v = AngleDomainConverter.ToDomain(x, AngleDomain.Signed180);
            Assert.True(v > -180.0 && v <= 180.0, $"输入 {x} → 输出 {v} 越界");
        }
    }

    /// <summary>
    /// 折叠不是"随便换个值"：必须与原角物理等价，即两者相差 180° 的整数倍。
    /// 这条是"折叠后机械手仍能正确抓取"的数学依据。
    /// </summary>
    [Fact]
    public void Folded90_IsEquivalentModulo180()
    {
        for (var x = -720.0; x <= 720.0; x += 7.0)
        {
            var folded = AngleDomainConverter.ToDomain(x, AngleDomain.Folded90);
            var diff = folded - x;
            var remainder = diff % 180.0;
            // 允许浮点误差：余数应约为 0 或 ±180
            var minDistance = Math.Min(Math.Abs(remainder), Math.Min(Math.Abs(remainder - 180.0), Math.Abs(remainder + 180.0)));
            Assert.True(minDistance < 1e-6, $"输入 {x} → {folded}，差值 {diff} 不是 180 的整数倍");
        }
    }

    /// <summary>幂等：对已折叠的值再折叠不应改变结果（重复调用安全）。</summary>
    [Fact]
    public void Folded90_IsIdempotent()
    {
        for (var x = -360.0; x <= 360.0; x += 3.0)
        {
            var once = AngleDomainConverter.ToDomain(x, AngleDomain.Folded90);
            var twice = AngleDomainConverter.ToDomain(once, AngleDomain.Folded90);
            Assert.Equal(once, twice, 6);
        }
    }

    /// <summary>
    /// ★ 哨兵保护：-9999 表示"角度未知"，不是角度值。
    /// 若按数值换算，-9999 → 81°，会变成一个看起来完全正常的角度被发出去——比不换算更危险。
    /// </summary>
    [Theory]
    [InlineData(AngleDomain.Signed180)]
    [InlineData(AngleDomain.Folded90)]
    public void UnknownSentinel_IsNeverTransformed(AngleDomain domain)
    {
        Assert.Equal(AngleTracker.UnknownAngle,
            AngleDomainConverter.ToDomain(AngleTracker.UnknownAngle, domain));
    }

    [Theory]
    [InlineData(AngleDomain.Signed180)]
    [InlineData(AngleDomain.Folded90)]
    public void NaN_PassesThrough(AngleDomain domain)
    {
        Assert.True(double.IsNaN(AngleDomainConverter.ToDomain(double.NaN, domain)));
    }

    /// <summary>Signed180 域必须与既有 ToRobotAngle 行为完全一致（零回归）。</summary>
    [Fact]
    public void Signed180_MatchesLegacyToRobotAngle()
    {
        foreach (var x in new[] { -720.0, -359.0, -180.0, -90.0, -0.0, 0.0, 90.0, 179.9, 180.0, 270.0, 359.0, 720.0 })
        {
            Assert.Equal(ToVGT.ToRobotAngle(x), AngleDomainConverter.ToDomain(x, AngleDomain.Signed180), 9);
        }
    }

    [Fact]
    public void Describe_MentionsBothBounds()
    {
        Assert.Contains("180", AngleDomainConverter.Describe(AngleDomain.Signed180));
        Assert.Contains("90", AngleDomainConverter.Describe(AngleDomain.Folded90));
    }
}
