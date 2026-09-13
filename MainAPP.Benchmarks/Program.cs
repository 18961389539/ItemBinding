using System.Diagnostics;
using Extensions;
using MainAPP.Benchmarks.Benchmarks;
using BenchmarkDotNet.Running;
using OpenCvSharp;

// --probe：轻量耗时探针（不依赖 BenchmarkDotNet）。
// 存在的理由：BDN 会为基准方法生成独立工程并重新构建 MainAPP，而本仓 MainAPP 需要
// SuppressTfmSupportBuildWarnings 才能构建通过，该属性传不进 BDN 的生成工程
// （它把 TFM 提示当 error）。故提供一个"直接测、当场出数"的通道。
// 用法：dotnet run -c Release --project MainAPP.Benchmarks -- --probe
if (args.Contains("--probe"))
{
    RunProbe();
    RunBreakdown();
    RunMaskScaling();
    RunMonoRoundTrip();
    return;
}

// BenchmarkDotNet 入口点：以 Release 模式运行（dotnet run -c Release）
// 默认运行所有 [Benchmark] 方法，也可通过命令行参数筛选
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

static void RunBreakdown()
{
    Console.WriteLine();
    Console.WriteLine("── 成本分解：三张全图派生图各占多少 ──");
    Console.WriteLine("按 HeadTailFeaturePool 的实现逐步复刻（同参数），用于判断优化该从哪下手。");
    Console.WriteLine();
    Console.WriteLine($"{"画幅",-14}{"转灰度",-10}{"Sobel梯度",-12}{"局部标准差",-13}{"Canny",-10}{"合计",-10}");

    foreach (var (w, h) in new[] { (640, 480), (1280, 960), (1920, 1080), (2448, 2048) })
    {
        using var bgr = new Mat(h, w, MatType.CV_8UC3, new Scalar(90, 90, 90));
        // 加条纹让各算子有真实工作量（纯色会让 Canny 退化成极快）
        for (int x = 0; x < w; x += 16)
        {
            Cv2.Rectangle(bgr, new Rect(x, 0, 8, h), new Scalar(200, 200, 200), -1);
        }

        var grayMs = TimeOp(() => { using var g = new Mat(); Cv2.CvtColor(bgr, g, ColorConversionCodes.BGR2GRAY); return g; });
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        var sobelMs = TimeOp(() => SobelMag(gray));
        var stdMs = TimeOp(() => LocalStd(gray));
        var cannyMs = TimeOp(() => { var e = new Mat(); Cv2.Canny(gray, e, 80, 160); return e; });
        var total = sobelMs + stdMs + cannyMs;

        Console.WriteLine(
            $"{$"{w}x{h}",-14}{grayMs,-10:F1}{sobelMs,-12:F1}{stdMs,-13:F1}{cannyMs,-10:F1}{total,-10:F1}");
        Console.WriteLine($"{"",-14}单位 ms；合计不含转灰度（转灰度在 Canny/Sobel 之前各算一次，此处单列）");
    }
}

/// <summary>复刻 ComputeGradientMagnitude：两次 Sobel + 平方和开方。</summary>
static Mat SobelMag(Mat gray)
{
    using var gx = new Mat();
    Cv2.Sobel(gray, gx, MatType.CV_32F, 1, 0, 3);
    using var gy = new Mat();
    Cv2.Sobel(gray, gy, MatType.CV_32F, 0, 1, 3);
    using var gx2 = new Mat();
    Cv2.Multiply(gx, gx, gx2);
    using var gy2 = new Mat();
    Cv2.Multiply(gy, gy, gy2);
    using var sum = new Mat();
    Cv2.Add(gx2, gy2, sum);
    var mag = new Mat();
    Cv2.Sqrt(sum, mag);
    return mag;
}

/// <summary>复刻 ComputeLocalStd：ConvertTo + 两次 5×5 高斯 + 方差与开方。</summary>
static Mat LocalStd(Mat gray)
{
    using var g32 = new Mat();
    gray.ConvertTo(g32, MatType.CV_32F);
    using var mu = new Mat();
    Cv2.GaussianBlur(g32, mu, new Size(5, 5), 0);
    using var sq = new Mat();
    Cv2.Multiply(g32, g32, sq);
    using var muSq = new Mat();
    Cv2.GaussianBlur(sq, muSq, new Size(5, 5), 0);
    using var varMat = new Mat();
    Cv2.Subtract(muSq, mu, varMat);
    Cv2.Max(varMat, 0, varMat);
    var std = new Mat();
    Cv2.Sqrt(varMat, std);
    return std;
}

/// <summary>计时一个返回 Mat 的操作（含分配），取多次中位数。</summary>
static double TimeOp(Func<Mat> op)
{
    const int Warmup = 5;
    const int Iters = 15;
    for (int i = 0; i < Warmup; i++)
    {
        op().Dispose();
    }

    var samples = new List<double>(Iters);
    var sw = new Stopwatch();
    for (int i = 0; i < Iters; i++)
    {
        sw.Restart();
        var m = op();
        sw.Stop();
        m.Dispose();
        samples.Add(sw.Elapsed.TotalMilliseconds);
    }

    samples.Sort();
    return samples[samples.Count / 2];
}

