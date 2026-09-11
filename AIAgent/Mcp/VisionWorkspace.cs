using HikScanner;
using HikScannerType = HikScanner.HikScanner;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Metadata;
using JinlongYolo.YoloSharp.Plotting;
using SixLabors.ImageSharp;
using AnyImage = SixLabors.ImageSharp.Image;
using RgbImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ViewerControl = global::ImageViewer.Controls.ImageViewer;

namespace AIAgent.Mcp;

internal static class VisionWorkspace
{
    private const string LocalMcpUrl = "http://127.0.0.1:32145";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<string, YoloPredictor> Predictors = new(StringComparer.OrdinalIgnoreCase);

    private static ViewerControl? _viewer;
    private static HikScannerType? _scanner;
    private static HikGrabResult? _latestCapture;
    private static RgbImage? _latestImage;

    public static string McpUrl => LocalMcpUrl;

    public static async Task InitializeAsync(ViewerControl? viewer = null)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _viewer = viewer;
            await EnsureScannerInitializedAsync().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task ShutdownAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            DisposeScanner();
            DisposePredictors();

            _latestCapture = null;
            _latestImage?.Dispose();
            _latestImage = null;
            _viewer = null;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<ScannerSettingsDto> AdjustScannerExposureGainAsync(double exposureTime, double gain)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var scanner = await EnsureScannerInitializedAsync().ConfigureAwait(false);
            if (scanner is null)
            {
                return new ScannerSettingsDto
                {
                    Success = false,
                    Message = "未发现扫码枪"
                };
            }

            scanner.SetExposureTime((float)exposureTime);
            scanner.SetGain((float)gain);

