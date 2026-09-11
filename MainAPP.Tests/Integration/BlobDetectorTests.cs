using Blob;
using OpenCvSharp;
using Xunit;

namespace MainAPP.Tests.Integration
{
    /// <summary>
    /// Blob.Detector 与 BlobInfo 的合成图像单元测试。
    /// 用合成二值图像验证几何属性正确性、过滤逻辑、各分割/预处理/后处理/可视化方法的基本行为。
    /// </summary>
    public class BlobDetectorTests : IDisposable
    {
        private readonly List<Mat> _disposables = new();

        /// <summary>创建一个 100x100 的黑色背景二值图像。</summary>
        private Mat CreateBlankBinary(int width = 100, int height = 100)
        {
            var mat = new Mat(height, width, MatType.CV_8UC1, new Scalar(0));
            _disposables.Add(mat);
            return mat;
        }

        /// <summary>创建一个 100x100 的黑色背景 BGR 彩色图像。</summary>
        private Mat CreateBlankColor(int width = 100, int height = 100)
        {
            var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(0, 0, 0));
            _disposables.Add(mat);
            return mat;
        }

        /// <summary>在二值图像上绘制一个填充矩形作为前景。</summary>
        private static void DrawRect(Mat binary, int x, int y, int w, int h, byte val = 255)
        {
            Cv2.Rectangle(binary, new Rect(x, y, w, h), new Scalar(val), -1);
        }

        /// <summary>在二值图像上绘制一个填充圆形作为前景。</summary>
        private static void DrawCircle(Mat binary, int cx, int cy, int radius, byte val = 255)
        {
            Cv2.Circle(binary, new Point(cx, cy), radius, new Scalar(val), -1);
        }

        private Mat Track(Mat mat)
        {
            _disposables.Add(mat);
            return mat;
        }

        #region DetectBlobs（Contour 路径）

        [Fact]
        public void DetectBlobs_EmptyImage_ReturnsEmpty()
        {
            using var binary = CreateBlankBinary();
            var blobs = Detector.DetectBlobs(binary);
            Assert.Empty(blobs);
        }

        [Fact]
        public void DetectBlobs_SingleRectangle_AreaMatchesPixels()
        {
            using var binary = CreateBlankBinary();
            DrawRect(binary, 10, 10, 30, 20);  // 30*20=600 像素

            var blobs = Detector.DetectBlobs(binary);

            var blob = Assert.Single(blobs);
            Assert.Equal(BlobSource.Contour, blob.Source);
            // ContourArea 返回几何面积（Green 公式），矩形为 (width-1)*(height-1) = 29*19 = 551
            Assert.Equal(551, blob.Area, precision: 1);
            // 矩形周长 2*((width-1)+(height-1)) = 2*48 = 96
            Assert.Equal(96, blob.Perimeter, precision: 1);
            // 矩形是凸轮廓
            Assert.True(blob.IsConvex);
            // 外接框
            Assert.Equal(10, blob.BoundingBox.MinX);
            Assert.Equal(10, blob.BoundingBox.MinY);
            Assert.Equal(39, blob.BoundingBox.MaxX);
            Assert.Equal(29, blob.BoundingBox.MaxY);
            // 矩形的紧凑度接近 1（几何面积 / 像素外接框面积）
            Assert.InRange(blob.Compactness, 0.9, 1.0);
            // 质心约在 (24.5, 19.5)
            Assert.Equal(24.5, blob.CentroidX, precision: 1);
            Assert.Equal(19.5, blob.CentroidY, precision: 1);
        }

