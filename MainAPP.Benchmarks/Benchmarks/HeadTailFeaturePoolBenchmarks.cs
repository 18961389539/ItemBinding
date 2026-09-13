using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace MainAPP.Benchmarks.Benchmarks;

/// <summary>
/// 头尾判定（特征池）单帧耗时基准（2026-09-13）。
/// <para><b>为什么要测</b>：<c>HeadTailFeaturePool.Evaluate</c> 跑在检测主路径上，每帧都要执行
/// 三张<b>全图</b>派生图（Sobel 梯度幅值 / 5×5 高斯局部标准差 / Canny 边缘）
/// 加一次掩码 ROI 单遍扫描，是本路径上最重的一段计算。</para>
/// <para><b>成本结构</b>：三张派生图在<b>整幅灰度图</b>上计算（不是掩码 ROI），
/// 故耗时主要随<b>整幅图像面积</b>增长；掩码扫描是次要项。故按图像尺寸参数化。</para>
/// <para><b>注意</b>：BDN 会为基准生成独立工程并重新构建 MainAPP，而本仓 MainAPP 需要
/// <c>SuppressTfmSupportBuildWarnings</c> 才能通过（BDN 生成工程拿不到该属性且把 TFM
/// 提示当 error），故 BDN 路径在本机跑不通。日常取数请用
/// <c>dotnet run -c Release --project MainAPP.Benchmarks -- --probe</c> 快速探针。</para>
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class HeadTailFeaturePoolBenchmarks
{
    [Params(640, 1280, 1920, 2448)]
    public int Width { get; set; }

    private int Height => Width switch
    {
        640 => 480,
        1280 => 960,
        1920 => 1080,
        _ => 2048,
    };

    private HeadTailBenchFixture _fixture = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fixture = HeadTailBenchFixture.Create(Width, Height);
        _fixture.Evaluate(brightness: true); // 预热，排除 JIT 与 OpenCV 一次性初始化
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture?.Dispose();

    /// <summary>完整路径（含亮度特征与掩码内对比度拉伸）。</summary>
    [Benchmark(Baseline = true)]
    public bool Evaluate_WithBrightness() => _fixture.Evaluate(brightness: true);

    /// <summary>关闭亮度特征（不累积三张直方图）——量化亮度那一档的增量成本。</summary>
    [Benchmark]
    public bool Evaluate_NoBrightness() => _fixture.Evaluate(brightness: false);
}
