using System;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using OpenCvSharp;

namespace Extensions
{
    public static class ImageConverterExtensions
    {
        /// <summary>
        /// 将 ImageSharp Image 转换为 OpenCvSharp Mat
        /// </summary>
        /// <remarks>
        /// L375a: Rgba64 输入会被降采样为 8-bit (CV_8UC4) 输出（通过右移 8 位），
        /// 不保留完整 16-bit 深度；如需完整 16-bit 输出请使用专门的转换逻辑。
        /// M307a: 内部使用 Marshal.WriteByte 逐像素写入，性能较差；
        /// 大图像场景建议使用 unsafe 指针或 Buffer.MemoryCopy 重写以提升吞吐量。
        /// L394c: 已知限制 - 多分支重复代码可重构为泛型辅助方法减少重复，
        /// 但改动较大且各分支像素格式与通道数不同，暂不重构。
        /// </remarks>
        public static OpenCvSharp.Mat ToMat(this SixLabors.ImageSharp.Image image)
        {
            ArgumentNullException.ThrowIfNull(image);

            if (image is SixLabors.ImageSharp.Image<Rgba32> rgba32Image)
            {
                // H52c: 异常时释放 Mat，正常返回由调用方负责 Dispose
                var mat = new Mat(rgba32Image.Height, rgba32Image.Width, MatType.CV_8UC4);
                try
                {
                    rgba32Image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var pixel = pixelRow[x];
                                // OpenCV BGRA
                                Marshal.WriteByte(matRow, x * 4, pixel.B);
                                Marshal.WriteByte(matRow, x * 4 + 1, pixel.G);
                                Marshal.WriteByte(matRow, x * 4 + 2, pixel.R);
                                Marshal.WriteByte(matRow, x * 4 + 3, pixel.A);
                            }
                        }
                    });
                    return mat;
                }
                catch (Exception) { mat.Dispose(); throw; }
            }

            if (image is SixLabors.ImageSharp.Image<Bgra32> bgra32Image)
            {
                var mat = new Mat(bgra32Image.Height, bgra32Image.Width, MatType.CV_8UC4);
                try
                {
                    bgra32Image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var pixel = pixelRow[x];
                                // already BGRA
                                Marshal.WriteByte(matRow, x * 4, pixel.B);
                                Marshal.WriteByte(matRow, x * 4 + 1, pixel.G);
                                Marshal.WriteByte(matRow, x * 4 + 2, pixel.R);
                                Marshal.WriteByte(matRow, x * 4 + 3, pixel.A);
                            }
                        }
                    });
                    return mat;
                }
                catch (Exception) { mat.Dispose(); throw; }
            }

            if (image is SixLabors.ImageSharp.Image<Bgr24> bgr24Image)
            {
                var mat = new Mat(bgr24Image.Height, bgr24Image.Width, MatType.CV_8UC3);
                try
                {
                    bgr24Image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var pixel = pixelRow[x];
                                Marshal.WriteByte(matRow, x * 3, pixel.B);
                                Marshal.WriteByte(matRow, x * 3 + 1, pixel.G);
                                Marshal.WriteByte(matRow, x * 3 + 2, pixel.R);
                            }
                        }
                    });
                    return mat;
                }
                catch (Exception) { mat.Dispose(); throw; }
            }

            if (image is SixLabors.ImageSharp.Image<Rgb24> rgb24Image)
            {
                var mat = new Mat(rgb24Image.Height, rgb24Image.Width, MatType.CV_8UC3);
                try
                {
                    rgb24Image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var pixel = pixelRow[x];
                                Marshal.WriteByte(matRow, x * 3, pixel.B);
                                Marshal.WriteByte(matRow, x * 3 + 1, pixel.G);
                                Marshal.WriteByte(matRow, x * 3 + 2, pixel.R);
                            }
                        }
                    });
                    return mat;
                }
                catch (Exception) { mat.Dispose(); throw; }
            }

            if (image is SixLabors.ImageSharp.Image<Rgba64> rgba64Image)
            {
                // Convert 16-bit channels down to 8-bit by shifting
                var mat = new Mat(rgba64Image.Height, rgba64Image.Width, MatType.CV_8UC4);
                try
                {
                    rgba64Image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var pixel = pixelRow[x];
                                // Downscale from ushort to byte by shifting
                                Marshal.WriteByte(matRow, x * 4, (byte)(pixel.B >> 8));
                                Marshal.WriteByte(matRow, x * 4 + 1, (byte)(pixel.G >> 8));
                                Marshal.WriteByte(matRow, x * 4 + 2, (byte)(pixel.R >> 8));
                                Marshal.WriteByte(matRow, x * 4 + 3, (byte)(pixel.A >> 8));
                            }
                        }
                    });
                    return mat;
                }
                catch (Exception) { mat.Dispose(); throw; }
            }

            if (image is SixLabors.ImageSharp.Image<L16> l16Image)
            {
                // 16-bit grayscale
                var mat = new Mat(l16Image.Height, l16Image.Width, MatType.CV_16UC1);
                try
                {
                    l16Image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var val = pixelRow[x].PackedValue;
                                Marshal.WriteInt16(matRow, x * 2, (short)val);
                            }
                        }
                    });
                    return mat;
                }
                catch (Exception) { mat.Dispose(); throw; }
            }

            if (image is SixLabors.ImageSharp.Image<La16> la16Image)
            {
                // 16-bit gray + alpha -> 2-channel 16-bit
                var mat = new Mat(la16Image.Height, la16Image.Width, MatType.CV_16UC2);
                try
                {
                    la16Image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var pixel = pixelRow[x];
                                Marshal.WriteInt16(matRow, x * 4, (short)pixel.L);
                                Marshal.WriteInt16(matRow, x * 4 + 2, (short)pixel.A);
                            }
                        }
                    });
                    return mat;
                }
                catch (Exception) { mat.Dispose(); throw; }
            }

            if (image is SixLabors.ImageSharp.Image<L8> l8Image)
            {
                var mat = new Mat(l8Image.Height, l8Image.Width, MatType.CV_8UC1);
                try
                {
                    l8Image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                Marshal.WriteByte(matRow, x, pixelRow[x].PackedValue);
                            }
                        }
                    });
                    return mat;
                }
                catch (Exception) { mat.Dispose(); throw; }
            }

            // Fallback: convert to Rgba32 and process
            using var fallback = image.CloneAs<Rgba32>();
            // L108: fallback 路径同样需保护 Mat 释放
            var fallbackMat = new Mat(fallback.Height, fallback.Width, MatType.CV_8UC4);
            try
            {
                fallback.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        var pixelRow = accessor.GetRowSpan(y);
                        var matRow = fallbackMat.Ptr(y);
                        for (int x = 0; x < accessor.Width; x++)
                        {
                            var pixel = pixelRow[x];
                            Marshal.WriteByte(matRow, x * 4, pixel.B);
                            Marshal.WriteByte(matRow, x * 4 + 1, pixel.G);
                            Marshal.WriteByte(matRow, x * 4 + 2, pixel.R);
                            Marshal.WriteByte(matRow, x * 4 + 3, pixel.A);
                        }
                    }
                });
                return fallbackMat;
            }
            catch (Exception) { fallbackMat.Dispose(); throw; }
        }

        public static SixLabors.ImageSharp.Image ToImageSharp(this Mat mat)
        {
            ArgumentNullException.ThrowIfNull(mat);
            // M304a: 检查 Mat 是否为空，避免后续按 Width/Height 分配图像时产生零尺寸图像
            // L413b: 已知限制 - 未校验 Mat 是否已 Dispose。OpenCvSharp 的 Mat 未公开 IsDisposed 属性，
            // 已 Dispose 的 Mat 访问 Width/Height/Ptr 会抛出 ObjectDisposedException，由调用方保证生命周期
            if (mat.Empty()) throw new ArgumentException("Mat is empty", nameof(mat));

            if (mat.Type() == MatType.CV_8UC1)
            {
                var image = new Image<L8>(mat.Width, mat.Height);
                try
                {
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                pixelRow[x] = new L8(Marshal.ReadByte(matRow, x));
                            }
                        }
                    });
                    return image;
                }
                catch { image.Dispose(); throw; }
            }

            if (mat.Type() == MatType.CV_8UC3)
            {
                var image = new Image<Rgb24>(mat.Width, mat.Height);
                try
                {
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var b = Marshal.ReadByte(matRow, x * 3);
                                var g = Marshal.ReadByte(matRow, x * 3 + 1);
                                var r = Marshal.ReadByte(matRow, x * 3 + 2);
                                pixelRow[x] = new Rgb24(r, g, b);
                            }
                        }
                    });
                    return image;
                }
                catch { image.Dispose(); throw; }
            }

            if (mat.Type() == MatType.CV_8UC4)
            {
                var image = new Image<Rgba32>(mat.Width, mat.Height);
                try
                {
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var b = Marshal.ReadByte(matRow, x * 4);
                                var g = Marshal.ReadByte(matRow, x * 4 + 1);
                                var r = Marshal.ReadByte(matRow, x * 4 + 2);
                                var a = Marshal.ReadByte(matRow, x * 4 + 3);
                                pixelRow[x] = new Rgba32(r, g, b, a);
                            }
                        }
                    });
                    return image;
                }
                catch { image.Dispose(); throw; }
            }

            if (mat.Type() == MatType.CV_16UC1)
            {
                var image = new Image<L16>(mat.Width, mat.Height);
                try
                {
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var value = unchecked((ushort)Marshal.ReadInt16(matRow, x * 2));
                                pixelRow[x] = new L16(value);
                            }
                        }
                    });
                    return image;
                }
                catch { image.Dispose(); throw; }
            }

            if (mat.Type() == MatType.CV_16UC2)
            {
                // L366a: 假设此分支仅用于与 ToMat 中 La16→CV_16UC2 的 round-trip，
                // 将 16-bit 值截断为 8-bit (La16)，不支持真正的 16-bit 深度图转换
                var image = new Image<La16>(mat.Width, mat.Height);
                try
                {
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var pixelRow = accessor.GetRowSpan(y);
                            var matRow = mat.Ptr(y);
                            for (int x = 0; x < accessor.Width; x++)
                            {
                                var l = (byte)unchecked((ushort)Marshal.ReadInt16(matRow, x * 4));
                                var a = (byte)unchecked((ushort)Marshal.ReadInt16(matRow, x * 4 + 2));
                                pixelRow[x] = new La16(l, a);
                            }
                        }
                    });
                    return image;
                }
                catch { image.Dispose(); throw; }
            }

            throw new NotSupportedException($"Mat type {mat.Type()} is not supported");
        }
    }
}
