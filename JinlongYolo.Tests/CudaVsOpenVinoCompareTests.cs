using JinlongYolo.YoloSharp.Contracts.Services;
using JinlongYolo.YoloSharp.Services;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Xunit.Abstractions;

namespace JinlongYolo.YoloSharp;

/// <summary>
/// 对比 CUDA 与 CPU 后端在同一张图上的推理结果差异。
/// 用于诊断"两者推理结果有很大差别"的根因。
/// </summary>
public class CudaVsOpenVinoCompareTests
{
    private const string ModelPath = @"D:\ItemBinding\JinlongYolo.Tests\best_cuda.onnx";
    private const string ImagePath = @"D:\ItemBinding\JinlongYolo.Tests\1.jpg";

    private readonly ITestOutputHelper _output;

    public CudaVsOpenVinoCompareTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 主对比测试：在同一张图上分别用 CUDA、OpenVINO-CPU、ORT 默认 CPU 跑推理，
    /// 输出每个后端的检测框、置信度、坐标，并两两对比。
    /// </summary>
    [Fact]
    public void Compare_SameImage_CudaVsOpenVinoCpu_PrintsDifferences()
    {
        if (!File.Exists(ModelPath))
        {
            _output.WriteLine($"跳过：模型文件不存在 {ModelPath}");
            return;
        }

        if (!File.Exists(ImagePath))
        {
            _output.WriteLine($"跳过：图片不存在 {ImagePath}");
            return;
        }

        var configuration = new YoloConfiguration
        {
            Confidence = 0.3f,
            IoU = 0.45f,
            KeepAspectRatio = true,
            ApplyAutoOrient = false,
            MaximumDetections = 300,
        };

        _output.WriteLine($"模型: {ModelPath}");
        _output.WriteLine($"图片: {ImagePath}");

        using var baseImage = Image.Load<Rgb24>(ImagePath);
        _output.WriteLine($"图像尺寸: {baseImage.Width}x{baseImage.Height}");
        _output.WriteLine($"配置: Confidence={configuration.Confidence}, IoU={configuration.IoU}, KeepAspectRatio={configuration.KeepAspectRatio}");
        _output.WriteLine(string.Empty);

        // 1. CUDA 后端
        var cudaResult = TryRun("CUDA", () => new YoloPredictor(ModelPath, new YoloPredictorOptions
        {
            UseCuda = true,
            CudaDeviceId = 0,
            Configuration = configuration,
        }), baseImage);

        // 2. OpenVINO CPU 后端 - 默认配置（无精度提示，复现用户实际场景）
        var ovDefaultResult = TryRun("OpenVINO-CPU(默认)", () => new YoloPredictor(ModelPath, new YoloPredictorOptions
        {
            OpenVino = new OpenVinoOptions
            {
                DeviceType = "CPU",
                PerformanceHint = "LATENCY",
            },
            Configuration = configuration,
        }), baseImage);

        // 3. OpenVINO CPU 后端 - 强制 FP32 精度
        var ovFp32Result = TryRun("OpenVINO-CPU(FP32)", () => new YoloPredictor(ModelPath, new YoloPredictorOptions
        {
            OpenVino = new OpenVinoOptions
            {
                DeviceType = "CPU",
                PerformanceHint = "LATENCY",
                PrecisionHint = "f32",
            },
            Configuration = configuration,
        }), baseImage);

        // 4. 纯 CPU 后端（ORT 默认，无 OpenVINO）作为基准
        var cpuOnlyResult = TryRun("CPU(ORT默认)", () => new YoloPredictor(ModelPath, new YoloPredictorOptions
        {
            UseCuda = false,
            PreferOpenVinoCpuForDetection = false,
            Configuration = configuration,
        }), baseImage);

        _output.WriteLine(string.Empty);
        _output.WriteLine("=== 汇总对比 ===");

        if (cudaResult is { } cuda && ovDefaultResult is { } ovDefault)
        {
            CompareResults("CUDA", cuda, "OpenVINO-CPU(默认)", ovDefault);
        }

        if (ovDefaultResult is { } ovDef && ovFp32Result is { } ovFp32)
        {
            CompareResults("OpenVINO-CPU(默认)", ovDef, "OpenVINO-CPU(FP32)", ovFp32);
        }

        if (cudaResult is { } cuda2 && cpuOnlyResult is { } cpuOnly)
        {
            CompareResults("CUDA", cuda2, "CPU(ORT默认)", cpuOnly);
        }
    }

