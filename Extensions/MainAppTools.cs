using HikScanner;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Extensions
{
    /// <summary>
    /// MainAPP 通用工具方法（从 MainAPP.Services.Tools 迁移合并）。
    /// 提供 ImageSharp → WriteableBitmap 转换及条码结果绘制功能。
    /// </summary>
    public static class Tools
    {
        // L262: 条码绘制相关的魔法数字提取为命名常量
        private const int ScoreTextYOffset = 30;
        private const int BarcodeTextYOffset = 80;
        private const double DrawFontScale = 1.0;
        private const int DrawThickness = 2;

        public static WriteableBitmap UpdateShow(Image<Rgb24> image, WriteableBitmap? existing = null)
        {
            ArgumentNullException.ThrowIfNull(image);

            var pixelFormat = PixelFormats.Bgr24;
            int stride = (image.Width * (pixelFormat.BitsPerPixel / 8) + 3) / 4 * 4; // DWORD aligned
            int bufferLength = image.Height * stride;

            // REVIEW-FIX (显示回归修复 v2): WriteableBitmap 继承 DispatcherObject，**未冻结时
            // 只能在创建线程（UI 线程）上访问，包括 WritePixels**——任意线程直接写会抛
            // "调用线程无法访问此对象，因为另一个线程拥有该对象"（日志已证实）。
            // 正确分工：像素转换（ProcessPixelRows，耗时部分）留在后台线程；
            // 位图创建/复用 + WritePixels 都提升到 UI 线程（Dispatcher 队列天然串行化并发线程，
            // 无需外部锁）。
            var buffer = ArrayPool<byte>.Shared.Rent(bufferLength);
            try
            {
                // 后台线程：ImageSharp → BGR24 字节（本方法唯一耗时部分）
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgb24> sourceRow = accessor.GetRowSpan(y);
                        int rowOffset = y * stride;
                        for (int x = 0; x < accessor.Width; x++)
                        {
                            ref Rgb24 srcPixel = ref sourceRow[x];
                            int idx = rowOffset + x * 3;
                            // 写入 B, G, R 顺序匹配 Bgr24
                            buffer[idx + 0] = srcPixel.B;
                            buffer[idx + 1] = srcPixel.G;
                            buffer[idx + 2] = srcPixel.R;
                        }
                    }
                });

                // UI 线程：创建/复用 + WritePixels（WritePixels 内部加锁并标记脏区域）
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    return dispatcher.Invoke(() => WritePixelsOnUiThread(width: image.Width, height: image.Height,
                        pixelFormat, existing, buffer, stride));
                }
                return WritePixelsOnUiThread(image.Width, image.Height, pixelFormat, existing, buffer, stride);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// 必须在 UI 线程执行：复用或创建位图并写入像素（WriteableBitmap 未冻结时的线程约束）。
        /// </summary>
        private static WriteableBitmap WritePixelsOnUiThread(int width, int height, PixelFormat pixelFormat,
            WriteableBitmap? existing, byte[] buffer, int stride)
        {
            // 复用已有的 WriteableBitmap（尺寸匹配时避免每帧 new 导致 LOH 碎片）
            WriteableBitmap writeableBitmap;
            if (existing is not null
                && existing.PixelWidth == width
                && existing.PixelHeight == height
                && existing.Format == pixelFormat
                && !existing.IsFrozen)
            {
                writeableBitmap = existing;
            }
            else
            {
                writeableBitmap = new WriteableBitmap(width, height, 96, 96, pixelFormat, null);
            }

            // L345: WritePixels 内部已自行加锁并标记脏区域，无需外部 Lock/Unlock 或 AddDirtyRect
            writeableBitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, 0);
            return writeableBitmap;
        }

        /// <summary>
        /// L30: 提取共享的条码结果绘制方法，供 RecipeViewModel 和 RecipeScannerService 统一调用
        /// </summary>
        public static void DrawBarcodeResults(Mat mat, HikBarcodeResult[] results)
        {
            ArgumentNullException.ThrowIfNull(mat);
            ArgumentNullException.ThrowIfNull(results);

            foreach (var result in results)
            {
                var location = result.Location();
                // L261: 防御性 null 检查，避免 Location() 实现变更时 NRE
                // L112: 空数组跳过，避免 location[0] 抛 IndexOutOfRangeException
                if (location is null || location.Length == 0)
                {
                    continue;
                }
                var topLeft = new OpenCvSharp.Point(location[0].X, location[0].Y);
                // L262: 使用命名常量替代魔法数字
                mat.PutText($"Score:{result.Confidence()}", new OpenCvSharp.Point(topLeft.X, topLeft.Y - ScoreTextYOffset), HersheyFonts.HersheySimplex, DrawFontScale, Scalar.Red, DrawThickness);
                var points = Array.ConvertAll(location, p => new OpenCvSharp.Point(p.X, p.Y));
                Cv2.Polylines(mat, new[] { points }, true, Scalar.Lime, DrawThickness);
                mat.PutText($"Barcode:{result.CodeString()}", new OpenCvSharp.Point(topLeft.X, topLeft.Y - BarcodeTextYOffset), HersheyFonts.HersheySimplex, DrawFontScale, Scalar.Blue, DrawThickness);
            }
        }
    }
}
