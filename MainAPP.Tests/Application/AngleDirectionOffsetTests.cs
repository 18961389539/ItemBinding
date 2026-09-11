using MainAPP.Application;
using MainAPP.Models;
using OpenCvSharp;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// 角度模型"方向可信度"判据的单元测试（2026-09-11 新增）。
/// <para>背景：角度模型的方向来自"产品质心 → 特征质心"的有向向量。特征居中/对称时两个质心几乎重合，
/// 该向量趋近 0，<c>atan2</c> 的分子分母同时趋零 → 角度由噪声决定，而原有三道质量门
/// （OBB 有效 / 掩码填充率 / 特征面积占比）都只看"能不能算"，不看"方向可不可信"。</para>
/// <para><see cref="AngleDetectionProcessor.ComputeDirectionOffsetRatio"/> 就是补这个缺口的判据：
/// 偏移比低于 <see cref="YoloTool.MinCentroidOffsetRatio"/> 时调用方改用"掩码主轴角 + 灰度判向"兜底。</para>
/// <para>注：模型路径整体（ComputeAngleAsync）无法在此单测——<c>YoloPredictor.SegmentAsync</c>
/// 是走服务解析器的扩展方法，没有可替换的接口，需要真实 ONNX 模型。故只单测这条纯几何判据。</para>
/// </summary>
public class AngleDirectionOffsetTests
{
    /// <summary>特征质心在长轴方向上偏离产品质心，比值 = 偏移距离 / 长轴长度。</summary>
    [Fact]
    public void ComputeDirectionOffsetRatio_AxialOffset_ReturnsDistanceOverAxis()
    {
        // 长轴 100px，质心相距 30px → 0.30
        double ratio = AngleDetectionProcessor.ComputeDirectionOffsetRatio(
            new Point2f(130f, 50f), new Point2f(100f, 50f), 100.0);

        Assert.Equal(0.30, ratio, 6);
    }

    /// <summary>斜向偏移必须按欧氏距离计算，而不是取某个单轴的投影。</summary>
    [Fact]
    public void ComputeDirectionOffsetRatio_DiagonalOffset_UsesEuclideanDistance()
    {
        // 3-4-5 直角三角形：dx=30、dy=40 → 距离 50 → 比值 0.50
        double ratio = AngleDetectionProcessor.ComputeDirectionOffsetRatio(
            new Point2f(130f, 90f), new Point2f(100f, 50f), 100.0);

        Assert.Equal(0.50, ratio, 6);
    }

    /// <summary>两个质心重合（特征居中/对称）时偏移比为 0，即最严重的"方向退化"。</summary>
    [Fact]
    public void ComputeDirectionOffsetRatio_CoincidentCentroids_ReturnsZero()
    {
        double ratio = AngleDetectionProcessor.ComputeDirectionOffsetRatio(
            new Point2f(100f, 50f), new Point2f(100f, 50f), 100.0);

        Assert.Equal(0.0, ratio, 9);
    }

    /// <summary>长轴长度无效（≤1）时无法归一化，返回 0 由调用方按退化处理，不得抛异常或返回 NaN/Infinity。</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-10.0)]
    public void ComputeDirectionOffsetRatio_InvalidAxisLength_ReturnsZero(double axisLength)
    {
        double ratio = AngleDetectionProcessor.ComputeDirectionOffsetRatio(
            new Point2f(200f, 50f), new Point2f(100f, 50f), axisLength);

        Assert.Equal(0.0, ratio, 9);
    }

    /// <summary>
    /// 与配方阈值（<see cref="YoloTool.MinCentroidOffsetRatio"/>，默认 0.03）的边界语义：
    /// 低于阈值判为方向退化、回到掩码兜底；达到阈值才采信模型角度。
    /// </summary>
    [Fact]
    public void ComputeDirectionOffsetRatio_ThresholdSemantics()
    {
        var tool = new YoloTool();
        Assert.Equal(0.03f, tool.MinCentroidOffsetRatio, 6);

        // 偏移 2px / 长轴 100px = 0.02 < 0.03 → 方向退化
        double degenerate = AngleDetectionProcessor.ComputeDirectionOffsetRatio(
            new Point2f(102f, 50f), new Point2f(100f, 50f), 100.0);
        Assert.True(degenerate < tool.MinCentroidOffsetRatio,
            $"偏移比 {degenerate:F4} 应判为退化（阈值 {tool.MinCentroidOffsetRatio:F4}）");

        // 偏移 5px / 长轴 100px = 0.05 ≥ 0.03 → 采信模型角度
        double reliable = AngleDetectionProcessor.ComputeDirectionOffsetRatio(
            new Point2f(105f, 50f), new Point2f(100f, 50f), 100.0);
        Assert.True(reliable >= tool.MinCentroidOffsetRatio,
            $"偏移比 {reliable:F4} 应被采信（阈值 {tool.MinCentroidOffsetRatio:F4}）");
    }
}