    /// <summary>
    /// 关键诊断测试：直接读取 CUDA 与 CPU 推理后的原始输出张量（未解码），
    /// 输出张量的形状、值范围、NaN/Inf 计数、前 N 个值。
    /// 用于判断差异来自推理阶段还是后处理阶段。
    /// </summary>
    [Fact]
    public void Diagnose_RawTensorOutput_CudaVsCpu()
    {
        if (!File.Exists(ModelPath) || !File.Exists(ImagePath))
        {
            _output.WriteLine("跳过：模型或图片不存在");
            return;
        }

        var configuration = new YoloConfiguration
        {
            Confidence = 0.3f,
            IoU = 0.45f,
            KeepAspectRatio = true,
            ApplyAutoOrient = false,
        };

        using var baseImage = Image.Load<Rgb24>(ImagePath);
        _output.WriteLine($"图像: {ImagePath} ({baseImage.Width}x{baseImage.Height})");
        _output.WriteLine(string.Empty);

        // CUDA 原始张量
        var cudaRaw = TryGetRawOutput("CUDA", () => new YoloPredictor(ModelPath, new YoloPredictorOptions
        {
            UseCuda = true,
            CudaDeviceId = 0,
            Configuration = configuration,
        }), baseImage.CloneAs<Rgb24>());

        // CPU 原始张量
        var cpuRaw = TryGetRawOutput("CPU(ORT默认)", () => new YoloPredictor(ModelPath, new YoloPredictorOptions
        {
            UseCuda = false,
            PreferOpenVinoCpuForDetection = false,
            Configuration = configuration,
        }), baseImage.CloneAs<Rgb24>());

        if (cudaRaw == null || cpuRaw == null)
        {
            _output.WriteLine("无法获取原始张量，至少一个后端不可用");
            return;
        }

        // 拷贝到普通 float 数组，避免 Span 不能在 lambda 中捕获
        var cudaArr = cudaRaw.Output0Copy;
        var cpuArr = cpuRaw.Output0Copy;
        var cudaOut1Arr = cudaRaw.Output1Copy;
        var cpuOut1Arr = cpuRaw.Output1Copy;
        var cudaDims = cudaRaw.Output0Dims;
        var cpuDims = cpuRaw.Output0Dims;

        _output.WriteLine(string.Empty);
        _output.WriteLine("=== 原始张量逐元素对比（Output0）===");

        var len = Math.Min(cudaArr.Length, cpuArr.Length);
        _output.WriteLine($"CUDA len={cudaArr.Length} dims={FormatShape(cudaDims)}, CPU len={cpuArr.Length} dims={FormatShape(cpuDims)}, 对比长度={len}");

        // 统计差异
        int cudaNaNCount = 0, cudaInfCount = 0, cudaZeroCount = 0;
        int cpuNaNCount = 0, cpuInfCount = 0, cpuZeroCount = 0;
        float cudaMin = float.MaxValue, cudaMax = float.MinValue, cudaSum = 0;
        float cpuMin = float.MaxValue, cpuMax = float.MinValue, cpuSum = 0;
        double totalAbsDiff = 0;
        float maxAbsDiff = 0;
        int maxDiffIndex = -1;
        int exactEqualCount = 0;

        for (int i = 0; i < len; i++)
        {
            var cv = cudaArr[i];
            var pv = cpuArr[i];

            if (float.IsNaN(cv)) cudaNaNCount++;
            if (float.IsInfinity(cv)) cudaInfCount++;
            if (cv == 0) cudaZeroCount++;
            else { if (cv < cudaMin) cudaMin = cv; if (cv > cudaMax) cudaMax = cv; cudaSum += cv; }

            if (float.IsNaN(pv)) cpuNaNCount++;
            if (float.IsInfinity(pv)) cpuInfCount++;
            if (pv == 0) cpuZeroCount++;
            else { if (pv < cpuMin) cpuMin = pv; if (pv > cpuMax) cpuMax = pv; cpuSum += pv; }

            var diff = Math.Abs(cv - pv);
            totalAbsDiff += diff;
            if (diff > maxAbsDiff) { maxAbsDiff = diff; maxDiffIndex = i; }
            if (cv == pv) exactEqualCount++;
        }

        var avgAbsDiff = totalAbsDiff / len;
        var cudaMean = cudaSum / len;
        var cpuMean = cpuSum / len;

        _output.WriteLine($"CUDA: NaN={cudaNaNCount}, Inf={cudaInfCount}, Zero={cudaZeroCount}, Min={(cudaNaNCount + cudaInfCount + cudaZeroCount == len ? 0 : cudaMin):F6}, Max={(cudaNaNCount + cudaInfCount + cudaZeroCount == len ? 0 : cudaMax):F6}, Mean={cudaMean:F6}");
        _output.WriteLine($"CPU:  NaN={cpuNaNCount}, Inf={cpuInfCount}, Zero={cpuZeroCount}, Min={(cpuNaNCount + cpuInfCount + cpuZeroCount == len ? 0 : cpuMin):F6}, Max={(cpuNaNCount + cpuInfCount + cpuZeroCount == len ? 0 : cpuMax):F6}, Mean={cpuMean:F6}");
        _output.WriteLine($"差异: 平均绝对差={avgAbsDiff:F6}, 最大绝对差={maxAbsDiff:F6} @idx={maxDiffIndex}, 完全相等元素={exactEqualCount}/{len} ({100.0 * exactEqualCount / len:F2}%)");

        // 输出前 30 个值，对比看
        _output.WriteLine(string.Empty);
        _output.WriteLine("前 30 个元素对比 (idx: CUDA vs CPU | diff):");
        for (int i = 0; i < Math.Min(30, len); i++)
        {
            _output.WriteLine($"  [{i,4}] {cudaArr[i],12:F6} vs {cpuArr[i],12:F6}  Δ={Math.Abs(cudaArr[i] - cpuArr[i]):F6}");
        }

        // 输出最大值位置及其索引
        _output.WriteLine(string.Empty);
        _output.WriteLine("=== CUDA Output0 Top-10 最大值位置 ===");
        var cudaTopIndices = Enumerable.Range(0, len)
            .Select(i => (idx: i, val: cudaArr[i]))
            .Where(x => !float.IsNaN(x.val) && !float.IsInfinity(x.val))
            .OrderByDescending(x => x.val)
            .Take(10)
            .ToArray();
        foreach (var item in cudaTopIndices)
        {
            _output.WriteLine($"  idx={item.idx,6}: CUDA={item.val:F6}, CPU={cpuArr[item.idx]:F6}");
        }

        _output.WriteLine(string.Empty);
        _output.WriteLine("=== CPU Output0 Top-10 最大值位置 ===");
        var cpuTopIndices = Enumerable.Range(0, len)
            .Select(i => (idx: i, val: cpuArr[i]))
            .Where(x => !float.IsNaN(x.val) && !float.IsInfinity(x.val))
            .OrderByDescending(x => x.val)
            .Take(10)
            .ToArray();
        foreach (var item in cpuTopIndices)
        {
            _output.WriteLine($"  idx={item.idx,6}: CPU={item.val:F6}, CUDA={cudaArr[item.idx]:F6}");
        }

        // Output1（mask 系数）对比
        if (cudaOut1Arr != null && cpuOut1Arr != null)
        {
            _output.WriteLine(string.Empty);
            _output.WriteLine("=== Output1 (Mask Protos) 对比 ===");
            var len1 = Math.Min(cudaOut1Arr.Length, cpuOut1Arr.Length);
            _output.WriteLine($"CUDA len={cudaOut1Arr.Length}, CPU len={cpuOut1Arr.Length}, 对比长度={len1}");

            float c1Min = float.MaxValue, c1Max = float.MinValue, c1Sum = 0;
            float p1Min = float.MaxValue, p1Max = float.MinValue, p1Sum = 0;
            double totalDiff1 = 0; float maxDiff1 = 0;
            for (int i = 0; i < len1; i++)
            {
                var cv = cudaOut1Arr[i]; var pv = cpuOut1Arr[i];
                if (!float.IsNaN(cv) && !float.IsInfinity(cv)) { if (cv < c1Min) c1Min = cv; if (cv > c1Max) c1Max = cv; c1Sum += cv; }
                if (!float.IsNaN(pv) && !float.IsInfinity(pv)) { if (pv < p1Min) p1Min = pv; if (pv > p1Max) p1Max = pv; p1Sum += pv; }
                var d = Math.Abs(cv - pv);
                totalDiff1 += d;
                if (d > maxDiff1) maxDiff1 = d;
            }
            _output.WriteLine($"CUDA: Min={c1Min:F6}, Max={c1Max:F6}, Mean={c1Sum / len1:F6}");
            _output.WriteLine($"CPU:  Min={p1Min:F6}, Max={p1Max:F6}, Mean={p1Sum / len1:F6}");
            _output.WriteLine($"差异: 平均绝对差={totalDiff1 / len1:F6}, 最大绝对差={maxDiff1:F6}");
        }

        // === 关键诊断：按通道分析 Output0 ===
        // YOLOv8 seg 模型 output0 形状 [1, 37, 6720]：通道 0-3 = bbox(cx,cy,w,h)，
        // 通道 4 = class score (单类)，通道 5-36 = mask coefficients
        // 解码器在 (channel+4)*boxStride + boxIndex 处读取类别分数
        AnalyzeChannels(cudaArr, cpuArr, cudaDims, "Output0");
    }

