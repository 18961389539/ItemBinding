using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Globalization;
using Image = SixLabors.ImageSharp.Image;

namespace ConsoleApp.Test;

internal static class SegmentationLabelEvaluationRunner
{
    private sealed class EvaluationOptions
    {
        public string Provider { get; init; } = "openvino-gpu";

        public string? ModelPath { get; init; }

        public string? ImageDirectory { get; init; }

        public string? LabelDirectory { get; init; }

        public int? ImageCount { get; init; }

        public float Confidence { get; init; } = 0.25f;

        public float IoU { get; init; } = 0.45f;

        public float MaskThreshold { get; init; } = 0.5f;

        public float MatchIoUThreshold { get; init; } = 0.5f;

        public bool KeepAspectRatio { get; init; } = true;

        public int MaximumCandidateBoxes { get; init; } = 1024;

        public int MaximumDetections { get; init; } = 300;

        public int? IntraOpThreads { get; init; }

        public int? InterOpThreads { get; init; }
    }

    private sealed class EvaluationAggregate
    {
        private readonly List<ImageEvaluation> _images = [];

        private readonly List<double> _pixelIoUs = [];
        private readonly List<double> _pixelDices = [];

        public int ImageCount => _images.Count;

        public int TotalGroundTruthInstances { get; private set; }

        public int TotalPredictedInstances { get; private set; }

        public int TruePositiveInstances { get; private set; }

        public int FalsePositiveInstances { get; private set; }

        public int FalseNegativeInstances { get; private set; }

        public int InvalidLabelCount { get; private set; }

        public long GroundTruthPixels { get; private set; }

        public long PredictedPixels { get; private set; }

        public long IntersectionPixels { get; private set; }

        public long UnionPixels { get; private set; }

        public double MeanPixelIoU => Average(_pixelIoUs);

        public double MedianPixelIoU => Median(_pixelIoUs);

        public double MeanPixelDice => Average(_pixelDices);

        public double GlobalPixelIoU => UnionPixels == 0 ? 1d : (double)IntersectionPixels / UnionPixels;

        public double GlobalPixelDice => GroundTruthPixels + PredictedPixels == 0
            ? 1d
            : (2d * IntersectionPixels) / (GroundTruthPixels + PredictedPixels);

        public double GlobalPixelPrecision => PredictedPixels == 0
            ? (GroundTruthPixels == 0 ? 1d : 0d)
            : (double)IntersectionPixels / PredictedPixels;

        public double GlobalPixelRecall => GroundTruthPixels == 0
            ? (PredictedPixels == 0 ? 1d : 0d)
            : (double)IntersectionPixels / GroundTruthPixels;

        public double InstancePrecision => TruePositiveInstances + FalsePositiveInstances == 0
            ? (FalseNegativeInstances == 0 ? 1d : 0d)
            : (double)TruePositiveInstances / (TruePositiveInstances + FalsePositiveInstances);

        public double InstanceRecall => TruePositiveInstances + FalseNegativeInstances == 0
            ? (FalsePositiveInstances == 0 ? 1d : 0d)
            : (double)TruePositiveInstances / (TruePositiveInstances + FalseNegativeInstances);

        public double InstanceF1
        {
            get
            {
                var precision = InstancePrecision;
                var recall = InstanceRecall;

                return precision + recall == 0d ? 0d : 2d * precision * recall / (precision + recall);
            }
        }

