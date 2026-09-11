using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Plotting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Diagnostics;
using Image = SixLabors.ImageSharp.Image;
using System.Threading.Tasks;

namespace ConsoleApp.Test
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test-recipe")
            {
                await RecipeImageAcquisitionTest.RunAsync();
            }
            else
            {
                await RunOpenVinoGpuExampleAsync(args);
            }
        }

        private static async Task RunOpenVinoGpuExampleAsync(string[] args)
        {
            var repoRoot = FindRepoRoot();
            var modelPath = args.Length > 0
                ? args[0]
                : Path.Combine(repoRoot, "ConsoleApp10", "best_itembinding.onnx");

            var imagePath = args.Length > 1
                ? args[1]
                : Directory.EnumerateFiles(Path.Combine(repoRoot, "Pictures", "label", "images"), "*.png", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? string.Empty;

            var outputPath = args.Length > 2
                ? args[2]
                : Path.Combine(repoRoot,
                               "Pictures",
                               "label",
                               "results",
                               Path.GetFileNameWithoutExtension(string.IsNullOrWhiteSpace(imagePath) ? "demo" : imagePath) + "_openvino_gpu_demo.png");

            if (!File.Exists(modelPath))
            {
                Console.Error.WriteLine($"Model not found: {modelPath}");
                return;
            }

            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                Console.Error.WriteLine($"Image not found: {imagePath}");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var predictorOptions = new YoloPredictorOptions
            {
                Configuration = new YoloConfiguration
                {
                    Confidence = 0.25f,
                    IoU = 0.45f,
                    KeepAspectRatio = true,
                    ApplyAutoOrient = false,
                    MaximumCandidateBoxes = 1024,
                    MaximumDetections = 300,
                },
                OpenVino = new OpenVinoOptions
                {
                    DeviceType = "GPU",
                    PerformanceHint = "LATENCY",
                    CacheDirectory = Path.Combine(Path.GetTempPath(), "JinlongYolo.OpenVino.Cache", "program-demo-gpu")
                }
            };

            using var predictor = new YoloPredictor(modelPath, predictorOptions);
            using var image = Image.Load<Rgb24>(imagePath);

            var stopwatch = Stopwatch.StartNew();
            var result = predictor.Segment(image);
            stopwatch.Stop();

            Console.WriteLine("OpenVINO GPU example");
            Console.WriteLine($"Model: {modelPath}");
            Console.WriteLine($"Image: {imagePath}");
            Console.WriteLine($"Output: {outputPath}");
            Console.WriteLine($"Elapsed: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Segment count: {result.Count}");

            var previewIndex = 0;

            foreach (var segmentation in result.Take(5))
            {
                Console.WriteLine($"[{previewIndex}] {segmentation.Name.Name} Confidence={segmentation.Confidence:P2} Bounds={segmentation.Bounds}");
                previewIndex++;
            }

            using var plotted = await result.PlotImageAsync(image);
            plotted.Save(outputPath);
            Console.WriteLine("Saved plotted result.");
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
    }
}