    /// <summary>
    /// 按通道分析 Output0，重点查看类别分数通道（channel 4）的统计信息。
    /// 这是过滤检测框的关键通道 —— 决定每个 anchor 是否能进入候选集。
    /// </summary>
    private void AnalyzeChannels(float[] cudaArr, float[] cpuArr, int[] dims, string label)
    {
        _output.WriteLine(string.Empty);
        _output.WriteLine($"=== {label} 按通道分析 ===");
        _output.WriteLine($"形状: {FormatShape(dims)}");

        if (dims.Length < 3)
        {
            _output.WriteLine("  通道分析跳过：维度不足 3");
            return;
        }

        var channels = dims[1];
        var anchors = dims[2];
        _output.WriteLine($"  通道数={channels}, anchor 数={anchors}");

        // 对每个通道（前 6 个 + 后面采样）输出统计
        _output.WriteLine($"  通道    | CUDA Min/Max/Mean           | CPU Min/Max/Mean             | >0.3 数量 CUDA/CPU");
        for (int c = 0; c < Math.Min(channels, 6); c++)
        {
            AnalyzeChannel(cudaArr, cpuArr, c, anchors);
        }

        // 也输出 mask coefficient 通道 (channel 5-10)
        _output.WriteLine($"  --- Mask coefficient 通道采样 ---");
        for (int c = 5; c < Math.Min(channels, 11); c++)
        {
            AnalyzeChannel(cudaArr, cpuArr, c, anchors);
        }

        // === 重点：channel 4（类别分数）的详细分析 ===
        // 这是 decoder 实际过滤检测框使用的通道
        const int classChannel = 4;
        _output.WriteLine(string.Empty);
        _output.WriteLine($"=== 通道 {classChannel}（类别分数）详细分析 ===");
        var cudaScores = new float[anchors];
        var cpuScores = new float[anchors];
        for (int i = 0; i < anchors; i++)
        {
            cudaScores[i] = cudaArr[(classChannel) * anchors + i];
            cpuScores[i] = cpuArr[(classChannel) * anchors + i];
        }

        int cudaAbove03 = cudaScores.Count(v => v > 0.3f);
        int cpuAbove03 = cpuScores.Count(v => v > 0.3f);
        int cudaAbove1 = cudaScores.Count(v => v > 1.0f);
        int cpuAbove1 = cpuScores.Count(v => v > 1.0f);
        int cudaAbove5 = cudaScores.Count(v => v > 5.0f);
        int cpuAbove5 = cpuScores.Count(v => v > 5.0f);

        // sigmoid 后
        int cudaSigmoidAbove03 = cudaScores.Count(v => 1f / (1 + MathF.Exp(-v)) > 0.3f);
        int cpuSigmoidAbove03 = cpuScores.Count(v => 1f / (1 + MathF.Exp(-v)) > 0.3f);
        int cudaSigmoidAbove05 = cudaScores.Count(v => 1f / (1 + MathF.Exp(-v)) > 0.5f);
        int cpuSigmoidAbove05 = cpuScores.Count(v => 1f / (1 + MathF.Exp(-v)) > 0.5f);

        _output.WriteLine($"  原始值: CUDA >0.3={cudaAbove03}, >1.0={cudaAbove1}, >5.0={cudaAbove5}");
        _output.WriteLine($"  原始值: CPU  >0.3={cpuAbove03}, >1.0={cpuAbove1}, >5.0={cpuAbove5}");
        _output.WriteLine($"  Sigmoid 后: CUDA >0.3={cudaSigmoidAbove03}, >0.5={cudaSigmoidAbove05}");
        _output.WriteLine($"  Sigmoid 后: CPU  >0.3={cpuSigmoidAbove03}, >0.5={cpuSigmoidAbove05}");

        // 找出 CPU top-10 类别分数的 anchor 索引，看 CUDA 在这些 anchor 上的分数
        _output.WriteLine(string.Empty);
        _output.WriteLine($"  CPU Top-10 类别分数 anchor 索引（含 CUDA 对比值）:");
        var cpuTopAnchors = Enumerable.Range(0, anchors)
            .Select(i => (idx: i, val: cpuScores[i]))
            .OrderByDescending(x => x.val)
            .Take(10)
            .ToArray();
        foreach (var item in cpuTopAnchors)
        {
            var sigmoidCpu = 1f / (1 + MathF.Exp(-item.val));
            var sigmoidCuda = 1f / (1 + MathF.Exp(-cudaScores[item.idx]));
            _output.WriteLine($"    anchor={item.idx,5}: CUDA={cudaScores[item.idx],10:F4}(σ={sigmoidCuda:F4})  CPU={item.val,10:F4}(σ={sigmoidCpu:F4})  Δ={Math.Abs(cudaScores[item.idx] - item.val):F4}");
        }

        // CUDA top-10
        _output.WriteLine(string.Empty);
        _output.WriteLine($"  CUDA Top-10 类别分数 anchor 索引（含 CPU 对比值）:");
        var cudaTopAnchors = Enumerable.Range(0, anchors)
            .Select(i => (idx: i, val: cudaScores[i]))
            .OrderByDescending(x => x.val)
            .Take(10)
            .ToArray();
        foreach (var item in cudaTopAnchors)
        {
            var sigmoidCpu = 1f / (1 + MathF.Exp(-cpuScores[item.idx]));
            var sigmoidCuda = 1f / (1 + MathF.Exp(-item.val));
            _output.WriteLine($"    anchor={item.idx,5}: CUDA={item.val,10:F4}(σ={sigmoidCuda:F4})  CPU={cpuScores[item.idx],10:F4}(σ={sigmoidCpu:F4})  Δ={Math.Abs(item.val - cpuScores[item.idx]):F4}");
        }
    }