        public IReadOnlyList<ImageEvaluation> WorstImages => _images
            .OrderBy(image => image.PixelIoU)
            .ThenByDescending(image => Math.Abs(image.PredictedInstanceCount - image.GroundTruthInstanceCount))
            .ThenBy(image => image.ImageName, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        public void Add(ImageEvaluation image)
        {
            _images.Add(image);
            _pixelIoUs.Add(image.PixelIoU);
            _pixelDices.Add(image.PixelDice);
            TotalGroundTruthInstances += image.GroundTruthInstanceCount;
            TotalPredictedInstances += image.PredictedInstanceCount;
            TruePositiveInstances += image.TruePositiveInstances;
            FalsePositiveInstances += image.FalsePositiveInstances;
            FalseNegativeInstances += image.FalseNegativeInstances;
            InvalidLabelCount += image.InvalidLabelCount;
            GroundTruthPixels += image.GroundTruthPixels;
            PredictedPixels += image.PredictedPixels;
            IntersectionPixels += image.IntersectionPixels;
            UnionPixels += image.UnionPixels;
        }

        private static double Average(List<double> values) => values.Count == 0 ? 0d : values.Sum() / values.Count;

        private static double Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var ordered = values.OrderBy(value => value).ToArray();
            var midpoint = ordered.Length / 2;

            return ordered.Length % 2 == 0
                ? (ordered[midpoint - 1] + ordered[midpoint]) / 2d
                : ordered[midpoint];
        }
    }

    private sealed class ImageEvaluation
    {
        public required string ImageName { get; init; }

        public required double PixelIoU { get; init; }

        public required double PixelDice { get; init; }

        public required double PixelPrecision { get; init; }

        public required double PixelRecall { get; init; }

        public required double MeanMatchedIoU { get; init; }

        public required double MeanBestGroundTruthIoU { get; init; }

        public required int GroundTruthInstanceCount { get; init; }

        public required int PredictedInstanceCount { get; init; }

        public required int TruePositiveInstances { get; init; }

        public required int FalsePositiveInstances { get; init; }

        public required int FalseNegativeInstances { get; init; }

        public required int InvalidLabelCount { get; init; }

        public required long GroundTruthPixels { get; init; }

        public required long PredictedPixels { get; init; }

        public required long IntersectionPixels { get; init; }

        public required long UnionPixels { get; init; }
    }

    private sealed class MatchingSummary
    {
        public required int TruePositives { get; init; }

        public required int FalsePositives { get; init; }

        public required int FalseNegatives { get; init; }

        public required double MeanMatchedIoU { get; init; }

        public required double MeanBestGroundTruthIoU { get; init; }
    }

    private readonly record struct ImagePair(string ImagePath, string LabelPath);

    private sealed class BinaryMask
    {
        public required byte[] Buffer { get; init; }

        public required int Width { get; init; }

        public required int Height { get; init; }

        public required int PixelCount { get; init; }

        public static BinaryMask Empty(int width, int height) => new()
        {
            Buffer = new byte[width * height],
            Width = width,
            Height = height,
            PixelCount = 0,
        };
    }

    private readonly record struct FloatPoint(double X, double Y);

    public static int Run(string[] args)
    {
        var options = Parse(args);
        var repoRoot = FindRepoRoot();
        var modelPath = options.ModelPath ?? Path.Combine(repoRoot, "ConsoleApp10", "best_itembinding.onnx");
        var imageDirectory = options.ImageDirectory ?? Path.Combine(repoRoot, "Pictures", "label", "images");
        var labelDirectory = options.LabelDirectory ?? Path.Combine(repoRoot, "Pictures", "label", "labels");

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

        if (!Directory.Exists(labelDirectory))
        {
            Console.Error.WriteLine($"Label directory not found: {labelDirectory}");
            return 1;
        }

        var imagePairs = LoadPairs(imageDirectory, labelDirectory, options.ImageCount);

        if (imagePairs.Count == 0)
        {
            Console.Error.WriteLine("No matching image/label pairs were found.");
            return 1;
        }

        Console.WriteLine($"Provider: {options.Provider}");
        Console.WriteLine($"Model: {modelPath}");
        Console.WriteLine($"Images: {imageDirectory}");
        Console.WriteLine($"Labels: {labelDirectory}");
        Console.WriteLine($"Pairs: {imagePairs.Count}, Confidence: {options.Confidence:F2}, IoU: {options.IoU:F2}, MaskThreshold: {options.MaskThreshold:F2}, MatchIoU: {options.MatchIoUThreshold:F2}");

        var predictorOptions = CreatePredictorOptions(CreateConfiguration(options), options);
        var aggregate = new EvaluationAggregate();

        using var predictor = new YoloPredictor(modelPath, predictorOptions);

        foreach (var pair in imagePairs)
        {
            using var image = Image.Load<Rgb24>(pair.ImagePath);
            var groundTruthMasks = LoadGroundTruthMasks(pair.LabelPath, image.Width, image.Height, out var invalidLabelCount);
            var predictionMasks = BuildPredictionMasks(predictor.Segment(image), image.Width, image.Height, options.MaskThreshold);
            var imageEvaluation = EvaluateImage(Path.GetFileName(pair.ImagePath), groundTruthMasks, predictionMasks, image.Width, image.Height, invalidLabelCount, options.MatchIoUThreshold);
            aggregate.Add(imageEvaluation);
        }

        PrintSummary(aggregate);
        return 0;
    }

