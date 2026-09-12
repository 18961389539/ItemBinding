using MainAPP.Application;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// 检测结果 OK/NG 判定的单元测试（2026-09-12 新增）。
///
/// <para>判定规则由用户定义：<b>条码读取成功 且 置信度 ≥ 阈值（默认 75%）→ OK，否则 NG</b>。</para>
///
/// <para>★ 本测试最重要的作用不是覆盖分支，而是<b>把量纲钉死</b>：
/// 落库的 <c>DbModel.Score</c> 来自 <c>InferenceResultItem.Confidence</c>，量纲是 <b>0~1</b>；
/// 而配置项 <c>AlgorithmSettings.ResultOkScorePercent</c> 是<b>百分比 0~100</b>（默认 75）。
/// 两者单位不同，实现里必须先 ×100 再比较。若有人日后"顺手"去掉这个换算，
/// 下面 <see cref="Evaluate_ConfidenceJustBelowThreshold_ReturnsNg"/> 与
/// <see cref="Evaluate_ConfidenceJustAboveThreshold_ReturnsOk"/> 会立刻失败——
/// 因为 0.74 ≤ 75 会让<b>所有记录都变成 NG</b>，而这是个不会抛异常、只会让数据静默失真的错误。</para>
/// </summary>
public class DetectionResultEvaluatorTests
{
    private const double DefaultThresholdPercent = 75.0;

    /// <summary>条码没读到 → 直接 NG，与置信度多高无关。</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.99)]
    [InlineData(0.0)]
    public void Evaluate_NoBarcode_AlwaysReturnsNg(double confidence)
    {
        string result = DetectionResultEvaluator.Evaluate(false, confidence, DefaultThresholdPercent);

        Assert.Equal(DetectionResultEvaluator.Ng, result);
    }

    /// <summary>条码读到且置信度达到阈值 → OK。</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.90)]
    [InlineData(0.76)]
    public void Evaluate_ConfidenceJustAboveThreshold_ReturnsOk(double confidence)
    {
        string result = DetectionResultEvaluator.Evaluate(true, confidence, DefaultThresholdPercent);

        Assert.Equal(DetectionResultEvaluator.Ok, result);
    }

    /// <summary>★ 量纲回归：0.74 是 74%，低于 75% → NG。若实现漏了 ×100，这里会得到 OK。</summary>
    [Fact]
    public void Evaluate_ConfidenceJustBelowThreshold_ReturnsNg()
    {
        string result = DetectionResultEvaluator.Evaluate(true, 0.74, DefaultThresholdPercent);

        Assert.Equal(DetectionResultEvaluator.Ng, result);
    }

    /// <summary>★ 量纲回归：0.75 恰好是 75%，边界取「大于等于」→ OK。</summary>
    [Fact]
    public void Evaluate_ConfidenceExactlyAtThreshold_ReturnsOk()
    {
        string result = DetectionResultEvaluator.Evaluate(true, 0.75, DefaultThresholdPercent);

        Assert.Equal(DetectionResultEvaluator.Ok, result);
    }

    /// <summary>置信度 0 且条码正常 → NG（不是 OK）。</summary>
    [Fact]
    public void Evaluate_ZeroConfidenceWithBarcode_ReturnsNg()
    {
        string result = DetectionResultEvaluator.Evaluate(true, 0.0, DefaultThresholdPercent);

        Assert.Equal(DetectionResultEvaluator.Ng, result);
    }

    /// <summary>阈值可配：阈值降到 50% 后，0.60 应当判 OK。</summary>
    [Fact]
    public void Evaluate_CustomThreshold_RespectsConfiguredValue()
    {
        string relaxed = DetectionResultEvaluator.Evaluate(true, 0.60, 50.0);
        string strict = DetectionResultEvaluator.Evaluate(true, 0.60, 70.0);

        Assert.Equal(DetectionResultEvaluator.Ok, relaxed);
        Assert.Equal(DetectionResultEvaluator.Ng, strict);
    }

    /// <summary>默认阈值就是 75%（与用户定义一致），防止有人改默认值而不自知。</summary>
    [Fact]
    public void AlgorithmSettings_DefaultResultOkScorePercent_Is75()
    {
        var settings = new MainAPP.Models.AlgorithmSettings();

        Assert.Equal(75.0, settings.ResultOkScorePercent);
    }
}
