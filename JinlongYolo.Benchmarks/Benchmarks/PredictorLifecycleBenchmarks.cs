using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JinlongYolo.Benchmarks.Benchmarks;

/// <summary>
/// 预测器生命周期内存泄漏检测 — 使用真实 ONNX 模型测试完整推理链的 GC 压力。
/// 
/// 运行前：
///   dotnet run -c Release -- --filter "PredictorLifecycle" --envVars MODEL_PATH=D:\ItemBinding\Models\best0625.onnx
///   或修改下面的 _modelPath 默认值。
/// </summary>
[MemoryDiagnoser]
public class PredictorLifecycleBenchmarks
{
    private static readonly string _modelPath =
        Environment.GetEnvironmentVariable("MODEL_PATH")
        ?? @"D:\ItemBinding\Models\best0625.onnx";

    private byte[] _imageBytes = null!;
    private YoloPredictor _predictor = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (!File.Exists(_modelPath))
            throw new FileNotFoundException($"Model not found: {_modelPath}. Set MODEL_PATH env var.");

        // 生成 640×640 测试图像
        using var image = new Image<Rgb24>(640, 640);
        _imageBytes = new byte[640 * 640 * 3];
        image.CopyPixelDataTo(_imageBytes);

        _predictor = new YoloPredictor(_modelPath, YoloPredictorOptions.Default);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _predictor.Dispose();
    }

    // === 单次推理 — 内存基线 ===

    [Benchmark(Description = "Predict 单次 (Segment)")]
    [BenchmarkCategory("Single")]
    public YoloResult<Segmentation> Predict_Single_Segment()
    {
        using var image = Image.Load<Rgb24>(_imageBytes);
        return _predictor.Segment(image);
    }

    // === 推理循环 — 检测累积泄漏 ===

    [Benchmark(Description = "Predict ×10 (含 GC 测量)")]
    [BenchmarkCategory("Loop")]
    public int Predict_10_Loops()
    {
        for (int i = 0; i < 10; i++)
        {
            using var image = Image.Load<Rgb24>(_imageBytes);
            using var result = _predictor.Segment(image);
        }
        return 10;
    }

    // === Predictor Create/Dispose 循环 ===

    [Benchmark(Description = "Create + Predict + Dispose")]
    [BenchmarkCategory("Lifecycle")]
    public YoloResult<Segmentation> Predictor_Create_Predict_Dispose()
    {
        using var predictor = new YoloPredictor(_modelPath, YoloPredictorOptions.Default);
        using var image = Image.Load<Rgb24>(_imageBytes);
        return predictor.Segment(image);
    }

    // === PredictorPool Acquire/Return 循环 ===

    [Benchmark(Description = "Pool Acquire/Return ×10")]
    [BenchmarkCategory("Pool")]
    public int Pool_Acquire_Return_10()
    {
        var pool = YoloPredictorPool.Create(_modelPath,
            new YoloPredictorPoolLayout { CpuOnlyCount = 1 });

        try
        {
            using var image = Image.Load<Rgb24>(_imageBytes);
            for (int i = 0; i < 10; i++)
            {
                using var lease = pool.Acquire();
                lease.Predictor.Segment(image);
            }
        }
        finally
        {
            pool.Dispose();
        }

        return 10;
    }
}

/// <summary>
/// 长时间推理循环 — 使用 Monitoring 策略运行数百次迭代以检测渐进式内存增长。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 0, iterationCount: 5, invocationCount: 50)]
public class LongRunInferenceBenchmark
{
    private static readonly string _modelPath =
        Environment.GetEnvironmentVariable("MODEL_PATH")
        ?? @"D:\ItemBinding\Models\best0625.onnx";

    private byte[] _imageBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (!File.Exists(_modelPath))
            throw new FileNotFoundException($"Model not found: {_modelPath}. Set MODEL_PATH env var.");

        using var image = new Image<Rgb24>(640, 640);
        _imageBytes = new byte[640 * 640 * 3];
        image.CopyPixelDataTo(_imageBytes);
    }

    /// <summary>
    /// 每次迭代创建一个新的 Predictor → 推理 → Dispose。
    /// 检测 ServiceProvider 字典累积和 Session 泄漏。
    /// </summary>
    [Benchmark(Description = "长运行: Create→Predict→Dispose ×50")]
    public int LongRun_Create_Predict_Dispose_50()
    {
        for (int i = 0; i < 50; i++)
        {
            using var predictor = new YoloPredictor(_modelPath, YoloPredictorOptions.Default);
            using var image = Image.Load<Rgb24>(_imageBytes);
            predictor.Segment(image);
        }
        return 50;
    }

    /// <summary>
    /// 重用 Predictor，多次推理。
    /// 检测 SessionRunner 的 ThreadLocal 缓冲区累积。
    /// </summary>
    [Benchmark(Description = "长运行: 单Predictor×50次推理")]
    public int LongRun_SinglePredictor_50Inferences()
    {
        using var predictor = new YoloPredictor(_modelPath, YoloPredictorOptions.Default);
        for (int i = 0; i < 50; i++)
        {
            using var image = Image.Load<Rgb24>(_imageBytes);
            predictor.Segment(image);
        }
        return 50;
    }
}
