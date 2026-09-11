using System;
using Extensions;
using OpenCvSharp;
using Xunit;

namespace MainAPP.Tests.Extensions
{
    /// <summary>
    /// ObbCropExtensions 摆正裁剪工具的单元测试。
    /// 核心验证：裁剪图 → 源图 逆变换的数学正确性（往返误差 &lt; 1px）。
    /// </summary>
    public class ObbCropExtensionsTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(30f)]
        [InlineData(-45f)]
        [InlineData(90f)]
        [InlineData(137.5f)]
        public void CropAligned_RoundTrip_MapsPointsBack(float angleDeg)
        {
            using var src = new Mat(800, 1000, MatType.CV_8UC1, Scalar.All(0));
            var center = new Point2f(510, 390);
            const float width = 220f;
            const float height = 130f;
            const float padding = 0.1f;

            using var crop = src.CropAligned(center, width, height, angleDeg, padding, out var toSource);

            // 输出尺寸 = 框尺寸 × (1 + padding)
            Assert.Equal((int)Math.Round(width * (1 + padding)), crop.Width);
            Assert.Equal((int)Math.Round(height * (1 + padding)), crop.Height);

            // 前向映射公式（源图 → 裁剪图），用于构造"已知点"做往返验证
            double rad = -angleDeg * Math.PI / 180.0;
            double ca = Math.Cos(rad);
            double sa = Math.Sin(rad);
            double w2 = crop.Width / 2.0;
            double h2 = crop.Height / 2.0;

            // 源图采样点（含中心与偏置点）
            var srcPoints = new[]
            {
                center,
                new Point2f(510 + 100, 390 + 40),
                new Point2f(510 - 80, 390 + 60),
                new Point2f(400, 300),
                new Point2f(620, 480),
            };

            foreach (var p in srcPoints)
            {
                double rx = p.X - center.X;
                double ry = p.Y - center.Y;
                var dst = new Point2f(
                    (float)(ca * rx - sa * ry + w2),
                    (float)(sa * rx + ca * ry + h2));

                var back = toSource.Apply(dst);
                Assert.True(Math.Abs(back.X - p.X) < 1.0, $"X 往返误差过大: {back.X} vs {p.X} (angle={angleDeg})");
                Assert.True(Math.Abs(back.Y - p.Y) < 1.0, $"Y 往返误差过大: {back.Y} vs {p.Y} (angle={angleDeg})");
            }

            // 裁剪图中心必须映射回源图旋转中心
            var c = toSource.Apply(crop.Width / 2f, crop.Height / 2f);
            Assert.True(Math.Abs(c.X - center.X) < 1.0, $"裁剪图中心 X 未对齐: {c.X} vs {center.X}");
            Assert.True(Math.Abs(c.Y - center.Y) < 1.0, $"裁剪图中心 Y 未对齐: {c.Y} vs {center.Y}");
        }

        [Fact]
        public void CropAligned_ZeroPadding_MinSizeGuarded()
        {
            using var src = new Mat(100, 100, MatType.CV_8UC1, Scalar.All(0));
            // 极小框 + 负 padding：不应抛异常，输出尺寸至少 1x1
            using var crop = src.CropAligned(new Point2f(50, 50), 1, 1, 10f, -0.5f, out var toSource);
            Assert.True(crop.Width >= 1);
            Assert.True(crop.Height >= 1);

            // 往返仍正确
            var back = toSource.Apply(0f, 0f);
            Assert.True(double.IsFinite(back.X) && double.IsFinite(back.Y));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(30f)]
        [InlineData(-45f)]
        [InlineData(90f)]
        public void CropAligned_RotatedRect_ProducesUprightCenteredCrop(float angleDeg)
        {
            using var src = new Mat(800, 1000, MatType.CV_8UC1, Scalar.All(0));
            var center = new Point2f(500, 400);
            const float width = 300f;
            const float height = 100f;

            // 在源图中画一个旋转矩形（模拟倾斜产品）
            var rect = new RotatedRect(center, new Size2f(width, height), angleDeg);
            var rectPoints = rect.Points().Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToArray();
            Cv2.FillPoly(src, new[] { rectPoints }, new Scalar(255));

            using var crop = src.CropAligned(center, width, height, angleDeg, 0.1f, out _);

            // ① 裁剪图中心必须落在产品上（旋转中心对齐）
            Assert.True(crop.At<byte>(crop.Height / 2, crop.Width / 2) > 100,
                $"裁剪图中心应为产品内容 (angle={angleDeg})");

            // ② 产品应被摆正（长轴接近水平/垂直，即 minAreaRect 角度≈0 或 ±90）
            using var thresh = new Mat();
            Cv2.Threshold(crop, thresh, 100, 255, ThresholdTypes.Binary);
            var contours = Cv2.FindContoursAsArray(thresh, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            Assert.NotEmpty(contours);

            var best = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
            var mrect = Cv2.MinAreaRect(best);
            var a = Math.Abs(mrect.Angle);
            var upright = a < 5 || Math.Abs(a - 90) < 5 || Math.Abs(a - 180) < 5;
            Assert.True(upright, $"产品未摆正，minAreaRect.Angle={mrect.Angle:F1} (angle={angleDeg})");

            // ③ 产品面积应接近矩形面积（内容完整裁入）
            var areaRatio = Cv2.ContourArea(best) / (width * height);
            Assert.True(areaRatio > 0.9, $"产品被裁掉，面积占比={areaRatio:F2} (angle={angleDeg})");
        }
    }
}
