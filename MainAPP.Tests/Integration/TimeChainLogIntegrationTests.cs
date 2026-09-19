using System.IO;
using System.Text.RegularExpressions;
using CoordinateSystemMapping;
using HikScanner;
using JinlongYolo.YoloSharp.Data;
using MainAPP.Application;
using MainAPP.Models;
using MainAPP.Services;
using Xunit;
using Size = SixLabors.ImageSharp.Size;

namespace MainAPP.Tests.Integration;

/// <summary>
/// 2026-09-18: 三时间链日志（[时间]）的集成验证——真实走 BuildAndSaveAsync，
/// 断言专用日志文件（timechain-YYYYMMDD.txt）中的行内容与格式。
/// 覆盖四个分支：全格式（正常绑定）/ 编码器回退标记 / 超龄标记 / GrabTime 缺失兜底。
/// </summary>
public class TimeChainLogIntegrationTests : IAsyncDisposable
{
    // 与 DetectionRecordServiceIntegrationTests 相同的生产同款接线
    private static readonly IDetectionRuntimeConfig ProductionLikeConfig = new DetectionRuntimeConfig(
        algorithm: () => Settings.Instance.Algorithm,
        currentRecipeName: () => RecipesManage.Instance.CurrentRecipe?.Name,
        stationCode: () => StationCodeProvider.Current);

