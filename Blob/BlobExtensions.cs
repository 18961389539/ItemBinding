using OpenCvSharp;

namespace Blob
{
    /// <summary>
    /// Blob 业务便捷扩展方法。提供 OpenCV 不直接提供、但调用方反复要写的样板逻辑。
    /// </summary>
    public static class BlobExtensions
    {
        /// <summary>
        /// 返回面积最大的 Blob。列表为空时返回 null。
        /// </summary>
        public static BlobInfo? GetLargestBlob(this IReadOnlyList<BlobInfo> blobs)
        {
            if (blobs is null) throw new ArgumentNullException(nameof(blobs));
            if (blobs.Count == 0) return null;

            BlobInfo largest = blobs[0];
            for (int i = 1; i < blobs.Count; i++)
            {
                if (blobs[i].Area > largest.Area) largest = blobs[i];
            }
            return largest;
        }

        /// <summary>
        /// 返回面积在 [minArea, maxArea] 范围内的 Blob。
        /// 比 FilterBlobs(blobs, new BlobFilter{...}) 更简洁。
        /// </summary>
        public static List<BlobInfo> GetBlobsInRange(this IEnumerable<BlobInfo> blobs, double minArea, double maxArea)
        {
            if (blobs is null) throw new ArgumentNullException(nameof(blobs));
            if (maxArea < minArea) throw new ArgumentException("maxArea must be >= minArea.");

            return blobs.Where(b => b.Area >= minArea && b.Area <= maxArea).ToList();
        }

        /// <summary>
        /// 按面积降序返回新列表（不修改原列表）。
        /// </summary>
        public static List<BlobInfo> SortByArea(this IEnumerable<BlobInfo> blobs, bool descending = true)
        {
            if (blobs is null) throw new ArgumentNullException(nameof(blobs));
            return descending
                ? blobs.OrderByDescending(b => b.Area).ToList()
                : blobs.OrderBy(b => b.Area).ToList();
        }

        /// <summary>
        /// 计算 Blob 列表的统计信息：总数、总面积、平均面积、最大/最小面积。
        /// </summary>
        public static BlobStatistics GetStatistics(this IEnumerable<BlobInfo> blobs)
        {
            if (blobs is null) throw new ArgumentNullException(nameof(blobs));

            double total = 0, max = double.MinValue, min = double.MaxValue;
            int count = 0;
            foreach (var b in blobs)
            {
                total += b.Area;
                if (b.Area > max) max = b.Area;
                if (b.Area < min) min = b.Area;
                count++;
            }
            if (count == 0) return new BlobStatistics(0, 0, 0, 0, 0);
            return new BlobStatistics(count, total, total / count, max, min);
        }

        /// <summary>
        /// 计算 Blob 之间的两两质心欧氏距离矩阵。
        /// 对角线为 0，对称矩阵。N=2 时返回 1x1。
        /// </summary>
        /// <param name="blobs">Blob 列表。</param>
        /// <returns>N×N 距离矩阵。blobs[i] vs blobs[j] → matrix[i*N + j]。</returns>
        public static double[] GetDistanceMatrix(this IReadOnlyList<BlobInfo> blobs)
        {
            if (blobs is null) throw new ArgumentNullException(nameof(blobs));

            int n = blobs.Count;
            var matrix = new double[n * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = i; j < n; j++)
                {
                    if (i == j)
                    {
                        matrix[i * n + j] = 0;
                        continue;
                    }
                    double dx = blobs[i].CentroidX - blobs[j].CentroidX;
                    double dy = blobs[i].CentroidY - blobs[j].CentroidY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    matrix[i * n + j] = dist;
                    matrix[j * n + i] = dist;
                }
            }
            return matrix;
        }
    }

    /// <summary>Blob 列表的统计信息。值类型，便于传递。</summary>
    /// <param name="Count">Blob 总数。</param>
    /// <param name="TotalArea">所有 Blob 面积之和。</param>
    /// <param name="AverageArea">平均面积。</param>
    /// <param name="MaxArea">最大 Blob 面积。</param>
    /// <param name="MinArea">最小 Blob 面积。</param>
    public readonly record struct BlobStatistics(
        int Count,
        double TotalArea,
        double AverageArea,
        double MaxArea,
        double MinArea);
}
