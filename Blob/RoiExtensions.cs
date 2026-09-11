using OpenCvSharp;

namespace Blob
{
    /// <summary>
    /// ROI（感兴趣区域）相关扩展。提供"只检测 ROI 内 Blob"和"过滤 ROI 外 Blob"的便捷方法。
    /// </summary>
    public static class RoiExtensions
    {
        /// <summary>
        /// 仅在 ROI 矩形内检测 Blob：先裁剪子图，检测后再把坐标平移回原图坐标系。
        /// 比"全图检测后过滤"更快——避免处理 ROI 外的无关 Blob。
        /// </summary>
        /// <param name="binaryImage">原图二值图像。</param>
        /// <param name="roi">感兴趣区域（图像坐标系）。</param>
        /// <param name="minArea">最小面积过滤。</param>
        /// <param name="maxArea">最大面积过滤。&lt;= 0 表示不过滤。</param>
        /// <returns>ROI 内的 Blob 列表，坐标已平移回原图坐标系（BoundingBox/质心/MinAreaRect 均为原图坐标）。</returns>
        public static List<BlobInfo> DetectInRoi(this Mat binaryImage, Rect roi, double minArea = 0, double maxArea = 0)
        {
            if (binaryImage is null) throw new ArgumentNullException(nameof(binaryImage));
            if (roi.X < 0 || roi.Y < 0 || roi.Width <= 0 || roi.Height <= 0)
                throw new ArgumentException("ROI must be within image bounds with positive size.", nameof(roi));
            if (roi.X + roi.Width > binaryImage.Cols || roi.Y + roi.Height > binaryImage.Rows)
                throw new ArgumentException("ROI exceeds image bounds.", nameof(roi));

            // 裁剪子图（SubMat 共享内存，必须 Clone 防止 FindContours 修改原图）
            using var subMat = binaryImage.SubMat(roi).Clone();
            var blobs = Detector.DetectBlobs(subMat, minArea, maxArea);

            // 把 BlobInfo 中的坐标平移回原图坐标系
            foreach (var b in blobs)
            {
                b.Translate(roi.X, roi.Y);
            }
            return blobs;
        }

        /// <summary>
        /// 过滤质心在 ROI 内的 Blob。只读过滤，不修改 Blob。
        /// </summary>
        /// <param name="blobs">Blob 列表。</param>
        /// <param name="roi">感兴趣区域。</param>
        /// <returns>质心在 ROI 内的 Blob 列表。</returns>
        public static List<BlobInfo> FilterByRoi(this IEnumerable<BlobInfo> blobs, Rect roi)
        {
            if (blobs is null) throw new ArgumentNullException(nameof(blobs));
            if (roi.Width <= 0 || roi.Height <= 0)
                throw new ArgumentException("ROI must have positive size.", nameof(roi));

            return blobs.Where(b =>
                b.CentroidX >= roi.X && b.CentroidX < roi.X + roi.Width &&
                b.CentroidY >= roi.Y && b.CentroidY < roi.Y + roi.Height).ToList();
        }
    }

    /// <summary>BlobInfo 坐标平移扩展（内部使用，用于 DetectInRoi 坐标变换）。</summary>
    internal static class BlobInfoTranslateExtension
    {
        /// <summary>
        /// 将 Blob 的所有坐标按 (dx, dy) 平移。包括 BoundingBox、质心、ContourPoints、Pixels。
        /// MinAreaRect/FittedEllipse 用 ContourPoints 重新计算，无需单独平移。
        /// </summary>
        internal static void Translate(this BlobInfo blob, int dx, int dy)
        {
            if (blob.ContourPoints.Length == 0) return;

            // 平移 ContourPoints
            for (int i = 0; i < blob.ContourPoints.Length; i++)
            {
                blob.ContourPoints[i] = new Point(blob.ContourPoints[i].X + dx, blob.ContourPoints[i].Y + dy);
            }

            // 平移 Pixels
            for (int i = 0; i < blob.Pixels.Count; i++)
            {
                blob.Pixels[i] = (blob.Pixels[i].X + dx, blob.Pixels[i].Y + dy);
            }

            // 平移 BoundingBox
            var bb = blob.BoundingBox;
            blob.SetBoundingBoxInternal((bb.MinX + dx, bb.MinY + dy, bb.MaxX + dx, bb.MaxY + dy));

            // 平移质心
            blob.SetCentroidInternal(blob.CentroidX + dx, blob.CentroidY + dy);

            // 重新计算依赖 ContourPoints 的属性（MinAreaRect/FittedEllipse/ConvexHull）
            // 注意：Area/Perimeter 不随平移变化，无需重算
            blob.RecomputeGeometryFromContour();
        }
    }
}
