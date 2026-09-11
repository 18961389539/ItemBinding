using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace Extensions
{
    public static class NMSExtensions
    {
        /// <summary>
        /// 对矩形进行非极大值抑制 (NMS)
        /// </summary>
        /// <param name="rects">矩形列表</param>
        /// <param name="scores">对应的置信度分数 (可选，如果为null则使用默认分数)</param>
        /// <param name="iouThreshold">IoU阈值，高于此值的矩形将被抑制 (默认0.5)</param>
        /// <param name="scoreThreshold">分数阈值，低于此值的矩形将被忽略 (可选)</param>
        /// <returns>保留的矩形索引列表</returns>
        public static List<int> NonMaxSuppression(
            this List<Rect> rects,
            List<float>? scores = null,
            float iouThreshold = 0.5f,
            float? scoreThreshold = null)
        {
            return NonMaxSuppression(rects,
                (r1, r2) => CalculateIoU(r1, r2),
                scores,
                iouThreshold,
                scoreThreshold);
        }

        /// <summary>
        /// 对圆形进行非极大值抑制 (NMS)
        /// </summary>
        /// <param name="circles">圆形列表 (每个圆形表示为 (center.X, center.Y, radius))</param>
        /// <param name="scores">对应的置信度分数 (可选，如果为null则使用默认分数)</param>
        /// <param name="iouThreshold">IoU阈值，高于此值的圆形将被抑制 (默认0.5)</param>
        /// <param name="scoreThreshold">分数阈值，低于此值的圆形将被忽略 (可选)</param>
        /// <returns>保留的圆形索引列表</returns>
        public static List<int> NonMaxSuppression(
            this List<CircleSegment> circles,
            List<float>? scores = null,
            float iouThreshold = 0.5f,
            float? scoreThreshold = null)
        {
            return NonMaxSuppression(circles,
                (c1, c2) => CalculateIoU(c1, c2),
                scores,
                iouThreshold,
                scoreThreshold);
        }

        /// <summary>
        /// 对旋转矩形进行非极大值抑制 (NMS)
        /// </summary>
        /// <param name="rotatedRects">旋转矩形列表</param>
        /// <param name="scores">对应的置信度分数 (可选，如果为null则使用默认分数)</param>
        /// <param name="iouThreshold">IoU阈值，高于此值的矩形将被抑制 (默认0.5)</param>
        /// <param name="scoreThreshold">分数阈值，低于此值的矩形将被忽略 (可选)</param>
        /// <returns>保留的旋转矩形索引列表</returns>
        public static List<int> NonMaxSuppression(
            this List<RotatedRect> rotatedRects,
            List<float>? scores = null,
            float iouThreshold = 0.5f,
            float? scoreThreshold = null)
        {
            return NonMaxSuppression(rotatedRects,
                (r1, r2) => CalculateIoU(r1, r2),
                scores,
                iouThreshold,
                scoreThreshold);
        }

        /// <summary>
        /// 通用非极大值抑制算法
        /// </summary>
        /// <typeparam name="T">形状类型</typeparam>
        /// <param name="items">形状列表</param>
        /// <param name="iouFunc">计算两个形状IoU的函数</param>
        /// <param name="scores">对应的置信度分数 (可选，如果为null则使用默认分数)</param>
        /// <param name="iouThreshold">IoU阈值，高于此值的形状将被抑制</param>
        /// <param name="scoreThreshold">分数阈值，低于此值的形状将被忽略 (可选)</param>
        /// <returns>保留的形状索引列表</returns>
        public static List<int> NonMaxSuppression<T>(
            this List<T> items,
            Func<T, T, float> iouFunc,
            List<float>? scores = null,
            float iouThreshold = 0.5f,
            float? scoreThreshold = null)
        {
            // M316b/L403a: items 和 iouFunc 参数需 null 检查
            ArgumentNullException.ThrowIfNull(items);
            ArgumentNullException.ThrowIfNull(iouFunc);

            int n = items.Count;
            if (n == 0) return new List<int>();

            // 处理分数
            // L417b: 已知限制 - scores 为 null 时仍分配 usedScores 数组（默认值 1.0）。
            // 可改用懒加载或索引器避免分配，但需重构后续逻辑（usedScores 在多处使用），改动较大，暂不改动
            float[] usedScores;
            if (scores != null)
            {
                // L116: scores 数量与 items 不匹配时抛出异常，避免静默回退到默认分数导致结果错误
                if (scores.Count != n)
                    throw new ArgumentException($"scores 数量({scores.Count})与 items 数量({n})不匹配", nameof(scores));
                usedScores = scores.ToArray();
            }
            else
            {
                // 默认分数为1.0
                usedScores = Enumerable.Repeat(1.0f, n).ToArray();
            }

            // 应用分数阈值
            List<int> indices = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (scoreThreshold == null || usedScores[i] >= scoreThreshold.Value)
                {
                    indices.Add(i);
                }
            }

            // M251: 添加索引作为 tiebreaker，确保相同分数时排序稳定
            indices.Sort((a, b) => { int c = usedScores[b].CompareTo(usedScores[a]); return c != 0 ? c : a.CompareTo(b); });

            List<int> keep = new List<int>();
            // L421a: 已知限制 - 每次迭代分配新 List<int> remaining，整体 O(n²) 分配。
            // 可改用原地双指针覆盖 indices 避免重复分配，但改动较大且需谨慎处理索引顺序，暂不改动
            while (indices.Count > 0)
            {
                int current = indices[0];
                keep.Add(current);

                List<int> remaining = new List<int>();
                for (int i = 1; i < indices.Count; i++)
                {
                    int idx = indices[i];
                    float iou = iouFunc(items[current], items[idx]);
                    if (iou <= iouThreshold)
                    {
                        remaining.Add(idx);
                    }
                }
                indices = remaining;
            }

            // 按原始顺序排序返回的索引
            keep.Sort();
            return keep;
        }

        /// <summary>
        /// 计算两个矩形的交并比 (IoU)
        /// </summary>
        public static float CalculateIoU(Rect rect1, Rect rect2)
        {
            int x1 = Math.Max(rect1.Left, rect2.Left);
            int y1 = Math.Max(rect1.Top, rect2.Top);
            int x2 = Math.Min(rect1.Right, rect2.Right);
            int y2 = Math.Min(rect1.Bottom, rect2.Bottom);

            if (x2 <= x1 || y2 <= y1)
                return 0.0f;

            int intersection = (x2 - x1) * (y2 - y1);
            int area1 = rect1.Width * rect1.Height;
            int area2 = rect2.Width * rect2.Height;
            int union = area1 + area2 - intersection;

            // M132: 防止 union 为 0 时除零异常
            if (union <= 0) return 0.0f;

            return (float)intersection / union;
        }

        /// <summary>
        /// 计算两个圆形的交并比 (IoU)
        /// </summary>
        public static float CalculateIoU(CircleSegment circle1, CircleSegment circle2)
        {
            double centerDistance = Math.Sqrt(
                Math.Pow(circle1.Center.X - circle2.Center.X, 2) +
                Math.Pow(circle1.Center.Y - circle2.Center.Y, 2)
            );

            double r1 = circle1.Radius;
            double r2 = circle2.Radius;

            // M306a: 半径非正时无法构成有效圆形，直接返回 0
            if (r1 <= 0 || r2 <= 0) return 0.0f;

            // 如果两圆相离
            if (centerDistance >= r1 + r2)
                return 0.0f;

            // 如果一圆完全包含另一圆
            if (centerDistance <= Math.Abs(r1 - r2))
            {
                double minR = Math.Min(r1, r2);
                double maxR = Math.Max(r1, r2);
                return (float)(minR * minR / (maxR * maxR));
            }

            // 计算相交面积 (圆形相交面积公式)
            double r1Sq = r1 * r1;
            double r2Sq = r2 * r2;
            double d = centerDistance;

            double d1 = (r1Sq - r2Sq + d * d) / (2 * d);
            double d2 = d - d1;

            // M133: Math.Acos 输入需截断到 [-1, 1]，避免浮点误差导致 NaN
            // H67: Math.Sqrt 参数需 Math.Max(0, ...)，避免浮点误差导致负数开方产生 NaN
            double intersection = r1Sq * Math.Acos(Math.Clamp(d1 / r1, -1.0, 1.0)) - d1 * Math.Sqrt(Math.Max(0, r1Sq - d1 * d1))
                                + r2Sq * Math.Acos(Math.Clamp(d2 / r2, -1.0, 1.0)) - d2 * Math.Sqrt(Math.Max(0, r2Sq - d2 * d2));

            double area1 = Math.PI * r1Sq;
            double area2 = Math.PI * r2Sq;
            double union = area1 + area2 - intersection;

            return (float)(intersection / union);
        }

        /// <summary>
        /// 计算两个旋转矩形的交并比 (IoU) - 使用边界矩形近似
        /// </summary>
        /// <remarks>
        /// L369a: 此方法使用旋转矩形的外接矩形（axis-aligned bounding box）近似计算 IoU，
        /// 当旋转角度较大或矩形细长时精度损失明显（IoU 偏高）。
        /// 如需精确计算请使用 <see cref="CalculateIoUPrecise(RotatedRect, RotatedRect)"/>。
        /// </remarks>
        public static float CalculateIoU(RotatedRect rect1, RotatedRect rect2)
        {
            // 获取旋转矩形的边界矩形
            Rect bounding1 = rect1.BoundingRect();
            Rect bounding2 = rect2.BoundingRect();

            return CalculateIoU(bounding1, bounding2);
        }

        /// <summary>
        /// 计算两个旋转矩形的精确交并比 (IoU) - 使用多边形相交
        /// </summary>
        public static float CalculateIoUPrecise(RotatedRect rect1, RotatedRect rect2)
        {
            // 获取旋转矩形的四个顶点
            Point2f[] vertices1 = rect1.Points();
            Point2f[] vertices2 = rect2.Points();

            // 计算多边形相交面积
            float intersection = ConvexPolygonIntersectionArea(vertices1, vertices2);
            if (intersection <= 0) return 0.0f;

            float area1 = PolygonArea(vertices1);
            float area2 = PolygonArea(vertices2);
            float union = area1 + area2 - intersection;

            return intersection / union;
        }

        /// <summary>
        /// 计算凸多边形相交面积
        /// </summary>
        private static float ConvexPolygonIntersectionArea(Point2f[] poly1, Point2f[] poly2)
        {
            // M261: 不使用 out 参数结果，使用弃元避免无用分配
            return Cv2.IntersectConvexConvex(poly1, poly2, out _, true);
        }

        /// <summary>
        /// 计算多边形面积 (鞋带公式)
        /// </summary>
        private static float PolygonArea(Point2f[] polygon)
        {
            int n = polygon.Length;
            float area = 0.0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += polygon[i].X * polygon[j].Y;
                area -= polygon[j].X * polygon[i].Y;
            }
            return Math.Abs(area) / 2.0f;
        }

        /// <summary>
        /// 从原始列表中筛选出保留的矩形
        /// </summary>
        public static List<Rect> FilterRects(this List<Rect> rects, List<int> keepIndices)
        {
            // M334a: 参数 null 检查
            ArgumentNullException.ThrowIfNull(rects);
            ArgumentNullException.ThrowIfNull(keepIndices);
            List<Rect> result = new List<Rect>();
            // L256: 校验索引越界，避免 ArgumentOutOfRangeException 中止整个筛选
            foreach (int idx in keepIndices)
            {
                if (idx >= 0 && idx < rects.Count)
                    result.Add(rects[idx]);
            }
            return result;
        }

        /// <summary>
        /// 从原始列表中筛选出保留的圆形
        /// </summary>
        public static List<CircleSegment> FilterCircles(this List<CircleSegment> circles, List<int> keepIndices)
        {
            // M334a: 参数 null 检查
            ArgumentNullException.ThrowIfNull(circles);
            ArgumentNullException.ThrowIfNull(keepIndices);
            List<CircleSegment> result = new List<CircleSegment>();
            foreach (int idx in keepIndices)
            {
                if (idx >= 0 && idx < circles.Count)
                    result.Add(circles[idx]);
            }
            return result;
        }

        /// <summary>
        /// 从原始列表中筛选出保留的旋转矩形
        /// </summary>
        public static List<RotatedRect> FilterRotatedRects(this List<RotatedRect> rotatedRects, List<int> keepIndices)
        {
            // M334a: 参数 null 检查
            ArgumentNullException.ThrowIfNull(rotatedRects);
            ArgumentNullException.ThrowIfNull(keepIndices);
            List<RotatedRect> result = new List<RotatedRect>();
            foreach (int idx in keepIndices)
            {
                if (idx >= 0 && idx < rotatedRects.Count)
                    result.Add(rotatedRects[idx]);
            }
            return result;
        }

        /// <summary>
        /// 对矩形进行非极大值抑制并返回过滤后的矩形列表
        /// </summary>
        public static List<Rect> ApplyNMS(
            this List<Rect> rects,
            List<float>? scores = null,
            float iouThreshold = 0.5f,
            float? scoreThreshold = null)
        {
            List<int> keepIndices = rects.NonMaxSuppression(scores, iouThreshold, scoreThreshold);
            return FilterRects(rects, keepIndices);
        }

        /// <summary>
        /// 对圆形进行非极大值抑制并返回过滤后的圆形列表
        /// </summary>
        public static List<CircleSegment> ApplyNMS(
            this List<CircleSegment> circles,
            List<float>? scores = null,
            float iouThreshold = 0.5f,
            float? scoreThreshold = null)
        {
            List<int> keepIndices = circles.NonMaxSuppression(scores, iouThreshold, scoreThreshold);
            return FilterCircles(circles, keepIndices);
        }

        /// <summary>
        /// 对旋转矩形进行非极大值抑制并返回过滤后的旋转矩形列表
        /// </summary>
        public static List<RotatedRect> ApplyNMS(
            this List<RotatedRect> rotatedRects,
            List<float>? scores = null,
            float iouThreshold = 0.5f,
            float? scoreThreshold = null,
            bool precise = false)
        {
            Func<RotatedRect, RotatedRect, float> iouFunc = precise ?
                (r1, r2) => CalculateIoUPrecise(r1, r2) :
                (r1, r2) => CalculateIoU(r1, r2);

            List<int> keepIndices = rotatedRects.NonMaxSuppression(iouFunc, scores, iouThreshold, scoreThreshold);
            return FilterRotatedRects(rotatedRects, keepIndices);
        }

        /// <summary>
        /// 通用非极大值抑制并返回过滤后的列表
        /// </summary>
        public static List<T> ApplyNMS<T>(
            this List<T> items,
            Func<T, T, float> iouFunc,
            List<float>? scores = null,
            float iouThreshold = 0.5f,
            float? scoreThreshold = null)
        {
            List<int> keepIndices = items.NonMaxSuppression(iouFunc, scores, iouThreshold, scoreThreshold);
            List<T> result = new List<T>();
            foreach (int idx in keepIndices)
            {
                result.Add(items[idx]);
            }
            return result;
        }
    }
}