    private void AnalyzeChannel(float[] cudaArr, float[] cpuArr, int channel, int anchors)
    {
        float cudaMin = float.MaxValue, cudaMax = float.MinValue, cudaSum = 0;
        float cpuMin = float.MaxValue, cpuMax = float.MinValue, cpuSum = 0;
        int cudaAbove03 = 0, cpuAbove03 = 0;

        for (int i = 0; i < anchors; i++)
        {
            var cv = cudaArr[channel * anchors + i];
            var pv = cpuArr[channel * anchors + i];
            if (cv < cudaMin) cudaMin = cv;
            if (cv > cudaMax) cudaMax = cv;
            cudaSum += cv;
            if (cv > 0.3f) cudaAbove03++;
            if (pv < cpuMin) cpuMin = pv;
            if (pv > cpuMax) cpuMax = pv;
            cpuSum += pv;
            if (pv > 0.3f) cpuAbove03++;
        }

        _output.WriteLine($"  ch={channel,2}    | CUDA [{cudaMin,10:F3}, {cudaMax,10:F3}] μ={cudaSum / anchors,8:F3} | CPU [{cpuMin,10:F3}, {cpuMax,10:F3}] μ={cpuSum / anchors,8:F3} | >0.3: {cudaAbove03,5}/{cpuAbove03,5}");
    }

