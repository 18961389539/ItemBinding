using HikScanner;
using HikScannerType = HikScanner.HikScanner;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Plotting;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace ConsoleApp10
{
    internal class Program
    {
        static Mat ToMat(Image image)
        {
            using var ms = new MemoryStream();
            image.SaveAsBmp(ms);
            return Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
        }

        static void ImagesTest()
        {
            using var pool = YoloPredictorPool.Create(File.ReadAllBytes(@"best.onnx"), new YoloPredictorPoolLayout
            {
                OpenVinoGpuCount = 5,
                Configuration = new YoloConfiguration
                {
                    Confidence = 0.5f,
                    IoU = 0.5f,
                    ApplyAutoOrient = true,
                }
            });
            Cv2.NamedWindow("ImagesTest", WindowFlags.Normal);
            foreach (var fullname in Directory.EnumerateFiles(@"C:\Users\jinlong\Code\ItemBinding\MainAPP\bin\Debug\net8.0-windows\Images\BadInference"))
            {
                // Load the target image
                using var image = Image.Load(fullname);
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Min,
                    Size = new SixLabors.ImageSharp.Size(1368, 912)
                }));
                // Run model
                var watch = Stopwatch.StartNew();
                var result = pool.Use(predictor => predictor.Segment(image));
                watch.Stop();
                Console.WriteLine($"Cost time:{watch.ElapsedMilliseconds}");
                if (result.Count != 15)
                {
                    ;
                }

                // Create plotted image from model results
                using var plotted = result.PlotImage(image);

                using var mat = ToMat(plotted);
                Cv2.ImShow("ImagesTest", mat);
                if (Cv2.WaitKey(1) == 27)
                {
                    break;
                }

                // Write the plotted image to file
                //plotted.Save($"{fullname.Replace("20260317", "Result")}");
                Console.WriteLine(fullname);
            }
            Cv2.DestroyAllWindows();
            Console.WriteLine("Finished");
        }

        static void CameraTest()
        {
            using var predictor = new JinlongYolo.YoloSharp.YoloPredictor(@"D:\projects\source code vision\wumabangding01\yolodemo\ItemCodeBindingDataset\runs\segment\bag\weights\best.onnx",
                new JinlongYolo.YoloSharp.YoloPredictorOptions
                {
                    Configuration = new JinlongYolo.YoloSharp.YoloConfiguration
                    {
                        Confidence = 0.5f,
                        IoU = 0.5f,
                        ApplyAutoOrient = true,

                    }
                });
            var devicesInfo = HikScannerType.EnumerateDevices(HikDeviceType.GigE);
            var device = new HikScannerType();
            device.Connect(devicesInfo[0]);
            device.SwitchToSoftwareTrigger();
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(3000);
                    device.ExecuteSoftwareTrigger();

                }
            });
            for (int i = 0; i < 100; i++)
            {

                var results = device.GetImageAsync().Result;
                Stopwatch stopwatch = Stopwatch.StartNew();
                using var image = Image.Load(results.Image.RawData);
                image.Mutate(x => x.Resize(1280, 992));
                predictor.Segment(results.Image.RawData);
                stopwatch.Stop();
                Console.WriteLine($"Cost times:{stopwatch.ElapsedMilliseconds}");
            }

        }
        static void Main(string[] args)
        {
            ImagesTest();
        }
    }
}