    private static ImageEvaluation EvaluateImage(string imageName,
                                                 IReadOnlyList<BinaryMask> groundTruthMasks,
                                                 IReadOnlyList<BinaryMask> predictionMasks,
                                                 int imageWidth,
                                                 int imageHeight,
                                                 int invalidLabelCount,
                                                 float matchIoUThreshold)
    {
        var groundTruthUnion = CombineMasks(groundTruthMasks, imageWidth, imageHeight);
        var predictionUnion = CombineMasks(predictionMasks, imageWidth, imageHeight);
        var intersectionPixels = CountIntersection(groundTruthUnion, predictionUnion);
        var unionPixels = groundTruthUnion.PixelCount + predictionUnion.PixelCount - intersectionPixels;
        var precision = predictionUnion.PixelCount == 0
            ? (groundTruthUnion.PixelCount == 0 ? 1d : 0d)
            : (double)intersectionPixels / predictionUnion.PixelCount;
        var recall = groundTruthUnion.PixelCount == 0
            ? (predictionUnion.PixelCount == 0 ? 1d : 0d)
            : (double)intersectionPixels / groundTruthUnion.PixelCount;
        var pixelIoU = unionPixels == 0 ? 1d : (double)intersectionPixels / unionPixels;
        var pixelDice = groundTruthUnion.PixelCount + predictionUnion.PixelCount == 0
            ? 1d
            : (2d * intersectionPixels) / (groundTruthUnion.PixelCount + predictionUnion.PixelCount);
        var matching = MatchInstances(groundTruthMasks, predictionMasks, matchIoUThreshold);

        return new ImageEvaluation
        {
            ImageName = imageName,
            PixelIoU = pixelIoU,
            PixelDice = pixelDice,
            PixelPrecision = precision,
            PixelRecall = recall,
            MeanMatchedIoU = matching.MeanMatchedIoU,
            MeanBestGroundTruthIoU = matching.MeanBestGroundTruthIoU,
            GroundTruthInstanceCount = groundTruthMasks.Count,
            PredictedInstanceCount = predictionMasks.Count,
            TruePositiveInstances = matching.TruePositives,
            FalsePositiveInstances = matching.FalsePositives,
            FalseNegativeInstances = matching.FalseNegatives,
            InvalidLabelCount = invalidLabelCount,
            GroundTruthPixels = groundTruthUnion.PixelCount,
            PredictedPixels = predictionUnion.PixelCount,
            IntersectionPixels = intersectionPixels,
            UnionPixels = unionPixels,
        };
    }