    private readonly DetectionRecordService _service = new(
        BarcodeDataService.Instance,
        AngleTracker.Instance,
        ProductionLikeConfig);
    private readonly BarcodeDataService _dbService = BarcodeDataService.Instance;
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), $"TimeChainTest_{Guid.NewGuid():N}");

    public TimeChainLogIntegrationTests()
    {
        Directory.CreateDirectory(_tempFolder);
        using var ctx = new MainAPP.Models.AppDbContext();
        ctx.ApplySchemaSyncAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        try { await _dbService.ClearAllAsync(); } catch { }
        try { if (Directory.Exists(_tempFolder)) Directory.Delete(_tempFolder, recursive: true); } catch { }
    }

    private static FrameResult CreateFrameResult(uint frameNum)
    {
        const int width = 64, height = 64;
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
        return FrameResult.From(grabResult);
    }

    /// <summary>执行一次 BuildAndSaveAsync（空检测结果即可，[时间] 日志在检测循环之前无条件打），
    /// 返回 timechain 文件中匹配 lineFilter 特征串的 [时间] 行。</summary>
    private async Task<string> RunAndCaptureTimeLineAsync(FrameResult frameResult, string lineFilter)
    {
        var edgeResults = new YoloResult<Segmentation>(Array.Empty<Segmentation>())
        {
            ImageSize = new Size(1920, 1080),
            Speed = new SpeedResult(
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(2))
        };
        var transformer = new CoordinateTransformer();
        transformer.Initialize(
            new OpenCvSharp.Point2f(100, 100),
            new OpenCvSharp.Point2f(200, 100),
            new OpenCvSharp.Point2f(100, 200),
            distX: 0.5f, distY: 0.5f);

        try
        {
            await _service.BuildAndSaveAsync(
                frameResult, edgeResults, transformer,
                isResize: false, resizeWidth: 0, resizeHeight: 0,
                saveDraw: false, folder: _tempFolder, speed: 100, costTime: 50,
                angleSourceImage: null, anglePredictorPool: null, angleTool: null,
                offsetAngle: 0f, angleDetectionEnabled: false);
        }
        finally
        {
            frameResult.ReleaseImageData();
        }

        var logPath = Path.Combine(DataPaths.LogsDir, $"timechain-{DateTime.Now:yyyyMMdd}.txt");
        Assert.True(File.Exists(logPath), $"专用时间链日志应已生成: {logPath}");

        // Serilog 文件 sink 持有句柄（即使 shared 模式）——用 FileShare.ReadWrite 显式共享读
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().Split('\n');
        // 2026-09-18: 日志行已精简（无帧号）——按调用方给定的特征串定位本测试的行
        var line = lines.LastOrDefault(l => l.Contains("[时间]") && l.Contains(lineFilter));
        Assert.False(string.IsNullOrEmpty(line), $"timechain 日志中应含特征串「{lineFilter}」的 [时间] 行");
        return line!;
    }

    [Fact]
    public async Task TimeChainLog_FullFormat_WithFreshEncoderBinding()
    {
        const uint frameNum = 910001;
        var frameResult = CreateFrameResult(frameNum);
        var grabTime = DateTime.Now;
        frameResult.GrabTime = grabTime;
        // 编码器报文比图像早 60ms 到达（稳态：上报周期内），非超龄
        frameResult.EncoderReceivedTime = grabTime.AddMilliseconds(-60);
        frameResult.EncoderStale = false;
        // 2026-09-18: 带一个条码结果，验证设备端读码耗时（read-algo/read-total）进入时间链日志
        frameResult.Barcodes.Add(new HikBarcodeResult
        {
            Code = "TEST910001",
            AlgorithmCost = 30,
            TotalProcCost = 120,
            TriggerTime = grabTime.AddMilliseconds(-120),
            DeviceTriggerTime = grabTime.AddMilliseconds(-130)
        });
        frameResult.DeviceClockText = "2026-09-18 12:00:00";

        var line = await RunAndCaptureTimeLineAsync(frameResult, $"T_img={grabTime:HH:mm:ss.fff}");

        // T_img 在，超龄/未设置标记不在
        Assert.Contains("T_img=", line);
        Assert.DoesNotContain("T_img=未设置", line);
        Assert.DoesNotContain("[编码器超龄]", line);
        // 设备端读码耗时随条码结果进入
        Assert.Contains("read-algo=30ms", line);
        // 估算触发时刻进入
        Assert.Matches(new Regex(@"trig-est=\d{2}:\d{2}:\d{2}\.\d{3}"), line);
        // 2026-09-18: 设备墙钟进入日志（DeviceTime 参数，秒级）
        Assert.Contains("dev-clock=2026-09-18 12:00:00", line);
        // 2026-09-18: 精简格式锁定——以下字段按用户要求不再入行
        Assert.DoesNotContain("T_enc=", line);
        Assert.DoesNotContain("img-enc=", line);
        Assert.DoesNotContain("read-total=", line);
        Assert.DoesNotContain("dev-trig=", line);
        Assert.DoesNotContain("T_detect=", line);
        Assert.DoesNotContain("detect-img=", line);
        Assert.DoesNotContain("cost=", line);
        // 2026-09-19: dev-ts 按用户要求不再入行
        Assert.DoesNotContain("dev-ts=", line);
    }

    [Fact]
    public async Task TimeChainLog_EncoderFallback_Marked()
    {
        const uint frameNum = 910002;
        var frameResult = CreateFrameResult(frameNum);
        var grabTime = DateTime.Now;
        frameResult.GrabTime = grabTime;
        // 无编码器记录时 ResolveEncoderBinding 回退：T_enc = T_img（同值）
        frameResult.EncoderReceivedTime = grabTime;
        frameResult.EncoderStale = false;

        // 2026-09-18: 回退标记已随 T_enc 精简移除——回退信息由 EncodeTime=GrabTime 落库承载
        var line = await RunAndCaptureTimeLineAsync(frameResult, $"T_img={grabTime:HH:mm:ss.fff}");

        Assert.Contains("T_img=", line);
        Assert.DoesNotContain("回退", line);
    }

    [Fact]
    public async Task TimeChainLog_EncoderStale_Marked()
    {
        const uint frameNum = 910003;
        var frameResult = CreateFrameResult(frameNum);
        var grabTime = DateTime.Now;
        frameResult.GrabTime = grabTime;
        frameResult.EncoderReceivedTime = grabTime.AddMilliseconds(-900);
        frameResult.EncoderStale = true;

        var line = await RunAndCaptureTimeLineAsync(frameResult, "[编码器超龄]");

        Assert.Contains("[编码器超龄]", line);
    }

    [Fact]
    public async Task TimeChainLog_MissingGrabTime_ShowsPlaceholder()
    {
        // 测试直接构造 FrameResult（未经收图路径赋 GrabTime）→ default
        const uint frameNum = 910004;
        var frameResult = CreateFrameResult(frameNum);
        frameResult.EncoderReceivedTime = DateTime.Now;

        var line = await RunAndCaptureTimeLineAsync(frameResult, "T_img=未设置");

        Assert.Contains("T_img=未设置", line);
        Assert.DoesNotContain("dev-ts=", line); // 2026-09-19: dev-ts 按用户要求不再入行
        Assert.DoesNotContain("read-algo", line); // 无条码时省略
        Assert.DoesNotContain("trig-est", line);
    }
}
