using System.Drawing;
using BenchmarkDotNet.Attributes;
using HikScanner;

namespace HikScannerBenchmarks;

/// <summary>
/// DetectionBoxHelper 坐标缩放与绘制性能基准测试。
/// 覆盖条码/OCR/面单的坐标映射和 GDI+ 绘制热点。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class DetectionBoxBenchmarks
{
    private HikBarcodeResult _barcode;
    private HikOcrResult _ocr;
    private HikWaybillResult _waybill;
    private Bitmap _canvas;
    private Graphics _graphics;

    private const uint ImageWidth = 1920;
    private const uint ImageHeight = 1080;
    private static readonly Size DisplaySize = new(960, 540);

    [Params(1, 10, 50)]
    public int DetectionCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _barcode = new HikBarcodeResult
        {
            BoundingPoints = new PointF[]
            {
                new(100, 100), new(300, 100), new(300, 200), new(100, 200)
            }
        };

        _ocr = new HikOcrResult
        {
            CenterX = 960, CenterY = 540, Width = 200, Height = 60, Angle = 0f
        };

        _waybill = new HikWaybillResult
        {
            CenterX = 960, CenterY = 540, Width = 400, Height = 300, Angle = 15f
        };

        _canvas = new Bitmap(DisplaySize.Width, DisplaySize.Height);
        _graphics = Graphics.FromImage(_canvas);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _graphics.Dispose();
        _canvas.Dispose();
    }

    [Benchmark(Description = "ScaleBarcodeBounds ×N (坐标缩放)")]
    public PointF[] ScaleBarcodeBounds()
    {
        PointF[] result = null;
        for (int i = 0; i < DetectionCount; i++)
            result = DetectionBoxHelper.ScaleBarcodeBounds(_barcode, ImageWidth, ImageHeight, DisplaySize);
        return result;
    }

    [Benchmark(Description = "ScaleOcrBounds ×N (坐标缩放)")]
    public RectangleF ScaleOcrBounds()
    {
        RectangleF result = default;
        for (int i = 0; i < DetectionCount; i++)
            result = DetectionBoxHelper.ScaleOcrBounds(_ocr, ImageWidth, ImageHeight, DisplaySize);
        return result;
    }

    [Benchmark(Description = "ScaleWaybillBounds ×N (坐标缩放)")]
    public RectangleF ScaleWaybillBounds()
    {
        RectangleF result = default;
        for (int i = 0; i < DetectionCount; i++)
            result = DetectionBoxHelper.ScaleWaybillBounds(_waybill, ImageWidth, ImageHeight, DisplaySize);
        return result;
    }

    [Benchmark(Description = "DrawBarcode ×N (GDI+ 绘制)")]
    public void DrawBarcode()
    {
        for (int i = 0; i < DetectionCount; i++)
            DetectionBoxHelper.DrawBarcode(_graphics, _barcode, ImageWidth, ImageHeight, DisplaySize);
    }

    [Benchmark(Description = "DrawOcr ×N (GDI+ 旋转绘制)")]
    public void DrawOcr()
    {
        for (int i = 0; i < DetectionCount; i++)
            DetectionBoxHelper.DrawOcr(_graphics, _ocr, ImageWidth, ImageHeight, DisplaySize);
    }

    [Benchmark(Description = "DrawWaybill ×N (GDI+ 旋转绘制)")]
    public void DrawWaybill()
    {
        for (int i = 0; i < DetectionCount; i++)
            DetectionBoxHelper.DrawWaybill(_graphics, _waybill, ImageWidth, ImageHeight, DisplaySize);
    }
}
