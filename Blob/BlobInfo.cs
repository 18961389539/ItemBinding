using OpenCvSharp;

namespace Blob
{
    /// <summary>
    /// 单个连通域（Blob）的信息：包含面积、质心、外接矩形、
    /// 旋转外接矩形、拟合椭圆、凸包等几何属性。
    /// 所有派生属性在 <see cref="ComputeProperties"/> 中一次性计算。
    /// </summary>
    public class BlobInfo
    {
        /// <summary>
        /// 原始轮廓点集合（来自 Cv2.FindContours，已脱离 OpenCvSharp Mat 生命周期）。
        /// 注：轮廓点不等于连通域内像素；面积应使用 <see cref="Area"/> 而非 Pixels.Count。
        /// </summary>
        public List<(int X, int Y)> Pixels { get; } = new();

        /// <summary>
        /// 用于几何计算的原始 OpenCV 轮廓点。
        /// ConnectedComponent 路径下为外接矩形顶点（仅 4 点），不应再用于周长/凸包等计算。
        /// </summary>
        internal Point[] ContourPoints { get; private set; } = Array.Empty<Point>();

        /// <summary>
        /// 标识本 Blob 的来源路径，决定哪些字段有效。
        /// - <see cref="BlobSource.Contour"/>：来自 FindContours，所有几何字段有效
        /// - <see cref="BlobSource.ConnectedComponents"/>：来自 ConnectedComponentsWithStats，
        ///   Area/Centroid/BoundingBox 来自 stats 真实值，Perimeter/MinAreaRect/ConvexHull/FittedEllipse 无意义
        /// </summary>
        public BlobSource Source { get; private set; } = BlobSource.Contour;

        /// <summary>
        /// 面积。Contour 路径下来自 Cv2.ContourArea（几何面积）；
        /// ConnectedComponents 路径下来自 stats.Area（像素计数）。
        /// </summary>
        public double Area { get; private set; }

        /// <summary>质心 X 坐标。Contour 路径下来自 Cv2.Moments（图像矩质心）；CC 路径下来自 centroids。</summary>
        public double CentroidX { get; private set; }

        /// <summary>质心 Y 坐标。</summary>
        public double CentroidY { get; private set; }

        /// <summary>轴对齐外接矩形 (MinX, MinY, MaxX, MaxY)。</summary>
        public (int MinX, int MinY, int MaxX, int MaxY) BoundingBox { get; private set; }

        /// <summary>轴对齐外接框宽度。</summary>
        public int BoundingWidth => BoundingBox.MaxX - BoundingBox.MinX + 1;

        /// <summary>轴对齐外接框高度。</summary>
        public int BoundingHeight => BoundingBox.MaxY - BoundingBox.MinY + 1;

        /// <summary>长宽比（宽度/高度），高度为 0 时返回 0。</summary>
        public double AspectRatio => BoundingHeight > 0 ? (double)BoundingWidth / BoundingHeight : 0;

        /// <summary>
        /// 轮廓周长（由 Cv2.ArcLength 计算）。
        /// 仅在 <see cref="Source"/> == <see cref="BlobSource.Contour"/> 时有效；CC 路径下为 0。
        /// </summary>
        public double Perimeter { get; private set; }

        /// <summary>
        /// 圆形度 = 4π·Area / Perimeter²。
        /// 仅在 <see cref="Source"/> == <see cref="BlobSource.Contour"/> 且 Perimeter &gt; 0 时有效。
        /// </summary>
        public double Circularity => Perimeter > 0 ? 4 * Math.PI * Area / (Perimeter * Perimeter) : 0;

        /// <summary>紧凑度 = Area / (BoundingWidth × BoundingHeight)。两种路径下均有效。</summary>
        public double Compactness => (BoundingWidth * BoundingHeight) > 0 ? Area / (BoundingWidth * BoundingHeight) : 0;

        /// <summary>
        /// 最小面积外接矩形（旋转矩形）。
        /// 仅 Contour 路径且轮廓点数 &ge; 3 时不为 null；CC 路径下始终为 null（4 顶点矩形无意义）。
        /// </summary>
        public RotatedRect? MinAreaRect { get; private set; }

        /// <summary>
        /// 拟合椭圆（最小二乘拟合）。
        /// 仅 Contour 路径且轮廓点数 &ge; 5 时不为 null；CC 路径下始终为 null。
        /// </summary>
        public RotatedRect? FittedEllipse { get; private set; }

        /// <summary>凸包点集。仅 Contour 路径有效；CC 路径下为空数组。</summary>
        public Point[] ConvexHull { get; private set; } = Array.Empty<Point>();

        /// <summary>是否为凸轮廓。仅 Contour 路径有效。</summary>
        public bool IsConvex { get; private set; }

        /// <summary>
        /// 根据当前 <see cref="ContourPoints"/> 计算所有派生几何属性。
        /// 必须在填充 ContourPoints 后调用。
        /// </summary>
        public void ComputeProperties()
        {
            if (ContourPoints.Length == 0)
            {
                ResetToZero();
                return;
            }

            // 轴对齐外接框
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var p in ContourPoints)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            BoundingBox = (minX, minY, maxX, maxY);

