using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Extensions
{
    public static class ContoursExtensions
    {
        // L111: 矩计算的零值判定阈值，提取为常量避免魔法数字
        private const double MomentsEpsilon = 1e-4;

        #region GetCenterByAverage
        public static Point2d GetCenterByAverage(this IEnumerable<Point2d> contours)
        {
            // L110: 转为 List 避免多次枚举 IEnumerable
            var list = contours.ToList();
            if (list.Count == 0)
                throw new ArgumentException("Contours collection is empty.");

            var x = list.Average(c => c.X);
            var y = list.Average(c => c.Y);
            return new Point2d(x, y);
        }

        public static Point2d GetCenterByAverage(this IEnumerable<Point> contours)
        {
            // L110: 转为 List 避免多次枚举 IEnumerable
            var list = contours.ToList();
            if (list.Count == 0)
                throw new ArgumentException("Contours collection is empty.");

            var x = list.Average(c => c.X);
            var y = list.Average(c => c.Y);
            return new Point2d(x, y);
        }
        public static Point2d GetCenterByAverage(this IEnumerable<Point2f> contours)
        {
            // L110: 转为 List 避免多次枚举 IEnumerable
            var list = contours.ToList();
            if (list.Count == 0)
                throw new ArgumentException("Contours collection is empty.");

            var x = list.Average(c => c.X);
            var y = list.Average(c => c.Y);
            return new Point2d(x, y);
        }
        #endregion
        public static Point2d GetCenterByBoundingRect(this IEnumerable<Point> contours)
        {
            // M332b: 先 ToList 并检查空集合，避免 BoundingRect 对空集合行为异常
            var list = contours.ToList();
            if (list.Count == 0)
                throw new ArgumentException("Contours collection is empty.", nameof(contours));
            Rect rect = Cv2.BoundingRect(list);
            return new Point2d(
                rect.X + rect.Width / 2f,
                rect.Y + rect.Height / 2f
            );
        }
        public static Point2f GetCenterByMiniRect(this IEnumerable<Point> contours)
        {
            // M332b: MinAreaRect 至少需 2 个点，先 ToList 并检查，避免点数不足抛出 OpenCV 异常
            var list = contours.ToList();
            if (list.Count < 2)
                throw new ArgumentException("Contours collection needs at least 2 points.", nameof(contours));
            RotatedRect rotatedRect = Cv2.MinAreaRect(list);
            return rotatedRect.Center;  // 旋转矩形的中心点
        }
        public static Point2d GetCenterByMoments(this IEnumerable<Point> contours)
        {
            // L426b: 已知设计限制 - 当输入轮廓为空、共线或面积为零时（M00 ≈ 0），
            // 静默返回 (0,0) 而不抛异常，调用方无法区分"轮廓中心在原点"与"无效输入"。
            // 改为 nullable 返回值或抛异常可解决，但会破坏所有调用方契约，改动较大暂不实现。
            // 调用方需在使用前自行校验轮廓有效性（如 contours.Any()、面积阈值检查）。
            Moments m = Cv2.Moments(contours);
            if (Math.Abs(m.M00) < MomentsEpsilon)
                return new Point2d(0, 0);
            return new Point2d(
                m.M10 / m.M00,
                m.M01 / m.M00
            );
        }

        #region 轮廓几何特征
        /// <summary>
        /// 计算轮廓面积
        /// </summary>
        public static double Area(this IEnumerable<Point> contours)
        {
            return Cv2.ContourArea(contours);
        }

        /// <summary>
        /// 计算轮廓面积
        /// </summary>
        public static double Area(this IEnumerable<Point2f> contours)
        {
            return Cv2.ContourArea(contours);
        }


        /// <summary>
        /// 计算轮廓周长
        /// </summary>
        /// <param name="closed">是否将轮廓视为闭合</param>
        public static double ArcLength(this IEnumerable<Point> contours, bool closed = true)
        {
            return Cv2.ArcLength(contours, closed);
        }

        /// <summary>
        /// 计算轮廓周长
        /// </summary>
        /// <param name="closed">是否将轮廓视为闭合</param>
        public static double ArcLength(this IEnumerable<Point2f> contours, bool closed = true)
        {
            return Cv2.ArcLength(contours, closed);
        }


        /// <summary>
        /// 获取轮廓的外接矩形
        /// </summary>
        public static Rect BoundingRect(this IEnumerable<Point> contours)
        {
            return Cv2.BoundingRect(contours);
        }

        /// <summary>
        /// 获取轮廓的最小外接旋转矩形
        /// </summary>
        public static RotatedRect MinAreaRect(this IEnumerable<Point> contours)
        {
            return Cv2.MinAreaRect(contours);
        }

        /// <summary>
        /// 获取轮廓的最小外接圆
        /// </summary>
        /// <param name="center">圆心坐标</param>
        /// <param name="radius">半径</param>
        public static void MinEnclosingCircle(this IEnumerable<Point> contours, out Point2f center, out float radius)
        {
            Cv2.MinEnclosingCircle(contours, out center, out radius);
        }

        /// <summary>
        /// 拟合椭圆
        /// </summary>
        public static RotatedRect FitEllipse(this IEnumerable<Point> contours)
        {
            return Cv2.FitEllipse(contours);
        }

        /// <summary>
        /// 计算轮廓的凸包
        /// </summary>
        /// <param name="clockwise">是否顺时针方向输出凸包点</param>
        public static Point[] ConvexHull(this IEnumerable<Point> contours, bool clockwise = true)
        {
            return Cv2.ConvexHull(contours, clockwise);
        }

        /// <summary>
        /// 判断轮廓是否为凸
        /// </summary>
        public static bool IsConvex(this IEnumerable<Point> contours)
        {
            return Cv2.IsContourConvex(contours);
        }

        /// <summary>
        /// 计算轮廓宽高比（基于外接矩形）
        /// </summary>
        public static double AspectRatio(this IEnumerable<Point> contours)
        {
            var rect = contours.BoundingRect();
            // L427b: 零高度时返回 PositiveInfinity 而非 MaxValue，更符合 IEEE 754 除零语义
            if (rect.Height == 0) return double.PositiveInfinity;
            return (double)rect.Width / rect.Height;
        }

        /// <summary>
        /// 计算轮廓面积与外接矩形面积之比
        /// </summary>
        public static double Extent(this IEnumerable<Point> contours)
        {
            // M259: 转为 List 避免多次枚举 IEnumerable
            var list = contours.ToList();
            var area = list.Area();
            var rect = list.BoundingRect();
            var rectArea = rect.Width * rect.Height;
            if (rectArea == 0) return 0;
            return area / rectArea;
        }

        /// <summary>
        /// 计算轮廓面积与凸包面积之比（坚实度）
        /// </summary>
        public static double Solidity(this IEnumerable<Point> contours)
        {
            // M259: 转为 List 避免多次枚举 IEnumerable
            var list = contours.ToList();
            var area = list.Area();
            var hull = list.ConvexHull();
            var hullArea = Cv2.ContourArea(hull);
            if (hullArea == 0) return 0;
            return area / hullArea;
        }
        #endregion

        #region 轮廓变换
        /// <summary>
        /// 缩放轮廓点
        /// </summary>
        /// <param name="scaleX">X轴缩放比例</param>
        /// <param name="scaleY">Y轴缩放比例</param>
        public static IEnumerable<Point> Scale(this IEnumerable<Point> contours, double scaleX, double scaleY)
        {
            foreach (var pt in contours)
            {
                yield return new Point((int)(pt.X * scaleX), (int)(pt.Y * scaleY));
            }
        }

        /// <summary>
        /// 缩放轮廓点
        /// </summary>
        /// <param name="scaleX">X轴缩放比例</param>
        /// <param name="scaleY">Y轴缩放比例</param>
        public static IEnumerable<Point2f> Scale(this IEnumerable<Point2f> contours, double scaleX, double scaleY)
        {
            foreach (var pt in contours)
            {
                yield return new Point2f((float)(pt.X * scaleX), (float)(pt.Y * scaleY));
            }
        }

        /// <summary>
        /// 缩放轮廓点
        /// </summary>
        /// <param name="scaleX">X轴缩放比例</param>
        /// <param name="scaleY">Y轴缩放比例</param>
        public static IEnumerable<Point2d> Scale(this IEnumerable<Point2d> contours, double scaleX, double scaleY)
        {
            foreach (var pt in contours)
            {
                yield return new Point2d(pt.X * scaleX, pt.Y * scaleY);
            }
        }

        /// <summary>
        /// 平移轮廓点
        /// </summary>
        /// <param name="dx">X轴平移量</param>
        /// <param name="dy">Y轴平移量</param>
        public static IEnumerable<Point> Translate(this IEnumerable<Point> contours, double dx, double dy)
        {
            foreach (var pt in contours)
            {
                yield return new Point((int)(pt.X + dx), (int)(pt.Y + dy));
            }
        }

        /// <summary>
        /// 平移轮廓点
        /// </summary>
        /// <param name="dx">X轴平移量</param>
        /// <param name="dy">Y轴平移量</param>
        public static IEnumerable<Point2f> Translate(this IEnumerable<Point2f> contours, double dx, double dy)
        {
            foreach (var pt in contours)
            {
                yield return new Point2f((float)(pt.X + dx), (float)(pt.Y + dy));
            }
        }

        /// <summary>
        /// 平移轮廓点
        /// </summary>
        /// <param name="dx">X轴平移量</param>
        /// <param name="dy">Y轴平移量</param>
        public static IEnumerable<Point2d> Translate(this IEnumerable<Point2d> contours, double dx, double dy)
        {
            foreach (var pt in contours)
            {
                yield return new Point2d(pt.X + dx, pt.Y + dy);
            }
        }

        /// <summary>
        /// 绕指定中心旋转轮廓点
        /// </summary>
        /// <param name="center">旋转中心</param>
        /// <param name="angleDegrees">旋转角度（度）</param>
        public static IEnumerable<Point> Rotate(this IEnumerable<Point> contours, Point2d center, double angleDegrees)
        {
            double angleRad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            foreach (var pt in contours)
            {
                double dx = pt.X - center.X;
                double dy = pt.Y - center.Y;
                double newX = cos * dx - sin * dy + center.X;
                double newY = sin * dx + cos * dy + center.Y;
                yield return new Point((int)Math.Round(newX), (int)Math.Round(newY));
            }
        }

        /// <summary>
        /// 绕指定中心旋转轮廓点
        /// </summary>
        /// <param name="center">旋转中心</param>
        /// <param name="angleDegrees">旋转角度（度）</param>
        public static IEnumerable<Point2f> Rotate(this IEnumerable<Point2f> contours, Point2f center, double angleDegrees)
        {
            double angleRad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            foreach (var pt in contours)
            {
                double dx = pt.X - center.X;
                double dy = pt.Y - center.Y;
                double newX = cos * dx - sin * dy + center.X;
                double newY = sin * dx + cos * dy + center.Y;
                yield return new Point2f((float)newX, (float)newY);
            }
        }

        /// <summary>
        /// 绕指定中心旋转轮廓点
        /// </summary>
        /// <param name="center">旋转中心</param>
        /// <param name="angleDegrees">旋转角度（度）</param>
        public static IEnumerable<Point2d> Rotate(this IEnumerable<Point2d> contours, Point2d center, double angleDegrees)
        {
            double angleRad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            foreach (var pt in contours)
            {
                double dx = pt.X - center.X;
                double dy = pt.Y - center.Y;
                double newX = cos * dx - sin * dy + center.X;
                double newY = sin * dx + cos * dy + center.Y;
                yield return new Point2d(newX, newY);
            }
        }

        /// <summary>
        /// 裁剪轮廓到指定矩形区域内
        /// </summary>
        /// <param name="roi">感兴趣区域</param>
        public static IEnumerable<Point> Clip(this IEnumerable<Point> contours, Rect roi)
        {
            foreach (var pt in contours)
            {
                if (pt.X >= roi.X && pt.X <= roi.X + roi.Width &&
                    pt.Y >= roi.Y && pt.Y <= roi.Y + roi.Height)
                {
                    yield return pt;
                }
            }
        }

        #endregion

        #region 轮廓关系与比较
        /// <summary>
        /// 计算两个轮廓之间的最小距离
        /// </summary>
        /// <remarks>
        /// 复杂度为 O(n·m)，其中 n 与 m 分别为两个轮廓的顶点数。
        /// 对于大轮廓（顶点数上千）可能出现性能问题，如需更高性能可考虑空间索引（如 R树）或降采样。
        /// </remarks>
        public static double DistanceTo(this IEnumerable<Point> contours, IEnumerable<Point> other)
        {
            double minDistance = double.MaxValue;
            bool hasPair = false;
            foreach (var p1 in contours)
            {
                foreach (var p2 in other)
                {
                    double dx = p1.X - p2.X;
                    double dy = p1.Y - p2.Y;
                    double dist = dx * dx + dy * dy;
                    if (dist < minDistance) minDistance = dist;
                    hasPair = true;
                }
            }
            // L255: 任一集合为空时返回 0，避免返回 Sqrt(MaxValue) 的无意义大数
            return hasPair ? Math.Sqrt(minDistance) : 0.0;
        }

        /// <summary>
        /// 计算两个轮廓的外接矩形重叠区域面积（基于边界矩形近似，非多边形相交）
        /// </summary>
        public static double BoundingBoxOverlapArea(this IEnumerable<Point> contours, IEnumerable<Point> other)
        {
            // M346b: 任一集合为空时返回 0，避免 BoundingRect 对空集合行为异常
            // （BoundingRect 内部 ToArray 对空集合可能返回零尺寸矩形导致误判重叠）
            if (contours is ICollection<Point> { Count: 0 } || !contours.Any())
                return 0;
            if (other is ICollection<Point> { Count: 0 } || !other.Any())
                return 0;
            // M155: 移除未使用的 poly1/poly2（原注释称使用多边形相交，实际仅用外接矩形），避免双重枚举
            var rect1 = contours.BoundingRect();
            var rect2 = other.BoundingRect();
            var intersect = rect1.Intersect(rect2);
            return intersect.Area();
        }

        /// <summary>
        /// 判断点是否在轮廓内
        /// </summary>
        /// <param name="point">测试点</param>
        public static bool Contains(this IEnumerable<Point> contours, Point point)
        {
            // M345b: 空集合快速返回 false，避免 PointPolygonTest 对空输入行为异常
            if (contours is ICollection<Point> { Count: 0 } || !contours.Any())
                return false;
            return Cv2.PointPolygonTest(contours, point, false) >= 0;
        }

        /// <summary>
        /// 判断点是否在轮廓内
        /// </summary>
        /// <param name="point">测试点</param>
        public static bool Contains(this IEnumerable<Point2f> contours, Point2f point)
        {
            // M345b: 空集合快速返回 false，避免 PointPolygonTest 对空输入行为异常
            if (contours is ICollection<Point2f> { Count: 0 } || !contours.Any())
                return false;
            return Cv2.PointPolygonTest(contours, point, false) >= 0;
        }

        /// <summary>
        /// 判断当前轮廓是否完全包含另一轮廓
        /// </summary>
        public static bool Contains(this IEnumerable<Point> contours, IEnumerable<Point> other)
        {
            // H79d: 转为 List 避免对 contours 重复枚举 O(n·m)，防止 contours 为延迟查询时每次循环重新执行
            var list = contours.ToList();
            foreach (var pt in other)
            {
                if (Cv2.PointPolygonTest(list, pt, false) < 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 判断两个轮廓是否相交
        /// </summary>
        public static bool Intersects(this IEnumerable<Point> contours, IEnumerable<Point> other)
        {
            // M259: 转为 List 避免多次枚举 IEnumerable
            var list = contours.ToList();
            var otherList = other.ToList();
            // L402b: 空 contours 时 ConvexHull 可能抛异常，提前返回 false
            if (list.Count == 0 || otherList.Count == 0)
                return false;
            // M347b: 边界矩形快速排除 O(1)，仅当边界矩形相交时才做 O(n·m·(n+m)) 精确检测
            var rect1 = Cv2.BoundingRect(list);
            var rect2 = Cv2.BoundingRect(otherList);
            if (rect1.Intersect(rect2).Area() <= 0)
                return false;
            // 检查是否有任意一个顶点在另一轮廓内（精确判定，可处理非凸轮廓）
            foreach (var pt in list)
            {
                if (otherList.Contains(pt)) return true;
            }
            foreach (var pt in otherList)
            {
                if (list.Contains(pt)) return true;
            }
            // H79c: 使用凸包相交检测边相交情况，替代原边界矩形近似
            // 注意：非凸轮廓需先求凸包，凸包比原轮廓大，可能高估相交；
            // 但可覆盖"边相交但无顶点在内部"的情况，比边界矩形更精确
            var hull1 = Cv2.ConvexHull(list);
            var hull2 = Cv2.ConvexHull(otherList);
            return Cv2.IntersectConvexConvex(hull1, hull2, out _, true) > 0;
        }
        #endregion

        #region 轮廓矩与特征
        /// <summary>
        /// 计算轮廓矩
        /// </summary>
        public static Moments Moments(this IEnumerable<Point> contours)
        {
            return Cv2.Moments(contours);
        }

        /// <summary>
        /// 计算轮廓矩
        /// </summary>
        public static Moments Moments(this IEnumerable<Point2f> contours)
        {
            return Cv2.Moments(contours);
        }

        /// <summary>
        /// 计算轮廓主轴方向（弧度）
        /// </summary>
        public static double Orientation(this IEnumerable<Point> contours)
        {
            var m = contours.Moments();
            // M300a: 统一使用 MomentsEpsilon 常量替代 double.Epsilon，与 GetCenterByMoments 保持一致
            if (Math.Abs(m.M00) < MomentsEpsilon) return 0;

            double mu20 = m.M20 / m.M00;
            double mu02 = m.M02 / m.M00;
            double mu11 = m.M11 / m.M00;

            double theta = 0.5 * Math.Atan2(2 * mu11, mu20 - mu02);
            return theta;
        }

        /// <summary>
        /// 计算轮廓偏心率（基于拟合椭圆）
        /// </summary>
        public static double Eccentricity(this IEnumerable<Point> contours)
        {
            try
            {
                var ellipse = contours.FitEllipse();
                double a = Math.Max(ellipse.Size.Width, ellipse.Size.Height) / 2;
                double b = Math.Min(ellipse.Size.Width, ellipse.Size.Height) / 2;
                if (a == 0) return 0;
                return Math.Sqrt(1 - (b * b) / (a * a));
            }
            // L112: 仅捕获 OpenCV 异常（如点数不足），其它异常应向上传播
            catch (OpenCvSharp.OpenCVException)
            {
                return 0;
            }
        }
        #endregion

        #region 轮廓绘制与可视化
        /// <summary>
        /// 在图像上绘制轮廓
        /// </summary>
        /// <param name="image">目标图像</param>
        /// <param name="color">轮廓颜色</param>
        /// <param name="thickness">线条粗细</param>
        /// <param name="lineType">线条类型</param>
        /// <param name="hierarchy">轮廓层次信息（可选）</param>
        /// <param name="maxLevel">最大绘制层级</param>
        public static void Draw(this IEnumerable<Point> contours, Mat image, Scalar color,
            int thickness = 1, LineTypes lineType = LineTypes.Link8,
            IEnumerable<HierarchyIndex>? hierarchy = null, int maxLevel = int.MaxValue)
        {
            // M317c: image 和 contours 参数 null 检查
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(contours);
            if (image.Empty()) return;

            // L416a: 在 contoursArray 构造前检查 contours 是否为空，空则 return，避免空轮廓绘制行为异常
            var pointsArray = contours.ToArray();
            if (pointsArray.Length == 0) return;

            var contoursArray = new Point[][] { pointsArray };
            var hierarchyArray = hierarchy?.ToArray();

            Cv2.DrawContours(image, contoursArray, 0, color, thickness, lineType,
                hierarchyArray, maxLevel);
        }

        /// <summary>
        /// 在图像上填充轮廓
        /// </summary>
        /// <param name="image">目标图像</param>
        /// <param name="color">填充颜色</param>
        /// <param name="lineType">线条类型</param>
        /// <param name="hierarchy">轮廓层次信息（可选）</param>
        /// <param name="maxLevel">最大绘制层级</param>
        public static void DrawFilled(this IEnumerable<Point> contours, Mat image, Scalar color,
            LineTypes lineType = LineTypes.Link8, IEnumerable<HierarchyIndex>? hierarchy = null, int maxLevel = int.MaxValue)
        {
            // M317c: image 和 contours 参数 null 检查
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(contours);
            if (image.Empty()) return;

            contours.Draw(image, color, -1, lineType, hierarchy, maxLevel);
        }

        /// <summary>
        /// 绘制轮廓中心点
        /// </summary>
        /// <param name="image">目标图像</param>
        /// <param name="color">中心点颜色</param>
        /// <param name="radius">中心点半径</param>
        public static void DrawCenter(this IEnumerable<Point> contours, Mat image, Scalar color, int radius = 3)
        {
            // M317c: image 和 contours 参数 null 检查
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(contours);
            if (image.Empty()) return;

            // M318c: 空 contours 时静默绘制（GetCenterByMoments 会返回 (0,0)），开头检查为空则 return
            var list = contours.ToList();
            if (list.Count == 0) return;

            var center = list.GetCenterByMoments();
            Cv2.Circle(image, new Point((int)center.X, (int)center.Y), radius, color, -1);
        }

        /// <summary>
        /// 绘制轮廓外接矩形
        /// </summary>
        /// <param name="image">目标图像</param>
        /// <param name="color">矩形颜色</param>
        /// <param name="thickness">线条粗细</param>
        public static void DrawBoundingRect(this IEnumerable<Point> contours, Mat image, Scalar color, int thickness = 1)
        {
            // M317c: image 和 contours 参数 null 检查
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(contours);
            if (image.Empty()) return;

            var rect = contours.BoundingRect();
            Cv2.Rectangle(image, rect, color, thickness);
        }

        /// <summary>
        /// 绘制轮廓最小外接旋转矩形
        /// </summary>
        /// <param name="image">目标图像</param>
        /// <param name="color">矩形颜色</param>
        /// <param name="thickness">线条粗细</param>
        public static void DrawMinAreaRect(this IEnumerable<Point> contours, Mat image, Scalar color, int thickness = 1)
        {
            // M317c: image 和 contours 参数 null 检查
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(contours);
            if (image.Empty()) return;

            var rotatedRect = contours.MinAreaRect();
            image.DrawRotatedRect(rotatedRect, color, thickness);
        }
        #endregion
    }
}