static void RunProbe()
{
    // 覆盖常见工业相机画幅：640（模型输入）、1280、1920（1080p）、2448（5MP）
    var sizes = new (int W, int H)[]
    {
        (640, 480), (1280, 960), (1920, 1080), (2448, 2048),
    };

    const int Warmup = 30;

    Console.WriteLine("头尾判定（HeadTailFeaturePool.Evaluate）单帧耗时");
    Console.WriteLine("说明：单线程串行计时；耗时含 3 张全图派生图（Sobel/局部标准差/Canny）");
    Console.WriteLine("      与掩码 ROI 单遍扫描。合成输入见 HeadTailBenchFixture。");
    Console.WriteLine();
    Console.WriteLine($"{"画幅",-14}{"亮度",-6}{"均值",-11}{"中位",-11}{"P95",-11}{"最小",-11}{"托管分配",-12}");

    foreach (var (w, h) in sizes)
    {
        using var fixture = HeadTailBenchFixture.Create(w, h);

        foreach (var brightness in new[] { true, false })
        {
            for (int i = 0; i < Warmup; i++)
            {
                fixture.Evaluate(brightness);
            }

            // 目标采样时长 1.5s：慢画幅少跑几次、快画幅多跑几次，保证统计量稳定
            var samples = new List<double>(4096);
            var sw = new Stopwatch();
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed.TotalSeconds < 1.5)
            {
                sw.Restart();
                fixture.Evaluate(brightness);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds);
            }

            var allocPerCall = (GC.GetAllocatedBytesForCurrentThread() - allocBefore) / (double)samples.Count;

            samples.Sort();
            var mean = samples.Average();
            var median = Percentile(samples, 50);
            var p95 = Percentile(samples, 95);
            var min = samples[0];

            Console.WriteLine(
                $"{$"{w}x{h}",-14}{(brightness ? "开" : "关"),-6}" +
                $"{mean,-11:F2}{median,-11:F2}{p95,-11:F2}{min,-11:F2}{allocPerCall / 1024.0,-12:F1}");
            Console.WriteLine($"{"",-14}（{samples.Count} 次采样；单位 ms，分配列单位 KB）");
        }

        Console.WriteLine();
    }
}

static double Percentile(List<double> sorted, double p)
{
    if (sorted.Count == 0)
    {
        return double.NaN;
    }

    var idx = (int)Math.Round((p / 100.0) * (sorted.Count - 1));
    return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
}

