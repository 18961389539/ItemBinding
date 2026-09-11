using System;
using OpenCvSharp;

namespace Extensions
{
    /// <summary>
    /// 裁剪图坐标 → 源图坐标的 2D 仿射逆变换（齐次 3x3 矩阵）。
    /// 用于将角度分割模型在裁剪图上输出的特征点坐标还原到源图坐标系。
    /// </summary>
    public readonly struct ObbCropToSource
    {
        public readonly double M00, M01, M02;
        public readonly double M10, M11, M12;

        public ObbCropToSource(double m00, double m01, double m02, double m10, double m11, double m12)
        {
            M00 = m00; M01 = m01; M02 = m02;
            M10 = m10; M11 = m11; M12 = m12;
        }

        /// <summary>将裁剪图坐标 (x, y) 变换为源图坐标。</summary>
        public Point2f Apply(float x, float y) => new(
            (float)(M00 * x + M01 * y + M02),
            (float)(M10 * x + M11 * y + M12));

        /// <summary>将裁剪图坐标 PointF 变换为源图坐标。</summary>
        public Point2f Apply(Point2f p) => Apply(p.X, p.Y);
    }

    /// <summary>
    /// OBB（旋转框）摆正裁剪工具。
    /// 按旋转框中心/尺寸，将源图旋转 -angle 使产品长轴水平后裁剪出固定尺寸区域，
    /// 并同时给出裁剪图 → 源图的逆变换，供坐标还原使用。
    /// </summary>
    public static class ObbCropExtensions
    {
        /// <summary>
        /// 摆正裁剪：以 (center) 为中心、旋转 -angle 度，裁剪出 w×(1+pad) × h×(1+pad) 的区域。
        /// 输出图中心与旋转中心对齐；越界区域以黑色(0)填充。
        /// </summary>
        /// <param name="src">源图（Mat，如推理图）。</param>
        /// <param name="center">旋转框中心（源图坐标）。</param>
        /// <param name="width">旋转框宽度（长轴尺寸，像素）。</param>
        /// <param name="height">旋转框高度（短轴尺寸，像素）。</param>
        /// <param name="angleDeg">旋转框角度（度，逆时针为正，与 JinlongYolo MinAreaRect.Angle 语义一致）。</param>
        /// <param name="paddingRatio">padding 比例（相对框尺寸，如 0.1 表示四周各加 10%）。</param>
        /// <param name="toSource">输出：裁剪图坐标 → 源图坐标的逆变换。</param>
        /// <returns>摆正裁剪后的图像（调用方负责 Dispose）。</returns>
        public static Mat CropAligned(this Mat src,
                                      Point2f center,
                                      float width,
                                      float height,
                                      float angleDeg,
                                      float paddingRatio,
                                      out ObbCropToSource toSource)
        {
            if (src is null)
            {
                throw new ArgumentNullException(nameof(src));
            }

            if (paddingRatio < 0)
            {
                paddingRatio = 0;
            }

            int dstW = Math.Max(1, (int)Math.Round(width * (1 + paddingRatio)));
            int dstH = Math.Max(1, (int)Math.Round(height * (1 + paddingRatio)));

            // 摆正 = 旋转 -angleDeg。旋转角（弧度）
            double a = -angleDeg * Math.PI / 180.0;
            double ca = Math.Cos(a);
            double sa = Math.Sin(a);
            double cx = center.X;
            double cy = center.Y;
            double w2 = dstW / 2.0;
            double h2 = dstH / 2.0;

            // 前向矩阵 M（src→dst）：dst = R(-angle)·src + t，且要求 M·center = (w2, h2)
            // t = (w2, h2) - R·center
            double t0 = w2 - (ca * cx - sa * cy);
            double t1 = h2 - (sa * cx + ca * cy);

            using (var m = new Mat(2, 3, MatType.CV_64FC1))
            {
                m.Set<double>(0, 0, ca);
                m.Set<double>(0, 1, -sa);
                m.Set<double>(0, 2, t0);
                m.Set<double>(1, 0, sa);
                m.Set<double>(1, 1, ca);
                m.Set<double>(1, 2, t1);

                var dst = new Mat();
                Cv2.WarpAffine(src, dst, m, new Size(dstW, dstH),
                    InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));

                // 逆变换（dst→src）：src = R(+angle)·(dst - t)
                // R(+angle) 的 2x2 = [ca sa; -sa ca]
                toSource = new ObbCropToSource(
                    ca, sa, -(ca * t0 + sa * t1),
                    -sa, ca, -(-sa * t0 + ca * t1));

                return dst;
            }
        }
    }
}
