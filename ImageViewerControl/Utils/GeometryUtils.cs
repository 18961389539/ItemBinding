using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ImageViewer.Utils
{
    public static class GeometryUtils
    {
        /// <summary>
        /// 几何工具方法集合
        /// Chinese: 提供常用的几何计算辅助方法，例如两点距离、角度计算、点旋转与包围盒计算。
        /// English: Collection of common geometry helper methods such as distance, angle, rotate point and bounding box.
        /// </summary>

        public static double Distance(Point p1, Point p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double DistanceToSegment(Point point, Point segmentStart, Point segmentEnd)
        {
            double l2 = Math.Pow(segmentStart.X - segmentEnd.X, 2) + Math.Pow(segmentStart.Y - segmentEnd.Y, 2);
            if (l2 == 0)
            {
                return Distance(point, segmentStart);
            }

            double t = ((point.X - segmentStart.X) * (segmentEnd.X - segmentStart.X) +
                        (point.Y - segmentStart.Y) * (segmentEnd.Y - segmentStart.Y)) / l2;
            t = Math.Max(0, Math.Min(1, t));

            Point projection = new(
                segmentStart.X + t * (segmentEnd.X - segmentStart.X),
                segmentStart.Y + t * (segmentEnd.Y - segmentStart.Y));

            return Distance(point, projection);
        }

        public static bool IsPointNearSegment(Point point, Point segmentStart, Point segmentEnd, double threshold)
        {
            return DistanceToSegment(point, segmentStart, segmentEnd) < threshold;
        }

        /// <summary>
        /// 计算两点之间的欧几里得距离。
        /// Chinese: 传入两个点，返回它们之间的直线距离。
        /// English: Computes the Euclidean distance between two points.
        /// </summary>
        /// <param name="p1">第一个点 / First point</param>
        /// <param name="p2">第二个点 / Second point</param>
        /// <returns>两点之间的距离（double） / The distance between the two points.</returns>

        public static double Angle(Point p1, Point center, Point p2)
        {
            double angle1 = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
            double angle2 = Math.Atan2(p2.Y - center.Y, p2.X - center.X);
            double result = (angle2 - angle1) * 180 / Math.PI;
            if (result < 0) result += 360;
            return result;
        }

        /// <summary>
        /// 计算以 center 为顶点，从 p1 指向 p2 的角度（度）。
        /// Chinese: 返回以 center 为中心的扇形角度，从向量(center->p1) 到向量(center->p2) 的角度，范围 [0,360)。
        /// English: Calculates the angle (in degrees) from p1 to p2 around the given center point.
        /// </summary>
        /// <param name="p1">起点 / Start point</param>
        /// <param name="center">顶点 / Center vertex</param>
        /// <param name="p2">终点 / End point</param>
        /// <returns>角度（度） / Angle in degrees in range [0,360).</returns>

        public static Point RotatePoint(Point point, Point center, double angleDegrees)
        {
            double angleRadians = angleDegrees * Math.PI / 180;
            double cos = Math.Cos(angleRadians);
            double sin = Math.Sin(angleRadians);

            double dx = point.X - center.X;
            double dy = point.Y - center.Y;

            return new Point(
                center.X + dx * cos - dy * sin,
                center.Y + dx * sin + dy * cos
            );
        }

        /// <summary>
        /// 绕指定中心旋转点。
        /// Chinese: 将给定点绕 center 旋转 angleDegrees（度），并返回旋转后的新坐标。
        /// English: Rotates the point around the specified center by angleDegrees and returns the new point.
        /// </summary>
        /// <param name="point">要旋转的点 / Point to rotate</param>
        /// <param name="center">旋转中心 / Rotation center</param>
        /// <param name="angleDegrees">旋转角度（度） / Rotation angle in degrees</param>
        /// <returns>旋转后的点坐标 / The rotated point.</returns>

        public static Rect GetBoundingBox(IEnumerable<Point> points)
        {
            ArgumentNullException.ThrowIfNull(points);

            using IEnumerator<Point> enumerator = points.GetEnumerator();
            if (!enumerator.MoveNext()) return Rect.Empty;

            Point first = enumerator.Current;
            double minX = first.X;
            double maxX = first.X;
            double minY = first.Y;
            double maxY = first.Y;

            while (enumerator.MoveNext())
            {
                Point point = enumerator.Current;
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        public static Point GetCentroid(IReadOnlyList<Point> points)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count == 0)
            {
                return default;
            }

            double sumX = 0;
            double sumY = 0;
            foreach (Point point in points)
            {
                sumX += point.X;
                sumY += point.Y;
            }

            return new Point(sumX / points.Count, sumY / points.Count);
        }

        public static bool IsPointInPolygon(Point point, IReadOnlyList<Point> polygon)
        {
            if (polygon.Count < 3)
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        public static double PolygonPerimeter(IReadOnlyList<Point> points)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count < 2)
            {
                return 0;
            }

            double perimeter = 0;
            for (int i = 0; i < points.Count; i++)
            {
                perimeter += Distance(points[i], points[(i + 1) % points.Count]);
            }

            return perimeter;
        }

        public static double PolygonArea(IReadOnlyList<Point> points)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count < 3)
            {
                return 0;
            }

            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point p1 = points[i];
                Point p2 = points[(i + 1) % points.Count];
                area += (p1.X * p2.Y) - (p2.X * p1.Y);
            }

            return Math.Abs(area) / 2;
        }

        public static (double Area, double Perimeter, Point Centroid) GetPolygonMetrics(IReadOnlyList<Point> points)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (points.Count == 0)
            {
                return (0, 0, default);
            }

            double areaAccumulator = 0;
            double perimeter = 0;
            double sumX = 0;
            double sumY = 0;

            for (int i = 0; i < points.Count; i++)
            {
                Point current = points[i];
                Point next = points[(i + 1) % points.Count];
                sumX += current.X;
                sumY += current.Y;

                if (points.Count >= 2)
                {
                    perimeter += Distance(current, next);
                }

                if (points.Count >= 3)
                {
                    areaAccumulator += (current.X * next.Y) - (next.X * current.Y);
                }
            }

            return (Math.Abs(areaAccumulator) / 2, perimeter, new Point(sumX / points.Count, sumY / points.Count));
        }

        public static double SmallestAngle(Point p1, Point vertex, Point p2)
        {
            double angle1 = Math.Atan2(p1.Y - vertex.Y, p1.X - vertex.X);
            double angle2 = Math.Atan2(p2.Y - vertex.Y, p2.X - vertex.X);

            double diff = Math.Abs(angle1 - angle2) * 180 / Math.PI;
            return diff > 180 ? 360 - diff : diff;
        }

        public static bool TryFitEllipse(IReadOnlyList<Point> points, out Point center, out double radiusX, out double radiusY, out double angleDegrees)
        {
            center = default;
            radiusX = 0;
            radiusY = 0;
            angleDegrees = 0;

            if (points == null || points.Count < 5)
            {
                return false;
            }

            center = GetCentroid(points);

            double xx = 0;
            double xy = 0;
            double yy = 0;
            foreach (Point point in points)
            {
                double dx = point.X - center.X;
                double dy = point.Y - center.Y;
                xx += dx * dx;
                xy += dx * dy;
                yy += dy * dy;
            }

            double angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double sumLocalX2 = 0;
            double sumLocalY2 = 0;
            foreach (Point point in points)
            {
                double dx = point.X - center.X;
                double dy = point.Y - center.Y;
                double localX = dx * cos + dy * sin;
                double localY = -dx * sin + dy * cos;
                sumLocalX2 += localX * localX;
                sumLocalY2 += localY * localY;
            }

            radiusX = Math.Sqrt(Math.Max(sumLocalX2 * 2 / points.Count, 1e-6));
            radiusY = Math.Sqrt(Math.Max(sumLocalY2 * 2 / points.Count, 1e-6));
            if (!TryNormalizeEllipseParameters(ref center, ref radiusX, ref radiusY, ref angle))
            {
                return false;
            }

            double[] parameters = [center.X, center.Y, Math.Log(radiusX), Math.Log(radiusY), angle];
            double damping = 1e-3;
            double error = ComputeEllipseResidualError(points, parameters);

            for (int iteration = 0; iteration < 24; iteration++)
            {
                if (!TryBuildEllipseNormalEquations(points, parameters, damping, out double[,] normalMatrix, out double[] gradient) ||
                    !TrySolveLinearSystem(normalMatrix, gradient, out double[] delta))
                {
                    break;
                }

                if (delta.All(value => Math.Abs(value) < 1e-6))
                {
                    break;
                }

                double[] candidate = new double[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    candidate[i] = parameters[i] + delta[i];
                }

                double candidateError = ComputeEllipseResidualError(points, candidate);
                if (double.IsFinite(candidateError) && candidateError < error)
                {
                    parameters = candidate;
                    error = candidateError;
                    damping = Math.Max(1e-6, damping * 0.4);
                }
                else
                {
                    damping = Math.Min(1e6, damping * 4);
                }
            }

            center = new Point(parameters[0], parameters[1]);
            radiusX = Math.Exp(parameters[2]);
            radiusY = Math.Exp(parameters[3]);
            angle = parameters[4];

            if (!TryNormalizeEllipseParameters(ref center, ref radiusX, ref radiusY, ref angle))
            {
                return false;
            }

            angleDegrees = NormalizeAngleDegrees(angle * 180 / Math.PI);
            return true;
        }

        private static bool TryBuildEllipseNormalEquations(IReadOnlyList<Point> points, double[] parameters, double damping, out double[,] normalMatrix, out double[] gradient)
        {
            normalMatrix = new double[5, 5];
            gradient = new double[5];

            double centerX = parameters[0];
            double centerY = parameters[1];
            double radiusX = Math.Exp(parameters[2]);
            double radiusY = Math.Exp(parameters[3]);
            double angle = parameters[4];
            if (radiusX <= 0 || radiusY <= 0 || !double.IsFinite(radiusX) || !double.IsFinite(radiusY))
            {
                return false;
            }

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double invRadiusX2 = 1 / (radiusX * radiusX);
            double invRadiusY2 = 1 / (radiusY * radiusY);

            foreach (Point point in points)
            {
                double dx = point.X - centerX;
                double dy = point.Y - centerY;
                double localX = dx * cos + dy * sin;
                double localY = -dx * sin + dy * cos;
                double residual = localX * localX * invRadiusX2 + localY * localY * invRadiusY2 - 1;

                double[] jacobian =
                [
                    (-2 * localX * cos * invRadiusX2) + (2 * localY * sin * invRadiusY2),
                    (-2 * localX * sin * invRadiusX2) - (2 * localY * cos * invRadiusY2),
                    -2 * localX * localX * invRadiusX2,
                    -2 * localY * localY * invRadiusY2,
                    2 * localX * localY * (invRadiusX2 - invRadiusY2)
                ];

                for (int row = 0; row < jacobian.Length; row++)
                {
                    gradient[row] -= jacobian[row] * residual;
                    for (int column = row; column < jacobian.Length; column++)
                    {
                        normalMatrix[row, column] += jacobian[row] * jacobian[column];
                    }
                }
            }

            for (int row = 0; row < 5; row++)
            {
                for (int column = 0; column < row; column++)
                {
                    normalMatrix[row, column] = normalMatrix[column, row];
                }

                normalMatrix[row, row] += damping;
            }

            return true;
        }

        private static double ComputeEllipseResidualError(IReadOnlyList<Point> points, double[] parameters)
        {
            double centerX = parameters[0];
            double centerY = parameters[1];
            double radiusX = Math.Exp(parameters[2]);
            double radiusY = Math.Exp(parameters[3]);
            double angle = parameters[4];
            if (radiusX <= 0 || radiusY <= 0 || !double.IsFinite(radiusX) || !double.IsFinite(radiusY))
            {
                return double.PositiveInfinity;
            }

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double invRadiusX2 = 1 / (radiusX * radiusX);
            double invRadiusY2 = 1 / (radiusY * radiusY);
            double error = 0;
            foreach (Point point in points)
            {
                double dx = point.X - centerX;
                double dy = point.Y - centerY;
                double localX = dx * cos + dy * sin;
                double localY = -dx * sin + dy * cos;
                double residual = localX * localX * invRadiusX2 + localY * localY * invRadiusY2 - 1;
                error += residual * residual;
            }

            return error;
        }

        private static bool TryNormalizeEllipseParameters(ref Point center, ref double radiusX, ref double radiusY, ref double angle)
        {
            if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) ||
                !double.IsFinite(radiusX) || !double.IsFinite(radiusY) ||
                radiusX <= 0 || radiusY <= 0)
            {
                return false;
            }

            if (radiusY > radiusX)
            {
                (radiusX, radiusY) = (radiusY, radiusX);
                angle += Math.PI / 2;
            }

            angle = NormalizeAngleRadians(angle);
            return true;
        }

        private static double NormalizeAngleRadians(double angle)
        {
            while (angle <= -Math.PI / 2)
            {
                angle += Math.PI;
            }

            while (angle > Math.PI / 2)
            {
                angle -= Math.PI;
            }

            return angle;
        }

        private static double NormalizeAngleDegrees(double angle)
        {
            while (angle <= -90)
            {
                angle += 180;
            }

            while (angle > 90)
            {
                angle -= 180;
            }

            return angle;
        }

        private static bool TrySolveLinearSystem(double[,] matrix, double[] rhs, out double[] solution)
        {
            int size = rhs.Length;
            solution = new double[size];
            var augmented = new double[size, size + 1];
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    augmented[row, column] = matrix[row, column];
                }

                augmented[row, size] = rhs[row];
            }

            for (int pivot = 0; pivot < size; pivot++)
            {
                int bestRow = pivot;
                double bestValue = Math.Abs(augmented[pivot, pivot]);
                for (int row = pivot + 1; row < size; row++)
                {
                    double candidate = Math.Abs(augmented[row, pivot]);
                    if (candidate > bestValue)
                    {
                        bestValue = candidate;
                        bestRow = row;
                    }
                }

                if (bestValue < 1e-12)
                {
                    return false;
                }

                if (bestRow != pivot)
                {
                    for (int column = pivot; column <= size; column++)
                    {
                        (augmented[pivot, column], augmented[bestRow, column]) = (augmented[bestRow, column], augmented[pivot, column]);
                    }
                }

                double pivotValue = augmented[pivot, pivot];
                for (int column = pivot; column <= size; column++)
                {
                    augmented[pivot, column] /= pivotValue;
                }

                for (int row = 0; row < size; row++)
                {
                    if (row == pivot)
                    {
                        continue;
                    }

                    double factor = augmented[row, pivot];
                    if (Math.Abs(factor) < 1e-12)
                    {
                        continue;
                    }

                    for (int column = pivot; column <= size; column++)
                    {
                        augmented[row, column] -= factor * augmented[pivot, column];
                    }
                }
            }

            for (int row = 0; row < size; row++)
            {
                solution[row] = augmented[row, size];
            }

            return solution.All(double.IsFinite);
        }

        /// <summary>
        /// 计算给定点集的轴对齐包围盒（bounding box）。
        /// Chinese: 返回包含所有点的最小矩形（axis-aligned bounding box）。如果点集为空则返回 Rect.Empty。
        /// English: Returns the axis-aligned bounding box that contains all points; returns Rect.Empty if sequence is empty.
        /// </summary>
        /// <param name="points">点集合 / Collection of points</param>
        /// <returns>包围盒矩形 / Bounding rectangle that encloses all points, or Rect.Empty for empty input.</returns>
    }
}
