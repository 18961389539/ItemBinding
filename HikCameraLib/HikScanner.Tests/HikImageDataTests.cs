using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace HikScanner.Tests;

/// <summary>
/// HikImageData 图像数据模型单元测试，覆盖 ToBitmap / ToJpegBytes 转换逻辑。
/// </summary>
public class HikImageDataTests
{
    // ─── ToBitmap: 空数据 ───

    [Fact]
    public void ToBitmap_NullRawData_Should_Return_Null()
    {
        var img = new HikImageData { RawData = null };
        Assert.Null(img.ToBitmap());
    }

    [Fact]
    public void ToBitmap_EmptyRawData_Should_Return_Null()
    {
        var img = new HikImageData { RawData = Array.Empty<byte>() };
        Assert.Null(img.ToBitmap());
    }

    // ─── ToBitmap: JPEG ───

    [Fact]
    public void ToBitmap_JpegData_Should_Return_Valid_Bitmap()
    {
        var jpegBytes = CreateTestJpeg(4, 4);
        var img = new HikImageData
        {
            RawData = jpegBytes,
            Width = 4,
            Height = 4,
            IsJpeg = true
        };

        using var bmp = img.ToBitmap();
        Assert.NotNull(bmp);
        Assert.Equal(4, bmp!.Width);
        Assert.Equal(4, bmp.Height);
    }

    // ─── ToBitmap: Mono8 ───

    [Fact]
    public void ToBitmap_Mono8Data_Should_Return_8bppIndexed_Bitmap()
    {
        byte[] data = new byte[8 * 4]; // 8x4 Mono8
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 10);

        var img = new HikImageData
        {
            RawData = data,
            Width = 8,
            Height = 4,
            IsMono8 = true
        };

        using var bmp = img.ToBitmap();
        Assert.NotNull(bmp);
        Assert.Equal(8, bmp!.Width);
        Assert.Equal(4, bmp.Height);
        Assert.Equal(PixelFormat.Format8bppIndexed, bmp.PixelFormat);
    }

    [Fact]
    public void ToBitmap_Mono8_Should_Have_Grayscale_Palette()
    {
        byte[] data = new byte[4 * 4]; // 宽度需 4 字节对齐（Bitmap stride 要求）
        var img = new HikImageData
        {
            RawData = data,
            Width = 4,
            Height = 4,
            IsMono8 = true
        };

        using var bmp = img.ToBitmap();
        Assert.NotNull(bmp);
        var palette = bmp!.Palette;
        for (int i = 0; i < 256; i++)
        {
            Assert.Equal(i, palette.Entries[i].R);
            Assert.Equal(i, palette.Entries[i].G);
            Assert.Equal(i, palette.Entries[i].B);
        }
    }

    // ─── ToBitmap: 非JPEG非Mono8 ───

    [Fact]
    public void ToBitmap_NeitherJpegNorMono8_Should_Return_Null()
    {
        var img = new HikImageData
        {
            RawData = new byte[] { 1, 2, 3, 4 },
            Width = 2,
            Height = 2,
            IsMono8 = false,
            IsJpeg = false
        };
        Assert.Null(img.ToBitmap());
    }

    // ─── ToJpegBytes: 空数据 ───

    [Fact]
    public void ToJpegBytes_NullRawData_Should_Return_Null()
    {
        var img = new HikImageData { RawData = null };
        Assert.Null(img.ToJpegBytes());
    }

    [Fact]
    public void ToJpegBytes_EmptyRawData_Should_Return_Null()
    {
        var img = new HikImageData { RawData = Array.Empty<byte>() };
        Assert.Null(img.ToJpegBytes());
    }

    // ─── ToJpegBytes: JPEG 直通 ───

    [Fact]
    public void ToJpegBytes_JpegData_Should_Return_Same_Reference()
    {
        var jpegBytes = CreateTestJpeg(4, 4);
        var img = new HikImageData
        {
            RawData = jpegBytes,
            Width = 4,
            Height = 4,
            IsJpeg = true
        };

        var result = img.ToJpegBytes();
        Assert.NotNull(result);
        Assert.Same(jpegBytes, result);
    }

    // ─── ToJpegBytes: Mono8 转 JPEG ───

    [Fact]
    public void ToJpegBytes_Mono8Data_Should_Return_Jpeg_Bytes()
    {
        byte[] data = new byte[8 * 8];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 256);

        var img = new HikImageData
        {
            RawData = data,
            Width = 8,
            Height = 8,
            IsMono8 = true
        };

        var result = img.ToJpegBytes();
        Assert.NotNull(result);
        Assert.True(result!.Length > 0);
        // JPEG 文件以 0xFF 0xD8 开头
        Assert.Equal(0xFF, result[0]);
        Assert.Equal(0xD8, result[1]);
    }

    // ─── ToJpegBytes: 非JPEG非Mono8 ───

    [Fact]
    public void ToJpegBytes_NeitherJpegNorMono8_Should_Return_Null()
    {
        var img = new HikImageData
        {
            RawData = new byte[] { 1, 2, 3 },
            Width = 1,
            Height = 1,
            IsMono8 = false,
            IsJpeg = false
        };
        Assert.Null(img.ToJpegBytes());
    }

    // ─── 辅助方法 ───

    private static byte[] CreateTestJpeg(int width, int height)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }
}
