using System;
using System.Collections.Generic;
using System.Linq;

namespace MainAPP.Services;

/// <summary>均值-极差（X̄-R）控制图计算结果（2026-09-13）。</summary>
public sealed record SpcResult(
    double[] MeanValues,          // 各组均值（X̄）
    double[] RangeValues,         // 各组极差（R）
    double CenterLine,            // 中心线（总均值）
    double Ucl,                   // 上控制限
    double Lcl,                   // 下控制限
    double? Cpk,                  // 过程能力指数（需传入规格限；否则 null）
    IReadOnlyList<int> AlarmIndices,   // 判异组下标（1-based 显示）
    IReadOnlyList<string> AlarmMessages);

/// <summary>
/// SPC 过程控制计算（2026-09-13，纯函数无 IO，供图表页与单测共用）。
/// <para>按子组大小把连续样本分组，算各组的均值 X̄ 与极差 R；控制限用 A2 法：
/// CL=X̄、UCL=X̄+A2·R̄、LCL=X̄−A2·R̄。判异规则（第一版）：组均值超出控制限、
/// 连续 7 点位于中心线同侧（趋势信号）。规格限存在时计算 CPK（σ̂=R̄/d2）。</para>
/// </summary>
public static class SpcCalculator
{
    // A2 常数表（子组大小 2..10 的均值控制图系数）
    private static readonly double[] A2Table = { 0, 0, 1.880, 1.023, 0.729, 0.577, 0.483, 0.419, 0.373, 0.337, 0.308 };

    /// <summary>d2 常数表（R̄ → σ̂ 换算）。</summary>
    private static double D2(int n) => n switch
    {
        2 => 1.128, 3 => 1.693, 4 => 2.059, 5 => 2.326,
        6 => 2.534, 7 => 2.704, 8 => 2.847, 9 => 2.970,
        10 => 3.078, _ => 1.0,
    };

    public static SpcResult Compute(IReadOnlyList<double> samples, int subgroupSize, double? usl = null, double? lsl = null)
    {
        var n = Math.Clamp(subgroupSize, 2, 10);
        var sampleArr = samples.ToArray();
        var groupCount = sampleArr.Length / n;
        var means = new double[groupCount];
        var ranges = new double[groupCount];
        for (var g = 0; g < groupCount; g++)
        {
            double min = double.MaxValue, max = double.MinValue, sum = 0;
            for (var i = 0; i < n; i++)
            {
                var v = sampleArr[g * n + i];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            means[g] = sum / n;
            ranges[g] = max - min;
        }

        if (groupCount < 2)
        {
            return new SpcResult(means, ranges, 0, 0, 0, null,
                Array.Empty<int>(), new[] { "样本子组数不足（<2），无法建立控制限" });
        }

        var cl = means.Average();
        var rbar = ranges.Average();
        var a2 = n < A2Table.Length ? A2Table[n] : 3.0 / Math.Sqrt(n);
        var ucl = cl + a2 * rbar;
        var lcl = cl - a2 * rbar;

        double? cpk = null;
        if (usl.HasValue && lsl.HasValue && rbar > 1e-12)
        {
            var sigma = rbar / D2(n);
            cpk = Math.Min((usl.Value - cl) / (3 * sigma), (cl - lsl.Value) / (3 * sigma));
        }

        var alarmIdx = new List<int>();
        var messages = new List<string>();
        for (var i = 0; i < means.Length; i++)
        {
            if (means[i] > ucl || means[i] < lcl)
            {
                alarmIdx.Add(i + 1);
                messages.Add($"组 {i + 1} 均值 {means[i]:F3} 超出控制限 [{lcl:F3}, {ucl:F3}]");
            }
        }

        // 连续 7 点同侧（趋势）：每类最多报一组，避免刷屏
        for (var start = 0; start + 6 < means.Length; start++)
        {
            var above = true;
            var below = true;
            for (var j = 0; j < 7; j++)
            {
                if (means[start + j] <= cl) above = false;
                if (means[start + j] >= cl) below = false;
            }

            if (above || below)
            {
                alarmIdx.Add(start + 1);
                messages.Add($"组 {start + 1}~{start + 7} 连续 7 点位于中心线{(above ? "上" : "下")}方（趋势信号）");
                break;
            }
        }

        return new SpcResult(means, ranges, cl, ucl, lcl, cpk,
            alarmIdx.Distinct().ToArray(), messages.Distinct().ToArray());
    }
}