using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using BenchmarkDotNet.Attributes;
using HikScanner;
using HikScannerLib = HikScanner.HikScanner;

namespace HikScannerBenchmarks;

/// <summary>
/// HikImageData 图像转换性能基准测试。
/// 覆盖 Mono8→Bitmap、JPEG→Bitmap、Mono8→JPEG 等图像处理热点路径。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class ImageDataBenchmarks
{
    private HikImageData _mono8Image;
    private HikImageData _jpegImage;
    private byte[] _jpegBytes;

    [Params(128, 512, 1024)]
    public int ImageSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Mono8 灰度图像（宽度 4 字节对齐）
        int width = ImageSize;
        int height = ImageSize;
        // 确保 stride 对齐
        if (width % 4 != 0) width += 4 - (width % 4);

        var mono8Data = new byte[width * height];
        var rand = new Random(42);
        rand.NextBytes(mono8Data);

        _mono8Image = new HikImageData
        {
            RawData = mono8Data,
            Width = (uint)width,
            Height = (uint)height,
            IsMono8 = true,
            IsJpeg = false
        };

        // 生成等价 JPEG 图像
        using (var bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed))
        {
            var palette = bmp.Palette;
            for (int i = 0; i < 256; i++)
                palette.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = palette;

            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            System.Runtime.InteropServices.Marshal.Copy(mono8Data, 0, bmpData.Scan0, mono8Data.Length);
            bmp.UnlockBits(bmpData);

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Jpeg);
            _jpegBytes = ms.ToArray();
        }

        _jpegImage = new HikImageData
        {
            RawData = _jpegBytes,
            Width = (uint)ImageSize,
            Height = (uint)ImageSize,
            IsMono8 = false,
            IsJpeg = true
        };
    }

    [Benchmark(Description = "Mono8 → Bitmap (灰度图转换)")]
    public Bitmap ToBitmap_Mono8()
    {
        var bmp = _mono8Image.ToBitmap();
        bmp.Dispose();
        return bmp;
    }

    [Benchmark(Description = "JPEG → Bitmap (JPEG解码)")]
    public Bitmap ToBitmap_Jpeg()
    {
        var bmp = _jpegImage.ToBitmap();
        bmp.Dispose();
        return bmp;
    }

    [Benchmark(Description = "Mono8 → JPEG 字节 (编码)")]
    public byte[] ToJpegBytes_Mono8()
    {
        return _mono8Image.ToJpegBytes();
    }

    [Benchmark(Description = "JPEG 直通 (零拷贝引用返回)")]
    public byte[] ToJpegBytes_JpegPassthrough()
    {
        return _jpegImage.ToJpegBytes();
    }
}