            // 质心：使用 Cv2.Moments 计算图像矩质心（对非凸轮廓准确）
            var m = Cv2.Moments(ContourPoints);
            CentroidX = m.M00 > 0 ? m.M10 / m.M00 : 0;
            CentroidY = m.M00 > 0 ? m.M01 / m.M00 : 0;

            // 面积、周长：使用 OpenCV 标准实现，避免简单计数/折线求和的误差
            Area = Math.Abs(Cv2.ContourArea(ContourPoints, true));
            Perimeter = Cv2.ArcLength(ContourPoints, true);

            // 旋转外接矩形、凸包、凸性（至少 3 点才有意义）
            if (ContourPoints.Length >= 3)
            {
                MinAreaRect = Cv2.MinAreaRect(ContourPoints);
                ConvexHull = Cv2.ConvexHull(ContourPoints, false);
                IsConvex = Cv2.IsContourConvex(ContourPoints);
            }
            else
            {
                MinAreaRect = null;
                ConvexHull = Array.Empty<Point>();
                IsConvex = false;
            }

            // 拟合椭圆需要至少 5 点
            FittedEllipse = ContourPoints.Length >= 5
                ? Cv2.FitEllipse(ContourPoints)
                : null;
        }

        /// <summary>从 OpenCV 轮廓点构造并立即计算派生属性（Contour 路径）。</summary>
        public static BlobInfo FromContour(Point[] contour)
        {
            var blob = new BlobInfo
            {
                ContourPoints = contour,
                Source = BlobSource.Contour
            };
            blob.Pixels.Capacity = contour.Length;
            foreach (var p in contour)
            {
                blob.Pixels.Add((p.X, p.Y));
            }
            blob.ComputeProperties();
            return blob;
        }

        /// <summary>
        /// 从 ConnectedComponentsWithStats 的 stats 与 centroids 构造（CC 路径）。
        /// 不提取真实轮廓，因此 Perimeter/MinAreaRect/ConvexHull/FittedEllipse 均无意义。
        /// </summary>
        internal static BlobInfo FromConnectedComponent(int area, int left, int top, int width, int height, double cx, double cy)
        {
            var blob = new BlobInfo
            {
                Area = area,
                CentroidX = cx,
                CentroidY = cy,
                BoundingBox = (left, top, left + width - 1, top + height - 1),
                Source = BlobSource.ConnectedComponents
            };
            // 同步投影外接矩形顶点到 Pixels/ContourPoints 供 DrawBlobs 使用
            var contour = new[]
            {
                new Point(left, top),
                new Point(left + width - 1, top),
                new Point(left + width - 1, top + height - 1),
                new Point(left, top + height - 1)
            };
            blob.ContourPoints = contour;
            blob.Pixels.Capacity = contour.Length;
            foreach (var p in contour)
            {
                blob.Pixels.Add((p.X, p.Y));
            }
            return blob;
        }

        /// <summary>供 RoiExtensions.Translate 调用：直接设置 BoundingBox。</summary>
        internal void SetBoundingBoxInternal((int MinX, int MinY, int MaxX, int MaxY) value)
            => BoundingBox = value;

        /// <summary>供 RoiExtensions.Translate 调用：直接设置质心。</summary>
        internal void SetCentroidInternal(double cx, double cy)
        {
            CentroidX = cx;
            CentroidY = cy;
        }

        /// <summary>
        /// 供 RoiExtensions.Translate 调用：在 ContourPoints 已平移后，
        /// 重新计算依赖 ContourPoints 的派生属性（MinAreaRect/FittedEllipse/ConvexHull/IsConvex）。
        /// Area/Perimeter 不随平移变化，无需重算。
        /// </summary>
        internal void RecomputeGeometryFromContour()
        {
            if (ContourPoints.Length >= 3)
            {
                MinAreaRect = Cv2.MinAreaRect(ContourPoints);
                ConvexHull = Cv2.ConvexHull(ContourPoints, false);
                IsConvex = Cv2.IsContourConvex(ContourPoints);
            }
            FittedEllipse = ContourPoints.Length >= 5
                ? Cv2.FitEllipse(ContourPoints)
                : null;
        }

        private void ResetToZero()
        {
            CentroidX = 0;
            CentroidY = 0;
            BoundingBox = (0, 0, 0, 0);
            Area = 0;
            Perimeter = 0;
            MinAreaRect = null;
            FittedEllipse = null;
            ConvexHull = Array.Empty<Point>();
            IsConvex = false;
        }
    }

    /// <summary>
    /// 标识 <see cref="BlobInfo"/> 的来源路径，决定哪些字段有效。
    /// </summary>
    public enum BlobSource
    {
        /// <summary>来自 Cv2.FindContours，所有几何字段有效。</summary>
        Contour,
        /// <summary>
        /// 来自 Cv2.ConnectedComponentsWithStats。
        /// Area/Centroid/BoundingBox 来自 stats 真实值，
        /// Perimeter/MinAreaRect/ConvexHull/FittedEllipse 为 0/null/空（因未提取真实轮廓）。
        /// </summary>
        ConnectedComponents
    }
}
