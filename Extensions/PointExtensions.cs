using OpenCvSharp;
using System;

namespace Extensions
{
    public static class PointExtensions
    {
        // L325: 提取魔法数字为常量
        private const double RadiansToDegrees = 180.0 / Math.PI;
        private const double FullCircleDegrees = 360.0;
        // L399b: 使用 1e-10 作为零容差，double.Epsilon 过于严格（约 4.9e-324）几乎不可能相等
        private const double Epsilon = 1e-10;
        public static double LineAngle(this Point2f start, Point2f end, bool normalizeTo360 = true)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double angle = Math.Atan2(dy, dx) * RadiansToDegrees;

            if (normalizeTo360 && angle < 0)
                angle += FullCircleDegrees;

            return angle;
        }

        public static double LineAngle(this Point2d start, Point2d end, bool normalizeTo360 = true)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double angle = Math.Atan2(dy, dx) * RadiansToDegrees;

            if (normalizeTo360 && angle < 0)
                angle += FullCircleDegrees;

            return angle;
        }

        public static double LineAngle(this OpenCvSharp.Point start, OpenCvSharp.Point end, bool normalizeTo360 = true)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double angle = Math.Atan2(dy, dx) * RadiansToDegrees;

            if (normalizeTo360 && angle < 0)
                angle += FullCircleDegrees;

            return angle;
        }

        /// <summary>
        /// 计算两个点之间的欧几里得距离
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>两点之间的距离</returns>
        public static double Distance(this Point2f p1, Point2f p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 计算两个点之间的欧几里得距离
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>两点之间的距离</returns>
        public static double Distance(this Point2d p1, Point2d p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 计算两个点之间的欧几里得距离
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>两点之间的距离</returns>
        public static double Distance(this OpenCvSharp.Point p1, OpenCvSharp.Point p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 计算点到直线的距离（直线由两个点定义）
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="lineStart">直线起点</param>
        /// <param name="lineEnd">直线终点</param>
        /// <returns>点到直线的距离</returns>
        public static double DistanceToLine(this Point2f point, Point2f lineStart, Point2f lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            // 如果直线两点重合，返回点到该点的距离
            if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
                return point.Distance(lineStart);

            double numerator = Math.Abs(dy * point.X - dx * point.Y + lineEnd.X * lineStart.Y - lineEnd.Y * lineStart.X);
            double denominator = Math.Sqrt(dy * dy + dx * dx);

            return numerator / denominator;
        }

        /// <summary>
        /// 计算点到直线的距离（直线由两个点定义）
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="lineStart">直线起点</param>
        /// <param name="lineEnd">直线终点</param>
        /// <returns>点到直线的距离</returns>
        public static double DistanceToLine(this Point2d point, Point2d lineStart, Point2d lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            // 如果直线两点重合，返回点到该点的距离
            if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
                return point.Distance(lineStart);

            double numerator = Math.Abs(dy * point.X - dx * point.Y + lineEnd.X * lineStart.Y - lineEnd.Y * lineStart.X);
            double denominator = Math.Sqrt(dy * dy + dx * dx);

            return numerator / denominator;
        }

        /// <summary>
        /// 计算点到直线的距离（直线由两个点定义）
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="lineStart">直线起点</param>
        /// <param name="lineEnd">直线终点</param>
        /// <returns>点到直线的距离</returns>
        public static double DistanceToLine(this OpenCvSharp.Point point, OpenCvSharp.Point lineStart, OpenCvSharp.Point lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            // 如果直线两点重合，返回点到该点的距离
            if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
                return point.Distance(lineStart);

            double numerator = Math.Abs(dy * point.X - dx * point.Y + lineEnd.X * lineStart.Y - lineEnd.Y * lineStart.X);
            double denominator = Math.Sqrt(dy * dy + dx * dx);

            return numerator / denominator;
        }

        /// <summary>
        /// 计算两个点的中点
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>中点坐标</returns>
        public static Point2f Midpoint(this Point2f p1, Point2f p2)
        {
            return new Point2f((p1.X + p2.X) * 0.5f, (p1.Y + p2.Y) * 0.5f);
        }

        /// <summary>
        /// 计算两个点的中点
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>中点坐标</returns>
        public static Point2d Midpoint(this Point2d p1, Point2d p2)
        {
            return new Point2d((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5);
        }

        /// <summary>
        /// 计算两个点的中点
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>中点坐标</returns>
        public static OpenCvSharp.Point Midpoint(this OpenCvSharp.Point p1, OpenCvSharp.Point p2)
        {
            return new OpenCvSharp.Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
        }

        /// <summary>
        /// 将点绕指定中心旋转指定角度
        /// </summary>
        /// <param name="point">要旋转的点</param>
        /// <param name="center">旋转中心</param>
        /// <param name="angleDegrees">旋转角度（度）</param>
        /// <returns>旋转后的点</returns>
        public static Point2f Rotate(this Point2f point, Point2f center, double angleDegrees)
        {
            double angleRad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            double dx = point.X - center.X;
            double dy = point.Y - center.Y;

            double newX = cos * dx - sin * dy + center.X;
            double newY = sin * dx + cos * dy + center.Y;

            return new Point2f((float)newX, (float)newY);
        }

        /// <summary>
        /// 将点绕指定中心旋转指定角度
        /// </summary>
        /// <param name="point">要旋转的点</param>
        /// <param name="center">旋转中心</param>
        /// <param name="angleDegrees">旋转角度（度）</param>
        /// <returns>旋转后的点</returns>
        public static Point2d Rotate(this Point2d point, Point2d center, double angleDegrees)
        {
            double angleRad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            double dx = point.X - center.X;
            double dy = point.Y - center.Y;

            double newX = cos * dx - sin * dy + center.X;
            double newY = sin * dx + cos * dy + center.Y;

            return new Point2d(newX, newY);
        }

        /// <summary>
        /// 将点绕指定中心旋转指定角度
        /// </summary>
        /// <param name="point">要旋转的点</param>
        /// <param name="center">旋转中心</param>
        /// <param name="angleDegrees">旋转角度（度）</param>
        /// <returns>旋转后的点</returns>
        public static OpenCvSharp.Point Rotate(this OpenCvSharp.Point point, OpenCvSharp.Point center, double angleDegrees)
        {
            double angleRad = angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            double dx = point.X - center.X;
            double dy = point.Y - center.Y;

            double newX = cos * dx - sin * dy + center.X;
            double newY = sin * dx + cos * dy + center.Y;

            return new OpenCvSharp.Point((int)Math.Round(newX), (int)Math.Round(newY));
        }

        /// <summary>
        /// 计算点到直线上的垂直投影点
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="lineStart">直线起点</param>
        /// <param name="lineEnd">直线终点</param>
        /// <returns>投影点坐标</returns>
        public static Point2f ProjectToLine(this Point2f point, Point2f lineStart, Point2f lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            // 如果直线两点重合，返回起点
            if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
                return lineStart;

            double u = ((point.X - lineStart.X) * dx + (point.Y - lineStart.Y) * dy) / (dx * dx + dy * dy);
            return new Point2f((float)(lineStart.X + u * dx), (float)(lineStart.Y + u * dy));
        }

        /// <summary>
        /// 计算点到直线上的垂直投影点
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="lineStart">直线起点</param>
        /// <param name="lineEnd">直线终点</param>
        /// <returns>投影点坐标</returns>
        public static Point2d ProjectToLine(this Point2d point, Point2d lineStart, Point2d lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            // 如果直线两点重合，返回起点
            if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
                return lineStart;

            double u = ((point.X - lineStart.X) * dx + (point.Y - lineStart.Y) * dy) / (dx * dx + dy * dy);
            return new Point2d(lineStart.X + u * dx, lineStart.Y + u * dy);
        }

        /// <summary>
        /// 计算点到直线上的垂直投影点
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="lineStart">直线起点</param>
        /// <param name="lineEnd">直线终点</param>
        /// <returns>投影点坐标</returns>
        public static OpenCvSharp.Point ProjectToLine(this OpenCvSharp.Point point, OpenCvSharp.Point lineStart, OpenCvSharp.Point lineEnd)
        {
            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;

            // 如果直线两点重合，返回起点
            if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon)
                return lineStart;

            double u = ((point.X - lineStart.X) * dx + (point.Y - lineStart.Y) * dy) / (dx * dx + dy * dy);
            return new OpenCvSharp.Point((int)Math.Round(lineStart.X + u * dx), (int)Math.Round(lineStart.Y + u * dy));
        }

        /// <summary>
        /// 判断点是否在线段上（带容差）
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="segmentStart">线段起点</param>
        /// <param name="segmentEnd">线段终点</param>
        /// <param name="tolerance">容差（默认1e-6）</param>
        /// <returns>如果点在线段上返回true，否则返回false</returns>
        public static bool IsOnSegment(this Point2f point, Point2f segmentStart, Point2f segmentEnd, double tolerance = 1e-6)
        {
            // 使用向量叉积和点积判断
            double cross = (point.X - segmentStart.X) * (segmentEnd.Y - segmentStart.Y) -
                          (point.Y - segmentStart.Y) * (segmentEnd.X - segmentStart.X);

            if (Math.Abs(cross) > tolerance)
                return false;

            double dot = (point.X - segmentStart.X) * (segmentEnd.X - segmentStart.X) +
                        (point.Y - segmentStart.Y) * (segmentEnd.Y - segmentStart.Y);

            if (dot < 0 || dot > segmentStart.DistanceSquared(segmentEnd))
                return false;

            return true;
        }

        /// <summary>
        /// 判断点是否在线段上（带容差）
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="segmentStart">线段起点</param>
        /// <param name="segmentEnd">线段终点</param>
        /// <param name="tolerance">容差（默认1e-6）</param>
        /// <returns>如果点在线段上返回true，否则返回false</returns>
        public static bool IsOnSegment(this Point2d point, Point2d segmentStart, Point2d segmentEnd, double tolerance = 1e-6)
        {
            // 使用向量叉积和点积判断
            double cross = (point.X - segmentStart.X) * (segmentEnd.Y - segmentStart.Y) -
                          (point.Y - segmentStart.Y) * (segmentEnd.X - segmentStart.X);

            if (Math.Abs(cross) > tolerance)
                return false;

            double dot = (point.X - segmentStart.X) * (segmentEnd.X - segmentStart.X) +
                        (point.Y - segmentStart.Y) * (segmentEnd.Y - segmentStart.Y);

            if (dot < 0 || dot > segmentStart.DistanceSquared(segmentEnd))
                return false;

            return true;
        }

        /// <summary>
        /// 判断点是否在线段上（带容差）
        /// </summary>
        /// <param name="point">目标点</param>
        /// <param name="segmentStart">线段起点</param>
        /// <param name="segmentEnd">线段终点</param>
        /// <param name="tolerance">容差（默认1e-6）</param>
        /// <returns>如果点在线段上返回true，否则返回false</returns>
        public static bool IsOnSegment(this OpenCvSharp.Point point, OpenCvSharp.Point segmentStart, OpenCvSharp.Point segmentEnd, double tolerance = 1e-6)
        {
            // 使用向量叉积和点积判断
            double cross = (point.X - segmentStart.X) * (segmentEnd.Y - segmentStart.Y) -
                          (point.Y - segmentStart.Y) * (segmentEnd.X - segmentStart.X);

            if (Math.Abs(cross) > tolerance)
                return false;

            double dot = (point.X - segmentStart.X) * (segmentEnd.X - segmentStart.X) +
                        (point.Y - segmentStart.Y) * (segmentEnd.Y - segmentStart.Y);

            double segmentLengthSquared = segmentStart.DistanceSquared(segmentEnd);
            if (dot < 0 || dot > segmentLengthSquared)
                return false;

            return true;
        }

        /// <summary>
        /// 计算两点距离的平方（避免开方运算，用于比较）
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>距离的平方</returns>
        private static double DistanceSquared(this Point2f p1, Point2f p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// 计算两点距离的平方（避免开方运算，用于比较）
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>距离的平方</returns>
        private static double DistanceSquared(this Point2d p1, Point2d p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// 计算两点距离的平方（避免开方运算，用于比较）
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <returns>距离的平方</returns>
        private static double DistanceSquared(this OpenCvSharp.Point p1, OpenCvSharp.Point p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// 判断两个点是否近似相等（带容差）
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <param name="tolerance">容差（默认1e-6）</param>
        /// <returns>如果点近似相等返回true，否则返回false</returns>
        public static bool ApproximatelyEqual(this Point2f p1, Point2f p2, double tolerance = 1e-6)
        {
            return Math.Abs(p1.X - p2.X) <= tolerance && Math.Abs(p1.Y - p2.Y) <= tolerance;
        }

        /// <summary>
        /// 判断两个点是否近似相等（带容差）
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <param name="tolerance">容差（默认1e-6）</param>
        /// <returns>如果点近似相等返回true，否则返回false</returns>
        public static bool ApproximatelyEqual(this Point2d p1, Point2d p2, double tolerance = 1e-6)
        {
            return Math.Abs(p1.X - p2.X) <= tolerance && Math.Abs(p1.Y - p2.Y) <= tolerance;
        }

        /// <summary>
        /// 判断两个点是否近似相等（带容差）
        /// </summary>
        /// <param name="p1">第一个点</param>
        /// <param name="p2">第二个点</param>
        /// <param name="tolerance">容差（默认1e-6）</param>
        /// <returns>如果点近似相等返回true，否则返回false</returns>
        public static bool ApproximatelyEqual(this OpenCvSharp.Point p1, OpenCvSharp.Point p2, double tolerance = 1e-6)
        {
            return Math.Abs(p1.X - p2.X) <= tolerance && Math.Abs(p1.Y - p2.Y) <= tolerance;
        }
    }
}
