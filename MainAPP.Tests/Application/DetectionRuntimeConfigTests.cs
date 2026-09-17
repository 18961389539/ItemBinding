using MainAPP.Application;
using MainAPP.Models;
using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// 检测运行期配置端口与"按帧参数快照"的测试（2026-09-16）。
///
/// <para><b>回归背景</b>：<see cref="DetectionRecordService"/> 原先在热路径上直接读进程级单例
/// （<c>Settings.Instance.Algorithm.*</c> 6 处、<c>RecipesManage.Instance.CurrentRecipe</c> 2 处）——
/// 依赖不可见、无法脱离宿主单测、同一帧内每个检测框都重复取值。
/// 现改为注入 <see cref="IDetectionRuntimeConfig"/>，帧首解析一次快照。</para>
///
/// <para><b>本文件钉住的两件事</b>：
/// ① 取值一律来自注入的配置（用例用 33/44/88 这类"非默认值"，
///    一旦有人改回读全局单例，断言会立刻失败——全局默认是 10/10/75）；
/// ② 端口必须"每次访问都重新解析"——因为 <c>Settings.Reload()</c> 会**替换** Algorithm 子对象，
///    若实现改成构造期缓存引用，设置页重载后检测逻辑就会静默读到过期配置。</para>
/// </summary>
public class DetectionRuntimeConfigTests
{
    /// <summary>仅供 AngleTracker 构造使用的最小替身，避免把全局 Settings 单例拖进本测试。</summary>
    private sealed class StubAlgorithmSettings : IAlgorithmSettings
    {
        public bool DedupEnabled => false;
        public bool DedupByEncoder => false;
        public string DedupTrackAxis => "X";
        public double DedupPositionThreshold => 10;
        public double DedupAngleThreshold => 5;
        public int TrackerExpireSeconds => 30;
    }

    /// <summary>
    /// 构造被测服务。本文件只验证"参数解析"这条纯逻辑，不触碰数据层，
    /// 故数据服务传 null（构造函数不会使用它）。
    /// </summary>
    private static DetectionRecordService CreateSut(IDetectionRuntimeConfig config) =>
        new(null!, new AngleTracker(new StubAlgorithmSettings()), config);

    private static AlgorithmSettings DistinctiveAlgorithm() => new()
    {
        // 刻意全部偏离默认值（默认：边距 10、死区 0.10、OK 阈值 75、亮度判向 true）
        EdgeMarginLeftPixels = 33,
        EdgeMarginTopPixels = 44,
        EdgeMarginRightPixels = 55,
        EdgeMarginBottomPixels = 66,
        MinMaskAreaPixels = 111,
        MaxMaskAreaPixels = 999999,
        BrightnessDirectionEnabled = false,
        HeadTailFeaturePoolEnabled = false,
        HeadTailAdaptiveDeadbandEnabled = true,
        HeadTailFeatureDeadband = 0.42,
        HeadTailAdaptiveDeadbandK = 2.5,
        BrightnessContrastStretchEnabled = false,
        BrightnessStretchLowPercentile = 3,
        BrightnessStretchHighPercentile = 97,
        ResultOkScorePercent = 88,
    };

    [Fact]
    public void ResolveFrameOptions_TakesEveryValueFromInjectedConfig()
    {
        var algorithm = DistinctiveAlgorithm();
        var config = new DetectionRuntimeConfig(() => algorithm, () => "配方X", () => "STATION-1");
        var sut = CreateSut(config);

        var options = sut.ResolveFrameOptions(recipeBrightnessDirectionEnabled: null);

        // 只要有一处改回读全局单例，下面任一条即为"默认值"而失败
        Assert.Equal(33, options.MarginLeft);
        Assert.Equal(44, options.MarginTop);
        Assert.Equal(55, options.MarginRight);
        Assert.Equal(66, options.MarginBottom);
        Assert.Equal(111, options.MaskAreaMinPixels);
        Assert.Equal(999999, options.MaskAreaMaxPixels);
        Assert.False(options.BrightnessDirectionEnabled);
        Assert.False(options.FeaturePoolEnabled);
        Assert.True(options.AdaptiveDeadbandEnabled);
        Assert.Equal(0.42, options.FeatureDeadband);
        Assert.Equal(2.5, options.AdaptiveDeadbandK);
        Assert.False(options.StretchEnabled);
        Assert.Equal(3, options.StretchLowPercentile);
        Assert.Equal(97, options.StretchHighPercentile);
        Assert.Equal(88, options.ResultOkScorePercent);
        Assert.Equal("配方X", options.RecipeName);
        Assert.Equal("STATION-1", options.StationCode);
    }

    [Fact]
    public void ResolveFrameOptions_RecipeOverrideWinsOverGlobal()
    {
        // 灰度判向：配方级覆盖优先于全局默认（两处解析口径必须一致）
        var algorithm = DistinctiveAlgorithm(); // 全局 = false
        var sut = CreateSut(new DetectionRuntimeConfig(() => algorithm, () => null, () => null));

        Assert.False(sut.ResolveFrameOptions(null).BrightnessDirectionEnabled);
        Assert.True(sut.ResolveFrameOptions(true).BrightnessDirectionEnabled);
        Assert.False(sut.ResolveFrameOptions(false).BrightnessDirectionEnabled);
    }

    [Fact]
    public void ResolveFrameOptions_ToleratesMissingRecipeAndStation()
    {
        var sut = CreateSut(new DetectionRuntimeConfig(() => new AlgorithmSettings(), () => null, () => null));

        var options = sut.ResolveFrameOptions(null);

        Assert.Null(options.RecipeName);
        Assert.Null(options.StationCode);
    }

    [Fact]
    public void RuntimeConfig_ReResolvesAlgorithmOnEveryAccess()
    {
        // 模拟 Settings.Reload()：它用反射把新实例的可写属性拷回，Algorithm 子对象会被替换成新实例。
        // 端口必须每次访问都向数据源取值，否则重载后读到的是过期配置（静默错误）。
        var current = new AlgorithmSettings { ResultOkScorePercent = 75 };
        var config = new DetectionRuntimeConfig(() => current, () => "R", () => "S");

        Assert.Equal(75, config.Algorithm.ResultOkScorePercent);

        current = new AlgorithmSettings { ResultOkScorePercent = 90 }; // Reload 替换子对象

        Assert.Equal(90, config.Algorithm.ResultOkScorePercent);
    }

    [Fact]
    public void RuntimeConfig_ReReadsRecipeNameOnEveryAccess()
    {
        // 配方切换后新帧必须取到新配方名（追溯列的 RecipeName 依赖它）
        string? recipe = "配方A";
        var config = new DetectionRuntimeConfig(() => new AlgorithmSettings(), () => recipe, () => null);

        Assert.Equal("配方A", config.CurrentRecipeName);

        recipe = "配方B";

        Assert.Equal("配方B", config.CurrentRecipeName);
    }

    [Fact]
    public void RuntimeConfig_RejectsNullDelegates()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DetectionRuntimeConfig(null!, () => null, () => null));
        Assert.Throws<ArgumentNullException>(() =>
            new DetectionRuntimeConfig(() => new AlgorithmSettings(), null!, () => null));
        Assert.Throws<ArgumentNullException>(() =>
            new DetectionRuntimeConfig(() => new AlgorithmSettings(), () => null, null!));
    }
}
