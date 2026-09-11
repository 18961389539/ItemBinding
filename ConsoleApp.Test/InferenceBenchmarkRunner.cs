using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Contracts.Services;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Decoders.Base;
using JinlongYolo.YoloSharp.Memory;
using JinlongYolo.YoloSharp.Metadata;
using JinlongYolo.YoloSharp.Services;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using Size = SixLabors.ImageSharp.Size;
using Point = SixLabors.ImageSharp.Point;
using RectangleF = SixLabors.ImageSharp.RectangleF;
using Image = SixLabors.ImageSharp.Image;

namespace ConsoleApp.Test;

internal static class InferenceBenchmarkRunner
{
    private sealed class BenchmarkOptions
    {
        public string Mode { get; init; } = "detect";

        public string Provider { get; init; } = "cpu";

        public bool CompareOpenVino { get; init; }

        public string? ModelPath { get; init; }

        public string? ImageDirectory { get; init; }

        public int Warmup { get; init; } = 5;

        public int Iterations { get; init; } = 30;

        public int ImageCount { get; init; } = 12;

        public int? IntraOpThreads { get; init; }

        public int? InterOpThreads { get; init; }

        public string ExecutionMode { get; init; } = "sequential";

        public string GraphOptimization { get; init; } = "all";

        public bool KeepAspectRatio { get; init; } = true;

        public int MaximumCandidateBoxes { get; init; } = 1024;

        public int MaximumDetections { get; init; } = 300;

        public int NmsCandidateBoxes { get; init; } = 1024;

        public int NmsLabels { get; init; } = 8;

        public float NmsIoUThreshold { get; init; } = 0.45f;

        public int NmsSeed { get; init; } = 42;
    }

    private sealed class BenchmarkSummary
    {
        public required string Provider { get; init; }

        public required string Task { get; init; }

        public double StartupMilliseconds { get; init; }

        public required ThroughputSummary SteadyState { get; init; }

        public required PhaseAggregate Legacy { get; init; }

        public required PhaseAggregate Optimized { get; init; }
    }

    private sealed class ThroughputSummary
    {
        public required int ImageCount { get; init; }

        public double TotalMilliseconds { get; init; }

        public double ImagesPerSecond => TotalMilliseconds <= 0d ? 0d : ImageCount / (TotalMilliseconds / 1000d);

        public double MeanMillisecondsPerImage => ImageCount == 0 ? 0d : TotalMilliseconds / ImageCount;
    }

    private sealed class PhaseAggregate
    {
        private readonly List<double> _preprocess = [];
        private readonly List<double> _inference = [];
        private readonly List<double> _postprocess = [];
        private readonly List<double> _total = [];
        private readonly List<double> _resize = [];
        private readonly List<double> _normalize = [];

        public void Add(TimeSpan preprocess, TimeSpan inference, TimeSpan postprocess, TimeSpan? resize = null, TimeSpan? normalize = null)
        {
            _preprocess.Add(preprocess.TotalMilliseconds);
            _inference.Add(inference.TotalMilliseconds);
            _postprocess.Add(postprocess.TotalMilliseconds);
            _total.Add((preprocess + inference + postprocess).TotalMilliseconds);

            if (resize.HasValue)
            {
                _resize.Add(resize.Value.TotalMilliseconds);
            }

            if (normalize.HasValue)
            {
                _normalize.Add(normalize.Value.TotalMilliseconds);
            }
        }

        public double MeanPreprocess => Average(_preprocess);

        public double MeanInference => Average(_inference);

        public double MeanPostprocess => Average(_postprocess);

        public double MeanTotal => Average(_total);

        public double MeanResize => Average(_resize);

        public double MeanNormalize => Average(_normalize);

        public string Format(string label)
        {
            var lines = new List<string>
            {
                $"{label} preprocess: {MeanPreprocess:F3} ms",
                $"{label} inference: {MeanInference:F3} ms",
                $"{label} postprocess: {MeanPostprocess:F3} ms",
                $"{label} total: {MeanTotal:F3} ms"
            };

            if (_resize.Count > 0)
            {
                lines.Add($"{label} resize: {MeanResize:F3} ms");
            }

            if (_normalize.Count > 0)
            {
                lines.Add($"{label} normalize: {MeanNormalize:F3} ms");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static double Average(List<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            return values.Sum() / values.Count;
        }
    }

    private sealed class ElapsedAggregate
    {
        private readonly List<double> _elapsed = [];

        public void Add(TimeSpan elapsed)
        {
            _elapsed.Add(elapsed.TotalMilliseconds);
        }

        public double Mean => _elapsed.Count == 0 ? 0d : _elapsed.Sum() / _elapsed.Count;
    }

    public static int Run(string[] args)
    {
        var options = Parse(args);

        if (options.Mode.Equals("nms", StringComparison.OrdinalIgnoreCase))
        {
            return RunNmsBenchmark(options);
        }

        var repoRoot = FindRepoRoot();
        var modelPath = options.ModelPath ?? Path.Combine(repoRoot, "ConsoleApp.Test", "best.onnx");
        var imageDirectory = options.ImageDirectory ?? Path.Combine(repoRoot, "Pictures", "fsdasd", "20260316");

        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"Model not found: {modelPath}");
            return 1;
        }

        if (!Directory.Exists(imageDirectory))
        {
            Console.Error.WriteLine($"Image directory not found: {imageDirectory}");
            return 1;
        }

        var images = LoadImages(imageDirectory, options.ImageCount);

        if (images.Count == 0)
        {
            Console.Error.WriteLine($"No benchmark images found in: {imageDirectory}");
            return 1;
        }

        try
        {
            if (options.CompareOpenVino)
            {
                return RunProviderComparison(modelPath, images, options);
            }

            return options.Mode.ToLowerInvariant() switch
            {
                "detect" => RunDetectionBenchmark(modelPath, images, options),
                "segment" => RunSegmentationBenchmark(modelPath, images, options),
                _ => PrintUsageAndFail(options.Mode)
            };
        }
        finally
        {
            foreach (var image in images)
            {
                image.Dispose();
            }
        }
    }

