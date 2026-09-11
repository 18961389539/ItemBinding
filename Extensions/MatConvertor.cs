using OpenCvSharp;
using System;

namespace Extensions
{
    public static class MatConvertor
    {
        public static Mat ToColor(this Mat image)
        {
            ArgumentNullException.ThrowIfNull(image);
            // L429b: 校验 Mat 非空，避免对空 Mat 调用 Channels/CvtColor 抛出 OpenCV 内部异常
            if (image.Empty()) throw new ArgumentException("Mat is empty", nameof(image));
            if (image.Channels() == 3)
            {
                // M153: 返回克隆以保持与其它通道数分支一致的语义（CvtColor 总是返回新 Mat），
                // 避免调用方 Dispose 结果时意外释放原始 Mat
                return image.Clone();
            }
            if (image.Channels() == 1)
            {
                return image.CvtColor(ColorConversionCodes.GRAY2BGR);
            }
            // M149: 支持 4 通道（BGRA）转 3 通道（BGR）
            if (image.Channels() == 4)
            {
                return image.CvtColor(ColorConversionCodes.BGRA2BGR);
            }
            throw new NotSupportedException($"不支持的通道数: {image.Channels()}");
        }
    }
}