/// <summary>
/// 掩码面积扫描（2026-09-13）：固定图像尺寸、只改掩码大小，用于把总耗时拆成
/// "与掩码无关的全图派生图" 与 "与掩码面积成正比的 ROI 扫描" 两部分。
/// <para>原理：派生图在整幅图上算，成本与掩码无关（截距）；ROI 扫描随掩码像素数线性增长（斜率）。
/// 若某画幅下时间几乎不随掩码变化，说明瓶颈在派生图；若近似线性增长，说明 ROI 扫描占了主要部分。</para>
/// </summary>
static void RunMaskScaling()
{
    Console.WriteLine();
    Console.WriteLine("── 掩码面积扫描：分离「全图派生图」与「ROI 扫描」──");
    Console.WriteLine("固定 2448x2048，只改掩码边长；截距≈派生图成本，斜率≈每百万 ROI 像素的扫描成本。");
    Console.WriteLine();
    Console.WriteLine($"{"掩码比例",-12}{"ROI 像素",-14}{"耗时中位",-12}{"较基准增量",-14}");

    const int W = 2448, H = 2048;
    double? baseline = null;

    foreach (var scale in new[] { 0.25, 0.50, 0.75, 1.0, 1.30 })
    {
        using var fixture = HeadTailBenchFixture.Create(W, H, scale);
        var roiPixels = (long)fixture.MaskArea;

        for (int i = 0; i < 10; i++)
        {
            fixture.Evaluate(brightness: false);
        }

        var samples = new List<double>(64);
        var sw = new Stopwatch();
        for (int i = 0; i < 24; i++)
        {
            sw.Restart();
            fixture.Evaluate(brightness: false);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        baseline ??= median;
        var delta = median - baseline.Value;

        Console.WriteLine(
            $"{scale,-12:F2}{roiPixels,-14:N0}{median,-12:F1}{delta,-14:+0.0;-0.0;0.0}");
    }

    Console.WriteLine("（单位 ms；掩码比例 1.0 = 宽 75% × 高 60% 的居中椭圆，即主探针的默认形态）");
}

/// <summary>
/// 灰度往返开销（2026-09-13）：相机是 Mono8，但当前管线的形状是
/// 「Mono8 展开 → Image&lt;Rgb24&gt;（1→3 字节/像素）→ ToMat 为 CV_8UC3 →
///  HeadTailFeaturePool 内 CvtColor(BGR2GRAY) 折回 1 通道」。
/// <para>本段测量"展开"与"ToMat 拷贝"两步（CvtColor 已在成本分解里单列），
/// 用于判断这条往返是否值得动架构（如让管线保留单通道）。</para>
/// </summary>
static void RunMonoRoundTrip()
{
    Console.WriteLine();
    Console.WriteLine("── 灰度往返开销：Mono8 → Rgb24 → Mat(CV_8UC3) → CvtColor 折回 ──");
    Console.WriteLine("相机实为单通道，但 DecodeImageData 先把 Mono8 逐像素展开为 Rgb24（1→3 字节），");
    Console.WriteLine("特征池内再用 CvtColor(BGR2GRAY) 折回单通道。本段量化这条往返各步的成本。");
    Console.WriteLine();
    Console.WriteLine($"{"画幅",-14}{"Mono8→Rgb24 展开",-20}{"Rgb24→Mat 拷贝",-18}{"CvtColor 折回",-16}");

    foreach (var (w, h) in new[] { (1280, 960), (1920, 1080), (2448, 2048) })
    {
        var pixels = w * (long)h;
        var mono = new byte[pixels];
        for (long i = 0; i < pixels; i++)
        {
            mono[i] = (byte)(i & 0xFF);
        }

        // ① 复刻 DecodeImageData 的 Mono8 分支：逐像素展开为 Rgb24（ImageSharp ProcessPixelRows 同构循环）
        var expandMs = TimeMonoOp(() =>
        {
            var rgb = new byte[pixels * 3];
            for (long i = 0; i < pixels; i++)
            {
                var v = mono[i];
                rgb[i * 3] = v;
                rgb[i * 3 + 1] = v;
                rgb[i * 3 + 2] = v;
            }

            return rgb;
        });

        // 供 ② 使用的 Rgb24 缓冲（与展开结果同形状）
        var rgbForMat = new byte[pixels * 3];
        for (long i = 0; i < pixels; i++)
        {
            var v = mono[i];
            rgbForMat[i * 3] = v;
            rgbForMat[i * 3 + 1] = v;
            rgbForMat[i * 3 + 2] = v;
        }

        // ② 真实的 ToMat 路径（Rgb24→CV_8UC3）：实现是【逐像素 3 次 Marshal.WriteByte】，
        //    不是一次拷贝 —— 若用单次 Marshal.Copy 会把这一步测低数倍。故直接调真实扩展方法。
        var img = SixLabors.ImageSharp.Image.WrapMemory<SixLabors.ImageSharp.PixelFormats.Rgb24>(
            rgbForMat, w, h);
        var copyMs = TimeMatOp(() => img.ToMat());

        // ③ CvtColor 折回单通道（与成本分解里的"转灰度"同参数，便于对照）
        using var bgr = new Mat(h, w, MatType.CV_8UC3);
        var grayMs = TimeMatOp(() =>
        {
            var g = new Mat();
            Cv2.CvtColor(bgr, g, ColorConversionCodes.BGR2GRAY);
            return g;
        });

        Console.WriteLine(
            $"{$"{w}x{h}",-14}{expandMs,-20:F1}{copyMs,-18:F1}{grayMs,-16:F1}");
    }

    Console.WriteLine("（单位 ms/帧。注：① 是「逐像素托管循环 + 3 倍内存写入」，与 ImageSharp 的");
    Console.WriteLine(" ProcessPixelRows 同构；② 是一次性 Marshal.Copy。这两步发生在图像解码阶段，");
    Console.WriteLine(" 不包含在特征池的 130 ms 里 —— 它们是这条灰度往返真正未计入的部分。）");
}

/// <summary>计时返回 byte[] 的操作（含分配），取中位数。</summary>
static double TimeMonoOp(Func<byte[]> op)
{
    const int Warmup = 3;
    const int Iters = 10;
    for (int i = 0; i < Warmup; i++)
    {
        op();
    }

    var samples = new List<double>(Iters);
    var sw = new Stopwatch();
    for (int i = 0; i < Iters; i++)
    {
        sw.Restart();
        var r = op();
        sw.Stop();
        samples.Add(sw.Elapsed.TotalMilliseconds);
        GC.KeepAlive(r);
    }

    samples.Sort();
    return samples[samples.Count / 2];
}

/// <summary>计时返回 Mat 的操作（含分配与释放），取中位数。</summary>
static double TimeMatOp(Func<Mat> op)
{
    const int Warmup = 3;
    const int Iters = 10;
    for (int i = 0; i < Warmup; i++)
    {
        op().Dispose();
    }

    var samples = new List<double>(Iters);
    var sw = new Stopwatch();
    for (int i = 0; i < Iters; i++)
    {
        sw.Restart();
        var m = op();
        sw.Stop();
        m.Dispose();
        samples.Add(sw.Elapsed.TotalMilliseconds);
    }

    samples.Sort();
    return samples[samples.Count / 2];
}