    private static int RunDetectionBenchmark(string modelPath, IReadOnlyList<Image<Rgb24>> images, BenchmarkOptions options)
    {
        var summary = RunDetectionBenchmarkCore(modelPath, images, options);

        PrintSummary(modelPath, images.Count, options, summary);

        return 0;
    }

    private static BenchmarkSummary RunDetectionBenchmarkCore(string modelPath, IReadOnlyList<Image<Rgb24>> images, BenchmarkOptions options)
    {
        var configuration = CreateBenchmarkConfiguration(options);

        PrepareProviderEnvironment(options.Provider);

        var startupWatch = Stopwatch.StartNew();

        using var predictor = new YoloPredictor(modelPath, CreatePredictorOptions(configuration, options));
        startupWatch.Stop();

        if (predictor.Metadata.Task is not (YoloTask.Detect or YoloTask.Segment))
        {
            throw new InvalidOperationException($"Detection benchmark requires a detect or segment model, but got: {predictor.Metadata.Task}");
        }

        var runner = (SessionRunner)predictor.ResolveService<ISessionRunner>(configuration);
        var allocator = predictor.ResolveService<IMemoryAllocator>(configuration);
        var normalizer = predictor.ResolveService<IPixelsNormalizer>(configuration);
        var transformer = predictor.ResolveService<IBoundingBoxTransformer>(configuration);
        var boxDecoder = predictor.ResolveService<IBoundingBoxDecoder>(configuration);
        var nonMaxSuppression = predictor.ResolveService<INonMaxSuppression>(configuration);
        var yoloSession = predictor.ResolveService<YoloSession>(configuration);

        WarmupDetection(predictor.Metadata, configuration, images, options.Warmup, allocator, normalizer, transformer, boxDecoder, nonMaxSuppression, runner, HasExplicitEndToEnd(yoloSession));

    var steadyState = MeasureDetectionThroughput(images, options.Iterations, predictor.Metadata, configuration, allocator, normalizer, transformer, boxDecoder, runner);

        var legacy = new PhaseAggregate();
        var optimized = new PhaseAggregate();

        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            var image = images[iteration % images.Count];

            BenchmarkLegacyDetection(image, predictor.Metadata, configuration, allocator, normalizer, transformer, nonMaxSuppression, yoloSession, runner, legacy);

            BenchmarkOptimizedDetection(image, predictor.Metadata, configuration, allocator, normalizer, transformer, boxDecoder, runner, optimized);
        }