    private static MatchingSummary MatchInstances(IReadOnlyList<BinaryMask> groundTruthMasks,
                                                  IReadOnlyList<BinaryMask> predictionMasks,
                                                  float matchIoUThreshold)
    {
        if (groundTruthMasks.Count == 0)
        {
            return new MatchingSummary
            {
                TruePositives = 0,
                FalsePositives = predictionMasks.Count,
                FalseNegatives = 0,
                MeanMatchedIoU = predictionMasks.Count == 0 ? 1d : 0d,
                MeanBestGroundTruthIoU = predictionMasks.Count == 0 ? 1d : 0d,
            };
        }

        var candidates = new List<(int GroundTruthIndex, int PredictionIndex, double IoU)>();
        var bestGroundTruthIoUs = new double[groundTruthMasks.Count];

        for (var gtIndex = 0; gtIndex < groundTruthMasks.Count; gtIndex++)
        {
            for (var predictionIndex = 0; predictionIndex < predictionMasks.Count; predictionIndex++)
            {
                var iou = ComputeIoU(groundTruthMasks[gtIndex], predictionMasks[predictionIndex]);

                if (iou > bestGroundTruthIoUs[gtIndex])
                {
                    bestGroundTruthIoUs[gtIndex] = iou;
                }

                if (iou > 0d)
                {
                    candidates.Add((gtIndex, predictionIndex, iou));
                }
            }
        }

        candidates.Sort((left, right) => right.IoU.CompareTo(left.IoU));

        var usedGroundTruth = new bool[groundTruthMasks.Count];
        var usedPredictions = new bool[predictionMasks.Count];
        var matchedIoUs = new List<double>();

        foreach (var candidate in candidates)
        {
            if (candidate.IoU < matchIoUThreshold)
            {
                break;
            }

            if (usedGroundTruth[candidate.GroundTruthIndex] || usedPredictions[candidate.PredictionIndex])
            {
                continue;
            }

            usedGroundTruth[candidate.GroundTruthIndex] = true;
            usedPredictions[candidate.PredictionIndex] = true;
            matchedIoUs.Add(candidate.IoU);
        }

        return new MatchingSummary
        {
            TruePositives = matchedIoUs.Count,
            FalsePositives = predictionMasks.Count - matchedIoUs.Count,
            FalseNegatives = groundTruthMasks.Count - matchedIoUs.Count,
            MeanMatchedIoU = matchedIoUs.Count == 0 ? 0d : matchedIoUs.Sum() / matchedIoUs.Count,
            MeanBestGroundTruthIoU = bestGroundTruthIoUs.Length == 0 ? 0d : bestGroundTruthIoUs.Average(),
        };
    }

    private static IReadOnlyList<BinaryMask> LoadGroundTruthMasks(string labelPath, int imageWidth, int imageHeight, out int invalidLabelCount)
    {
        var masks = new List<BinaryMask>();
        invalidLabelCount = 0;

        foreach (var rawLine in File.ReadLines(labelPath))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (!TryParsePolygon(line, imageWidth, imageHeight, out var points))
            {
                invalidLabelCount++;
                continue;
            }

            var mask = RasterizePolygon(points, imageWidth, imageHeight);

            if (mask.PixelCount == 0)
            {
                invalidLabelCount++;
                continue;
            }

            masks.Add(mask);
        }

