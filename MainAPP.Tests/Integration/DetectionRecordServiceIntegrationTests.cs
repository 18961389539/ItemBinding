using CoordinateSystemMapping;
using HikScanner;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Memory;
using JinlongYolo.YoloSharp.Metadata;
using MainAPP.Application;
using MainAPP.Models;
using MainAPP.Services;
using OpenCvSharp;
using SixLabors.ImageSharp;
using Xunit;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Size = SixLabors.ImageSharp.Size;

namespace MainAPP.Tests.Integration;

/// <summary>
/// DetectionRecordService 集成测试。
/// 验证检测结果组装与数据库落库的完整流程，包括：
/// - YOLO 分割结果与条码识别结果的匹配绑定
/// - 坐标变换（图像坐标 → 世界坐标）
/// - 检测框超出图像范围的过滤
/// - 批量写入数据库
/// </summary>
public class DetectionRecordServiceIntegrationTests : IAsyncDisposable
{
    private readonly DetectionRecordService _service = new(
        BarcodeDataService.Instance,
        AngleTracker.Instance);
    private readonly BarcodeDataService _dbService = BarcodeDataService.Instance;
    // BuildAndSaveAsync 的 folder 参数即使 saveDraw=false 也会校验非空，使用临时目录满足约束
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"DetectionTest_{Guid.NewGuid():N}");

    public DetectionRecordServiceIntegrationTests()
    {
        Directory.CreateDirectory(_tempFolder);
    }

    public async ValueTask DisposeAsync()
    {
        // 测试后清理数据库中的测试数据
        try
        {
            await _dbService.ClearAllAsync();
        }
        catch
        {
            // 清理忽略异常
        }
        // 清理临时目录
        try { if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true); } catch { }
    }

    /// <summary>
    /// 创建一个已初始化的 CoordinateTransformer。
    /// 使用简单的标定参数：原点(100,100)，X轴方向(200,100)，Y轴方向(100,200)，
    /// 每像素物理距离 0.5mm。
    /// </summary>
    private static CoordinateTransformer CreateTransformer()
    {
        var transformer = new CoordinateTransformer();
        transformer.Initialize(
            new Point2f(100, 100),
            new Point2f(200, 100),
            new Point2f(100, 200),
            distX: 0.5,
            distY: 0.5);
        return transformer;
    }

    /// <summary>
    /// 创建一个包含图像数据的 FrameResult
    /// </summary>
    private static FrameResult CreateFrameResult(int width = 1920, int height = 1080, uint frameNum = 1, uint encoderValue = 1000)
    {
        var grabResult = new HikGrabResult
        {
            Status = HikGrabStatus.Success,
            Image = new HikImageData
            {
                Width = (uint)width,
                Height = (uint)height,
                FrameNum = frameNum,
                IsMono8 = true,
                RawData = new byte[width * height]
            }
        };
        var frameResult = FrameResult.From(grabResult);
        frameResult.EncoderValue = encoderValue;
        return frameResult;
    }

    /// <summary>
    /// 创建一个 Segmentation 检测结果
    /// </summary>
    private static Segmentation CreateSegmentation(int x, int y, int width, int height, float confidence = 0.9f)
    {
        // 创建掩码缓冲区，设置部分像素高于阈值
        var mask = new BitmapBuffer(width, height);
        for (int my = 0; my < height; my++)
        {
            for (int mx = 0; mx < width; mx++)
            {
                mask[my, mx] = 0.9f;
            }
        }

        return new Segmentation
        {
            Mask = mask,
            Bounds = new Rectangle(x, y, width, height),
            Name = new YoloName(0, "product"),
            Confidence = confidence
        };
    }

    /// <summary>
    /// 创建 YoloResult<Segmentation>
    /// </summary>
    private static YoloResult<Segmentation> CreateYoloResult(params Segmentation[] segments)
    {
        return new YoloResult<Segmentation>(segments)
        {
            ImageSize = new Size(1920, 1080),
            Speed = new SpeedResult(
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(2))
        };
    }

    [Fact]
    public async Task BuildAndSaveAsync_SingleDetection_NoBarcode_SavesToDatabase()
    {
        var frameResult = CreateFrameResult();
        var transformer = CreateTransformer();
        var edgeResults = CreateYoloResult(
            CreateSegmentation(100, 100, 200, 200));

        try
        {
            var buildResult = await _service.BuildAndSaveAsync(
                frameResult, edgeResults, transformer,
                isResize: false, resizeWidth: 0, resizeHeight: 0,
                saveDraw: false, folder: _tempFolder, speed: 100, costTime: 50,
                angleSourceImage: null, anglePredictorPool: null, angleTool: null, offsetAngle: 0f, angleDetectionEnabled: false);

            await _dbService.SavePendingAsync();

            Assert.Single(buildResult.ValidRecords);
            var record = buildResult.ValidRecords[0];
            // 无条码时应为 "noread"
            Assert.Equal("noread", record.Barcode);
            Assert.Equal(0, record.BarcodeScore);
            // Encode 值应从 FrameResult 传递
            Assert.Equal(1000u, record.Encode);
            Assert.Equal(100, record.Speed);
            Assert.Equal(50, record.CostTime);
            // 面积 = width * height
            Assert.Equal(200L * 200, record.Area);

            // 验证已写入数据库
            var dbRecord = await _dbService.GetByIdAsync(record.Id);
            Assert.NotNull(dbRecord);
            Assert.Equal("noread", dbRecord!.Barcode);
        }
        finally
        {
            frameResult.ReleaseImageData();
        }
    }

    [Fact]
    public async Task BuildAndSaveAsync_DetectionOutsideImageBounds_IsFiltered()
    {
        var frameResult = CreateFrameResult(width: 500, height: 500);
        var transformer = CreateTransformer();
        // 检测框远超图像边界（超出 BoundsTolerancePixels = 1000）
        var edgeResults = CreateYoloResult(
            CreateSegmentation(2000, 2000, 200, 200));

        try
        {
            var buildResult = await _service.BuildAndSaveAsync(
                frameResult, edgeResults, transformer,
                isResize: false, resizeWidth: 0, resizeHeight: 0,
                saveDraw: false, folder: _tempFolder, speed: 100, costTime: 50,
                angleSourceImage: null, anglePredictorPool: null, angleTool: null, offsetAngle: 0f, angleDetectionEnabled: false);

            // 超出边界的检测框应被过滤
            Assert.Empty(buildResult.ValidRecords);
            // P1-3: IndexedRecords 仍与 edgeResults 同序，被跳过项为 null
            Assert.Single(buildResult.IndexedRecords);
            Assert.Null(buildResult.IndexedRecords[0]);
        }
        finally
        {
            frameResult.ReleaseImageData();
        }
    }

    [Fact]
    public async Task BuildAndSaveAsync_MultipleDetections_SavesAllRecords()
    {
        var frameResult = CreateFrameResult();
        var transformer = CreateTransformer();
        var edgeResults = CreateYoloResult(
            CreateSegmentation(100, 100, 200, 200),
            CreateSegmentation(400, 400, 150, 150),
            CreateSegmentation(700, 700, 100, 100));

        try
        {
            var buildResult = await _service.BuildAndSaveAsync(
                frameResult, edgeResults, transformer,
                isResize: false, resizeWidth: 0, resizeHeight: 0,
                saveDraw: false, folder: _tempFolder, speed: 100, costTime: 50,
                angleSourceImage: null, anglePredictorPool: null, angleTool: null, offsetAngle: 0f, angleDetectionEnabled: false);

            await _dbService.SavePendingAsync();

            Assert.Equal(3, buildResult.ValidRecords.Count);
            // P1-3: IndexedRecords 应与 edgeResults 同序同长（3 项均非 null）
            Assert.Equal(3, buildResult.IndexedRecords.Count);
            Assert.All(buildResult.IndexedRecords, r => Assert.NotNull(r));
            // 验证所有记录都已写入数据库
            foreach (var record in buildResult.ValidRecords)
            {
                var dbRecord = await _dbService.GetByIdAsync(record.Id);
                Assert.NotNull(dbRecord);
            }
        }
        finally
        {
            frameResult.ReleaseImageData();
        }
    }

    [Fact]
    public async Task BuildAndSaveAsync_SaveDrawTrue_SetsImageFullName()
    {
        var frameResult = CreateFrameResult(frameNum: 42);
        var transformer = CreateTransformer();
        var edgeResults = CreateYoloResult(
            CreateSegmentation(100, 100, 200, 200));

        try
        {
            var buildResult = await _service.BuildAndSaveAsync(
                frameResult, edgeResults, transformer,
                isResize: false, resizeWidth: 0, resizeHeight: 0,
                saveDraw: true, folder: _tempFolder, speed: 100, costTime: 50,
                angleSourceImage: null, anglePredictorPool: null, angleTool: null, offsetAngle: 0f, angleDetectionEnabled: false);

            Assert.Single(buildResult.ValidRecords);
            // saveDraw=true 时 ImageFullName 应包含帧号和索引
            Assert.Contains("draw_42_0", buildResult.ValidRecords[0].ImageFullName);
            Assert.True(buildResult.ValidRecords[0].ImageFullName.EndsWith(".jpg"));
        }
        finally
        {
            frameResult.ReleaseImageData();
        }
    }

    [Fact]
    public async Task BuildAndSaveAsync_WithResize_ScalesCoordinates()
    {
        var frameResult = CreateFrameResult(width: 1920, height: 1080);
        var transformer = CreateTransformer();
        // 原始检测在 640x360 的推理图上，需要 resize 回 1920x1080
        var edgeResults = CreateYoloResult(
            CreateSegmentation(33, 33, 66, 66));

        try
        {
            var buildResult = await _service.BuildAndSaveAsync(
                frameResult, edgeResults, transformer,
                isResize: true, resizeWidth: 3, resizeHeight: 3,
                saveDraw: false, folder: _tempFolder, speed: 100, costTime: 50,
                angleSourceImage: null, anglePredictorPool: null, angleTool: null, offsetAngle: 0f, angleDetectionEnabled: false);

            Assert.Single(buildResult.ValidRecords);
            var record = buildResult.ValidRecords[0];
            // resize 后的边界：33*3=99, 66*3=198
            Assert.Equal(198, record.Width);
            Assert.Equal(198, record.Height);
            Assert.Equal(198L * 198, record.Area);
        }
        finally
        {
            frameResult.ReleaseImageData();
        }
    }

    [Fact]
    public async Task BuildAndSaveAsync_NullArguments_ThrowsArgumentNullException()
    {
        var frameResult = CreateFrameResult();
        var transformer = CreateTransformer();
        var edgeResults = CreateYoloResult();

        try
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _service.BuildAndSaveAsync(null!, edgeResults, transformer,
                    false, 0, 0, false, "", 0, 0, null, null, null, 0f, false));

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _service.BuildAndSaveAsync(frameResult, null!, transformer,
                    false, 0, 0, false, "", 0, 0, null, null, null, 0f, false));

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _service.BuildAndSaveAsync(frameResult, edgeResults, null!,
                    false, 0, 0, false, "", 0, 0, null, null, null, 0f, false));
        }
        finally
        {
            frameResult.ReleaseImageData();
        }
    }

    [Fact]
    public async Task BuildAndSaveAsync_MixedFilteredAndValid_IndexedRecordsKeepsOrder()
    {
        // P1-3: 验证 IndexedRecords 与 edgeResults 严格同序，被边界过滤的位置为 null，UI 据此跳过显示
        var frameResult = CreateFrameResult(width: 500, height: 500);
        var transformer = CreateTransformer();
        var edgeResults = CreateYoloResult(
            CreateSegmentation(100, 100, 50, 50),    // 索引 0：有效
            CreateSegmentation(2000, 2000, 100, 100),// 索引 1：超出边界，应跳过
            CreateSegmentation(300, 300, 80, 80));   // 索引 2：有效

        try
        {
            var buildResult = await _service.BuildAndSaveAsync(
                frameResult, edgeResults, transformer,
                isResize: false, resizeWidth: 0, resizeHeight: 0,
                saveDraw: false, folder: _tempFolder, speed: 100, costTime: 50,
                angleSourceImage: null, anglePredictorPool: null, angleTool: null, offsetAngle: 0f, angleDetectionEnabled: false);

            // ValidRecords 只含 2 条（过滤掉索引 1）
            Assert.Equal(2, buildResult.ValidRecords.Count);
            // IndexedRecords 长度与 edgeResults 相同（3 条），保持同序
            Assert.Equal(3, buildResult.IndexedRecords.Count);
            // 索引 0 与 2 非空，索引 1 为 null
            Assert.NotNull(buildResult.IndexedRecords[0]);
            Assert.Null(buildResult.IndexedRecords[1]);
            Assert.NotNull(buildResult.IndexedRecords[2]);
            // 索引 0 的记录与 ValidRecords[0] 一致（同一个对象引用）
            Assert.Same(buildResult.IndexedRecords[0], buildResult.ValidRecords[0]);
            // 索引 2 的记录与 ValidRecords[1] 一致
            Assert.Same(buildResult.IndexedRecords[2], buildResult.ValidRecords[1]);
        }
        finally
        {
            frameResult.ReleaseImageData();
        }
    }
}