        return new BenchmarkSummary
        {
            Provider = options.Provider,
            Task = "Detection",
            StartupMilliseconds = startupWatch.Elapsed.TotalMilliseconds,
            SteadyState = steadyState,
            Legacy = legacy,
            Optimized = optimized,
        };
    }

    private static int RunSegmentationBenchmark(string modelPath, IReadOnlyList<Image<Rgb24>> images, BenchmarkOptions options)
    {
        var summary = RunSegmentationBenchmarkCore(modelPath, images, options);

        PrintSummary(modelPath, images.Count, options, summary);

        return 0;
    }

    private static BenchmarkSummary RunSegmentationBenchmarkCore(string modelPath, IReadOnlyList<Image<Rgb24>> images, BenchmarkOptions options)
    {
        var configuration = CreateBenchmarkConfiguration(options);

        PrepareProviderEnvironment(options.Provider);

        var startupWatch = Stopwatch.StartNew();

        using var predictor = new YoloPredictor(modelPath, CreatePredictorOptions(configuration, options));
        startupWatch.Stop();

        if (predictor.Metadata.Task != YoloTask.Segment)
        {
            throw new InvalidOperationException($"Segmentation benchmark requires a segment model, but got: {predictor.Metadata.Task}");
        }

        var runner = (SessionRunner)predictor.ResolveService<ISessionRunner>(configuration);
        var allocator = predictor.ResolveService<IMemoryAllocator>(configuration);
        var normalizer = predictor.ResolveService<IPixelsNormalizer>(configuration);
        var decoder = predictor.ResolveService<IDecoder<Segmentation>>(configuration);
        var transformer = predictor.ResolveService<IBoundingBoxTransformer>(configuration);
        var nonMaxSuppression = predictor.ResolveService<INonMaxSuppression>(configuration);
        var yoloSession = predictor.ResolveService<YoloSession>(configuration);

        WarmupSegmentation(predictor.Metadata, configuration, images, options.Warmup, allocator, normalizer, decoder, runner);

    var steadyState = MeasureSegmentationThroughput(images, options.Iterations, predictor.Metadata, configuration, allocator, normalizer, decoder, runner);

        var legacy = new PhaseAggregate();
        var optimized = new PhaseAggregate();

        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            var image = images[iteration % images.Count];

            BenchmarkLegacySegmentation(image, predictor.Metadata, configuration, allocator, normalizer, transformer, nonMaxSuppression, yoloSession, runner, legacy);

            BenchmarkOptimizedSegmentation(image, predictor.Metadata, configuration, allocator, normalizer, decoder, runner, optimized);
        }

        return new BenchmarkSummary
        {
            Provider = options.Provider,
            Task = "Segmentation",
            StartupMilliseconds = startupWatch.Elapsed.TotalMilliseconds,
            SteadyState = steadyState,
            Legacy = legacy,
            Optimized = optimized,
        };
    }

    private static int RunProviderComparison(string modelPath, IReadOnlyList<Image<Rgb24>> images, BenchmarkOptions options)
    {
        var providers = new[] { "cpu", "openvino-cpu", "openvino-gpu" };
        var results = new List<BenchmarkSummary>(providers.Length);

        foreach (var provider in providers)
        {
            var providerOptions = WithProvider(options, provider, false);

            try
            {
                var summary = options.Mode.Equals("segment", StringComparison.OrdinalIgnoreCase)
                    ? RunSegmentationBenchmarkCore(modelPath, images, providerOptions)
                    : RunDetectionBenchmarkCore(modelPath, images, providerOptions);

                results.Add(summary);
                PrintSummary(modelPath, images.Count, providerOptions, summary);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{provider} benchmark failed: {ex.Message}");

                if (provider.Equals("cpu", StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }
            }
        }

        if (results.Count > 0)
        {
            PrintProviderComparison(results);
        }

        return results.Count > 1 ? 0 : 2;
    }

    private static int RunNmsBenchmark(BenchmarkOptions options)
    {
        var baseline = GenerateNmsBoxes(options.NmsCandidateBoxes, options.NmsLabels, options.NmsSeed);
        var optimizedNms = new NonMaxSuppression();
        var legacy = new ElapsedAggregate();
        var optimized = new ElapsedAggregate();
        var legacyCount = 0;
        var optimizedCount = 0;

        for (var index = 0; index < options.Warmup; index++)
        {
            var warmupInput = baseline.ToArray();
            _ = LegacyApplyNms(warmupInput.AsSpan(), options.NmsIoUThreshold);

            warmupInput = baseline.ToArray();
            _ = optimizedNms.Apply(warmupInput.AsSpan(), options.NmsIoUThreshold);
        }

        for (var iteration = 0; iteration < options.Iterations; iteration++)
        {
            var legacyInput = baseline.ToArray();
            var legacyWatch = Stopwatch.StartNew();
            legacyCount = LegacyApplyNms(legacyInput.AsSpan(), options.NmsIoUThreshold).Length;
            legacyWatch.Stop();
            legacy.Add(legacyWatch.Elapsed);

            var optimizedInput = baseline.ToArray();
            var optimizedWatch = Stopwatch.StartNew();
            optimizedCount = optimizedNms.Apply(optimizedInput.AsSpan(), options.NmsIoUThreshold).Length;
            optimizedWatch.Stop();
            optimized.Add(optimizedWatch.Elapsed);
        }

        Console.WriteLine($"NMS benchmark candidates: {options.NmsCandidateBoxes}, labels: {options.NmsLabels}, IoU: {options.NmsIoUThreshold:F2}");
        Console.WriteLine($"Legacy NMS: {legacy.Mean:F3} ms, kept {legacyCount} boxes");
        Console.WriteLine($"Optimized NMS: {optimized.Mean:F3} ms, kept {optimizedCount} boxes");
        Console.WriteLine($"NMS speedup: {legacy.Mean / optimized.Mean:F2}x");

        return 0;
    }

    private static BenchmarkOptions WithProvider(BenchmarkOptions options, string provider, bool compareOpenVino)
    {
        return new BenchmarkOptions
        {
            Mode = options.Mode,
            Provider = provider,
            CompareOpenVino = compareOpenVino,
            ModelPath = options.ModelPath,
            ImageDirectory = options.ImageDirectory,
            Warmup = options.Warmup,
            Iterations = options.Iterations,
            ImageCount = options.ImageCount,
            IntraOpThreads = options.IntraOpThreads,
            InterOpThreads = options.InterOpThreads,
            ExecutionMode = options.ExecutionMode,
            GraphOptimization = options.GraphOptimization,
            KeepAspectRatio = options.KeepAspectRatio,
            MaximumCandidateBoxes = options.MaximumCandidateBoxes,
            MaximumDetections = options.MaximumDetections,
            NmsCandidateBoxes = options.NmsCandidateBoxes,
            NmsLabels = options.NmsLabels,
            NmsIoUThreshold = options.NmsIoUThreshold,
            NmsSeed = options.NmsSeed,
        };
    }

    private static RawBoundingBox[] GenerateNmsBoxes(int count, int labels, int seed)
    {
        if (count <= 0)
        {
            return [];
        }

        var random = new Random(seed);
        var safeLabels = Math.Max(1, labels);
        var boxes = new RawBoundingBox[count];
        var clustersPerLabel = Math.Max(1, count / (safeLabels * 8));

        for (var index = 0; index < count; index++)
        {
            var label = index % safeLabels;
            var cluster = (index / safeLabels) % clustersPerLabel;
            var centerX = 40f + (cluster % 16) * 18f + label * 2f;
            var centerY = 40f + (cluster / 16) * 18f + label * 2f;
            var width = 28f + (float)(random.NextDouble() * 18d);
            var height = 28f + (float)(random.NextDouble() * 18d);
            var jitterX = (float)((random.NextDouble() - 0.5d) * width * 0.35d);
            var jitterY = (float)((random.NextDouble() - 0.5d) * height * 0.35d);

            boxes[index] = new RawBoundingBox
            {
                Index = index,
                NameIndex = label,
                Confidence = 1f - ((float)index / (count + 1)),
                Bounds = new RectangleF(centerX + jitterX, centerY + jitterY, width, height),
            };
        }

        return boxes;
    }

    private static RawBoundingBox[] LegacyApplyNms(Span<RawBoundingBox> boxes, float iouThreshold)
    {
        if (boxes.Length == 0)
        {
            return [];
        }

        boxes.Sort(static (x, y) => y.CompareTo(x));

        var result = new List<RawBoundingBox>(8)
        {
            boxes[0]
        };

        for (var i = 1; i < boxes.Length; i++)
        {
            var candidate = boxes[i];
            var addToResult = true;

            for (var j = 0; j < result.Count; j++)
            {
                var selected = result[j];

                if (candidate.NameIndex != selected.NameIndex)
                {
                    continue;
                }

                if (CalculateIoU(candidate, selected) > iouThreshold)
                {
                    addToResult = false;
                    break;
                }
            }

            if (addToResult)
            {
                result.Add(candidate);
            }
        }

        return [.. result];
    }

    private static float CalculateIoU(RawBoundingBox box1, RawBoundingBox box2)
    {
        var rect1 = box1.Bounds;
        var rect2 = box2.Bounds;
        var area1 = rect1.Width * rect1.Height;

        if (area1 <= 0f)
        {
            return 0f;
        }

        var area2 = rect2.Width * rect2.Height;

        if (area2 <= 0f)
        {
            return 0f;
        }

        var intersection = RectangleF.Intersect(rect1, rect2);
        var intersectionArea = intersection.Width * intersection.Height;

        return intersectionArea / (area1 + area2 - intersectionArea);
    }

    private static void PrintSummary(string modelPath, int imageCount, BenchmarkOptions options, BenchmarkSummary summary)
    {
        Console.WriteLine($"{summary.Task} benchmark model: {modelPath}");
        Console.WriteLine($"{summary.Task} benchmark images: {imageCount}");
        PrintBenchmarkOptions(options, CreateBenchmarkConfiguration(options));
        Console.WriteLine($"Startup: {summary.StartupMilliseconds:F3} ms");
        Console.WriteLine($"Steady-state throughput: {summary.SteadyState.ImagesPerSecond:F2} images/s ({summary.SteadyState.MeanMillisecondsPerImage:F3} ms/image over {summary.SteadyState.ImageCount} images)");
        Console.WriteLine(summary.Legacy.Format("Legacy"));
        Console.WriteLine(summary.Optimized.Format("Optimized"));
        Console.WriteLine($"{summary.Task} speedup: {summary.Legacy.MeanTotal / summary.Optimized.MeanTotal:F2}x");
    }

    private static void PrintProviderComparison(IReadOnlyList<BenchmarkSummary> summaries)
    {
        Console.WriteLine("Provider startup and throughput comparison:");

        foreach (var summary in summaries)
        {
            Console.WriteLine($"- {summary.Provider}: startup {summary.StartupMilliseconds:F3} ms, throughput {summary.SteadyState.ImagesPerSecond:F2} images/s, optimized total {summary.Optimized.MeanTotal:F3} ms, optimized inference {summary.Optimized.MeanInference:F3} ms");
        }

        var cpuSummary = summaries.FirstOrDefault(summary => summary.Provider.Equals("cpu", StringComparison.OrdinalIgnoreCase));

        if (cpuSummary is null)
        {
            return;
        }

        foreach (var summary in summaries.Where(summary => !summary.Provider.Equals("cpu", StringComparison.OrdinalIgnoreCase)))
        {
            var throughputSpeedup = summary.SteadyState.ImagesPerSecond / cpuSummary.SteadyState.ImagesPerSecond;
            var optimizedLatencySpeedup = cpuSummary.Optimized.MeanTotal / summary.Optimized.MeanTotal;
            var startupRatio = summary.StartupMilliseconds / cpuSummary.StartupMilliseconds;

            Console.WriteLine($"Compared with CPU, {summary.Provider} startup ratio: {startupRatio:F2}x, throughput speedup: {throughputSpeedup:F2}x, optimized total speedup: {optimizedLatencySpeedup:F2}x");
        }
    }

    private static ThroughputSummary MeasureDetectionThroughput(IReadOnlyList<Image<Rgb24>> images,
                                                                int cycles,
                                                                YoloMetadata metadata,
                                                                YoloConfiguration configuration,
                                                                IMemoryAllocator allocator,
                                                                IPixelsNormalizer normalizer,
                                                                IBoundingBoxTransformer transformer,
                                                                IBoundingBoxDecoder boxDecoder,
                                                                SessionRunner runner)
    {
        var cycleCount = Math.Max(1, cycles);
        var imageCount = 0;

        var watch = Stopwatch.StartNew();

        for (var cycle = 0; cycle < cycleCount; cycle++)
        {
            foreach (var image in images)
            {
                using var input = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);
                normalizer.ResizeAndNormalizeToTensor(image, input.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, out _);

                using var output = runner.RunPreparedInput(input.Tensor);
                _ = DecodeDetections(output, image.Size, metadata, transformer, boxDecoder);
                imageCount++;
            }
        }

        watch.Stop();

        return new ThroughputSummary
        {
            ImageCount = imageCount,
            TotalMilliseconds = watch.Elapsed.TotalMilliseconds,
        };
    }

    private static ThroughputSummary MeasureSegmentationThroughput(IReadOnlyList<Image<Rgb24>> images,
                                                                   int cycles,
                                                                   YoloMetadata metadata,
                                                                   YoloConfiguration configuration,
                                                                   IMemoryAllocator allocator,
                                                                   IPixelsNormalizer normalizer,
                                                                   IDecoder<Segmentation> decoder,
                                                                   SessionRunner runner)
    {
        var cycleCount = Math.Max(1, cycles);
        var imageCount = 0;

        var watch = Stopwatch.StartNew();

        for (var cycle = 0; cycle < cycleCount; cycle++)
        {
            foreach (var image in images)
            {
                using var input = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);
                normalizer.ResizeAndNormalizeToTensor(image, input.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, out _);

                using var output = runner.RunPreparedInput(input.Tensor);
                _ = decoder.Decode(output, image.Size);
                imageCount++;
            }
        }

        watch.Stop();

        return new ThroughputSummary
        {
            ImageCount = imageCount,
            TotalMilliseconds = watch.Elapsed.TotalMilliseconds,
        };
    }

    private static void BenchmarkLegacyDetection(Image<Rgb24> image,
                                                 YoloMetadata metadata,
                                                 YoloConfiguration configuration,
                                                 IMemoryAllocator allocator,
                                                 IPixelsNormalizer normalizer,
                                                 IBoundingBoxTransformer transformer,
                                                 INonMaxSuppression nonMaxSuppression,
                                                 YoloSession yoloSession,
                                                 SessionRunner runner,
                                                 PhaseAggregate aggregate)
    {
        using var input = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);

        LegacyPreprocess(image, input.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, normalizer, out var resize, out var normalize);

        var inferenceWatch = Stopwatch.StartNew();
        using var output = runner.RunPreparedInput(input.Tensor);
        inferenceWatch.Stop();

        var postWatch = Stopwatch.StartNew();
        _ = LegacyDecodeDetections(output, image.Size, metadata, configuration, transformer, nonMaxSuppression, HasExplicitEndToEnd(yoloSession));
        postWatch.Stop();

        aggregate.Add(resize + normalize, inferenceWatch.Elapsed, postWatch.Elapsed, resize, normalize);
    }

    private static void BenchmarkOptimizedDetection(Image<Rgb24> image,
                                                    YoloMetadata metadata,
                                                    YoloConfiguration configuration,
                                                    IMemoryAllocator allocator,
                                                    IPixelsNormalizer normalizer,
                                                    IBoundingBoxTransformer transformer,
                                                    IBoundingBoxDecoder boxDecoder,
                                                    SessionRunner runner,
                                                    PhaseAggregate aggregate)
    {
        using var input = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);

        var preprocessWatch = Stopwatch.StartNew();
        normalizer.ResizeAndNormalizeToTensor(image, input.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, out _);
        preprocessWatch.Stop();

        var inferenceWatch = Stopwatch.StartNew();
        using var output = runner.RunPreparedInput(input.Tensor);
        inferenceWatch.Stop();

        var postWatch = Stopwatch.StartNew();
        _ = DecodeDetections(output, image.Size, metadata, transformer, boxDecoder);
        postWatch.Stop();

        aggregate.Add(preprocessWatch.Elapsed, inferenceWatch.Elapsed, postWatch.Elapsed);
    }

    private static void BenchmarkLegacySegmentation(Image<Rgb24> image,
                                                    YoloMetadata metadata,
                                                    YoloConfiguration configuration,
                                                    IMemoryAllocator allocator,
                                                    IPixelsNormalizer normalizer,
                                                    IBoundingBoxTransformer transformer,
                                                    INonMaxSuppression nonMaxSuppression,
                                                    YoloSession yoloSession,
                                                    SessionRunner runner,
                                                    PhaseAggregate aggregate)
    {
        using var input = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);

        LegacyPreprocess(image, input.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, normalizer, out var resize, out var normalize);

        var inferenceWatch = Stopwatch.StartNew();
        using var output = runner.RunPreparedInput(input.Tensor);
        inferenceWatch.Stop();

        var postWatch = Stopwatch.StartNew();
        _ = LegacyDecodeSegmentations(output, image.Size, metadata, configuration, allocator, transformer, nonMaxSuppression, HasExplicitEndToEnd(yoloSession));
        postWatch.Stop();

        aggregate.Add(resize + normalize, inferenceWatch.Elapsed, postWatch.Elapsed, resize, normalize);
    }

    private static void BenchmarkOptimizedSegmentation(Image<Rgb24> image,
                                                       YoloMetadata metadata,
                                                       YoloConfiguration configuration,
                                                       IMemoryAllocator allocator,
                                                       IPixelsNormalizer normalizer,
                                                       IDecoder<Segmentation> decoder,
                                                       SessionRunner runner,
                                                       PhaseAggregate aggregate)
    {
        using var input = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);

        var preprocessWatch = Stopwatch.StartNew();
        normalizer.ResizeAndNormalizeToTensor(image, input.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, out _);
        preprocessWatch.Stop();

        var inferenceWatch = Stopwatch.StartNew();
        using var output = runner.RunPreparedInput(input.Tensor);
        inferenceWatch.Stop();

        var postWatch = Stopwatch.StartNew();
        _ = decoder.Decode(output, image.Size);
        postWatch.Stop();

        aggregate.Add(preprocessWatch.Elapsed, inferenceWatch.Elapsed, postWatch.Elapsed);
    }

    private static void WarmupDetection(YoloMetadata metadata,
                                        YoloConfiguration configuration,
                                        IReadOnlyList<Image<Rgb24>> images,
                                        int warmup,
                                        IMemoryAllocator allocator,
                                        IPixelsNormalizer normalizer,
                                        IBoundingBoxTransformer transformer,
                                        IBoundingBoxDecoder boxDecoder,
                                        INonMaxSuppression nonMaxSuppression,
                                        SessionRunner runner,
                                        bool explicitEndToEnd)
    {
        for (var index = 0; index < warmup; index++)
        {
            var image = images[index % images.Count];

            using var legacyInput = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);
            LegacyPreprocess(image, legacyInput.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, normalizer, out _, out _);
            using var legacyOutput = runner.RunPreparedInput(legacyInput.Tensor);
            _ = LegacyDecodeDetections(legacyOutput, image.Size, metadata, configuration, transformer, nonMaxSuppression, explicitEndToEnd);

            using var optimizedInput = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);
            normalizer.ResizeAndNormalizeToTensor(image, optimizedInput.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, out _);
            using var optimizedOutput = runner.RunPreparedInput(optimizedInput.Tensor);
            _ = DecodeDetections(optimizedOutput, image.Size, metadata, transformer, boxDecoder);
        }
    }

    private static void WarmupSegmentation(YoloMetadata metadata,
                                           YoloConfiguration configuration,
                                           IReadOnlyList<Image<Rgb24>> images,
                                           int warmup,
                                           IMemoryAllocator allocator,
                                           IPixelsNormalizer normalizer,
                                           IDecoder<Segmentation> decoder,
                                           SessionRunner runner)
    {
        for (var index = 0; index < warmup; index++)
        {
            var image = images[index % images.Count];

            using var input = allocator.AllocateTensor<float>(runner.IoShapeInfo.Input0);
            normalizer.ResizeAndNormalizeToTensor(image, input.Tensor, metadata.ImageSize, configuration.KeepAspectRatio, out _);
            using var output = runner.RunPreparedInput(input.Tensor);
            _ = decoder.Decode(output, image.Size);
        }
    }

    private static void LegacyPreprocess(Image<Rgb24> image,
                                         MemoryTensor<float> target,
                                         Size inputSize,
                                         bool keepAspectRatio,
                                         IPixelsNormalizer normalizer,
                                         out TimeSpan resize,
                                         out TimeSpan normalize)
    {
        if (image.Width == inputSize.Width && image.Height == inputSize.Height)
        {
            resize = TimeSpan.Zero;
            var normalizeWatch = Stopwatch.StartNew();
            normalizer.NormalizerPixelsToTensor(image, target, default);
            normalizeWatch.Stop();
            normalize = normalizeWatch.Elapsed;
            return;
        }

        var resizeWatch = Stopwatch.StartNew();

        using var resized = LegacyResizeImage(image, inputSize, keepAspectRatio, out var padding);

        resizeWatch.Stop();
        resize = resizeWatch.Elapsed;

        var normalizeWatch2 = Stopwatch.StartNew();
        normalizer.NormalizerPixelsToTensor(resized, target, padding);
        normalizeWatch2.Stop();
        normalize = normalizeWatch2.Elapsed;
    }

    private static Image<Rgb24> LegacyResizeImage(Image<Rgb24> image, Size inputSize, bool keepAspectRatio, out Vector<int> padding)
    {
        PixelsNormalizer.ComputeResizeLayout(image.Size, inputSize, keepAspectRatio, out var resizedWidth, out var resizedHeight, out padding);

        var resized = image.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(resizedWidth, resizedHeight),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.NearestNeighbor
        }));

        return resized;
    }

    private static Detection[] LegacyDecodeDetections(IYoloRawOutput output,
                                                      Size imageSize,
                                                      YoloMetadata metadata,
                                                      YoloConfiguration configuration,
                                                      IBoundingBoxTransformer transformer,
                                                      INonMaxSuppression nonMaxSuppression,
                                                      bool explicitEndToEnd)
    {
        var boxes = LegacyDecodeBoxes(output.Output0, metadata, configuration, nonMaxSuppression, explicitEndToEnd);
        var transform = transformer.Compute(imageSize);
        var result = new Detection[boxes.Length];

        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];

            result[i] = new Detection
            {
                Name = metadata.Names[box.NameIndex],
                Bounds = transformer.Apply(box.Bounds, transform),
                Confidence = box.Confidence,
            };
        }

        return result;
    }

    private static Detection[] DecodeDetections(IYoloRawOutput output,
                                                Size imageSize,
                                                YoloMetadata metadata,
                                                IBoundingBoxTransformer transformer,
                                                IBoundingBoxDecoder boxDecoder)
    {
        var boxes = boxDecoder.Decode(output.Output0);
        var transform = transformer.Compute(imageSize);
        var result = new Detection[boxes.Length];

        for (var i = 0; i < boxes.Length; i++)
        {
            var box = boxes[i];

            result[i] = new Detection
            {
                Name = metadata.Names[box.NameIndex],
                Bounds = transformer.Apply(box.Bounds, transform),
                Confidence = box.Confidence,
            };
        }

        return result;
    }

    private static Segmentation[] LegacyDecodeSegmentations(IYoloRawOutput output,
                                                            Size imageSize,
                                                            YoloMetadata metadata,
                                                            YoloConfiguration configuration,
                                                            IMemoryAllocator allocator,
                                                            IBoundingBoxTransformer transformer,
                                                            INonMaxSuppression nonMaxSuppression,
                                                            bool explicitEndToEnd)
    {
        var transform = transformer.Compute(imageSize);
        var output0 = output.Output0;
        var output1 = output.Output1 ?? throw new InvalidOperationException("Segmentation output1 is missing");

        var maskWidth = output1.Dimensions[3];
        var maskHeight = output1.Dimensions[2];
        var maskChannelCount = output1.Dimensions[1];

        var maskPaddingX = transform.Padding.X * maskWidth / metadata.ImageSize.Width;
        var maskPaddingY = transform.Padding.Y * maskHeight / metadata.ImageSize.Height;

        maskWidth -= maskPaddingX * 2;
        maskHeight -= maskPaddingY * 2;

        using var rawMaskBuffer = allocator.Allocate<float>(maskWidth * maskHeight);
        using var weightsBuffer = allocator.Allocate<float>(maskChannelCount);

        var weightsSpan = weightsBuffer.Memory.Span;
        var mask = new BitmapBuffer(rawMaskBuffer.Memory, maskWidth, maskHeight);

        var offsetToWeights = metadata.AttributeOffset;
        var strideF = output0.Strides[metadata.FeatureAxis];
        var strideP = output0.Strides[metadata.PredictionAxis];
        var output0Span = output0.Span;

        var boxes = LegacyDecodeBoxes(output0, metadata, configuration, nonMaxSuppression, explicitEndToEnd);
        var result = new Segmentation[boxes.Length];

        for (var index = 0; index < boxes.Length; index++)
        {
            var box = boxes[index];
            var boxIndex = box.Index;
            var bounds = transformer.Apply(box.Bounds, transform);

            for (var i = 0; i < maskChannelCount; i++)
            {
                weightsSpan[i] = output0Span[boxIndex * strideP + (offsetToWeights + i) * strideF];
            }

            mask.Clear();

            for (var y = 0; y < mask.Height; y++)
            {
                for (var x = 0; x < mask.Width; x++)
                {
                    var value = 0f;

                    for (var i = 0; i < maskChannelCount; i++)
                    {
                        value += output1[0, i, y + maskPaddingY, x + maskPaddingX] * weightsSpan[i];
                    }

                    mask[y, x] = 1f / (1f + MathF.Exp(-value));
                }
            }

            var resizedMask = new BitmapBuffer(bounds.Width, bounds.Height);
            LegacyResizeToTarget(mask, resizedMask, bounds.Location, imageSize);

            result[index] = new Segmentation
            {
                Mask = resizedMask,
                Name = metadata.Names[box.NameIndex],
                Bounds = bounds,
                Confidence = box.Confidence,
            };
        }

        return result;
    }

    private static RawBoundingBox[] LegacyDecodeBoxes(MemoryTensor<float> tensor,
                                                      YoloMetadata metadata,
                                                      YoloConfiguration configuration,
                                                      INonMaxSuppression nonMaxSuppression,
                                                      bool explicitEndToEnd)
    {
        return metadata.Architecture switch
        {
            YoloArchitecture.AnchorFree => LegacyDecodeAnchorFreeBoxes(tensor, metadata, configuration, nonMaxSuppression, explicitEndToEnd),
            YoloArchitecture.AnchorBased => LegacyDecodeAnchorBasedBoxes(tensor, metadata, configuration, nonMaxSuppression, explicitEndToEnd),
            _ => throw new NotSupportedException($"Legacy benchmark does not support architecture: {metadata.Architecture}")
        };
    }

    private static RawBoundingBox[] LegacyDecodeAnchorFreeBoxes(MemoryTensor<float> tensor,
                                                                YoloMetadata metadata,
                                                                YoloConfiguration configuration,
                                                                INonMaxSuppression nonMaxSuppression,
                                                                bool explicitEndToEnd)
    {
        var strideP = tensor.Strides[metadata.PredictionAxis];
        var strideF = tensor.Strides[metadata.FeatureAxis];
        var boxesCount = tensor.Dimensions[metadata.PredictionAxis];
        var tensorSpan = tensor.Span;
        var boxes = new List<RawBoundingBox>(boxesCount);

        for (var boxIndex = 0; boxIndex < boxesCount; boxIndex++)
        {
            var boxOffset = boxIndex * strideP;
            var confidence = tensorSpan[boxOffset + 4 * strideF];

            if (confidence <= configuration.Confidence)
            {
                continue;
            }

            var xMin = (int)tensorSpan[boxOffset + 0 * strideF];
            var yMin = (int)tensorSpan[boxOffset + 1 * strideF];
            var xMax = (int)tensorSpan[boxOffset + 2 * strideF];
            var yMax = (int)tensorSpan[boxOffset + 3 * strideF];
            var bounds = new RectangleF(xMin, yMin, xMax - xMin, yMax - yMin);

            if (bounds.Width == 0 || bounds.Height == 0)
            {
                continue;
            }

            boxes.Add(new RawBoundingBox
            {
                Index = boxIndex,
                NameIndex = (int)tensorSpan[boxOffset + 5 * strideF],
                Confidence = confidence,
                Bounds = bounds,
            });
        }

        var result = boxes.ToArray();

        if (explicitEndToEnd)
        {
            return result;
        }

        return nonMaxSuppression.Apply(result.AsSpan(), configuration.IoU);
    }

    private static RawBoundingBox[] LegacyDecodeAnchorBasedBoxes(MemoryTensor<float> tensor,
                                                                 YoloMetadata metadata,
                                                                 YoloConfiguration configuration,
                                                                 INonMaxSuppression nonMaxSuppression,
                                                                 bool explicitEndToEnd)
    {
        var boxStride = tensor.Strides[1];
        var boxesCount = tensor.Dimensions[2];
        var namesCount = metadata.Names.Length;
        var tensorSpan = tensor.Buffer.Span;
        var boxes = new List<RawBoundingBox>(boxesCount);

        for (var boxIndex = 0; boxIndex < boxesCount; boxIndex++)
        {
            for (var nameIndex = 0; nameIndex < namesCount; nameIndex++)
            {
                var confidence = tensorSpan[(nameIndex + 4) * boxStride + boxIndex];

                if (confidence <= configuration.Confidence)
                {
                    continue;
                }

                var x = tensorSpan[boxIndex];
                var y = tensorSpan[1 * boxStride + boxIndex];
                var w = tensorSpan[2 * boxStride + boxIndex];
                var h = tensorSpan[3 * boxStride + boxIndex];
                var bounds = new RectangleF(x - w / 2, y - h / 2, w, h);

                if (bounds.Width == 0 || bounds.Height == 0)
                {
                    continue;
                }

                boxes.Add(new RawBoundingBox
                {
                    Index = boxIndex,
                    NameIndex = nameIndex,
                    Confidence = confidence,
                    Bounds = bounds,
                });
            }
        }

        var result = boxes.ToArray();

        if (explicitEndToEnd)
        {
            return result;
        }

        return nonMaxSuppression.Apply(result.AsSpan(), configuration.IoU);
    }

    private static void LegacyResizeToTarget(BitmapBuffer source, BitmapBuffer target, Point position, Size size)
    {
        for (var y = 0; y < target.Height; y++)
        {
            for (var x = 0; x < target.Width; x++)
            {
                var sourceX = (float)(x + position.X) * (source.Width - 1) / (size.Width - 1);
                var sourceY = (float)(y + position.Y) * (source.Height - 1) / (size.Height - 1);

                if (sourceY < 0 || sourceY >= source.Height || sourceX < 0 || sourceX >= source.Width)
                {
                    target[y, x] = 0f;
                    continue;
                }

                var x0 = Math.Max(0, Math.Min((int)sourceX, source.Width - 2));
                var y0 = Math.Max(0, Math.Min((int)sourceY, source.Height - 2));
                var x1 = x0 + 1;
                var y1 = y0 + 1;
                var xLerp = sourceX - x0;
                var yLerp = sourceY - y0;

                var top = Lerp(source[y0, x0], source[y0, x1], xLerp);
                var bottom = Lerp(source[y1, x0], source[y1, x1], xLerp);

                target[y, x] = Lerp(top, bottom, yLerp);
            }
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static bool HasExplicitEndToEnd(YoloSession session)
    {
        return session.Session.ModelMetadata.CustomMetadataMap.TryGetValue("end2end", out var value)
               && value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static YoloConfiguration CreateBenchmarkConfiguration(BenchmarkOptions options)
    {
        return new YoloConfiguration
        {
            Confidence = 0.25f,
            IoU = 0.45f,
            KeepAspectRatio = options.KeepAspectRatio,
            ApplyAutoOrient = false,
            SuppressParallelInference = false,
            MaximumCandidateBoxes = options.MaximumCandidateBoxes,
            MaximumDetections = options.MaximumDetections,
        };
    }

    private static YoloPredictorOptions CreatePredictorOptions(YoloConfiguration configuration, BenchmarkOptions options)
    {
        if (IsOpenVinoProvider(options.Provider))
        {
            return new YoloPredictorOptions
            {
                Configuration = configuration,
                OpenVino = new OpenVinoOptions
                {
                    DeviceType = GetOpenVinoDeviceType(options.Provider),
                    PerformanceHint = "LATENCY",
                    InferenceThreads = options.IntraOpThreads,
                    NumberOfStreams = options.InterOpThreads,
                    CacheDirectory = GetOpenVinoCacheDirectory(options.Provider)
                }
            };
        }

        return new YoloPredictorOptions
        {
            Configuration = configuration,
            SessionOptions = CreateSessionOptions(options),
        };
    }

    private static SessionOptions CreateSessionOptions(BenchmarkOptions options)
    {
        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = ParseGraphOptimizationLevel(options.GraphOptimization),
            ExecutionMode = ParseExecutionMode(options.ExecutionMode),
        };

        if (options.IntraOpThreads is int intra)
        {
            sessionOptions.IntraOpNumThreads = intra;
        }

        if (options.InterOpThreads is int inter)
        {
            sessionOptions.InterOpNumThreads = inter;
        }

        return sessionOptions;
    }

    private static GraphOptimizationLevel ParseGraphOptimizationLevel(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "disable" => GraphOptimizationLevel.ORT_DISABLE_ALL,
            "basic" => GraphOptimizationLevel.ORT_ENABLE_BASIC,
            "extended" => GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
            _ => GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
    }

    private static ExecutionMode ParseExecutionMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "parallel" => ExecutionMode.ORT_PARALLEL,
            _ => ExecutionMode.ORT_SEQUENTIAL,
        };
    }

    private static void PrintBenchmarkOptions(BenchmarkOptions options, YoloConfiguration configuration)
    {
        Console.WriteLine($"Provider: {options.Provider}, ExecutionMode: {options.ExecutionMode}, GraphOptimization: {options.GraphOptimization}, IntraOp: {options.IntraOpThreads?.ToString() ?? "default"}, InterOp: {options.InterOpThreads?.ToString() ?? "default"}, KeepAspectRatio: {configuration.KeepAspectRatio}, MaxCandidates: {configuration.MaximumCandidateBoxes}, MaxDetections: {configuration.MaximumDetections}");
    }

    private static bool IsOpenVinoProvider(string provider)
    {
        return provider.StartsWith("openvino", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetOpenVinoDeviceType(string provider)
    {
        return provider.Equals("openvino-gpu", StringComparison.OrdinalIgnoreCase) ? "GPU" : "CPU";
    }

    private static string GetOpenVinoCacheDirectory(string provider)
    {
        return Path.Combine(Path.GetTempPath(), "JinlongYolo.OpenVino.Cache", provider.ToLowerInvariant());
    }

    private static void PrepareProviderEnvironment(string provider)
    {
        if (!IsOpenVinoProvider(provider))
        {
            return;
        }

        var cacheDirectory = GetOpenVinoCacheDirectory(provider);

        if (!Directory.Exists(cacheDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(cacheDirectory, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static List<Image<Rgb24>> LoadImages(string directory, int count)
    {
        var files = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            .Take(count)
            .ToArray();

        var images = new List<Image<Rgb24>>(files.Length);

        foreach (var file in files)
        {
            images.Add(Image.Load<Rgb24>(file));
        }

        return images;
    }

    private static BenchmarkOptions Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : "true";
                options[key] = value;
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new BenchmarkOptions
        {
            Mode = options.TryGetValue("mode", out var mode)
                   ? mode
                   : positional.FirstOrDefault()
                     ?? "detect",
                        Provider = options.TryGetValue("provider", out var provider) ? provider : "cpu",
                        CompareOpenVino = options.ContainsKey("compare-openvino"),
            ModelPath = options.TryGetValue("model", out var model) ? model : null,
            ImageDirectory = options.TryGetValue("images", out var images) ? images : null,
            Warmup = options.TryGetValue("warmup", out var warmup) && int.TryParse(warmup, out var warmupValue) ? warmupValue : 5,
            Iterations = options.TryGetValue("iterations", out var iterations) && int.TryParse(iterations, out var iterationValue) ? iterationValue : 30,
            ImageCount = options.TryGetValue("count", out var count) && int.TryParse(count, out var countValue) ? countValue : 12,
            IntraOpThreads = options.TryGetValue("intra", out var intra) && int.TryParse(intra, out var intraValue) ? intraValue : null,
            InterOpThreads = options.TryGetValue("inter", out var inter) && int.TryParse(inter, out var interValue) ? interValue : null,
            ExecutionMode = options.TryGetValue("execution", out var execution) ? execution : "sequential",
            GraphOptimization = options.TryGetValue("graph", out var graph) ? graph : "all",
            KeepAspectRatio = !options.TryGetValue("keep-aspect", out var keepAspect) || bool.TryParse(keepAspect, out var keepAspectValue) && keepAspectValue,
            MaximumCandidateBoxes = options.TryGetValue("max-candidates", out var maxCandidates) && int.TryParse(maxCandidates, out var maxCandidatesValue) ? maxCandidatesValue : 1024,
            MaximumDetections = options.TryGetValue("max-detections", out var maxDetections) && int.TryParse(maxDetections, out var maxDetectionsValue) ? maxDetectionsValue : 300,
            NmsCandidateBoxes = options.TryGetValue("nms-count", out var nmsCount) && int.TryParse(nmsCount, out var nmsCountValue) ? nmsCountValue : 1024,
            NmsLabels = options.TryGetValue("nms-labels", out var nmsLabels) && int.TryParse(nmsLabels, out var nmsLabelsValue) ? nmsLabelsValue : 8,
            NmsIoUThreshold = options.TryGetValue("nms-iou", out var nmsIou) && float.TryParse(nmsIou, out var nmsIouValue) ? nmsIouValue : 0.45f,
            NmsSeed = options.TryGetValue("nms-seed", out var nmsSeed) && int.TryParse(nmsSeed, out var nmsSeedValue) ? nmsSeedValue : 42,
        };
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ItemBinding.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static int PrintUsageAndFail(string mode)
    {
        Console.Error.WriteLine($"Unsupported benchmark mode: {mode}");
        Console.Error.WriteLine("Usage: benchmark [detect|segment|nms] [--provider cpu|openvino-cpu|openvino-gpu] [--compare-openvino] [--model path] [--images directory] [--iterations N] [--warmup N] [--count N] [--intra N] [--inter N] [--execution sequential|parallel] [--graph disable|basic|extended|all] [--keep-aspect true|false] [--max-candidates N] [--max-detections N] [--nms-count N] [--nms-labels N] [--nms-iou F] [--nms-seed N]");
        return 1;
    }
}