        return masks;
    }

    private static bool TryParsePolygon(string line, int imageWidth, int imageHeight, out FloatPoint[] points)
    {
        points = [];

        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length < 7 || (tokens.Length - 1) % 2 != 0)
        {
            return false;
        }

        if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        var parsed = new FloatPoint[(tokens.Length - 1) / 2];

        for (var tokenIndex = 1; tokenIndex < tokens.Length; tokenIndex += 2)
        {
            if (!double.TryParse(tokens[tokenIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var normalizedX)
                || !double.TryParse(tokens[tokenIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var normalizedY))
            {
                points = [];
                return false;
            }

            var pointIndex = (tokenIndex - 1) / 2;
            parsed[pointIndex] = new FloatPoint(
                Math.Clamp(normalizedX, 0d, 1d) * (imageWidth - 1),
                Math.Clamp(normalizedY, 0d, 1d) * (imageHeight - 1));
        }

        points = parsed;
        return parsed.Length >= 3;
    }

    private static BinaryMask RasterizePolygon(FloatPoint[] points, int imageWidth, int imageHeight)
    {
        var buffer = new byte[imageWidth * imageHeight];
        var intersections = new List<double>(points.Length);
        var minimumY = Math.Max(0, (int)Math.Floor(points.Min(point => point.Y)));
        var maximumY = Math.Min(imageHeight - 1, (int)Math.Ceiling(points.Max(point => point.Y)));
        var pixelCount = 0;

        for (var y = minimumY; y <= maximumY; y++)
        {
            var scanY = y + 0.5d;
            intersections.Clear();

            for (var current = 0; current < points.Length; current++)
            {
                var previous = current == 0 ? points[^1] : points[current - 1];
                var next = points[current];

                if (Math.Abs(previous.Y - next.Y) < double.Epsilon)
                {
                    continue;
                }

                var crosses = (previous.Y <= scanY && next.Y > scanY)
                              || (next.Y <= scanY && previous.Y > scanY);

                if (!crosses)
                {
                    continue;
                }

                var intersectionX = previous.X + ((scanY - previous.Y) * (next.X - previous.X) / (next.Y - previous.Y));
                intersections.Add(intersectionX);
            }

            intersections.Sort();

            for (var index = 0; index + 1 < intersections.Count; index += 2)
            {
                var startX = Math.Max(0, (int)Math.Ceiling(intersections[index] - 0.5d));
                var endXExclusive = Math.Min(imageWidth, (int)Math.Ceiling(intersections[index + 1] - 0.5d));

                for (var x = startX; x < endXExclusive; x++)
                {
                    var bufferIndex = y * imageWidth + x;

                    if (buffer[bufferIndex] != 0)
                    {
                        continue;
                    }

                    buffer[bufferIndex] = byte.MaxValue;
                    pixelCount++;
                }
            }
        }

        return new BinaryMask
        {
            Buffer = buffer,
            Width = imageWidth,
            Height = imageHeight,
            PixelCount = pixelCount,
        };
    }

    private static IReadOnlyList<BinaryMask> BuildPredictionMasks(YoloResult<Segmentation> result,
                                                                  int imageWidth,
                                                                  int imageHeight,
                                                                  float maskThreshold)
    {
        var masks = new List<BinaryMask>(result.Count);

        foreach (var segmentation in result)
        {
            var buffer = new byte[imageWidth * imageHeight];
            var bounds = segmentation.Bounds;
            var mask = segmentation.Mask;
            var pixelCount = 0;

            for (var localY = 0; localY < mask.Height; localY++)
            {
                var globalY = bounds.Y + localY;

                if (globalY < 0 || globalY >= imageHeight)
                {
                    continue;
                }

                var rowOffset = globalY * imageWidth;

                for (var localX = 0; localX < mask.Width; localX++)
                {
                    if (mask[localY, localX] < maskThreshold)
                    {
                        continue;
                    }

                    var globalX = bounds.X + localX;

                    if (globalX < 0 || globalX >= imageWidth)
                    {
                        continue;
                    }

                    var bufferIndex = rowOffset + globalX;

                    if (buffer[bufferIndex] != 0)
                    {
                        continue;
                    }

                    buffer[bufferIndex] = byte.MaxValue;
                    pixelCount++;
                }
            }

            if (pixelCount == 0)
            {
                continue;
            }

            masks.Add(new BinaryMask
            {
                Buffer = buffer,
                Width = imageWidth,
                Height = imageHeight,
                PixelCount = pixelCount,
            });
        }

        return masks;
    }

    private static BinaryMask CombineMasks(IReadOnlyList<BinaryMask> masks, int imageWidth, int imageHeight)
    {
        if (masks.Count == 0)
        {
            return BinaryMask.Empty(imageWidth, imageHeight);
        }

        var unionBuffer = new byte[imageWidth * imageHeight];
        var pixelCount = 0;

        foreach (var mask in masks)
        {
            var source = mask.Buffer;

            for (var index = 0; index < source.Length; index++)
            {
                if (source[index] == 0 || unionBuffer[index] != 0)
                {
                    continue;
                }

                unionBuffer[index] = byte.MaxValue;
                pixelCount++;
            }
        }

        return new BinaryMask
        {
            Buffer = unionBuffer,
            Width = imageWidth,
            Height = imageHeight,
            PixelCount = pixelCount,
        };
    }

    private static int CountIntersection(BinaryMask left, BinaryMask right)
    {
        var count = 0;
        var leftBuffer = left.Buffer;
        var rightBuffer = right.Buffer;

        for (var index = 0; index < leftBuffer.Length; index++)
        {
            if (leftBuffer[index] != 0 && rightBuffer[index] != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static double ComputeIoU(BinaryMask left, BinaryMask right)
    {
        var intersection = CountIntersection(left, right);
        var union = left.PixelCount + right.PixelCount - intersection;
        return union == 0 ? 1d : (double)intersection / union;
    }

    private static List<ImagePair> LoadPairs(string imageDirectory, string labelDirectory, int? count)
    {
        IEnumerable<string> images = Directory.EnumerateFiles(imageDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

        if (count is int requestedCount)
        {
            images = images.Take(requestedCount);
        }

        var pairs = new List<ImagePair>();

        foreach (var imagePath in images)
        {
            var labelPath = Path.Combine(labelDirectory, Path.GetFileNameWithoutExtension(imagePath) + ".txt");

            if (!File.Exists(labelPath))
            {
                continue;
            }

            pairs.Add(new ImagePair(imagePath, labelPath));
        }

        return pairs;
    }

    private static YoloConfiguration CreateConfiguration(EvaluationOptions options)
    {
        return new YoloConfiguration
        {
            Confidence = options.Confidence,
            IoU = options.IoU,
            KeepAspectRatio = options.KeepAspectRatio,
            ApplyAutoOrient = false,
            SuppressParallelInference = false,
            MaximumCandidateBoxes = options.MaximumCandidateBoxes,
            MaximumDetections = options.MaximumDetections,
        };
    }

    private static YoloPredictorOptions CreatePredictorOptions(YoloConfiguration configuration, EvaluationOptions options)
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
                    CacheDirectory = Path.Combine(Path.GetTempPath(), "JinlongYolo.OpenVino.Cache", options.Provider.ToLowerInvariant(), "eval")
                }
            };
        }

        var sessionOptions = new SessionOptions();

        if (options.IntraOpThreads is int intraOp)
        {
            sessionOptions.IntraOpNumThreads = intraOp;
        }

        if (options.InterOpThreads is int interOp)
        {
            sessionOptions.InterOpNumThreads = interOp;
        }

        return new YoloPredictorOptions
        {
            Configuration = configuration,
            SessionOptions = sessionOptions,
        };
    }

    private static bool IsOpenVinoProvider(string provider)
    {
        return provider.StartsWith("openvino", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetOpenVinoDeviceType(string provider)
    {
        return provider.Equals("openvino-gpu", StringComparison.OrdinalIgnoreCase) ? "GPU" : "CPU";
    }

    private static EvaluationOptions Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                return PrintUsageAndThrow(argument);
            }

            var key = argument[2..];

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = "true";
                continue;
            }

            options[key] = args[++index];
        }

        try
        {
            return new EvaluationOptions
            {
                Provider = Get(options, "provider", "openvino-gpu"),
                ModelPath = GetOptional(options, "model"),
                ImageDirectory = GetOptional(options, "images"),
                LabelDirectory = GetOptional(options, "labels"),
                ImageCount = GetOptionalInt(options, "count"),
                Confidence = GetOptionalFloat(options, "confidence") ?? 0.25f,
                IoU = GetOptionalFloat(options, "iou") ?? 0.45f,
                MaskThreshold = GetOptionalFloat(options, "mask-threshold") ?? 0.5f,
                MatchIoUThreshold = GetOptionalFloat(options, "match-iou") ?? 0.5f,
                KeepAspectRatio = GetOptionalBool(options, "keep-aspect") ?? true,
                MaximumCandidateBoxes = GetOptionalInt(options, "max-candidates") ?? 1024,
                MaximumDetections = GetOptionalInt(options, "max-detections") ?? 300,
                IntraOpThreads = GetOptionalInt(options, "intra"),
                InterOpThreads = GetOptionalInt(options, "inter")
            };
        }
        catch (FormatException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            Environment.ExitCode = 1;
            throw;
        }
    }

    private static EvaluationOptions PrintUsageAndThrow(string argument)
    {
        Console.Error.WriteLine($"Unexpected argument: {argument}");
        PrintUsage();
        throw new FormatException($"Unexpected argument: {argument}");
    }

    private static string Get(Dictionary<string, string> options, string key, string defaultValue)
    {
        return options.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private static string? GetOptional(Dictionary<string, string> options, string key)
    {
        return options.TryGetValue(key, out var value) ? value : null;
    }

    private static int? GetOptionalInt(Dictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return null;
        }

        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static float? GetOptionalFloat(Dictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return null;
        }

        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    private static bool? GetOptionalBool(Dictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var value))
        {
            return null;
        }

        return bool.Parse(value);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ItemBinding.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }

    private static void PrintSummary(EvaluationAggregate aggregate)
    {
        Console.WriteLine();
        Console.WriteLine("Pixel metrics");
        Console.WriteLine($"  mean IoU: {aggregate.MeanPixelIoU:P2}");
        Console.WriteLine($"  median IoU: {aggregate.MedianPixelIoU:P2}");
        Console.WriteLine($"  global IoU: {aggregate.GlobalPixelIoU:P2}");
        Console.WriteLine($"  mean Dice: {aggregate.MeanPixelDice:P2}");
        Console.WriteLine($"  global Dice: {aggregate.GlobalPixelDice:P2}");
        Console.WriteLine($"  global precision: {aggregate.GlobalPixelPrecision:P2}");
        Console.WriteLine($"  global recall: {aggregate.GlobalPixelRecall:P2}");
        Console.WriteLine();
        Console.WriteLine("Instance metrics");
        Console.WriteLine($"  images: {aggregate.ImageCount}");
        Console.WriteLine($"  ground truth instances: {aggregate.TotalGroundTruthInstances}");
        Console.WriteLine($"  predicted instances: {aggregate.TotalPredictedInstances}");
        Console.WriteLine($"  IoU@0.5 precision: {aggregate.InstancePrecision:P2}");
        Console.WriteLine($"  IoU@0.5 recall: {aggregate.InstanceRecall:P2}");
        Console.WriteLine($"  IoU@0.5 F1: {aggregate.InstanceF1:P2}");

        if (aggregate.InvalidLabelCount > 0)
        {
            Console.WriteLine($"  invalid labels skipped: {aggregate.InvalidLabelCount}");
        }

        Console.WriteLine();
        Console.WriteLine("Worst 5 images by pixel IoU");

        foreach (var image in aggregate.WorstImages)
        {
            Console.WriteLine($"  {image.ImageName}: IoU={image.PixelIoU:P2}, Dice={image.PixelDice:P2}, Precision={image.PixelPrecision:P2}, Recall={image.PixelRecall:P2}, GT={image.GroundTruthInstanceCount}, Pred={image.PredictedInstanceCount}, TP={image.TruePositiveInstances}, FP={image.FalsePositiveInstances}, FN={image.FalseNegativeInstances}, matchedIoU={image.MeanMatchedIoU:P2}, bestGtIoU={image.MeanBestGroundTruthIoU:P2}");
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: segment-eval [--provider cpu|openvino-cpu|openvino-gpu] [--model path] [--images directory] [--labels directory] [--count N] [--confidence F] [--iou F] [--mask-threshold F] [--match-iou F] [--keep-aspect true|false] [--max-candidates N] [--max-detections N] [--intra N] [--inter N]");
    }
}