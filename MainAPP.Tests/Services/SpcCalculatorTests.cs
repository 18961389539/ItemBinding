using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// SPC 控制图计算测试（2026-09-13）：分组均值/极差、控制限、CPK、判异（超限 + 连续 7 点同侧）。
/// </summary>
public class SpcCalculatorTests
{
    [Fact]
    public void Compute_StableData_CenterlineEqualsMean_UclAboveLcl()
    {
        // 4 组 × 5 个子样本 = 20 个稳定样本（100±1）
        var samples = new double[20];
        for (var i = 0; i < 20; i++)
        {
            samples[i] = 100 + (i % 5 - 2) * 0.5;
        }

        var r = SpcCalculator.Compute(samples, 5);

        Assert.Equal(4, r.MeanValues.Length);
        Assert.Equal(100.0, r.CenterLine, 3);
        Assert.True(r.Ucl > r.CenterLine);
        Assert.True(r.Lcl < r.CenterLine);
        Assert.Empty(r.AlarmMessages);
        Assert.Equal(100.0, r.MeanValues.Average(), 3);
    }

    [Fact]
    public void Compute_DataTooSmall_ReturnsInfoMessage()
    {
        var r = SpcCalculator.Compute(new[] { 1.0, 2.0, 3.0 }, 5);

        Assert.Single(r.AlarmMessages);
        Assert.Contains("样本子组数不足", r.AlarmMessages[0]);
    }

    [Fact]
    public void Compute_OutOfControlPoint_FlagsAlarm()
    {
        // 突跳点：让其中一组均值远超其余
        var samples = new double[] {
            100,100,100,100,100,                                    // 组1
            100,100,100,100,100,                                    // 组2
            100,100,100,100,100,                                    // 组3
            1000,1000,1000,1000,1000,                               // 组4（失控）
        };

        var r = SpcCalculator.Compute(samples, 5);

        Assert.Contains(r.AlarmIndices, i => i == 4);
        Assert.Contains(r.AlarmMessages, m => m.Contains("超出控制限"));
    }

    [Fact]
    public void Compute_TrendSevenPointsSameSide_FlagsTrend()
    {
        // 7 组均值偏高（100>CL）且均在控制限内 + 1 组偏低（90）拉低中心线：
        // 组内 [98,100,102,100,98] → 均值 100、极差 4；偏低组 [88,90,92,90,88] → 均值 90。
        // CL≈98.75、UCL=CL+1.848×4≈106>100 → 高组不超限，但连续 7 点位于中心线上方 → 趋势判异。
        var samples = new List<double>();
        for (var g = 0; g < 7; g++)
        {
            samples.AddRange(new[] { 98.0, 100.0, 102.0, 100.0, 98.0 });
        }

        samples.AddRange(new[] { 88.0, 90.0, 92.0, 90.0, 88.0 });

        var r = SpcCalculator.Compute(samples, 5);

        var trend = r.AlarmMessages.Where(m => m.Contains("连续 7 点")).ToList();
        Assert.True(trend.Count > 0, $"预期出现趋势判异，实际: {string.Join("; ", r.AlarmMessages)}");
    }

    [Fact]
    public void Compute_WithSpecLimits_ComputesCpk()
    {
        var samples = new double[20];
        for (var i = 0; i < 20; i++)
        {
            samples[i] = 100 + (i % 5 - 2) * 0.5;
        }

        var r = SpcCalculator.Compute(samples, 5, usl: 102, lsl: 98);

        Assert.NotNull(r.Cpk);
        Assert.True(r.Cpk > 0 && r.Cpk < 10, $"CPK={r.Cpk} 应在正常量级");
    }
}