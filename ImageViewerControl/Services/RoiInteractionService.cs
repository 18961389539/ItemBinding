using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ImageViewer.Abstractions;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Services
{
    public sealed class RoiInteractionService
    {
        private readonly RoiPluginRegistry _pluginRegistry;

        public RoiInteractionService(RoiPluginRegistry? pluginRegistry = null)
        {
            _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        }

        internal static IReadOnlyDictionary<Type, IRoiBehavior> CreateBuiltInBehaviorMap()
        {
            return new Dictionary<Type, IRoiBehavior>
            {
                [typeof(RotatedRect)] = new RotatedRectBehavior(),
                [typeof(BlobAnalysisRoi)] = new RotatedRectBehavior(),
                [typeof(EllipseRoi)] = new EllipseRoiBehavior(),
                [typeof(FittedEllipseRoi)] = new EllipseRoiBehavior(),
                [typeof(CircleRoi)] = new CircleRoiBehavior(),
                [typeof(RingRoi)] = new RingRoiBehavior(),
                [typeof(CircularCaliperMeasureRoi)] = new CircularCaliperMeasureBehavior(),
                [typeof(ArcCaliperMeasureRoi)] = new ArcCaliperMeasureBehavior(),
                [typeof(PolygonRoi)] = new PolygonRoiBehavior(),
                [typeof(PolylineRoi)] = new PolylineRoiBehavior(),
                [typeof(PointAnnotationRoi)] = new PointAnnotationBehavior(),
                [typeof(TextAnnotationRoi)] = new TextAnnotationBehavior(),
                [typeof(ArrowAnnotationRoi)] = new LineMeasureBehavior(),
                [typeof(LineMeasureRoi)] = new LineMeasureBehavior(),
                [typeof(LineCaliperMeasureRoi)] = new LineMeasureBehavior(),
                [typeof(CaliperMeasureRoi)] = new CaliperMeasureBehavior(),
                [typeof(AngleMeasureRoi)] = new AngleMeasureBehavior(),
                [typeof(ArcMeasureRoi)] = new ArcMeasureBehavior(),
                [typeof(PointToLineDistanceRoi)] = new PointToLineDistanceBehavior(),
                [typeof(PointToCircleDistanceRoi)] = new PointToCircleDistanceBehavior(),
                [typeof(ParallelismMeasureRoi)] = new ParallelismMeasureBehavior(),
                [typeof(PerpendicularityMeasureRoi)] = new PerpendicularityMeasureBehavior(),
                [typeof(ConcentricityMeasureRoi)] = new ConcentricityMeasureBehavior(),
                [typeof(BlobAnalysisRoi)] = new BlobAnalysisBehavior()
            };
        }

        public RoiBase? HitTest(ImageViewerViewModel viewModel, Point point, double scale, double hitTestTolerance)
        {
            foreach (var plugin in _pluginRegistry.GetPluginsInHitTestOrder())
            {
                foreach (var roi in plugin.GetRois(viewModel).Reverse())
                {
                    if (!roi.IsVisible)
                    {
                        continue;
                    }

                    if (plugin.Behavior.HitTest(roi, point, scale, hitTestTolerance))
                    {
                        return roi;
                    }
                }

            }

            return null;
        }

        public ResizeHandle GetHandleAt(RoiBase? roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
        {
            if (roi == null || roi.IsLocked)
            {
                return ResizeHandle.None;
            }

            return GetBehavior(roi)?.GetHandleAt(roi, point, scale, handleSize, handleHitPadding, infoTextOffset, polygonVertexHitPadding) ?? ResizeHandle.None;
        }

        public int GetPolygonPointIndexAt(RoiBase? roi, Point point, double scale, double handleSize, double polygonVertexHitPadding)
        {
            if (roi == null || roi.IsLocked)
            {
                return -1;
            }

            return GetBehavior(roi)?.GetVertexIndexAt(roi, point, scale, handleSize, polygonVertexHitPadding) ?? -1;
        }

        public int GetPolygonSegmentAt(RoiBase? roi, Point point, double scale, double hitTestTolerance)
        {
            if (roi == null || roi.IsLocked)
            {
                return -1;
            }

            return GetBehavior(roi)?.GetSegmentIndexAt(roi, point, scale, hitTestTolerance) ?? -1;
        }

        public void MoveRoi(RoiBase roi, double dx, double dy)
        {
            if (roi.IsLocked)
            {
                return;
            }

            GetBehavior(roi)?.Move(roi, dx, dy);
        }

        public void ResizeRoi(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
        {
            if (roi.IsLocked)
            {
                return;
            }

            GetBehavior(roi)?.Resize(roi, handle, dx, dy, currentPos, minimumRoiDimension);
        }

        private IRoiBehavior? GetBehavior(RoiBase roi)
        {
            return _pluginRegistry.FindByRoi(roi)?.Behavior;
        }

        private static Dictionary<ResizeHandle, Point> CreateBoxHandlePositions(Point center, double halfWidth, double halfHeight)
        {
            return new Dictionary<ResizeHandle, Point>
            {
                { ResizeHandle.TopLeft, new Point(center.X - halfWidth, center.Y - halfHeight) },
                { ResizeHandle.TopCenter, new Point(center.X, center.Y - halfHeight) },
                { ResizeHandle.TopRight, new Point(center.X + halfWidth, center.Y - halfHeight) },
                { ResizeHandle.MiddleRight, new Point(center.X + halfWidth, center.Y) },
                { ResizeHandle.BottomRight, new Point(center.X + halfWidth, center.Y + halfHeight) },
                { ResizeHandle.BottomCenter, new Point(center.X, center.Y + halfHeight) },
                { ResizeHandle.BottomLeft, new Point(center.X - halfWidth, center.Y + halfHeight) },
                { ResizeHandle.MiddleLeft, new Point(center.X - halfWidth, center.Y) }
            };
        }

        private static bool IsNear(Point p1, Point p2, double threshold)
        {
            return Math.Abs(p1.X - p2.X) < threshold / 2 && Math.Abs(p1.Y - p2.Y) < threshold / 2;
        }

        private static bool IsWithinHandle(Point point, Point handlePoint, double size)
        {
            return point.X >= handlePoint.X - size / 2 && point.X <= handlePoint.X + size / 2 &&
                   point.Y >= handlePoint.Y - size / 2 && point.Y <= handlePoint.Y + size / 2;
        }

        private static void UpdateBounds(ResizeHandle handle, double dx, double dy, ref double left, ref double top, ref double right, ref double bottom, double minimumRoiDimension)
        {
            switch (handle)
            {
                case ResizeHandle.TopLeft:
                    left += dx;
                    top += dy;
                    break;
                case ResizeHandle.TopCenter:
                    top += dy;
                    break;
                case ResizeHandle.TopRight:
                    right += dx;
                    top += dy;
                    break;
                case ResizeHandle.MiddleRight:
                    right += dx;
                    break;
                case ResizeHandle.BottomRight:
                    right += dx;
                    bottom += dy;
                    break;
                case ResizeHandle.BottomCenter:
                    bottom += dy;
                    break;
                case ResizeHandle.BottomLeft:
                    left += dx;
                    bottom += dy;
                    break;
                case ResizeHandle.MiddleLeft:
                    left += dx;
                    break;
            }

            if (right - left < minimumRoiDimension)
            {
                if (dx > 0) right = left + minimumRoiDimension;
                else left = right - minimumRoiDimension;
            }

            if (bottom - top < minimumRoiDimension)
            {
                if (dy > 0) bottom = top + minimumRoiDimension;
                else top = bottom - minimumRoiDimension;
            }
        }

        private sealed class RotatedRectBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is RotatedRect;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var rect = (RotatedRect)roi;
                var matrix = new Matrix();
                matrix.RotateAt(-rect.Angle, rect.Center.X, rect.Center.Y);
                Point localPoint = matrix.Transform(point);
                double halfW = rect.Width / 2;
                double halfH = rect.Height / 2;
                return localPoint.X >= rect.Center.X - halfW && localPoint.X <= rect.Center.X + halfW &&
                       localPoint.Y >= rect.Center.Y - halfH && localPoint.Y <= rect.Center.Y + halfH;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var rect = (RotatedRect)roi;
                double halfW = rect.Width / 2;
                double halfH = rect.Height / 2;
                double hSize = (handleSize + handleHitPadding) / scale;
                var handlePositions = CreateBoxHandlePositions(rect.Center, halfW, halfH);
                handlePositions[ResizeHandle.Rotation] = new Point(rect.Center.X, rect.Center.Y - halfH - infoTextOffset / scale);
                var rotateTransform = new RotateTransform(rect.Angle, rect.Center.X, rect.Center.Y);

                foreach (var kvp in handlePositions)
                {
                    if (IsWithinHandle(point, rotateTransform.Transform(kvp.Value), hSize))
                    {
                        return kvp.Key;
                    }
                }

                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var rect = (RotatedRect)roi;
                rect.Center = new Point(rect.Center.X + dx, rect.Center.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var rect = (RotatedRect)roi;
                if (handle == ResizeHandle.Rotation)
                {
                    double angle = Math.Atan2(currentPos.Y - rect.Center.Y, currentPos.X - rect.Center.X) * 180 / Math.PI;
                    rect.Angle = angle + 90;
                    return;
                }

                double rad = -rect.Angle * Math.PI / 180.0;
                double dxLocal = dx * Math.Cos(rad) - dy * Math.Sin(rad);
                double dyLocal = dx * Math.Sin(rad) + dy * Math.Cos(rad);
                double halfW = rect.Width / 2;
                double halfH = rect.Height / 2;
                double left = -halfW;
                double top = -halfH;
                double right = halfW;
                double bottom = halfH;
                UpdateBounds(handle, dxLocal, dyLocal, ref left, ref top, ref right, ref bottom, minimumRoiDimension);
                rect.Width = right - left;
                rect.Height = bottom - top;
                double offsetX = (left + right) / 2;
                double offsetY = (top + bottom) / 2;
                double radBack = rect.Angle * Math.PI / 180.0;
                double globalOffsetX = offsetX * Math.Cos(radBack) - offsetY * Math.Sin(radBack);
                double globalOffsetY = offsetX * Math.Sin(radBack) + offsetY * Math.Cos(radBack);
                rect.Center = new Point(rect.Center.X + globalOffsetX, rect.Center.Y + globalOffsetY);
            }
        }

        private sealed class EllipseRoiBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is EllipseRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var ellipse = (EllipseRoi)roi;
                var matrix = new Matrix();
                matrix.RotateAt(-ellipse.Angle, ellipse.Center.X, ellipse.Center.Y);
                Point localPoint = matrix.Transform(point);
                double dx = localPoint.X - ellipse.Center.X;
                double dy = localPoint.Y - ellipse.Center.Y;
                return ellipse.RadiusX > 0 && ellipse.RadiusY > 0 &&
                       (dx * dx) / (ellipse.RadiusX * ellipse.RadiusX) + (dy * dy) / (ellipse.RadiusY * ellipse.RadiusY) <= 1;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var ellipse = (EllipseRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                var handlePositions = CreateBoxHandlePositions(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY);
                handlePositions[ResizeHandle.Rotation] = new Point(ellipse.Center.X, ellipse.Center.Y - ellipse.RadiusY - infoTextOffset / scale);
                var rotateTransform = new RotateTransform(ellipse.Angle, ellipse.Center.X, ellipse.Center.Y);

                foreach (var kvp in handlePositions)
                {
                    if (IsWithinHandle(point, rotateTransform.Transform(kvp.Value), hSize))
                    {
                        return kvp.Key;
                    }
                }

                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var ellipse = (EllipseRoi)roi;
                ellipse.Center = new Point(ellipse.Center.X + dx, ellipse.Center.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var ellipse = (EllipseRoi)roi;
                if (handle == ResizeHandle.Rotation)
                {
                    double angle = Math.Atan2(currentPos.Y - ellipse.Center.Y, currentPos.X - ellipse.Center.X) * 180 / Math.PI;
                    ellipse.Angle = angle + 90;
                    return;
                }

                double rad = -ellipse.Angle * Math.PI / 180.0;
                double dxLocal = dx * Math.Cos(rad) - dy * Math.Sin(rad);
                double dyLocal = dx * Math.Sin(rad) + dy * Math.Cos(rad);
                double left = -ellipse.RadiusX;
                double top = -ellipse.RadiusY;
                double right = ellipse.RadiusX;
                double bottom = ellipse.RadiusY;
                UpdateBounds(handle, dxLocal, dyLocal, ref left, ref top, ref right, ref bottom, minimumRoiDimension);
                ellipse.RadiusX = (right - left) / 2;
                ellipse.RadiusY = (bottom - top) / 2;
                double offsetX = (left + right) / 2;
                double offsetY = (top + bottom) / 2;
                double radBack = ellipse.Angle * Math.PI / 180.0;
                double globalOffsetX = offsetX * Math.Cos(radBack) - offsetY * Math.Sin(radBack);
                double globalOffsetY = offsetX * Math.Sin(radBack) + offsetY * Math.Cos(radBack);
                ellipse.Center = new Point(ellipse.Center.X + globalOffsetX, ellipse.Center.Y + globalOffsetY);
            }
        }

        private sealed class CircleRoiBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is CircleRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var circle = (CircleRoi)roi;
                return GeometryUtils.Distance(circle.Center, point) <= circle.Radius;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var circle = (CircleRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                foreach (var kvp in CreateBoxHandlePositions(circle.Center, circle.Radius, circle.Radius))
                {
                    if (IsWithinHandle(point, kvp.Value, hSize))
                    {
                        return kvp.Key;
                    }
                }

                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var circle = (CircleRoi)roi;
                circle.Center = new Point(circle.Center.X + dx, circle.Center.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var circle = (CircleRoi)roi;
                if (handle != ResizeHandle.None)
                {
                    circle.Radius = Math.Max(minimumRoiDimension, GeometryUtils.Distance(circle.Center, currentPos));
                }
            }
        }

        private sealed class RingRoiBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is RingRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var ring = (RingRoi)roi;
                double distance = GeometryUtils.Distance(ring.Center, point);
                return distance >= ring.InnerRadius - hitTestTolerance / scale && distance <= ring.OuterRadius + hitTestTolerance / scale;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var ring = (RingRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                foreach (var kvp in CreateBoxHandlePositions(ring.Center, ring.OuterRadius, ring.OuterRadius))
                {
                    if (IsWithinHandle(point, kvp.Value, hSize))
                    {
                        return kvp.Key;
                    }
                }

                if (IsNear(point, new Point(ring.Center.X + ring.InnerRadius, ring.Center.Y), hSize))
                {
                    return ResizeHandle.P1;
                }

                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var ring = (RingRoi)roi;
                ring.Center = new Point(ring.Center.X + dx, ring.Center.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var ring = (RingRoi)roi;
                double radius = Math.Max(minimumRoiDimension, GeometryUtils.Distance(ring.Center, currentPos));
                if (handle == ResizeHandle.P1)
                {
                    ring.InnerRadius = Math.Min(radius, Math.Max(minimumRoiDimension, ring.OuterRadius - minimumRoiDimension));
                }
                else if (handle != ResizeHandle.None)
                {
                    ring.OuterRadius = Math.Max(radius, ring.InnerRadius + minimumRoiDimension);
                }
            }
        }

        private sealed class CircularCaliperMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is CircularCaliperMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var caliper = (CircularCaliperMeasureRoi)roi;
                double distance = GeometryUtils.Distance(caliper.Center, point);
                double innerRadius = Math.Max(0, caliper.Radius - caliper.CaliperSearchRange);
                double outerRadius = caliper.Radius + caliper.CaliperSearchRange;
                return distance >= innerRadius && distance <= outerRadius;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var caliper = (CircularCaliperMeasureRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                foreach (var kvp in CreateBoxHandlePositions(caliper.Center, caliper.Radius, caliper.Radius))
                {
                    if (IsWithinHandle(point, kvp.Value, hSize))
                    {
                        return kvp.Key;
                    }
                }

                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var caliper = (CircularCaliperMeasureRoi)roi;
                caliper.Center = new Point(caliper.Center.X + dx, caliper.Center.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var caliper = (CircularCaliperMeasureRoi)roi;
                if (handle != ResizeHandle.None)
                {
                    caliper.Radius = Math.Max(minimumRoiDimension, GeometryUtils.Distance(caliper.Center, currentPos));
                }
            }
        }

        private sealed class ArcCaliperMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is ArcCaliperMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var caliper = (ArcCaliperMeasureRoi)roi;
                double distance = GeometryUtils.Distance(caliper.Center, point);
                double innerRadius = Math.Max(0, caliper.Radius - caliper.CaliperSearchRange);
                double outerRadius = caliper.Radius + caliper.CaliperSearchRange;
                if (distance < innerRadius || distance > outerRadius)
                {
                    return false;
                }

                double angle = Math.Atan2(point.Y - caliper.Center.Y, point.X - caliper.Center.X) * 180 / Math.PI;
                return IsAngleWithinArc(angle, caliper.StartAngle, caliper.SweepAngle);
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var caliper = (ArcCaliperMeasureRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                foreach (var kvp in CreateBoxHandlePositions(caliper.Center, caliper.Radius, caliper.Radius))
                {
                    if (IsWithinHandle(point, kvp.Value, hSize))
                    {
                        return kvp.Key;
                    }
                }

                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var caliper = (ArcCaliperMeasureRoi)roi;
                caliper.Center = new Point(caliper.Center.X + dx, caliper.Center.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var caliper = (ArcCaliperMeasureRoi)roi;
                if (handle != ResizeHandle.None)
                {
                    caliper.Radius = Math.Max(minimumRoiDimension, GeometryUtils.Distance(caliper.Center, currentPos));
                }
            }

            private static bool IsAngleWithinArc(double angleDegrees, double startAngle, double sweepAngle)
            {
                double normalizedAngle = NormalizeAngle(angleDegrees);
                double normalizedStart = NormalizeAngle(startAngle);
                double normalizedEnd = NormalizeAngle(startAngle + sweepAngle);
                if (sweepAngle >= 0)
                {
                    return normalizedStart <= normalizedEnd
                        ? normalizedAngle >= normalizedStart && normalizedAngle <= normalizedEnd
                        : normalizedAngle >= normalizedStart || normalizedAngle <= normalizedEnd;
                }

                return normalizedEnd <= normalizedStart
                    ? normalizedAngle >= normalizedEnd && normalizedAngle <= normalizedStart
                    : normalizedAngle >= normalizedEnd || normalizedAngle <= normalizedStart;
            }

            private static double NormalizeAngle(double angleDegrees)
            {
                double angle = angleDegrees % 360;
                return angle < 0 ? angle + 360 : angle;
            }
        }

        private sealed class PolygonRoiBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is PolygonRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var poly = (PolygonRoi)roi;
                return GeometryUtils.IsPointInPolygon(point, poly.Points);
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                return ResizeHandle.None;
            }

            public int GetVertexIndexAt(RoiBase roi, Point point, double scale, double handleSize, double polygonVertexHitPadding)
            {
                var poly = (PolygonRoi)roi;
                double hSize = (handleSize + polygonVertexHitPadding) / scale;
                for (int i = 0; i < poly.Points.Count; i++)
                {
                    if (IsWithinHandle(point, poly.Points[i], hSize))
                    {
                        return i;
                    }
                }

                return -1;
            }

            public int GetSegmentIndexAt(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var poly = (PolygonRoi)roi;
                double threshold = hitTestTolerance / scale;
                for (int i = 0; i < poly.Points.Count; i++)
                {
                    Point p1 = poly.Points[i];
                    Point p2 = poly.Points[(i + 1) % poly.Points.Count];
                    if (GeometryUtils.IsPointNearSegment(point, p1, p2, threshold))
                    {
                        return i;
                    }
                }

                return -1;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var poly = (PolygonRoi)roi;
                for (int i = 0; i < poly.Points.Count; i++)
                {
                    poly.Points[i] = new Point(poly.Points[i].X + dx, poly.Points[i].Y + dy);
                }
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
            }
        }

        private sealed class PolylineRoiBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is PolylineRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var polyline = (PolylineRoi)roi;
                for (int i = 1; i < polyline.Points.Count; i++)
                {
                    if (GeometryUtils.IsPointNearSegment(point, polyline.Points[i - 1], polyline.Points[i], hitTestTolerance / scale))
                    {
                        return true;
                    }
                }

                return false;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var polyline = (PolylineRoi)roi;
                for (int i = 0; i < polyline.Points.Count; i++)
                {
                    polyline.Points[i] = new Point(polyline.Points[i].X + dx, polyline.Points[i].Y + dy);
                }
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
            }
        }

        private sealed class PointAnnotationBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is PointAnnotationRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var annotation = (PointAnnotationRoi)roi;
                return GeometryUtils.Distance(annotation.Position, point) <= 8 / scale;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var point = (PointAnnotationRoi)roi;
                point.Position = new Point(point.Position.X + dx, point.Position.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
            }
        }

        private sealed class TextAnnotationBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is TextAnnotationRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var text = (TextAnnotationRoi)roi;
                return GeometryUtils.Distance(text.Position, point) <= 14 / scale;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var text = (TextAnnotationRoi)roi;
                text.Position = new Point(text.Position.X + dx, text.Position.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
            }
        }

        private sealed class LineMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is LineMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var line = (LineMeasureRoi)roi;
                return GeometryUtils.IsPointNearSegment(point, line.P1, line.P2, hitTestTolerance / scale);
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var line = (LineMeasureRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                if (IsNear(point, line.P1, hSize)) return ResizeHandle.P1;
                if (IsNear(point, line.P2, hSize)) return ResizeHandle.P2;
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var line = (LineMeasureRoi)roi;
                line.P1 = new Point(line.P1.X + dx, line.P1.Y + dy);
                line.P2 = new Point(line.P2.X + dx, line.P2.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var line = (LineMeasureRoi)roi;
                if (handle == ResizeHandle.P1) line.P1 = currentPos;
                else if (handle == ResizeHandle.P2) line.P2 = currentPos;
            }
        }

        private sealed class CaliperMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is CaliperMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var caliper = (CaliperMeasureRoi)roi;
                caliper.EnsureCaliperRegion();
                var matrix = new Matrix();
                matrix.RotateAt(-(caliper.CaliperAngleDegrees + 90), caliper.CaliperCenter.X, caliper.CaliperCenter.Y);
                Point localPoint = matrix.Transform(point);
                double halfW = caliper.GetResolvedCaliperRegionLength() / 2;
                double halfH = caliper.CaliperSearchRange;
                return localPoint.X >= caliper.CaliperCenter.X - halfW && localPoint.X <= caliper.CaliperCenter.X + halfW &&
                       localPoint.Y >= caliper.CaliperCenter.Y - halfH && localPoint.Y <= caliper.CaliperCenter.Y + halfH;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var caliper = (CaliperMeasureRoi)roi;
                caliper.EnsureCaliperRegion();
                double halfW = caliper.GetResolvedCaliperRegionLength() / 2;
                double halfH = caliper.CaliperSearchRange;
                double hSize = (handleSize + handleHitPadding) / scale;
                var handlePositions = CreateBoxHandlePositions(caliper.CaliperCenter, halfW, halfH);
                handlePositions[ResizeHandle.Rotation] = new Point(caliper.CaliperCenter.X, caliper.CaliperCenter.Y - halfH - infoTextOffset / scale);
                var rotateTransform = new RotateTransform(caliper.CaliperAngleDegrees + 90, caliper.CaliperCenter.X, caliper.CaliperCenter.Y);

                foreach (var kvp in handlePositions)
                {
                    if (IsWithinHandle(point, rotateTransform.Transform(kvp.Value), hSize))
                    {
                        return kvp.Key;
                    }
                }

                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var caliper = (CaliperMeasureRoi)roi;
                caliper.EnsureCaliperRegion();
                caliper.CaliperCenter = new Point(caliper.CaliperCenter.X + dx, caliper.CaliperCenter.Y + dy);
                caliper.P1 = new Point(caliper.P1.X + dx, caliper.P1.Y + dy);
                caliper.P2 = new Point(caliper.P2.X + dx, caliper.P2.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var caliper = (CaliperMeasureRoi)roi;
                caliper.EnsureCaliperRegion();
                if (handle == ResizeHandle.Rotation)
                {
                    double angle = Math.Atan2(currentPos.Y - caliper.CaliperCenter.Y, currentPos.X - caliper.CaliperCenter.X) * 180 / Math.PI;
                    caliper.CaliperAngleDegrees = angle;
                    return;
                }

                double visualAngle = caliper.CaliperAngleDegrees + 90;
                double rad = -visualAngle * Math.PI / 180.0;
                double dxLocal = dx * Math.Cos(rad) - dy * Math.Sin(rad);
                double dyLocal = dx * Math.Sin(rad) + dy * Math.Cos(rad);
                double halfW = caliper.GetResolvedCaliperRegionLength() / 2;
                double halfH = caliper.CaliperSearchRange;
                double left = -halfW;
                double top = -halfH;
                double right = halfW;
                double bottom = halfH;
                UpdateBounds(handle, dxLocal, dyLocal, ref left, ref top, ref right, ref bottom, minimumRoiDimension);
                caliper.CaliperRegionLength = right - left;
                caliper.CaliperSearchRange = Math.Max(1, (int)Math.Round((bottom - top) / 2));
                double offsetX = (left + right) / 2;
                double offsetY = (top + bottom) / 2;
                double radBack = visualAngle * Math.PI / 180.0;
                double globalOffsetX = offsetX * Math.Cos(radBack) - offsetY * Math.Sin(radBack);
                double globalOffsetY = offsetX * Math.Sin(radBack) + offsetY * Math.Cos(radBack);
                caliper.CaliperCenter = new Point(caliper.CaliperCenter.X + globalOffsetX, caliper.CaliperCenter.Y + globalOffsetY);
            }
        }

        private sealed class AngleMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is AngleMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var angle = (AngleMeasureRoi)roi;
                return GeometryUtils.IsPointNearSegment(point, angle.P1, angle.Vertex, hitTestTolerance / scale) ||
                       GeometryUtils.IsPointNearSegment(point, angle.Vertex, angle.P2, hitTestTolerance / scale);
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var angle = (AngleMeasureRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                if (IsNear(point, angle.P1, hSize)) return ResizeHandle.P1;
                if (IsNear(point, angle.Vertex, hSize)) return ResizeHandle.Vertex;
                if (IsNear(point, angle.P2, hSize)) return ResizeHandle.P2;
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var angle = (AngleMeasureRoi)roi;
                angle.P1 = new Point(angle.P1.X + dx, angle.P1.Y + dy);
                angle.Vertex = new Point(angle.Vertex.X + dx, angle.Vertex.Y + dy);
                angle.P2 = new Point(angle.P2.X + dx, angle.P2.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var angle = (AngleMeasureRoi)roi;
                if (handle == ResizeHandle.P1) angle.P1 = currentPos;
                else if (handle == ResizeHandle.Vertex) angle.Vertex = currentPos;
                else if (handle == ResizeHandle.P2) angle.P2 = currentPos;
            }
        }

        private sealed class ArcMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is ArcMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var arc = (ArcMeasureRoi)roi;
                return GeometryUtils.IsPointNearSegment(point, arc.StartPoint, arc.EndPoint, hitTestTolerance / scale) ||
                       GeometryUtils.IsPointNearSegment(point, arc.StartPoint, arc.ArcPoint, hitTestTolerance / scale) ||
                       GeometryUtils.IsPointNearSegment(point, arc.ArcPoint, arc.EndPoint, hitTestTolerance / scale);
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var arc = (ArcMeasureRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                if (IsNear(point, arc.StartPoint, hSize)) return ResizeHandle.P1;
                if (IsNear(point, arc.EndPoint, hSize)) return ResizeHandle.P2;
                if (IsNear(point, arc.ArcPoint, hSize)) return ResizeHandle.Vertex;
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var arc = (ArcMeasureRoi)roi;
                arc.StartPoint = new Point(arc.StartPoint.X + dx, arc.StartPoint.Y + dy);
                arc.EndPoint = new Point(arc.EndPoint.X + dx, arc.EndPoint.Y + dy);
                arc.ArcPoint = new Point(arc.ArcPoint.X + dx, arc.ArcPoint.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var arc = (ArcMeasureRoi)roi;
                if (handle == ResizeHandle.P1) arc.StartPoint = currentPos;
                else if (handle == ResizeHandle.P2) arc.EndPoint = currentPos;
                else if (handle == ResizeHandle.Vertex) arc.ArcPoint = currentPos;
            }
        }

        private sealed class BlobAnalysisBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is BlobAnalysisRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var rect = (BlobAnalysisRoi)roi;
                var matrix = new Matrix();
                matrix.RotateAt(-rect.Angle, rect.Center.X, rect.Center.Y);
                Point localPoint = matrix.Transform(point);
                double halfW = rect.Width / 2;
                double halfH = rect.Height / 2;
                return localPoint.X >= rect.Center.X - halfW && localPoint.X <= rect.Center.X + halfW &&
                       localPoint.Y >= rect.Center.Y - halfH && localPoint.Y <= rect.Center.Y + halfH;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var rect = (BlobAnalysisRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                foreach (var kvp in CreateBoxHandlePositions(rect.Center, rect.Width / 2, rect.Height / 2))
                {
                    var matrix = new Matrix();
                    matrix.RotateAt(rect.Angle, rect.Center.X, rect.Center.Y);
                    if (IsWithinHandle(point, matrix.Transform(kvp.Value), hSize))
                    {
                        return kvp.Key;
                    }
                }

                var rotateMatrix = new Matrix();
                rotateMatrix.RotateAt(rect.Angle, rect.Center.X, rect.Center.Y);
                Point rotationHandlePos = rotateMatrix.Transform(new Point(rect.Center.X, rect.Center.Y - rect.Height / 2 - infoTextOffset / scale));
                if (IsWithinHandle(point, rotationHandlePos, hSize))
                {
                    return ResizeHandle.Rotation;
                }

                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var rect = (BlobAnalysisRoi)roi;
                rect.Center = new Point(rect.Center.X + dx, rect.Center.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var rect = (BlobAnalysisRoi)roi;
                if (handle == ResizeHandle.Rotation)
                {
                    double angle = Math.Atan2(currentPos.Y - rect.Center.Y, currentPos.X - rect.Center.X) * 180 / Math.PI;
                    rect.Angle = angle + 90;
                    return;
                }

                double rad = -rect.Angle * Math.PI / 180.0;
                double dxLocal = dx * Math.Cos(rad) - dy * Math.Sin(rad);
                double dyLocal = dx * Math.Sin(rad) + dy * Math.Cos(rad);
                double halfW = rect.Width / 2;
                double halfH = rect.Height / 2;
                double left = -halfW;
                double top = -halfH;
                double right = halfW;
                double bottom = halfH;
                UpdateBounds(handle, dxLocal, dyLocal, ref left, ref top, ref right, ref bottom, minimumRoiDimension);
                rect.Width = right - left;
                rect.Height = bottom - top;
                double offsetX = (left + right) / 2;
                double offsetY = (top + bottom) / 2;
                double radBack = rect.Angle * Math.PI / 180.0;
                double globalOffsetX = offsetX * Math.Cos(radBack) - offsetY * Math.Sin(radBack);
                double globalOffsetY = offsetX * Math.Sin(radBack) + offsetY * Math.Cos(radBack);
                rect.Center = new Point(rect.Center.X + globalOffsetX, rect.Center.Y + globalOffsetY);
            }
        }

        private sealed class PointToLineDistanceBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is PointToLineDistanceRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var p2l = (PointToLineDistanceRoi)roi;
                return GeometryUtils.Distance(p2l.Point, point) <= hitTestTolerance / scale ||
                       GeometryUtils.IsPointNearSegment(point, p2l.LineP1, p2l.LineP2, hitTestTolerance / scale);
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var p2l = (PointToLineDistanceRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                if (IsNear(point, p2l.Point, hSize)) return ResizeHandle.P1;
                if (IsNear(point, p2l.LineP1, hSize)) return ResizeHandle.P2;
                if (IsNear(point, p2l.LineP2, hSize)) return ResizeHandle.Vertex;
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var p2l = (PointToLineDistanceRoi)roi;
                p2l.Point = new Point(p2l.Point.X + dx, p2l.Point.Y + dy);
                p2l.LineP1 = new Point(p2l.LineP1.X + dx, p2l.LineP1.Y + dy);
                p2l.LineP2 = new Point(p2l.LineP2.X + dx, p2l.LineP2.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var p2l = (PointToLineDistanceRoi)roi;
                if (handle == ResizeHandle.P1) p2l.Point = currentPos;
                else if (handle == ResizeHandle.P2) p2l.LineP1 = currentPos;
                else if (handle == ResizeHandle.Vertex) p2l.LineP2 = currentPos;
            }
        }

        private sealed class PointToCircleDistanceBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is PointToCircleDistanceRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var p2c = (PointToCircleDistanceRoi)roi;
                return GeometryUtils.Distance(p2c.Point, point) <= hitTestTolerance / scale ||
                       GeometryUtils.Distance(p2c.Center, point) <= p2c.Radius + hitTestTolerance / scale;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var p2c = (PointToCircleDistanceRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                if (IsNear(point, p2c.Point, hSize)) return ResizeHandle.P1;
                if (IsNear(point, p2c.Center, hSize)) return ResizeHandle.P2;
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var p2c = (PointToCircleDistanceRoi)roi;
                p2c.Point = new Point(p2c.Point.X + dx, p2c.Point.Y + dy);
                p2c.Center = new Point(p2c.Center.X + dx, p2c.Center.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var p2c = (PointToCircleDistanceRoi)roi;
                if (handle == ResizeHandle.P1) p2c.Point = currentPos;
                else if (handle == ResizeHandle.P2)
                {
                    double dx2 = currentPos.X - p2c.Center.X;
                    double dy2 = currentPos.Y - p2c.Center.Y;
                    p2c.Radius = Math.Max(minimumRoiDimension, Math.Sqrt(dx2 * dx2 + dy2 * dy2));
                }
            }
        }

        private sealed class ParallelismMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is ParallelismMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var para = (ParallelismMeasureRoi)roi;
                return GeometryUtils.IsPointNearSegment(point, para.Line1P1, para.Line1P2, hitTestTolerance / scale) ||
                       GeometryUtils.IsPointNearSegment(point, para.Line2P1, para.Line2P2, hitTestTolerance / scale);
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var para = (ParallelismMeasureRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                if (IsNear(point, para.Line1P1, hSize)) return ResizeHandle.P1;
                if (IsNear(point, para.Line1P2, hSize)) return ResizeHandle.P2;
                if (IsNear(point, para.Line2P1, hSize)) return ResizeHandle.Vertex;
                if (IsNear(point, para.Line2P2, hSize)) return ResizeHandle.P3;
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var para = (ParallelismMeasureRoi)roi;
                para.Line1P1 = new Point(para.Line1P1.X + dx, para.Line1P1.Y + dy);
                para.Line1P2 = new Point(para.Line1P2.X + dx, para.Line1P2.Y + dy);
                para.Line2P1 = new Point(para.Line2P1.X + dx, para.Line2P1.Y + dy);
                para.Line2P2 = new Point(para.Line2P2.X + dx, para.Line2P2.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var para = (ParallelismMeasureRoi)roi;
                if (handle == ResizeHandle.P1) para.Line1P1 = currentPos;
                else if (handle == ResizeHandle.P2) para.Line1P2 = currentPos;
                else if (handle == ResizeHandle.Vertex) para.Line2P1 = currentPos;
                else if (handle == ResizeHandle.P3) para.Line2P2 = currentPos;
            }
        }

        private sealed class PerpendicularityMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is PerpendicularityMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var perp = (PerpendicularityMeasureRoi)roi;
                return GeometryUtils.IsPointNearSegment(point, perp.Line1P1, perp.Line1P2, hitTestTolerance / scale) ||
                       GeometryUtils.IsPointNearSegment(point, perp.Line2P1, perp.Line2P2, hitTestTolerance / scale);
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var perp = (PerpendicularityMeasureRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                if (IsNear(point, perp.Line1P1, hSize)) return ResizeHandle.P1;
                if (IsNear(point, perp.Line1P2, hSize)) return ResizeHandle.P2;
                if (IsNear(point, perp.Line2P1, hSize)) return ResizeHandle.Vertex;
                if (IsNear(point, perp.Line2P2, hSize)) return ResizeHandle.P3;
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var perp = (PerpendicularityMeasureRoi)roi;
                perp.Line1P1 = new Point(perp.Line1P1.X + dx, perp.Line1P1.Y + dy);
                perp.Line1P2 = new Point(perp.Line1P2.X + dx, perp.Line1P2.Y + dy);
                perp.Line2P1 = new Point(perp.Line2P1.X + dx, perp.Line2P1.Y + dy);
                perp.Line2P2 = new Point(perp.Line2P2.X + dx, perp.Line2P2.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var perp = (PerpendicularityMeasureRoi)roi;
                if (handle == ResizeHandle.P1) perp.Line1P1 = currentPos;
                else if (handle == ResizeHandle.P2) perp.Line1P2 = currentPos;
                else if (handle == ResizeHandle.Vertex) perp.Line2P1 = currentPos;
                else if (handle == ResizeHandle.P3) perp.Line2P2 = currentPos;
            }
        }

        private sealed class ConcentricityMeasureBehavior : IRoiBehavior
        {
            public bool CanHandle(RoiBase roi) => roi is ConcentricityMeasureRoi;

            public bool HitTest(RoiBase roi, Point point, double scale, double hitTestTolerance)
            {
                var conc = (ConcentricityMeasureRoi)roi;
                return GeometryUtils.Distance(conc.Center1, point) <= conc.Radius1 + hitTestTolerance / scale ||
                       GeometryUtils.Distance(conc.Center2, point) <= conc.Radius2 + hitTestTolerance / scale;
            }

            public ResizeHandle GetHandleAt(RoiBase roi, Point point, double scale, double handleSize, double handleHitPadding, double infoTextOffset, double polygonVertexHitPadding)
            {
                var conc = (ConcentricityMeasureRoi)roi;
                double hSize = (handleSize + handleHitPadding) / scale;
                if (IsNear(point, conc.Center1, hSize)) return ResizeHandle.P1;
                if (IsNear(point, conc.Center2, hSize)) return ResizeHandle.P2;
                return ResizeHandle.None;
            }

            public void Move(RoiBase roi, double dx, double dy)
            {
                var conc = (ConcentricityMeasureRoi)roi;
                conc.Center1 = new Point(conc.Center1.X + dx, conc.Center1.Y + dy);
                conc.Center2 = new Point(conc.Center2.X + dx, conc.Center2.Y + dy);
            }

            public void Resize(RoiBase roi, ResizeHandle handle, double dx, double dy, Point currentPos, double minimumRoiDimension)
            {
                var conc = (ConcentricityMeasureRoi)roi;
                if (handle == ResizeHandle.P1)
                {
                    double dx2 = currentPos.X - conc.Center1.X;
                    double dy2 = currentPos.Y - conc.Center1.Y;
                    conc.Radius1 = Math.Max(minimumRoiDimension, Math.Sqrt(dx2 * dx2 + dy2 * dy2));
                }
                else if (handle == ResizeHandle.P2)
                {
                    double dx2 = currentPos.X - conc.Center2.X;
                    double dy2 = currentPos.Y - conc.Center2.Y;
                    conc.Radius2 = Math.Max(minimumRoiDimension, Math.Sqrt(dx2 * dx2 + dy2 * dy2));
                }
            }
        }
    }
}
