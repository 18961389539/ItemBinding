using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// WindowSizing 单元测试（2026-09-15 分辨率适配）。
///
/// 回归背景：原实现用 Math.Max(1024, WindowWidth) / Math.Max(700, WindowHeight) 做下限保护，
/// 在 1024×768 的工业机上会让 1200×800 的默认窗口比屏幕还大（导航栏、顶栏按钮落在可视区外），
/// 且 1024 的下限恰等于屏宽，用户无法拖拽缩小。
///
/// 这里用纯计算入口 <see cref="WindowSizing.Fit"/> 覆盖各类工作区，
/// 其中 1024×728 即 1024×768 屏减 40px 任务栏后的实际工作区。
/// </summary>
public class WindowSizingTests
{
    /// <summary>1024×768 屏（工作区 1024×728）下，默认设置 1200×800 必须被收敛到工作区。</summary>
    [Fact]
    public void Fit_SmallScreen1024x768_ClampsDefaultWindowToWorkArea()
    {
        var (width, height) = WindowSizing.Fit(1200, 800, 1024, 728);

        Assert.Equal(1024, width);
        Assert.Equal(728, height);
    }

    /// <summary>1024×768 屏下，ChartsView 的 1600×1000 设计尺寸同样收敛到工作区。</summary>
    [Fact]
    public void Fit_SmallScreen1024x768_ClampsOversizedDesignSize()
    {
        var (width, height) = WindowSizing.Fit(1600, 1000, 1024, 728);

        Assert.Equal(1024, width);
        Assert.Equal(728, height);
    }

    /// <summary>大屏下期望尺寸落在 [下限, 工作区] 之间时，必须原样保留（不干预正常场景）。</summary>
    [Theory]
    [InlineData(1600, 900, 1200, 800, 1200, 800)]
    [InlineData(1920, 1000, 1400, 900, 1400, 900)]
    [InlineData(1600, 900, 900, 600, 900, 600)]
    public void Fit_LargeScreen_KeepsDesiredSizeWithinRange(
        double workWidth,
        double workHeight,
        double desiredWidth,
        double desiredHeight,
        double expectedWidth,
        double expectedHeight)
    {
        var (width, height) = WindowSizing.Fit(desiredWidth, desiredHeight, workWidth, workHeight);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    /// <summary>期望尺寸小于安全下限时抬到下限，避免布局被压垮。</summary>
    [Fact]
    public void Fit_DesiredBelowMinimum_RaisesToMinimum()
    {
        var (width, height) = WindowSizing.Fit(640, 480, 1600, 900);

        Assert.Equal(WindowSizing.MinUsableWidth, width);
        Assert.Equal(WindowSizing.MinUsableHeight, height);
    }

    /// <summary>工作区比安全下限还小（极端小屏）时以工作区为准——宁小勿溢出。</summary>
    [Fact]
    public void Fit_WorkAreaSmallerThanMinimum_FallsBackToWorkArea()
    {
        var (width, height) = WindowSizing.Fit(1200, 800, 800, 560);

        Assert.Equal(800, width);
        Assert.Equal(560, height);
    }

    /// <summary>极端小屏下也不得因为 Math.Clamp 的 min &gt; max 抛异常。</summary>
    [Fact]
    public void Fit_WorkAreaSmallerThanMinimum_DoesNotThrow()
    {
        var exception = Record.Exception(() => WindowSizing.Fit(1200, 800, 800, 560));

        Assert.Null(exception);
    }

    /// <summary>不变量：任何「期望尺寸 × 工作区」组合下，结果都不得超出工作区、也不得低于下限（工作区足够时）。</summary>
    [Theory]
    [InlineData(1024, 728, 1200, 800)]
    [InlineData(1024, 728, 1400, 900)]
    [InlineData(1280, 720, 1200, 800)]
    [InlineData(1366, 728, 1200, 800)]
    [InlineData(1600, 860, 1200, 800)]
    [InlineData(1920, 1040, 1400, 900)]
    [InlineData(1024, 728, 800, 500)]
    public void Fit_NeverExceedsWorkArea(
        double workWidth,
        double workHeight,
        double desiredWidth,
        double desiredHeight)
    {
        var (width, height) = WindowSizing.Fit(desiredWidth, desiredHeight, workWidth, workHeight);

        Assert.True(width <= workWidth, $"宽度 {width} 超出工作区 {workWidth}");
        Assert.True(height <= workHeight, $"高度 {height} 超出工作区 {workHeight}");
        Assert.True(width >= WindowSizing.MinUsableWidth || width == workWidth);
        Assert.True(height >= WindowSizing.MinUsableHeight || height == workHeight);
    }
}