            return new ScannerSettingsDto
            {
                Success = true,
                Message = "已更新扫码枪曝光和增益",
                DeviceName = scanner.DeviceInfo.UserDefinedName ?? scanner.DeviceInfo.ModelName,
                SerialNumber = scanner.DeviceInfo.SerialNumber,
                ExposureTime = scanner.GetExposureTime(),
                Gain = scanner.GetGain(),
                TriggerMode = scanner.GetTriggerMode() ? "On" : "Off",
                TriggerSource = scanner.GetTriggerSource() ? "Software" : "Hardware"
            };
        }
        catch (Exception ex)
        {
            return new ScannerSettingsDto
            {
                Success = false,
                Message = ex.Message
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<ScannerDeviceInfoDto> GetScannerDeviceInfoAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var scanner = await EnsureScannerInitializedAsync().ConfigureAwait(false);
            if (scanner is null)
            {
                return new ScannerDeviceInfoDto
                {
                    Success = false,
                    Message = "未发现扫码枪"
                };
            }

            var deviceInfo = scanner.DeviceInfo;
            return new ScannerDeviceInfoDto
            {
                Success = true,
                Message = "已读取设备信息",
                Index = 0,
                ModelName = deviceInfo.ModelName,
                SerialNumber = deviceInfo.SerialNumber,
                UserDefinedName = deviceInfo.UserDefinedName,
                DeviceVersion = deviceInfo.DeviceVersion,
                MacAddress = deviceInfo.MacAddress,
                IpAddress = deviceInfo.CurrentIp,
                SubnetMask = deviceInfo.SubnetMask,
                DefaultGateway = deviceInfo.DefaultGateway,
                InterfaceType = !string.IsNullOrEmpty(deviceInfo.CurrentIp) ? "GigE" : "USB",
                IsOpen = scanner.IsConnected,
                IsGrabbing = scanner.IsGrabbing
            };
        }
        catch (Exception ex)
        {
            return new ScannerDeviceInfoDto
            {
                Success = false,
                Message = ex.Message
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<ScannerSettingsDto> GetScannerSettingsAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var scanner = await EnsureScannerInitializedAsync().ConfigureAwait(false);
            if (scanner is null)
            {
                return new ScannerSettingsDto
                {
                    Success = false,
                    Message = "未发现扫码枪"
                };
            }

            return new ScannerSettingsDto
            {
                Success = true,
                Message = "已读取当前设置",
                DeviceName = scanner.DeviceInfo.UserDefinedName ?? scanner.DeviceInfo.ModelName,
                SerialNumber = scanner.DeviceInfo.SerialNumber,
                ExposureTime = scanner.GetExposureTime(),
                Gain = scanner.GetGain(),
                TriggerMode = scanner.GetTriggerMode() ? "On" : "Off",
                TriggerSource = scanner.GetTriggerSource() ? "Software" : "Hardware"
            };
        }
        catch (Exception ex)
        {
            return new ScannerSettingsDto
            {
                Success = false,
                Message = ex.Message
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task<ScannerCountDto> GetScannerCountAsync()
    {
        try
        {
            var devices = HikScannerType.EnumerateDevices(HikDeviceType.GigE);
            return Task.FromResult(new ScannerCountDto
            {
                Success = true,
                Message = devices.Count == 0 ? "未发现扫码枪" : "已获取扫码枪数量",
                Count = devices.Count
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ScannerCountDto
            {
                Success = false,
                Message = ex.Message,
                Count = 0
            });
        }
    }

    public static async Task<CaptureDto> CaptureAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var captureState = await CaptureCoreAsync().ConfigureAwait(false);
            if (captureState is null)
            {
                return new CaptureDto
                {
                    Success = false,
                    Message = "未发现可用扫码枪"
                };
            }

            string savedImagePath = string.Empty;
            bool autoSaved = false;
            string message;

            try
            {
                savedImagePath = await SaveCaptureImageAsync(captureState.Value.Capture, captureState.Value.Image).ConfigureAwait(false);
                autoSaved = true;
                message = $"拍照成功，已自动保存到 {savedImagePath}";
            }
            catch (Exception saveEx)
            {
                message = $"拍照成功，但自动保存失败: {saveEx.Message}";
            }

            await ShowImageAsync(captureState.Value.Image).ConfigureAwait(false);
            return CreateCaptureDto(captureState.Value.Capture, savedImagePath, autoSaved, message);
        }
        catch (Exception ex)
        {
            return new CaptureDto
            {
                Success = false,
                Message = ex.Message
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<OverlayDto> DrawQrOverlayAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var captureState = await GetOrCaptureAsync(captureIfMissing: true).ConfigureAwait(false);
            if (captureState is null)
            {
                return new OverlayDto
                {
                    Success = false,
                    Message = "未发现可用扫码枪"
                };
            }

            var qrBarcodes = captureState.Value.Capture.Barcodes
                .Where(barcode => barcode.IsQrCode())
                .ToArray();

            if (qrBarcodes.Length == 0)
            {
                await ShowImageAsync(captureState.Value.Image).ConfigureAwait(false);
                return new OverlayDto
                {
                    Success = true,
                    Message = "当前图像未发现二维码",
                    QrCount = 0,
                    QrCodes = []
                };
            }

            using var annotated = CreateQrOverlayImage(captureState.Value.Image, qrBarcodes);
            await ShowImageAsync(annotated).ConfigureAwait(false);

            return new OverlayDto
            {
                Success = true,
                Message = "已将二维码结果绘制到 ImageViewer",
                QrCount = qrBarcodes.Length,
                QrCodes = qrBarcodes.Select(ToBarcodeDto).ToArray()
            };
        }
        catch (Exception ex)
        {
            return new OverlayDto
            {
                Success = false,
                Message = ex.Message
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<InferenceDto> RunYoloInferenceAsync(string modelPath)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var captureState = await GetOrCaptureAsync(captureIfMissing: true).ConfigureAwait(false);
            if (captureState is null)
            {
                return new InferenceDto
                {
                    Success = false,
                    Message = "未发现可用扫码枪"
                };
            }

            var fullPath = ResolveModelPath(modelPath);
            if (!File.Exists(fullPath))
            {
                return new InferenceDto
                {
                    Success = false,
                    Message = $"模型文件不存在: {fullPath}",
                    ModelPath = fullPath
                };
            }

            var predictor = GetOrCreatePredictor(fullPath);
            using var inputImage = captureState.Value.Image.Clone();

            var inferenceDto = await RunInferenceAsync(predictor, inputImage, fullPath).ConfigureAwait(false);
            return inferenceDto;
        }
        catch (Exception ex)
        {
            return new InferenceDto
            {
                Success = false,
                Message = ex.Message,
                ModelPath = modelPath
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    private static string ResolveModelPath(string modelPath)
    {
        // REVIEW-FIX: 只允许加载 Models 目录（AppContext.BaseDirectory 下 Models/）内的 .onnx 文件，
        // 防止 MCP 被非法调用时通过任意路径加载模型。
        var modelsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Models"));

        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            string fullPath;
            if (Path.IsPathRooted(modelPath))
            {
                fullPath = Path.GetFullPath(modelPath);
            }
            else
            {
                var candidatePaths = new[]
                {
                    Path.Combine(modelsRoot, modelPath),
                    Path.Combine(AppContext.BaseDirectory, modelPath),
                    Path.Combine(Environment.CurrentDirectory, modelPath),
                    Path.Combine(Environment.CurrentDirectory, "Models", modelPath)
                };

                var candidate = candidatePaths.FirstOrDefault(File.Exists);
                fullPath = Path.GetFullPath(candidate ?? Path.Combine(modelsRoot, modelPath));
            }

            // REVIEW-FIX: 白名单校验——路径规范化后必须在 Models 目录内，且扩展名为 .onnx
            ValidateModelPath(fullPath, modelsRoot);
            return fullPath;
        }

        // 默认模型：从 Models 目录选择第一个 .onnx
        if (Directory.Exists(modelsRoot))
        {
            var preferredModel = Directory.EnumerateFiles(modelsRoot, "*.onnx", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(preferredModel))
            {
                return Path.GetFullPath(preferredModel);
            }
        }

        throw new ArgumentException($"未找到 .onnx 模型，请将模型文件放入 {modelsRoot} 目录", nameof(modelPath));
    }

    /// <summary>REVIEW-FIX: 模型白名单校验，路径规范化后必须位于允许目录内且扩展名为 .onnx</summary>
    private static void ValidateModelPath(string fullPath, string modelsRoot)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        var normalizedRoot = Path.GetFullPath(modelsRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!string.Equals(Path.GetExtension(normalizedPath), ".onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"只允许加载 .onnx 模型文件: {normalizedPath}", nameof(fullPath));
        }

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"模型文件必须在 Models 目录（{modelsRoot}）内: {normalizedPath}", nameof(fullPath));
        }
    }

    public static async Task<ImageProcessDto> ProcessLatestImageAsync(string operation, double amount = 0)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var captureState = await GetOrCaptureAsync(captureIfMissing: true).ConfigureAwait(false);
            if (captureState is null)
            {
                return new ImageProcessDto
                {
                    Success = false,
                    Message = "未发现可用扫码枪"
                };
            }

            using var processed = ApplyImageOperation(captureState.Value.Image, operation, amount);
            await ShowImageAsync(processed).ConfigureAwait(false);

            return new ImageProcessDto
            {
                Success = true,
                Message = "图像处理完成",
                Operation = operation,
                Amount = amount,
                Width = processed.Width,
                Height = processed.Height
            };
        }
        catch (Exception ex)
        {
            return new ImageProcessDto
            {
                Success = false,
                Message = ex.Message,
                Operation = operation,
                Amount = amount
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<(HikGrabResult Capture, RgbImage Image)?> CaptureCoreAsync()
    {
        var scanner = await EnsureScannerInitializedAsync().ConfigureAwait(false);
        if (scanner is null)
        {
            return null;
        }

        scanner.ExecuteSoftwareTrigger();
        // REVIEW-FIX: 显式传 10 秒超时。原调用使用默认 1 小时超时，且此处全程持有全局
        // Gate（SemaphoreSlim 1,1），抓拍卡死会阻塞所有 MCP 工具及 UI 状态刷新长达 1 小时。
        var capture = await scanner.GetImageAsync(10_000).ConfigureAwait(false);

        _latestCapture = capture;
        _latestImage?.Dispose();
        _latestImage = ConvertCaptureToRgbImage(capture);

        return (capture, _latestImage);
    }

    private static async Task<(HikGrabResult Capture, RgbImage Image)?> GetOrCaptureAsync(bool captureIfMissing)
    {
        if (_latestCapture is not null && _latestImage is not null)
        {
            return (_latestCapture, _latestImage);
        }

        if (!captureIfMissing)
        {
            return null;
        }

        return await CaptureCoreAsync().ConfigureAwait(false);
    }

    private static async Task<HikScannerType?> EnsureScannerInitializedAsync()
    {
        if (_scanner is not null)
        {
            return _scanner;
        }

        var devices = HikScannerType.EnumerateDevices(HikDeviceType.GigE);
        if (devices.Count == 0)
        {
            Debug.WriteLine("启动时未发现扫码枪");
            return null;
        }

        foreach (var deviceInfo in devices)
        {
            HikScannerType? candidate = null;
            try
            {
                candidate = new HikScannerType();
                candidate.Connect(deviceInfo);
                candidate.SwitchToSoftwareTrigger();

                _scanner = candidate;
                Debug.WriteLine($"已自动启用扫码枪: {deviceInfo.ModelName} ({deviceInfo.SerialNumber})");
                return _scanner;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"扫码枪初始化失败: {deviceInfo.SerialNumber}，{ex.Message}");
                candidate?.Dispose();
            }
        }

        Debug.WriteLine("已发现扫码枪，但没有任何一个成功打开");
        return null;
    }

    private static void DisposeScanner()
    {
        _scanner?.Dispose();
        _scanner = null;
    }

    private static void DisposePredictors()
    {
        foreach (var predictor in Predictors.Values)
        {
            predictor.Dispose();
        }

        Predictors.Clear();
    }

    private static YoloPredictor GetOrCreatePredictor(string modelPath)
    {
        if (Predictors.TryGetValue(modelPath, out var predictor))
        {
            return predictor;
        }

        predictor = new YoloPredictor(modelPath);
        Predictors.Add(modelPath, predictor);
        return predictor;
    }

    private static async Task<InferenceDto> RunInferenceAsync(YoloPredictor predictor, RgbImage image, string modelPath)
    {
        return predictor.Metadata.Task switch
        {
            YoloTask.Detect => await BuildInferenceAsync(modelPath, predictor, image, static (p, i) => p.Detect(i)).ConfigureAwait(false),
            YoloTask.Obb => await BuildInferenceAsync(modelPath, predictor, image, static (p, i) => p.DetectObb(i)).ConfigureAwait(false),
            YoloTask.Segment => await BuildInferenceAsync(modelPath, predictor, image, static (p, i) => p.Segment(i)).ConfigureAwait(false),
            YoloTask.Classify => await BuildInferenceAsync(modelPath, predictor, image, static (p, i) => p.Classify(i)).ConfigureAwait(false),
            YoloTask.Pose => await BuildInferenceAsync(modelPath, predictor, image, static (p, i) => p.Pose(i)).ConfigureAwait(false),
            _ => new InferenceDto
            {
                Success = false,
                Message = $"不支持的 YOLO 任务: {predictor.Metadata.Task}",
                ModelPath = modelPath,
                Task = predictor.Metadata.Task.ToString()
            }
        };
    }

    private static async Task<InferenceDto> BuildInferenceAsync<T>(string modelPath, YoloPredictor predictor, RgbImage image, Func<YoloPredictor, RgbImage, YoloResult<T>> infer) where T : YoloPrediction, IYoloPrediction<T>
    {
        var result = infer(predictor, image);
        var topPrediction = result.OrderByDescending(prediction => prediction.Confidence).FirstOrDefault();
        if (topPrediction is null)
        {
            return new InferenceDto
            {
                Success = false,
                Message = "未找到可绘制的目标",
                ModelPath = modelPath,
                Task = predictor.Metadata.Task.ToString(),
                PredictionCount = 0,
                Summary = string.Empty,
                Predictions = []
            };
        }

        var topOnlyResult = new YoloResult<T>(new[] { topPrediction })
        {
            ImageSize = result.ImageSize,
            Speed = result.Speed
        };

        using var plotted = topOnlyResult.PlotImage(image);
        await ShowImageAsync(plotted).ConfigureAwait(false);

        return new InferenceDto
        {
            Success = true,
            Message = "YOLO 推理完成，仅绘制置信度最高的目标到 ImageViewer",
            ModelPath = modelPath,
            Task = predictor.Metadata.Task.ToString(),
            PredictionCount = 1,
            Summary = topOnlyResult.ToString(),
            Predictions = [topPrediction.ToString() ?? string.Empty]
        };
    }

    private static ImageProcessDto ProcessResult(RgbImage image, string operation, double amount)
    {
        return new ImageProcessDto
        {
            Success = true,
            Message = "图像处理完成",
            Operation = operation,
            Amount = amount,
            Width = image.Width,
            Height = image.Height
        };
    }

    private static RgbImage ApplyImageOperation(RgbImage source, string operation, double amount)
    {
        var image = source.Clone();
        var normalizedOperation = operation.Trim().ToLowerInvariant();

        image.Mutate(context =>
        {
            switch (normalizedOperation)
            {
                case "grayscale":
                    context.Grayscale();
                    break;
                case "invert":
                    context.Invert();
                    break;
                case "threshold":
                    context.BinaryThreshold((float)Math.Clamp(amount <= 1 ? amount : amount / 255.0, 0.0, 1.0));
                    break;
                case "blur":
                    context.GaussianBlur((float)Math.Max(0.1, amount <= 0 ? 1.0 : amount));
                    break;
                case "rotate90":
                    context.Rotate(RotateMode.Rotate90);
                    break;
                case "rotate180":
                    context.Rotate(RotateMode.Rotate180);
                    break;
                case "rotate270":
                    context.Rotate(RotateMode.Rotate270);
                    break;
                case "fliphorizontal":
                    context.Flip(FlipMode.Horizontal);
                    break;
                case "flipvertical":
                    context.Flip(FlipMode.Vertical);
                    break;
                default:
                    throw new ArgumentException("不支持的图像处理操作", nameof(operation));
            }
        });

        return image;
    }

    private static RgbImage ConvertCaptureToRgbImage(HikGrabResult capture)
    {
        var image = capture.Image;
        if (image is null || image.RawData is null)
        {
            return new RgbImage(1, 1);
        }

        var width = (int)image.Width;
        var height = (int)image.Height;

        if (image.IsJpeg)
        {
            return AnyImage.Load<Rgb24>(image.RawData);
        }

        if (image.IsMono8)
        {
            return AnyImage.LoadPixelData<L8>(image.RawData, width, height).CloneAs<Rgb24>();
        }

        // 非 jpeg、非 mono8 视作 RGB8 Packed
        return AnyImage.LoadPixelData<Rgb24>(image.RawData, width, height);
    }

    private static BarcodeDto ToBarcodeDto(HikBarcodeResult result)
    {
        return new BarcodeDto
        {
            CodeType = result.CodeTypeName ?? string.Empty,
            CodeString = result.CodeString(),
            Confidence = result.Confidence(),
            Location = result.Location().Select(point => new PointDto { X = point.X, Y = point.Y }).ToArray()
        };
    }

    private static CaptureDto CreateCaptureDto(HikGrabResult capture, string savedImagePath, bool autoSaved, string message)
    {
        var barcodes = capture.Barcodes ?? new();
        var allBarcodes = barcodes.Select(ToBarcodeDto).ToArray();
        var qrBarcodes = barcodes
            .Where(barcode => barcode.IsQrCode())
            .Select(ToBarcodeDto)
            .ToArray();

        var image = capture.Image;
        var pixelFormat = image is null ? "Unknown"
            : image.IsJpeg ? "Jpeg"
            : image.IsMono8 ? "Mono8"
            : "RGB8_Packed";

        return new CaptureDto
        {
            Success = true,
            Message = message,
            Width = (int)(image?.Width ?? 0),
            Height = (int)(image?.Height ?? 0),
            PixelFormat = pixelFormat,
            FrameNumber = image?.FrameNum ?? 0,
            AutoSaved = autoSaved,
            SavedImagePath = savedImagePath,
            Barcodes = allBarcodes,
            QrCodes = qrBarcodes
        };
    }

    private static async Task<string> SaveCaptureImageAsync(HikGrabResult capture, RgbImage image)
    {
        var directory = GetCaptureDirectory();
        Directory.CreateDirectory(directory);

        var fileName = BuildCaptureFileName(capture);
        var fullPath = Path.Combine(directory, fileName);

        await Task.Run(() =>
        {
            using var fileStream = File.Create(fullPath);
            image.SaveAsPng(fileStream);
        }).ConfigureAwait(false);

        return fullPath;
    }

    private static string GetCaptureDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "CapturedImages");
    }

    private static string BuildCaptureFileName(HikGrabResult capture)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var serial = SanitizePathPart(_scanner?.DeviceInfo?.SerialNumber);
        return $"capture_{timestamp}_f{capture.Image?.FrameNum ?? 0}_{serial}.png";
    }

    private static string SanitizePathPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "scanner";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "scanner" : sanitized;
    }

    private static async Task ShowImageAsync(AnyImage image)
    {
        if (_viewer is null)
        {
            return;
        }

        var bitmap = CreateBitmapImage(image);
        await _viewer.Dispatcher.InvokeAsync(() => _viewer.ImageSource = bitmap).Task.ConfigureAwait(false);
    }

    private static async Task ShowImageAsync(Bitmap bitmap)
    {
        if (_viewer is null)
        {
            return;
        }

        var bitmapSource = CreateBitmapImage(bitmap);
        await _viewer.Dispatcher.InvokeAsync(() => _viewer.ImageSource = bitmapSource).Task.ConfigureAwait(false);
    }

    private static BitmapImage CreateBitmapImage(AnyImage image)
    {
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        stream.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapImage CreateBitmapImage(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        return bitmapImage;
    }

    private static Bitmap CreateQrOverlayImage(RgbImage sourceImage, IReadOnlyList<HikBarcodeResult> qrBarcodes)
    {
        var baseBitmap = CreateBitmap(sourceImage);
        var overlay = new Bitmap(baseBitmap.Width, baseBitmap.Height, PixelFormat.Format32bppArgb);

        using (baseBitmap)
        using (var graphics = Graphics.FromImage(overlay))
        using (var pen = new Pen(System.Drawing.Color.LimeGreen, Math.Max(3f, Math.Min(baseBitmap.Width, baseBitmap.Height) / 200f)))
        using (var textBrush = new SolidBrush(System.Drawing.Color.White))
        using (var backgroundBrush = new SolidBrush(System.Drawing.Color.FromArgb(180, 0, 0, 0)))
        using (var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawImage(baseBitmap, 0, 0, baseBitmap.Width, baseBitmap.Height);

            foreach (var barcode in qrBarcodes)
            {
                var points = barcode.Location().Select(point => new System.Drawing.Point(point.X, point.Y)).ToArray();
                if (points.Length >= 3)
                {
                    graphics.DrawPolygon(pen, points);
                }

                var label = $"{barcode.CodeTypeName}: {barcode.CodeString()}";
                var minX = points.Min(point => point.X);
                var minY = points.Min(point => point.Y);
                var textPosition = new System.Drawing.PointF(minX, Math.Max(0, minY - 24));
                var textSize = graphics.MeasureString(label, font);

                graphics.FillRectangle(backgroundBrush, textPosition.X, textPosition.Y, textSize.Width + 8, textSize.Height + 4);
                graphics.DrawString(label, font, textBrush, textPosition.X + 4, textPosition.Y + 2);
            }
        }

        return overlay;
    }

    private static Bitmap CreateBitmap(AnyImage sourceImage)
    {
        using var stream = new MemoryStream();
        sourceImage.SaveAsPng(stream);
        stream.Position = 0;

        using var temporaryBitmap = new Bitmap(stream);
        return new Bitmap(temporaryBitmap);
    }
}