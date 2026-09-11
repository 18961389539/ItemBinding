using OpenCvSharp;

namespace Blob
{
    /// <summary>
    /// Blob（连通域）检测器。对二值化图像进行连通域分析，
    /// 提取各连通域的面积、中心、外接矩形、旋转外接矩形、拟合椭圆、凸包等几何属性。
    /// 所有方法均为无状态静态函数，不缓存任何 Mat 资源。
    /// </summary>
    public static class Detector
    {
        /// <summary>
        /// 从二值图像中检测 Blob（基于 Cv2.FindContours）。
        /// 注：使用轮廓面积（Cv2.ContourArea）而非像素计数，正确反映 Blob 实际面积。
        /// </summary>
        /// <param name="binaryImage">二值图像（非零像素视为前景）。</param>
        /// <param name="minArea">最小面积阈值，面积小于此值的 Blob 将被过滤。&lt;= 0 表示不过滤。</param>
        /// <param name="maxArea">最大面积阈值，面积大于此值的 Blob 将被过滤。&lt;= 0 表示不过滤。</param>
        /// <returns>检测到的 Blob 列表（已计算所有派生属性，Source = Contour）。</returns>
        public static List<BlobInfo> DetectBlobs(Mat binaryImage, double minArea = 0, double maxArea = 0)
        {
            if (binaryImage == null) throw new ArgumentNullException(nameof(binaryImage));

            var contours = binaryImage.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            var blobs = new List<BlobInfo>(contours.Length);
            foreach (var contour in contours)
            {
                if (contour.Length == 0) continue;

                var blob = BlobInfo.FromContour(contour);

                if (minArea > 0 && blob.Area < minArea) continue;
                if (maxArea > 0 && blob.Area > maxArea) continue;

                blobs.Add(blob);
            }
            return blobs;
        }

        /// <summary>
        /// 基于 ConnectedComponentsWithStats 检测连通域，提供真实像素计数面积与统计信息。
        /// 适合需要逐像素精确面积、外接矩形、质心（图像矩）的场景。
        /// 返回的 BlobInfo 中 Area 为像素数，Perimeter/MinAreaRect/ConvexHull/FittedEllipse 均无意义（Source = ConnectedComponents）。
        /// </summary>
        /// <param name="binaryImage">二值图像。</param>
        /// <param name="minArea">最小像素面积过滤。</param>
        /// <param name="maxArea">最大像素面积过滤。&lt;= 0 表示不过滤。</param>
        /// <returns>检测到的 Blob 列表（背景已排除）。</returns>
        public static List<BlobInfo> DetectConnectedComponents(Mat binaryImage, int minArea = 1, int maxArea = 0)
        {
            if (binaryImage == null) throw new ArgumentNullException(nameof(binaryImage));

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var count = Cv2.ConnectedComponentsWithStats(binaryImage, labels, stats, centroids,
                PixelConnectivity.Connectivity8, MatType.CV_32S);

            var blobs = new List<BlobInfo>(Math.Max(0, count - 1));
            for (int i = 1; i < count; i++)
            {
                var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                if (area < minArea) continue;
                if (maxArea > 0 && area > maxArea) continue;

                var left = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                var top = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                var width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                var height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                var cx = centroids.At<double>(i, 0);
                var cy = centroids.At<double>(i, 1);

                blobs.Add(BlobInfo.FromConnectedComponent(area, left, top, width, height, cx, cy));
            }
            return blobs;
        }

        /// <summary>
        /// 绘制 Blob 轮廓到目标图像。
        /// </summary>
        /// <param name="image">目标图像（将被修改）。</param>
        /// <param name="blobs">Blob 列表。</param>
        /// <param name="color">轮廓颜色（默认绿色）。</param>
        /// <param name="thickness">轮廓厚度。</param>
        public static void DrawBlobs(Mat image, IEnumerable<BlobInfo> blobs, Scalar? color = null, int thickness = 2)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (blobs == null) throw new ArgumentNullException(nameof(blobs));

            var drawColor = color ?? Scalar.Green;
            foreach (var blob in blobs)
            {
                if (blob.ContourPoints.Length < 2) continue;
                Cv2.Polylines(image, new[] { blob.ContourPoints }, true, drawColor, thickness);
            }
        }

        /// <summary>
        /// 绘制每个 Blob 的最小面积外接矩形（旋转矩形）到目标图像。
        /// 仅绘制 <see cref="BlobInfo.MinAreaRect"/> 不为 null 的 Blob（即 Contour 路径且点数 ≥ 3）。
        /// </summary>
        public static void DrawMinAreaRects(Mat image, IEnumerable<BlobInfo> blobs, Scalar? color = null, int thickness = 1)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (blobs == null) throw new ArgumentNullException(nameof(blobs));