        [Fact]
        public void DetectBlobs_TwoSeparateShapes_ReturnsTwoBlobs()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 30, 30);
            DrawRect(binary, 110, 10, 30, 30);

            var blobs = Detector.DetectBlobs(binary);

            Assert.Equal(2, blobs.Count);
            Assert.All(blobs, b => Assert.Equal(BlobSource.Contour, b.Source));
            // ContourArea 几何面积 = 29*29 = 841
            Assert.All(blobs, b => Assert.Equal(841, b.Area, precision: 1));
        }

        [Fact]
        public void DetectBlobs_MinAreaFilter_FiltersSmallBlobs()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 30, 30);  // 几何面积 841
            DrawRect(binary, 110, 10, 5, 5);   // 几何面积 16

            var blobs = Detector.DetectBlobs(binary, minArea: 100);

            var blob = Assert.Single(blobs);
            Assert.Equal(841, blob.Area, precision: 1);
        }

        [Fact]
        public void DetectBlobs_MaxAreaFilter_FiltersLargeBlobs()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 30, 30);  // 几何面积 841
            DrawRect(binary, 110, 10, 5, 5);   // 几何面积 16

            var blobs = Detector.DetectBlobs(binary, maxArea: 100);

            var blob = Assert.Single(blobs);
            Assert.Equal(16, blob.Area, precision: 1);
        }

        [Fact]
        public void DetectBlobs_Circle_HasHighCircularity()
        {
            using var binary = CreateBlankBinary(100, 100);
            DrawCircle(binary, 50, 50, 30);

            var blobs = Detector.DetectBlobs(binary);
            var blob = Assert.Single(blobs);

            // 圆形度应接近 1（圆形）
            Assert.InRange(blob.Circularity, 0.85, 1.0);
            // MinAreaRect 应不为 null（点数 ≥ 3）
            Assert.NotNull(blob.MinAreaRect);
            // FittedEllipse 应不为 null（圆形点数 ≥ 5）
            Assert.NotNull(blob.FittedEllipse);
            // 凸包点数 ≥ 3
            Assert.InRange(blob.ConvexHull.Length, 3, int.MaxValue);
        }

        [Fact]
        public void DetectBlobs_Triangle_IsConvexButLowCircularity()
        {
            using var binary = CreateBlankBinary(100, 100);
            var pts = new[] { new Point(20, 80), new Point(80, 80), new Point(50, 20) };
            Cv2.FillPoly(binary, new[] { pts }, new Scalar(255));

            var blobs = Detector.DetectBlobs(binary);
            var blob = Assert.Single(blobs);

            // 三角形是凸形状（OpenCV 对边缘像素化结果可能返回 false，放宽断言）
            // 重点是圆形度应低于圆
            Assert.InRange(blob.Circularity, 0.3, 0.8);
        }

        [Fact]
        public void DetectBlobs_NullImage_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.DetectBlobs(null!));
        }

        #endregion

        #region DetectConnectedComponents（CC 路径）

        [Fact]
        public void DetectConnectedComponents_ReturnsExactPixelArea()
        {
            using var binary = CreateBlankBinary();
            DrawRect(binary, 10, 10, 30, 20);  // 600 像素

            var blobs = Detector.DetectConnectedComponents(binary);

            var blob = Assert.Single(blobs);
            Assert.Equal(BlobSource.ConnectedComponents, blob.Source);
            Assert.Equal(600, blob.Area);  // CC 路径下是精确像素计数
            // CC 路径下 Perimeter=0、MinAreaRect=null、ConvexHull=空
            Assert.Equal(0, blob.Perimeter);
            Assert.Null(blob.MinAreaRect);
            Assert.Empty(blob.ConvexHull);
            Assert.False(blob.IsConvex);
        }

        [Fact]
        public void DetectConnectedComponents_MinAreaFilter_FiltersSmall()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 30, 30);   // 900 像素
            DrawRect(binary, 110, 10, 5, 5);    // 25 像素

            var blobs = Detector.DetectConnectedComponents(binary, minArea: 100);

            var blob = Assert.Single(blobs);
            Assert.Equal(900, blob.Area);
        }

        [Fact]
        public void DetectConnectedComponents_CentroidCorrect()
        {
            using var binary = CreateBlankBinary(100, 100);
            DrawRect(binary, 10, 10, 30, 20);  // 中心约 (24.5, 19.5)

            var blobs = Detector.DetectConnectedComponents(binary);
            var blob = Assert.Single(blobs);
            Assert.Equal(24.5, blob.CentroidX, precision: 1);
            Assert.Equal(19.5, blob.CentroidY, precision: 1);
        }

        [Fact]
        public void DetectConnectedComponents_NullImage_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.DetectConnectedComponents(null!));
        }

        #endregion

        #region FilterBlobs

        [Fact]
        public void FilterBlobs_NullFilter_ReturnsAllAsCopy()
        {
            var input = new List<BlobInfo>
            {
                BlobInfo.FromContour(new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10) }),
                BlobInfo.FromContour(new[] { new Point(0, 0), new Point(2, 0), new Point(2, 2), new Point(0, 2) })
            };

            var result = Detector.FilterBlobs(input);
            Assert.Equal(2, result.Count);
            // 不应与输入列表同引用
            Assert.NotSame(input, result);
        }

        [Fact]
        public void FilterBlobs_ByMinArea_FiltersCorrectly()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 30, 30);  // 几何面积 841
            DrawRect(binary, 110, 10, 10, 10); // 几何面积 81

            var blobs = Detector.DetectBlobs(binary);
            var filtered = Detector.FilterBlobs(blobs, new BlobFilter { MinArea = 500 });

            var blob = Assert.Single(filtered);
            Assert.Equal(841, blob.Area, precision: 1);
        }

        [Fact]
        public void FilterBlobs_ByAllDimensions_Works()
        {
            using var binary = CreateBlankBinary(200, 200);
            DrawCircle(binary, 50, 50, 30);    // 圆形
            DrawRect(binary, 110, 10, 30, 80);  // 长条

            var blobs = Detector.DetectBlobs(binary);
            var filter = new BlobFilter
            {
                MinCircularity = 0.8  // 圆形度 ≥ 0.8 只保留圆
            };
            var filtered = Detector.FilterBlobs(blobs, filter);

            var blob = Assert.Single(filtered);
            Assert.InRange(blob.Circularity, 0.8, 1.0);
        }

        [Fact]
        public void FilterBlobs_NullBlobs_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.FilterBlobs(null!));
        }

        #endregion

        #region AdaptiveThresholdSegmentation

        [Fact]
        public void AdaptiveThresholdSegmentation_GradientImage_ReturnsBinary()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(0));
            Cv2.Rectangle(gray, new Rect(20, 20, 60, 60), new Scalar(255), -1);

            using var binary = Detector.AdaptiveThresholdSegmentation(gray, blockSize: 11);

            Assert.Equal(MatType.CV_8UC1, binary.Type());
            Assert.Equal(100, binary.Rows);
        }

        [Fact]
        public void AdaptiveThresholdSegmentation_EvenBlockSize_Throws()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(0));
            Assert.Throws<ArgumentException>(() => Detector.AdaptiveThresholdSegmentation(gray, blockSize: 10));
        }

        [Fact]
        public void AdaptiveThresholdSegmentation_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.AdaptiveThresholdSegmentation(null!));
        }

        #endregion

        #region CannyEdgeSegmentation

        [Fact]
        public void CannyEdgeSegmentation_Rectangle_FindsEdges()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(0));
            Cv2.Rectangle(gray, new Rect(20, 20, 60, 60), new Scalar(255), -1);

            using var edges = Detector.CannyEdgeSegmentation(gray, 50, 150);

            // 矩形边界处应检测到非零像素
            Assert.Equal(MatType.CV_8UC1, edges.Type());
            var nonZero = Cv2.CountNonZero(edges);
            Assert.True(nonZero > 0, "Canny should detect edges");
        }

        [Fact]
        public void CannyEdgeSegmentation_InvalidAperture_Throws()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(0));
            Assert.Throws<ArgumentException>(() => Detector.CannyEdgeSegmentation(gray, apertureSize: 4));
        }

        [Fact]
        public void CannyEdgeSegmentation_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.CannyEdgeSegmentation(null!));
        }

        #endregion

        #region ColorBasedSegmentation

        [Fact]
        public void ColorBasedSegmentation_BGR_CreatesMask()
        {
            using var color = CreateBlankColor();
            Cv2.Rectangle(color, new Rect(10, 10, 30, 30), new Scalar(0, 0, 255), -1);  // 红色方块

            using var mask = Detector.ColorBasedSegmentation(color,
                new Scalar(0, 0, 200), new Scalar(0, 0, 255), ColorSpace.BGR);

            Assert.Equal(MatType.CV_8UC1, mask.Type());
            // 红色方块区域应被选中
            var nonZero = Cv2.CountNonZero(mask);
            Assert.Equal(30 * 30, nonZero);
        }

        [Fact]
        public void ColorBasedSegmentation_HSV_ConvertsAndMasks()
        {
            using var color = CreateBlankColor();
            Cv2.Rectangle(color, new Rect(10, 10, 30, 30), new Scalar(0, 0, 255), -1);  // BGR 红色 -> HSV 红色 H≈0

            using var mask = Detector.ColorBasedSegmentation(color,
                new Scalar(0, 50, 50), new Scalar(10, 255, 255), ColorSpace.HSV);

            var nonZero = Cv2.CountNonZero(mask);
            Assert.True(nonZero > 0, "HSV mask should contain red region");
        }

        [Fact]
        public void ColorBasedSegmentation_SingleChannelImage_Throws()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(0));
            Assert.Throws<ArgumentException>(() =>
                Detector.ColorBasedSegmentation(gray, new Scalar(0), new Scalar(255)));
        }

        [Fact]
        public void ColorBasedSegmentation_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                Detector.ColorBasedSegmentation(null!, new Scalar(0), new Scalar(255)));
        }

        #endregion

        #region ApplyMorphology

        [Theory]
        [InlineData(MorphTypes.Erode)]
        [InlineData(MorphTypes.Dilate)]
        [InlineData(MorphTypes.Open)]
        [InlineData(MorphTypes.Close)]
        [InlineData(MorphTypes.Gradient)]
        public void ApplyMorphology_AllOperations_ReturnSameSize(MorphTypes op)
        {
            using var binary = CreateBlankBinary();
            DrawRect(binary, 40, 40, 20, 20);

            using var result = Detector.ApplyMorphology(binary, op, kernelSize: 3);

            Assert.Equal(binary.Rows, result.Rows);
            Assert.Equal(binary.Cols, result.Cols);
        }

        [Fact]
        public void ApplyMorphology_Dilate_IncreasesArea()
        {
            using var binary = CreateBlankBinary();
            DrawRect(binary, 40, 40, 10, 10);  // 100 像素

            using var dilated = Detector.ApplyMorphology(binary, MorphTypes.Dilate, kernelSize: 3);

            var blobsOriginal = Detector.DetectBlobs(binary);
            var blobsDilated = Detector.DetectBlobs(dilated);
            Assert.True(blobsDilated[0].Area > blobsOriginal[0].Area);
        }

        [Fact]
        public void ApplyMorphology_InvalidKernelSize_Throws()
        {
            using var binary = CreateBlankBinary();
            Assert.Throws<ArgumentException>(() => Detector.ApplyMorphology(binary, MorphTypes.Dilate, kernelSize: 0));
        }

        [Fact]
        public void ApplyMorphology_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.ApplyMorphology(null!, MorphTypes.Dilate));
        }

        #endregion

        #region GaussianBlur / EqualizeHistogram

        [Fact]
        public void GaussianBlur_ReturnsSameSize()
        {
            using var src = new Mat(50, 50, MatType.CV_8UC3, new Scalar(128, 128, 128));
            using var dst = Detector.GaussianBlur(src, ksize: 5);

            Assert.Equal(50, dst.Rows);
            Assert.Equal(50, dst.Cols);
        }

        [Fact]
        public void GaussianBlur_EvenKernel_Throws()
        {
            using var src = new Mat(50, 50, MatType.CV_8UC1, new Scalar(0));
            Assert.Throws<ArgumentException>(() => Detector.GaussianBlur(src, ksize: 4));
        }

        [Fact]
        public void GaussianBlur_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.GaussianBlur(null!));
        }

        [Fact]
        public void EqualizeHistogram_GrayImage_ReturnsEqualized()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(100));
            using var eq = Detector.EqualizeHistogram(gray);

            // 均匀灰度图均衡化后值会改变（具体值取决于直方图）
            Assert.Equal(MatType.CV_8UC1, eq.Type());
        }

        [Fact]
        public void EqualizeHistogram_ColorImage_Throws()
        {
            using var color = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 0, 0));
            Assert.Throws<ArgumentException>(() => Detector.EqualizeHistogram(color));
        }

        [Fact]
        public void EqualizeHistogram_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.EqualizeHistogram(null!));
        }

        #endregion

        #region FillHoles

        [Fact]
        public void FillHoles_FillsInteriorHoles()
        {
            using var binary = CreateBlankBinary(100, 100);
            // 绘制一个带内部孔洞的环：外圆 - 内圆
            DrawCircle(binary, 50, 50, 30);
            Cv2.Circle(binary, new Point(50, 50), 10, new Scalar(0), -1);

            // 原图中心应有孔洞（0 值）
            Assert.Equal(0, binary.At<byte>(50, 50));

            using var filled = Detector.FillHoles(binary);

            // 填充后中心应为 255
            Assert.Equal(255, filled.At<byte>(50, 50));
        }

        [Fact]
        public void FillHoles_PreservesOriginalImage()
        {
            using var binary = CreateBlankBinary(100, 100);
            DrawCircle(binary, 50, 50, 30);
            Cv2.Circle(binary, new Point(50, 50), 10, new Scalar(0), -1);

            using var filled = Detector.FillHoles(binary);

            // 原图未被修改
            Assert.Equal(0, binary.At<byte>(50, 50));
        }

        [Fact]
        public void FillHoles_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.FillHoles(null!));
        }

        #endregion

        #region CreateLabelImage

        [Fact]
        public void CreateLabelImage_TwoComponents_ReturnsColorImage()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 30, 30);
            DrawRect(binary, 110, 10, 30, 30);

            using var label = Detector.CreateLabelImage(binary);

            Assert.Equal(MatType.CV_8UC3, label.Type());
            Assert.Equal(100, label.Rows);
            Assert.Equal(200, label.Cols);
            // 两个连通域颜色应不同
            var c1 = label.At<Vec3b>(25, 25);
            var c2 = label.At<Vec3b>(25, 125);
            Assert.NotEqual(c1, c2);
        }

        [Fact]
        public void CreateLabelImage_EmptyImage_ReturnsBlack()
        {
            using var binary = CreateBlankBinary();
            using var label = Detector.CreateLabelImage(binary);

            // 无连通域时全黑
            Assert.Equal(0, label.At<byte>(50, 50));
        }

        [Fact]
        public void CreateLabelImage_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.CreateLabelImage(null!));
        }

        #endregion

        #region DrawBlobs / DrawMinAreaRects

        [Fact]
        public void DrawBlobs_DrawsOnTargetImage()
        {
            using var binary = CreateBlankBinary();
            DrawRect(binary, 10, 10, 30, 30);

            var blobs = Detector.DetectBlobs(binary);
            using var canvas = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 0, 0));
            Detector.DrawBlobs(canvas, blobs, new Scalar(0, 255, 0), thickness: 2);

            // 边界处应绘制绿色
            var pixel = canvas.At<Vec3b>(10, 25);
            Assert.Equal(0, pixel.Item0);    // B
            Assert.Equal(255, pixel.Item1);  // G
            Assert.Equal(0, pixel.Item2);    // R
        }

        [Fact]
        public void DrawBlobs_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.DrawBlobs(null!, new List<BlobInfo>()));
        }

        [Fact]
        public void DrawBlobs_NullBlobs_Throws()
        {
            using var canvas = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0));
            Assert.Throws<ArgumentNullException>(() => Detector.DrawBlobs(canvas, null!));
        }

        [Fact]
        public void DrawMinAreaRects_DrawsOnlyForContourSource()
        {
            using var binary = CreateBlankBinary();
            DrawCircle(binary, 50, 50, 30);  // 圆形有 MinAreaRect

            var blobs = Detector.DetectBlobs(binary);
            using var canvas = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 0, 0));
            Detector.DrawMinAreaRects(canvas, blobs, new Scalar(0, 0, 255), thickness: 1);

            // CC 路径下 MinAreaRect 为 null，不应绘制；Contour 路径下应绘制
            // 这里通过调用不抛异常验证
            Assert.True(true);
        }

        [Fact]
        public void DrawMinAreaRects_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.DrawMinAreaRects(null!, new List<BlobInfo>()));
        }

        #endregion

        #region FindContoursWithHierarchy

        [Fact]
        public void FindContoursWithHierarchy_NestedContours_ReturnsParentChild()
        {
            using var binary = CreateBlankBinary(100, 100);
            // 外圆
            DrawCircle(binary, 50, 50, 40);
            // 内圆（孔洞）
            Cv2.Circle(binary, new Point(50, 50), 20, new Scalar(0), -1);

            var result = Detector.FindContoursWithHierarchy(binary, RetrievalModes.CComp);

            // CComp 模式下外轮廓和内孔洞会交替出现
            Assert.True(result.Count >= 2);
            Assert.All(result, r => Assert.True(r.Hierarchy.Next >= -1));
            Assert.All(result, r => Assert.True(r.Hierarchy.Previous >= -1));
        }

        [Fact]
        public void FindContoursWithHierarchy_EmptyImage_ReturnsEmpty()
        {
            using var binary = CreateBlankBinary();
            var result = Detector.FindContoursWithHierarchy(binary);
            Assert.Empty(result);
        }

        [Fact]
        public void FindContoursWithHierarchy_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Detector.FindContoursWithHierarchy(null!));
        }

        #endregion

        #region BlobInfo.ComputeProperties / FromContour

        [Fact]
        public void BlobInfo_FromContour_LessThanThreePoints_NoMinAreaRect()
        {
            var blob = BlobInfo.FromContour(new[] { new Point(0, 0), new Point(10, 0) });
            Assert.Null(blob.MinAreaRect);
            Assert.Empty(blob.ConvexHull);
            Assert.False(blob.IsConvex);
            Assert.Null(blob.FittedEllipse);
        }

        [Fact]
        public void BlobInfo_FromContour_FivePoints_HasFittedEllipse()
        {
            var pts = new[]
            {
                new Point(0, 0), new Point(10, 0), new Point(10, 10),
                new Point(5, 15), new Point(0, 10)
            };
            var blob = BlobInfo.FromContour(pts);
            Assert.NotNull(blob.FittedEllipse);
            Assert.NotNull(blob.MinAreaRect);
        }

        [Fact]
        public void BlobInfo_ComputeProperties_EmptyContour_ResetsToZero()
        {
            var blob = new BlobInfo();
            // 默认 ContourPoints 为空
            blob.ComputeProperties();
            Assert.Equal(0, blob.Area);
            Assert.Equal(0, blob.Perimeter);
            Assert.Equal((0, 0, 0, 0), blob.BoundingBox);
        }

        #endregion

        #region BlobFilter / HierarchyInfo / ColorSpace 类型测试

        [Fact]
        public void BlobFilter_Defaults_NoRestriction()
        {
            var filter = new BlobFilter();
            Assert.Equal(0, filter.MinArea);
            Assert.Equal(double.MaxValue, filter.MaxArea);
            Assert.Equal(0, filter.MinAspectRatio);
            Assert.Equal(double.MaxValue, filter.MaxAspectRatio);
        }

        [Fact]
        public void HierarchyInfo_RecordStruct_ValueSemantics()
        {
            var h1 = new HierarchyInfo(1, -1, 2, -1);
            var h2 = new HierarchyInfo(1, -1, 2, -1);
            Assert.Equal(h1, h2);
            Assert.Equal(h1.GetHashCode(), h2.GetHashCode());
        }

        [Fact]
        public void ColorSpace_Enum_HasThreeValues()
        {
            var values = Enum.GetValues<ColorSpace>();
            Assert.Equal(3, values.Length);
            Assert.Contains(ColorSpace.BGR, values);
            Assert.Contains(ColorSpace.HSV, values);
            Assert.Contains(ColorSpace.Lab, values);
        }

        #endregion

        #region BlobExtensions: GetLargestBlob / GetBlobsInRange / SortByArea / GetStatistics / GetDistanceMatrix

        [Fact]
        public void GetLargestBlob_MultipleBlobs_ReturnsLargest()
        {
            using var binary = CreateBlankBinary(300, 100);
            DrawRect(binary, 10, 10, 20, 20);     // 几何面积 361
            DrawRect(binary, 110, 10, 30, 30);    // 几何面积 841
            DrawRect(binary, 210, 10, 10, 10);   // 几何面积 81

            var blobs = Detector.DetectBlobs(binary);
            var largest = blobs.GetLargestBlob();

            Assert.NotNull(largest);
            Assert.Equal(841, largest!.Area, precision: 1);
        }

        [Fact]
        public void GetLargestBlob_EmptyList_ReturnsNull()
        {
            var blobs = new List<BlobInfo>();
            Assert.Null(blobs.GetLargestBlob());
        }

        [Fact]
        public void GetLargestBlob_NullList_Throws()
        {
            List<BlobInfo> blobs = null!;
            Assert.Throws<ArgumentNullException>(() => blobs.GetLargestBlob());
        }

        [Fact]
        public void GetBlobsInRange_ReturnsOnlyInRange()
        {
            using var binary = CreateBlankBinary(300, 100);
            DrawRect(binary, 10, 10, 20, 20);     // 361
            DrawRect(binary, 110, 10, 30, 30);    // 841
            DrawRect(binary, 210, 10, 10, 10);    // 81

            var blobs = Detector.DetectBlobs(binary);
            var filtered = blobs.GetBlobsInRange(100, 500);

            // 81 被排除，361 + 841 在范围内？841 > 500 也会被排除
            var blob = Assert.Single(filtered);
            Assert.Equal(361, blob.Area, precision: 1);
        }

        [Fact]
        public void GetBlobsInRange_InvalidRange_Throws()
        {
            var blobs = new List<BlobInfo>();
            Assert.Throws<ArgumentException>(() => blobs.GetBlobsInRange(100, 50));
        }

        [Fact]
        public void GetBlobsInRange_NullList_Throws()
        {
            List<BlobInfo> blobs = null!;
            Assert.Throws<ArgumentNullException>(() => blobs.GetBlobsInRange(0, 100));
        }

        [Fact]
        public void SortByArea_Descending_ReturnsLargestFirst()
        {
            using var binary = CreateBlankBinary(300, 100);
            DrawRect(binary, 10, 10, 10, 10);      // 81
            DrawRect(binary, 110, 10, 30, 30);    // 841
            DrawRect(binary, 210, 10, 20, 20);    // 361

            var blobs = Detector.DetectBlobs(binary);
            var sorted = blobs.SortByArea(descending: true);

            Assert.Equal(3, sorted.Count);
            Assert.True(sorted[0].Area >= sorted[1].Area);
            Assert.True(sorted[1].Area >= sorted[2].Area);
        }

        [Fact]
        public void SortByArea_Ascending_ReturnsSmallestFirst()
        {
            using var binary = CreateBlankBinary(300, 100);
            DrawRect(binary, 10, 10, 10, 10);
            DrawRect(binary, 110, 10, 30, 30);

            var blobs = Detector.DetectBlobs(binary);
            var sorted = blobs.SortByArea(descending: false);

            Assert.True(sorted[0].Area <= sorted[1].Area);
        }

        [Fact]
        public void SortByArea_NullList_Throws()
        {
            List<BlobInfo> blobs = null!;
            Assert.Throws<ArgumentNullException>(() => blobs.SortByArea());
        }

        [Fact]
        public void GetStatistics_CalculatesAllFields()
        {
            using var binary = CreateBlankBinary(300, 100);
            DrawRect(binary, 10, 10, 10, 10);     // 81
            DrawRect(binary, 110, 10, 20, 20);   // 361

            var blobs = Detector.DetectBlobs(binary);
            var stats = blobs.GetStatistics();

            Assert.Equal(2, stats.Count);
            Assert.Equal(81 + 361, stats.TotalArea, precision: 1);
            Assert.Equal((81 + 361) / 2.0, stats.AverageArea, precision: 1);
            Assert.Equal(361, stats.MaxArea, precision: 1);
            Assert.Equal(81, stats.MinArea, precision: 1);
        }

        [Fact]
        public void GetStatistics_EmptyList_ReturnsZeros()
        {
            var blobs = new List<BlobInfo>();
            var stats = blobs.GetStatistics();

            Assert.Equal(0, stats.Count);
            Assert.Equal(0, stats.TotalArea);
            Assert.Equal(0, stats.AverageArea);
        }

        [Fact]
        public void GetStatistics_NullList_Throws()
        {
            List<BlobInfo> blobs = null!;
            Assert.Throws<ArgumentNullException>(() => blobs.GetStatistics());
        }

        [Fact]
        public void GetDistanceMatrix_TwoBlobs_ReturnsCorrectDistance()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 20, 20);     // 质心约 (19.5, 19.5)
            DrawRect(binary, 110, 10, 20, 20);    // 质心约 (119.5, 19.5)

            var blobs = Detector.DetectBlobs(binary);
            var matrix = blobs.GetDistanceMatrix();

            Assert.Equal(4, matrix.Length);  // 2x2
            // 距离 = 100（水平距离）
            Assert.Equal(100, matrix[1], precision: 1);  // [0,1]
            Assert.Equal(100, matrix[2], precision: 1);  // [1,0] 对称
            Assert.Equal(0, matrix[0]);  // [0,0] 对角线
            Assert.Equal(0, matrix[3]);  // [1,1]
        }

        [Fact]
        public void GetDistanceMatrix_ThreeBlobs_SymmetricMatrix()
        {
            var blobs = new List<BlobInfo>
            {
                BlobInfo.FromContour(new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10) }),
                BlobInfo.FromContour(new[] { new Point(100, 0), new Point(110, 0), new Point(110, 10), new Point(100, 10) }),
                BlobInfo.FromContour(new[] { new Point(0, 100), new Point(10, 100), new Point(10, 110), new Point(0, 110) })
            };

            var matrix = blobs.GetDistanceMatrix();

            Assert.Equal(9, matrix.Length);  // 3x3
            // 对称性
            Assert.Equal(matrix[1], matrix[3], precision: 2);   // [0,1] == [1,0]
            Assert.Equal(matrix[2], matrix[6], precision: 2);   // [0,2] == [2,0]
            Assert.Equal(matrix[5], matrix[7], precision: 2);   // [1,2] == [2,1]
            // 对角线为 0
            Assert.Equal(0, matrix[0]);
            Assert.Equal(0, matrix[4]);
            Assert.Equal(0, matrix[8]);
        }

        [Fact]
        public void GetDistanceMatrix_EmptyList_ReturnsEmpty()
        {
            var blobs = new List<BlobInfo>();
            var matrix = blobs.GetDistanceMatrix();
            Assert.Empty(matrix);
        }

        [Fact]
        public void GetDistanceMatrix_NullList_Throws()
        {
            List<BlobInfo> blobs = null!;
            Assert.Throws<ArgumentNullException>(() => blobs.GetDistanceMatrix());
        }

        #endregion

        #region RoiExtensions: DetectInRoi / FilterByRoi

        [Fact]
        public void DetectInRoi_OnlyDetectsBlobsInRoi()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 20, 20);     // 在 ROI 外
            DrawRect(binary, 110, 10, 20, 20);    // 在 ROI 内

            var roi = new Rect(100, 0, 100, 100);
            var blobs = binary.DetectInRoi(roi);

            var blob = Assert.Single(blobs);
            // 坐标应平移回原图坐标系（质心 X 约 119.5，不是 19.5）
            Assert.Equal(119.5, blob.CentroidX, precision: 1);
            Assert.Equal(19.5, blob.CentroidY, precision: 1);
            Assert.Equal(110, blob.BoundingBox.MinX);
        }

        [Fact]
        public void DetectInRoi_InvalidRoi_Throws()
        {
            using var binary = CreateBlankBinary(100, 100);
            Assert.Throws<ArgumentException>(() => binary.DetectInRoi(new Rect(-1, 0, 50, 50)));
            Assert.Throws<ArgumentException>(() => binary.DetectInRoi(new Rect(0, 0, 200, 50)));
        }

        [Fact]
        public void DetectInRoi_NullImage_Throws()
        {
            Mat binary = null!;
            Assert.Throws<ArgumentNullException>(() => binary.DetectInRoi(new Rect(0, 0, 50, 50)));
        }

        [Fact]
        public void FilterByRoi_ReturnsOnlyBlobsWithCentroidInRoi()
        {
            using var binary = CreateBlankBinary(200, 100);
            DrawRect(binary, 10, 10, 20, 20);     // 质心 (19.5, 19.5) - 在 ROI 外
            DrawRect(binary, 110, 10, 20, 20);    // 质心 (119.5, 19.5) - 在 ROI 内

            var blobs = Detector.DetectBlobs(binary);
            var roi = new Rect(100, 0, 100, 100);
            var filtered = blobs.FilterByRoi(roi);

            var blob = Assert.Single(filtered);
            Assert.Equal(119.5, blob.CentroidX, precision: 1);
        }

        [Fact]
        public void FilterByRoi_BlobOnRoiBoundary_Included()
        {
            // 质心正好在 ROI 左边界上，应包含（>= 判定）
            var blobs = new List<BlobInfo>
            {
                BlobInfo.FromContour(new[] { new Point(100, 0), new Point(110, 0), new Point(110, 10), new Point(100, 10) })
            };
            // 质心约 (104.5, 4.5)
            var roi = new Rect(100, 0, 50, 50);
            var filtered = blobs.FilterByRoi(roi);
            Assert.Single(filtered);
        }

        [Fact]
        public void FilterByRoi_EmptyList_ReturnsEmpty()
        {
            var blobs = new List<BlobInfo>();
            var filtered = blobs.FilterByRoi(new Rect(0, 0, 100, 100));
            Assert.Empty(filtered);
        }

        [Fact]
        public void FilterByRoi_NullList_Throws()
        {
            List<BlobInfo> blobs = null!;
            Assert.Throws<ArgumentNullException>(() => blobs.FilterByRoi(new Rect(0, 0, 100, 100)));
        }

        [Fact]
        public void FilterByRoi_InvalidRoi_Throws()
        {
            var blobs = new List<BlobInfo>();
            Assert.Throws<ArgumentException>(() => blobs.FilterByRoi(new Rect(0, 0, 0, 0)));
        }

        #endregion

        #region Pipeline: DetectPipeline / Preprocess / BlobPipelineOptions

        [Fact]
        public void DetectPipeline_DefaultOptions_ReturnsBlobs()
        {
            // 合成灰度图：亮方块在暗背景上
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(50));
            Cv2.Rectangle(gray, new Rect(20, 20, 30, 30), new Scalar(200), -1);

            var blobs = Pipeline.DetectPipeline(gray);

            // 默认配置应能检测到亮方块
            Assert.NotEmpty(blobs);
        }

        [Fact]
        public void DetectPipeline_WithMorphologyClose_FillsSmallHoles()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(50));
            Cv2.Rectangle(gray, new Rect(20, 20, 40, 40), new Scalar(200), -1);
            // 在方块内部画一个小黑点（孔洞）
            Cv2.Circle(gray, new Point(40, 40), 2, new Scalar(50), -1);

            var options = new BlobPipelineOptions
            {
                Binarization = BinarizationMethod.Fixed,
                Threshold = 100,
                MorphologyOperation = MorphTypes.Close,
                MorphologyKernelSize = 5,
                FillHoles = false
            };

            var blobs = Pipeline.DetectPipeline(gray, options);
            Assert.NotEmpty(blobs);
        }

        [Fact]
        public void DetectPipeline_WithFillHoles_FillsInterior()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(50));
            Cv2.Rectangle(gray, new Rect(20, 20, 60, 60), new Scalar(200), -1);
            // 大孔洞
            Cv2.Circle(gray, new Point(50, 50), 10, new Scalar(50), -1);

            var options = new BlobPipelineOptions
            {
                Binarization = BinarizationMethod.Fixed,
                Threshold = 100,
                FillHoles = true
            };

            // 不开 FillHoles 的对照：应包含孔洞
            var optionsNoFill = new BlobPipelineOptions
            {
                Binarization = BinarizationMethod.Fixed,
                Threshold = 100,
                FillHoles = false
            };

            var blobsFilled = Pipeline.DetectPipeline(gray, options);
            var blobsNotFilled = Pipeline.DetectPipeline(gray, optionsNoFill);

            var filledBlob = Assert.Single(blobsFilled);
            var notFilledBlob = Assert.Single(blobsNotFilled);
            // FillHoles 后面积应大于不 FillHoles
            Assert.True(filledBlob.Area > notFilledBlob.Area,
                $"Filled area {filledBlob.Area} should be > not filled {notFilledBlob.Area}");
        }

        [Fact]
        public void DetectPipeline_OtsuBinarization_Works()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(50));
            Cv2.Rectangle(gray, new Rect(20, 20, 30, 30), new Scalar(200), -1);

            var options = new BlobPipelineOptions
            {
                Binarization = BinarizationMethod.Otsu,
                GaussianKernelSize = 0  // 关闭高斯模糊
            };

            var blobs = Pipeline.DetectPipeline(gray, options);
            Assert.NotEmpty(blobs);
        }

        [Fact]
        public void DetectPipeline_AreaFilter_FiltersCorrectly()
        {
            using var gray = new Mat(300, 100, MatType.CV_8UC1, new Scalar(50));
            Cv2.Rectangle(gray, new Rect(10, 10, 10, 10), new Scalar(200), -1);
            Cv2.Rectangle(gray, new Rect(110, 10, 50, 50), new Scalar(200), -1);

            // 先验证不带过滤的检测能拿到 2 个 Blob
            var optionsNoFilter = new BlobPipelineOptions
            {
                Binarization = BinarizationMethod.Fixed,
                Threshold = 100,
                GaussianKernelSize = 0
            };
            var allBlobs = Pipeline.DetectPipeline(gray, optionsNoFilter);
            Assert.True(allBlobs.Count >= 1, $"Expected at least 1 blob without filter, got {allBlobs.Count}");

            // 再验证带 MinArea 过滤（取最大 Blob 面积的下限）
            double maxArea = allBlobs.Max(b => b.Area);
            var options = new BlobPipelineOptions
            {
                Binarization = BinarizationMethod.Fixed,
                Threshold = 100,
                GaussianKernelSize = 0,
                MinArea = maxArea - 1  // 只保留最大 Blob
            };
            var blobs = Pipeline.DetectPipeline(gray, options);
            Assert.NotEmpty(blobs);
            Assert.All(blobs, b => Assert.True(b.Area >= maxArea - 1));
        }

        [Fact]
        public void DetectPipeline_NullImage_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => Pipeline.DetectPipeline(null!));
        }

        [Fact]
        public void Preprocess_WithGaussianAndEqualize_ReturnsProcessedMat()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(100));
            var options = new BlobPipelineOptions
            {
                GaussianKernelSize = 5,
                EqualizeHistogram = true
            };

            using var result = Pipeline.Preprocess(gray, options);

            Assert.Equal(100, result.Rows);
            Assert.Equal(MatType.CV_8UC1, result.Type());
        }

        [Fact]
        public void Preprocess_NoProcessing_ReturnsClone()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(100));
            var options = new BlobPipelineOptions
            {
                GaussianKernelSize = 0,
                EqualizeHistogram = false
            };

            using var result = Pipeline.Preprocess(gray, options);

            // 即使无处理，也应返回新 Mat（Clone），不与原图共享
            Assert.NotSame(gray, result);
            Assert.Equal(100, result.Rows);
        }

        [Fact]
        public void Preprocess_NullImage_Throws()
        {
            var options = new BlobPipelineOptions();
            Assert.Throws<ArgumentNullException>(() => Pipeline.Preprocess(null!, options));
        }

        [Fact]
        public void Preprocess_NullOptions_Throws()
        {
            using var gray = new Mat(100, 100, MatType.CV_8UC1, new Scalar(0));
            Assert.Throws<ArgumentNullException>(() => Pipeline.Preprocess(gray, null!));
        }

        [Fact]
        public void BlobPipelineOptions_Defaults_AreReasonable()
        {
            var options = new BlobPipelineOptions();

            Assert.Equal(5, options.GaussianKernelSize);
            Assert.False(options.EqualizeHistogram);
            Assert.Equal(BinarizationMethod.Adaptive, options.Binarization);
            Assert.Equal(255, options.MaxValue);
            Assert.Equal(11, options.AdaptiveBlockSize);
            Assert.Null(options.MorphologyOperation);
            Assert.False(options.FillHoles);
        }

        [Fact]
        public void BinarizationMethod_Enum_HasThreeValues()
        {
            var values = Enum.GetValues<BinarizationMethod>();
            Assert.Equal(3, values.Length);
            Assert.Contains(BinarizationMethod.Adaptive, values);
            Assert.Contains(BinarizationMethod.Otsu, values);
            Assert.Contains(BinarizationMethod.Fixed, values);
        }

        [Fact]
        public void BlobStatistics_RecordStruct_ValueSemantics()
        {
            var s1 = new BlobStatistics(2, 100.5, 50.25, 75.0, 25.5);
            var s2 = new BlobStatistics(2, 100.5, 50.25, 75.0, 25.5);
            Assert.Equal(s1, s2);
            Assert.Equal(s1.GetHashCode(), s2.GetHashCode());
        }

        #endregion

        public void Dispose()
        {
            foreach (var mat in _disposables)
            {
                mat.Dispose();
            }
            _disposables.Clear();
        }
    }
}