    private YoloResult<Segmentation>? TryRun(string label, Func<YoloPredictor> factory, Image<Rgb24> baseImage)
    {
        _output.WriteLine($"--- {label} ---");
        try
        {
            using var predictor = factory();
            _output.WriteLine($"  Metadata.Task={predictor.Metadata.Task}, ImageSize={predictor.Metadata.ImageSize.Width}x{predictor.Metadata.ImageSize.Height}");

            // 每次推理使用克隆图像，避免 AutoOrient 副作用
            using var image = baseImage.CloneAs<Rgb24>();
            var result = predictor.Segment(image);

            _output.WriteLine($"  检测框数={result.Count}");
            _output.WriteLine($"  耗时: Pre={result.Speed.Preprocess.TotalMilliseconds:F2}ms, Infer={result.Speed.Inference.TotalMilliseconds:F2}ms, Post={result.Speed.Postprocess.TotalMilliseconds:F2}ms");

            var idx = 0;
            foreach (var seg in result)
            {
                _output.WriteLine($"    [{idx}] Name={seg.Name.Name}, Conf={seg.Confidence:F6}, Bounds=({seg.Bounds.X},{seg.Bounds.Y},{seg.Bounds.Width}x{seg.Bounds.Height})");
                idx++;
            }

            return result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or OnnxRuntimeException)
        {
            _output.WriteLine($"  后端不可用: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return null;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  推理失败: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return null;
        }
    }

    /// <summary>
    /// 直接调用 SessionRunner.PreprocessAndRun 获取未解码的原始张量输出。
    /// 返回的 RawOutput 中包含 Output0 与 Output1（如果存在）的数组拷贝以及维度信息。
    /// </summary>
    private RawOutput? TryGetRawOutput(
        string label, Func<YoloPredictor> factory, Image<Rgb24> image)
    {
        _output.WriteLine($"--- {label} (raw tensor) ---");
        try
        {
            using var predictor = factory();
            var runner = predictor.ResolveService<ISessionRunner>(null);

            _output.WriteLine($"  InputSize={predictor.Metadata.ImageSize}, Task={predictor.Metadata.Task}");

            // SessionRunner 暴露 IoShapeInfo 与 Session（ISessionRunner 接口未包含）
            var sessionRunner = (SessionRunner)runner;
            var shapeInfo = sessionRunner.IoShapeInfo;
            _output.WriteLine($"  Input0={FormatShape(shapeInfo.Input0.Dimensions)}");
            _output.WriteLine($"  Output0={FormatShape(shapeInfo.Output0.Dimensions)}");
            if (shapeInfo.Output1 != null)
            {
                _output.WriteLine($"  Output1={FormatShape(shapeInfo.Output1.Value.Dimensions)}");
            }

            // 推理
            using var output = runner.PreprocessAndRun(image, out var timer);
            // PredictAndRun 只记录了 pre 和 infer，需要调用 StartPostprocess + Stop 才能取出 SpeedResult
            timer.StartPostprocess();
            var speed = timer.Stop();
            _output.WriteLine($"  耗时: Pre={speed.Preprocess.TotalMilliseconds:F2}ms, Infer={speed.Inference.TotalMilliseconds:F2}ms");

            // 复制原始数据，避免后续 Dispose 后访问
            var output0Arr = output.Output0.Span.ToArray();
            var output0Dims = shapeInfo.Output0.Dimensions.ToArray();
            float[]? output1Arr = output.Output1?.Span.ToArray();
            int[]? output1Dims = shapeInfo.Output1 != null ? shapeInfo.Output1.Value.Dimensions.ToArray() : null;

            _output.WriteLine($"  Output0 实际长度={output0Arr.Length}");
            if (output1Arr != null)
            {
                _output.WriteLine($"  Output1 实际长度={output1Arr.Length}");
            }

            return new RawOutput
            {
                Output0Copy = output0Arr,
                Output0Dims = output0Dims,
                Output1Copy = output1Arr,
                Output1Dims = output1Dims,
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or NotSupportedException or OnnxRuntimeException)
        {
            _output.WriteLine($"  后端不可用: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return null;
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  推理失败: {ex.GetType().Name}: {ex.Message}");
            _output.WriteLine($"  堆栈: {ex.StackTrace}");
            return null;
        }
    }

    private sealed class RawOutput
    {
        public float[] Output0Copy = System.Array.Empty<float>();
        public int[] Output0Dims = System.Array.Empty<int>();
        public float[]? Output1Copy;
        public int[]? Output1Dims;
    }

    private static string FormatShape(IReadOnlyList<int> dims) => $"[{string.Join(", ", dims)}]";

    private void CompareResults(string labelA, YoloResult<Segmentation> a, string labelB, YoloResult<Segmentation> b)
    {
        _output.WriteLine($"--- {labelA} vs {labelB} ---");
        _output.WriteLine($"  框数: {labelA}={a.Count}, {labelB}={b.Count}, 差={a.Count - b.Count}");

        var maxCount = Math.Max(a.Count, b.Count);
        var minCount = Math.Min(a.Count, b.Count);

        if (maxCount == 0)
        {
            _output.WriteLine("  两侧均无检测框");
            return;
        }

        // 简单对应：按索引顺序对比（已按置信度排序）
        for (var i = 0; i < maxCount; i++)
        {
            if (i < minCount)
            {
                var ea = a[i];
                var eb = b[i];

                var confDiff = Math.Abs(ea.Confidence - eb.Confidence);
                var xDiff = Math.Abs(ea.Bounds.X - eb.Bounds.X);
                var yDiff = Math.Abs(ea.Bounds.Y - eb.Bounds.Y);
                var wDiff = Math.Abs(ea.Bounds.Width - eb.Bounds.Width);
                var hDiff = Math.Abs(ea.Bounds.Height - eb.Bounds.Height);

                _output.WriteLine($"  [{i}] Conf={ea.Confidence:F6} vs {eb.Confidence:F6} (Δ={confDiff:F6})");
                _output.WriteLine($"       Bounds Δ: X={xDiff:F2}, Y={yDiff:F2}, W={wDiff:F2}, H={hDiff:F2}");
                _output.WriteLine($"       Name: {ea.Name.Name} vs {eb.Name.Name}");
            }
            else if (i < a.Count)
            {
                _output.WriteLine($"  [{i}] 仅在 {labelA} 中存在: Conf={a[i].Confidence:F6}, Bounds={a[i].Bounds}");
            }
            else if (i < b.Count)
            {
                _output.WriteLine($"  [{i}] 仅在 {labelB} 中存在: Conf={b[i].Confidence:F6}, Bounds={b[i].Bounds}");
            }
        }
    }
}