            var drawColor = color ?? Scalar.Red;
            foreach (var blob in blobs)
            {
                if (blob.MinAreaRect is not RotatedRect rr) continue;
                var pts = Array.ConvertAll(rr.Points(), p => new Point((int)p.X, (int)p.Y));
                Cv2.Polylines(image, new[] { pts }, true, drawColor, thickness);
            }
        }

        /// <summary>使用自适应阈值进行图像分割。</summary>
        /// <param name="grayImage">灰度图像（单通道）。</param>
        /// <param name="maxValue">阈值化后的最大值。</param>
        /// <param name="adaptiveMethod">自适应阈值算法。</param>
        /// <param name="thresholdType">阈值类型。</param>
        /// <param name="blockSize">像素邻域大小（必须为奇数且 &gt; 1）。</param>
        /// <param name="c">从均值或加权均值中减去的常数。</param>
        /// <returns>新建的二值图像，调用方负责 Dispose。</returns>
        public static Mat AdaptiveThresholdSegmentation(Mat grayImage, double maxValue = 255,
            AdaptiveThresholdTypes adaptiveMethod = AdaptiveThresholdTypes.MeanC,
            ThresholdTypes thresholdType = ThresholdTypes.Binary,
            int blockSize = 11, double c = 2)
        {
            if (grayImage == null) throw new ArgumentNullException(nameof(grayImage));
            if (blockSize % 2 == 0 || blockSize <= 1)
                throw new ArgumentException("blockSize must be odd and greater than 1.", nameof(blockSize));

            var binary = new Mat();
            Cv2.AdaptiveThreshold(grayImage, binary, maxValue, adaptiveMethod, thresholdType, blockSize, c);
            return binary;
        }

        /// <summary>
        /// 按 <see cref="BlobFilter"/> 条件过滤 Blob 列表。
        /// 每个维度的上下限默认 0 / MaxValue 表示不限制该维度。
        /// </summary>
        public static List<BlobInfo> FilterBlobs(IEnumerable<BlobInfo> blobs, BlobFilter? filter = null)
        {
            if (blobs == null) throw new ArgumentNullException(nameof(blobs));
            if (filter is null) return blobs.ToList();

            return blobs.Where(b =>
                b.Area >= filter.MinArea && b.Area <= filter.MaxArea &&
                b.AspectRatio >= filter.MinAspectRatio && b.AspectRatio <= filter.MaxAspectRatio &&
                b.Circularity >= filter.MinCircularity && b.Circularity <= filter.MaxCircularity &&
                b.Compactness >= filter.MinCompactness && b.Compactness <= filter.MaxCompactness
            ).ToList();
        }

        /// <summary>
        /// 应用形态学操作改善分割结果。
        /// 直接转发到 Cv2.MorphologyEx，支持全部分支：Erode/Dilate/Open/Close/Gradient/TopHat/BlackHat/HitMiss。
        /// </summary>
        /// <param name="binaryImage">二值图像。</param>
        /// <param name="operation">形态学操作类型。</param>
        /// <param name="kernelSize">核大小。</param>
        /// <param name="iterations">迭代次数。</param>
        /// <returns>新建的二值图像，调用方负责 Dispose。</returns>
        public static Mat ApplyMorphology(Mat binaryImage, MorphTypes operation, int kernelSize = 3, int iterations = 1)
        {
            if (binaryImage == null) throw new ArgumentNullException(nameof(binaryImage));
            if (kernelSize <= 0) throw new ArgumentException("kernelSize must be positive.", nameof(kernelSize));

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize));
            var result = new Mat();
            Cv2.MorphologyEx(binaryImage, result, operation, kernel, null, iterations);
            return result;
        }

        /// <summary>
        /// 基于颜色范围进行分割。
        /// </summary>
        /// <param name="colorImage">彩色图像（3 通道）。</param>
        /// <param name="lower">颜色下限（与 <paramref name="colorSpace"/> 对应的多通道下限）。</param>
        /// <param name="upper">颜色上限。</param>
        /// <param name="colorSpace">输入图像的颜色空间，用于决定是否需要转换。默认 BGR。
        /// HSV 下 H 通道范围为 [0,180]，调用方应注意设置正确的上下限。</param>
        /// <returns>新建的二值掩码图像，调用方负责 Dispose。</returns>
        public static Mat ColorBasedSegmentation(Mat colorImage, Scalar lower, Scalar upper,
            ColorSpace colorSpace = ColorSpace.BGR)
        {
            if (colorImage == null) throw new ArgumentNullException(nameof(colorImage));
            if (colorImage.Channels() < 3)
                throw new ArgumentException("输入图像必须是 3 通道彩色图像。", nameof(colorImage));

            Mat? converted = null;
            Mat src = colorImage;
            try
            {
                if (colorSpace == ColorSpace.HSV)
                {
                    converted = new Mat();
                    Cv2.CvtColor(colorImage, converted, ColorConversionCodes.BGR2HSV);
                    src = converted;
                }
                else if (colorSpace == ColorSpace.Lab)
                {
                    converted = new Mat();
                    Cv2.CvtColor(colorImage, converted, ColorConversionCodes.BGR2Lab);
                    src = converted;
                }

                var mask = new Mat();
                Cv2.InRange(src, lower, upper, mask);
                return mask;
            }
            finally
            {
                converted?.Dispose();
            }
        }

        /// <summary>使用 Canny 边缘检测进行图像分割。</summary>
        /// <param name="grayImage">灰度图像（单通道）。</param>
        /// <param name="threshold1">低阈值。</param>
        /// <param name="threshold2">高阈值。</param>
        /// <param name="apertureSize">Sobel 算子孔径大小（3/5/7）。</param>
        /// <param name="L2gradient">是否使用 L2 范数。</param>
        /// <returns>新建的边缘二值图像，调用方负责 Dispose。</returns>
        public static Mat CannyEdgeSegmentation(Mat grayImage, double threshold1 = 50, double threshold2 = 150,
            int apertureSize = 3, bool L2gradient = false)
        {
            if (grayImage == null) throw new ArgumentNullException(nameof(grayImage));
            if (apertureSize != 3 && apertureSize != 5 && apertureSize != 7)
                throw new ArgumentException("apertureSize must be 3, 5, or 7.", nameof(apertureSize));

            var edges = new Mat();
            Cv2.Canny(grayImage, edges, threshold1, threshold2, apertureSize, L2gradient);
            return edges;
        }

        /// <summary>
        /// 高斯模糊预处理，降噪后再进入阈值化/边缘检测流程。
        /// </summary>
        /// <param name="src">输入图像（任意通道）。</param>
        /// <param name="ksize">高斯核大小（必须为正奇数）。</param>
        /// <param name="sigmaX">X 方向标准差；0 表示按 ksize 自动计算。</param>
        /// <returns>新建的模糊图像，调用方负责 Dispose。</returns>
        public static Mat GaussianBlur(Mat src, int ksize = 5, double sigmaX = 0)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (ksize <= 0 || ksize % 2 == 0)
                throw new ArgumentException("ksize must be positive odd.", nameof(ksize));

            var dst = new Mat();
            Cv2.GaussianBlur(src, dst, new Size(ksize, ksize), sigmaX);
            return dst;
        }

        /// <summary>
        /// 直方图均衡化预处理（仅适用于单通道灰度图）。
        /// </summary>
        /// <returns>新建的均衡化图像，调用方负责 Dispose。</returns>
        public static Mat EqualizeHistogram(Mat grayImage)
        {
            if (grayImage == null) throw new ArgumentNullException(nameof(grayImage));
            if (grayImage.Channels() != 1)
                throw new ArgumentException("EqualizeHistogram 仅支持单通道灰度图。", nameof(grayImage));

            var dst = new Mat();
            Cv2.EqualizeHist(grayImage, dst);
            return dst;
        }

        /// <summary>
        /// 孔洞填充：使用 floodfill 反向填充法填充前景内部孔洞。
        /// 直接 floodfill 即可覆盖大孔洞；不做 Close 预处理以保留大孔洞填充能力。
        /// 返回新的二值图像（输入不修改）。
        /// </summary>
        /// <param name="binaryImage">二值图像（前景为非零）。</param>
        /// <returns>新建的填充后图像，调用方负责 Dispose。</returns>
        public static Mat FillHoles(Mat binaryImage)
        {
            if (binaryImage == null) throw new ArgumentNullException(nameof(binaryImage));

            // 经典 floodfill 反向填充法：在图像外围加 1 像素背景边框，
            // 从 (0,0) 泛洪填充背景，取反得到孔洞掩码，与原图合并
            var padded = new Mat();
            Cv2.CopyMakeBorder(binaryImage, padded, 1, 1, 1, 1, BorderTypes.Constant, new Scalar(0));
            using var mask = new Mat(padded.Rows + 2, padded.Cols + 2, MatType.CV_8UC1, new Scalar(0));
            Cv2.FloodFill(padded, mask, new Point(0, 0), new Scalar(0),
                out _, new Scalar(0), new Scalar(0), FloodFillFlags.Link8);

            // 裁剪回原尺寸，取反得到孔洞区域，与原图合并
            var inner = padded.SubMat(new Rect(1, 1, binaryImage.Cols, binaryImage.Rows)).Clone();
            var holes = new Mat();
            Cv2.BitwiseNot(inner, holes);
            var filled = new Mat();
            Cv2.BitwiseOr(binaryImage, holes, filled);
            return filled;
        }

        /// <summary>
        /// 生成连通域标注图：不同连通域用不同颜色着色，便于可视化。
        /// </summary>
        /// <param name="binaryImage">二值图像。</param>
        /// <returns>新建的彩色标注图（8UC3），调用方负责 Dispose。</returns>
        public static Mat CreateLabelImage(Mat binaryImage)
        {
            if (binaryImage == null) throw new ArgumentNullException(nameof(binaryImage));

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var count = Cv2.ConnectedComponentsWithStats(binaryImage, labels, stats, centroids,
                PixelConnectivity.Connectivity8, MatType.CV_32S);

            var output = new Mat(binaryImage.Size(), MatType.CV_8UC3, Scalar.Black);
            var rng = new RNG(12345);
            for (int i = 1; i < count; i++)
            {
                var color = new Scalar(rng.Uniform(0, 256), rng.Uniform(0, 256), rng.Uniform(0, 256));
                using var cmp = new Mat();
                Cv2.Compare(labels, Scalar.All(i), cmp, CmpType.EQ);
                output.SetTo(color, cmp);
            }
            return output;
        }

        /// <summary>
        /// 带轮廓层级的检测：返回 (Blob, HierarchyInfo) 列表。
        /// 适用于需要区分外轮廓与内孔洞的场景。
        /// </summary>
        public static List<(BlobInfo Blob, HierarchyInfo Hierarchy)> FindContoursWithHierarchy(
            Mat binaryImage,
            RetrievalModes mode = RetrievalModes.CComp,
            ContourApproximationModes approx = ContourApproximationModes.ApproxSimple)
        {
            if (binaryImage == null) throw new ArgumentNullException(nameof(binaryImage));

            Cv2.FindContours(binaryImage, out var contours, out var hierarchyArray, mode, approx);

            var result = new List<(BlobInfo, HierarchyInfo)>(contours.Length);
            for (int i = 0; i < contours.Length; i++)
            {
                if (contours[i].Length == 0) continue;
                var blob = BlobInfo.FromContour(contours[i]);
                var h = hierarchyArray[i];
                result.Add((blob, new HierarchyInfo(h.Next, h.Previous, h.Child, h.Parent)));
            }
            return result;
        }
    }

    /// <summary>
    /// <see cref="BlobInfo"/> 过滤条件。所有维度默认 0 / MaxValue 表示不限制。
    /// </summary>
    public sealed class BlobFilter
    {
        public double MinArea { get; set; } = 0;
        public double MaxArea { get; set; } = double.MaxValue;
        public double MinAspectRatio { get; set; } = 0;
        public double MaxAspectRatio { get; set; } = double.MaxValue;
        public double MinCircularity { get; set; } = 0;
        public double MaxCircularity { get; set; } = double.MaxValue;
        public double MinCompactness { get; set; } = 0;
        public double MaxCompactness { get; set; } = double.MaxValue;
    }

    /// <summary>
    /// 轮廓层级信息。所有字段为同级或父子轮廓在 contours 数组中的索引，-1 表示无。
    /// </summary>
    /// <param name="Next">同级下一条轮廓索引。</param>
    /// <param name="Previous">同级上一条轮廓索引。</param>
    /// <param name="Child">第一个子轮廓索引（内轮廓）。</param>
    /// <param name="Parent">父轮廓索引。</param>
    public readonly record struct HierarchyInfo(int Next, int Previous, int Child, int Parent);

    /// <summary>
    /// <see cref="Detector.ColorBasedSegmentation"/> 支持的颜色空间。
    /// 调用方传入图像实际的颜色空间，方法会在必要时做 BGR↔HSV/Lab 转换。
    /// </summary>
    public enum ColorSpace
    {
        /// <summary>OpenCvSharp 默认的 BGR 顺序。</summary>
        BGR,
        /// <summary>HSV：色相/饱和度/明度，颜色范围分割更鲁棒。</summary>
        HSV,
        /// <summary>CIE L*a*b*：感知均匀，适合按感知颜色差异分割。</summary>
        Lab
    }
}
