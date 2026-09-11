using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Services
{
    internal static class ImageAnalysisService
    {
        internal const double MaxCaliperScore = 255.0;
        private readonly record struct CaliperEdgeSample(Point Point, double Score);

        internal static double NormalizeCaliperScore(double score)
        {
            return Math.Clamp(score / MaxCaliperScore * 100.0, 0, 100);
        }

        public static int[] CreateHistogram(BitmapSource bitmap, int binCount)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binCount);

            bitmap = NormalizeBitmap(bitmap);

            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            int[] histogram = new int[binCount];
            for (int index = 0; index < pixels.Length; index += bytesPerPixel)
            {
                byte intensity = GetPixelIntensity(pixels, index, bytesPerPixel, bitmap.Format);
                int binIndex = intensity * binCount / 256;
                if (binIndex >= binCount)
                {
                    binIndex = binCount - 1;
                }

                histogram[binIndex]++;
            }

            return histogram;
        }

        public static byte[] CreateProfile(BitmapSource bitmap, Point start, Point end)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            bitmap = NormalizeBitmap(bitmap);

            var points = GetLinePoints(start, end);
            if (points.Count == 0)
            {
                return Array.Empty<byte>();
            }

            int minX = (int)points.Min(p => p.X);
            int maxX = (int)points.Max(p => p.X);
            int minY = (int)points.Min(p => p.Y);
            int maxY = (int)points.Max(p => p.Y);

            if (maxX < 0 || maxY < 0 || minX >= bitmap.PixelWidth || minY >= bitmap.PixelHeight)
            {
                return Array.Empty<byte>();
            }

            int roiX = Math.Max(0, minX);
            int roiY = Math.Max(0, minY);
            int roiW = Math.Min(bitmap.PixelWidth, maxX + 1) - roiX;
            int roiH = Math.Min(bitmap.PixelHeight, maxY + 1) - roiY;
            if (roiW <= 0 || roiH <= 0)
            {
                return Array.Empty<byte>();
            }

            int bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
            int stride = roiW * bytesPerPixel;
            byte[] pixels = new byte[roiH * stride];
            bitmap.CopyPixels(new Int32Rect(roiX, roiY, roiW, roiH), pixels, stride, 0);

            byte[] profileData = new byte[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                int pixelX = (int)points[i].X;
                int pixelY = (int)points[i].Y;
                if (pixelX < roiX || pixelX >= roiX + roiW || pixelY < roiY || pixelY >= roiY + roiH)
                {
                    continue;
                }

                int localX = pixelX - roiX;
                int localY = pixelY - roiY;
                int index = localY * stride + localX * bytesPerPixel;
                profileData[i] = GetPixelIntensity(pixels, index, bytesPerPixel, bitmap.Format);
            }

            return profileData;
        }

        public static bool TryDetectLineMeasureEdges(BitmapSource bitmap, CaliperMeasureRoi line, out LineMeasureGradientDetectionResult result)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(line);
            line.EnsureCaliperRegion();
            if (line.CaliperSearchRange <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(line), line.CaliperSearchRange, "CaliperSearchRange must be positive.");
            }

            if (line.CaliperSamplingHalfWidth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(line), line.CaliperSamplingHalfWidth, "CaliperSamplingHalfWidth must be non-negative.");
            }
            result = default;

            Vector measurementDirection = line.GetCaliperMeasurementDirection();
            double estimatedDistance = GeometryUtils.Distance(line.P1, line.P2);

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            Vector caliperDirection = new(-measurementDirection.Y, measurementDirection.X);
            Point measurementCenter = line.CaliperCenter;
            int halfSearchRange = line.CaliperSearchRange;
            double regionHalfLength = line.GetResolvedCaliperRegionLength() / 2;
            int caliperCount = Math.Clamp(line.CaliperCount, 3, 31);
            int minimumValidCalipers = Math.Min(Math.Max(2, line.MinimumValidCalipers), caliperCount);

            List<Point> invalidCaliperCenters = new(caliperCount);
            List<CaliperEdgeSample> edge1Samples = new(caliperCount);
            List<CaliperEdgeSample> edge2Samples = new(caliperCount);

            for (int i = 0; i < caliperCount; i++)
            {
                double lerp = caliperCount == 1 ? 0.5 : (double)i / (caliperCount - 1);
                double tangentOffset = -regionHalfLength + regionHalfLength * 2 * lerp;
                Point caliperCenter = measurementCenter + caliperDirection * tangentOffset;
                if (!TryFindStrongestGradientPair(pixels, bitmap.PixelWidth, bitmap.PixelHeight, stride, bytesPerPixel, bitmap.Format, caliperCenter, measurementDirection, caliperDirection, halfSearchRange, line.CaliperSamplingHalfWidth, line.CaliperMinimumGradient, line.CaliperEdgePolarity, out CaliperEdgeSample edge1Sample, out CaliperEdgeSample edge2Sample))
                {
                    invalidCaliperCenters.Add(caliperCenter);
                    continue;
                }

                edge1Samples.Add(edge1Sample);
                edge2Samples.Add(edge2Sample);
            }

            if (edge1Samples.Count < minimumValidCalipers || edge2Samples.Count < minimumValidCalipers)
            {
                return false;
            }

            List<CaliperEdgeSample> filteredEdge1Samples = FilterInlierSamples(edge1Samples, caliperDirection, regionHalfLength, line.CaliperOutlierThreshold, minimumValidCalipers);
            List<CaliperEdgeSample> filteredEdge2Samples = FilterInlierSamples(edge2Samples, caliperDirection, regionHalfLength, line.CaliperOutlierThreshold, minimumValidCalipers);
            if (filteredEdge1Samples.Count < minimumValidCalipers || filteredEdge2Samples.Count < minimumValidCalipers)
            {
                return false;
            }

            Point[] filteredEdge1Points = [..filteredEdge1Samples.Select(sample => sample.Point)];
            Point[] filteredEdge2Points = [..filteredEdge2Samples.Select(sample => sample.Point)];
            Point[] rejectedEdge1Points = [..edge1Samples.Where(sample => !filteredEdge1Samples.Contains(sample)).Select(sample => sample.Point)];
            Point[] rejectedEdge2Points = [..edge2Samples.Where(sample => !filteredEdge2Samples.Contains(sample)).Select(sample => sample.Point)];
            LineSegmentOverlay fittedEdge1 = FitLine(filteredEdge1Points, caliperDirection, regionHalfLength);
            LineSegmentOverlay fittedEdge2 = FitLine(filteredEdge2Points, caliperDirection, regionHalfLength);
            if (!TryIntersectLines(measurementCenter, measurementDirection, fittedEdge1.Start, fittedEdge1.End - fittedEdge1.Start, out Point detectedP1) ||
                !TryIntersectLines(measurementCenter, measurementDirection, fittedEdge2.Start, fittedEdge2.End - fittedEdge2.Start, out Point detectedP2))
            {
                return false;
            }

            if (GeometryUtils.Distance(detectedP1, detectedP2) <= 0.5)
            {
                return false;
            }

            double edge1AverageScore = filteredEdge1Samples.Average(sample => sample.Score);
            double edge2AverageScore = filteredEdge2Samples.Average(sample => sample.Score);
            (double edge1ResidualRms, double edge1ResidualMax) = ComputeResidualMetrics(filteredEdge1Points, fittedEdge1);
            (double edge2ResidualRms, double edge2ResidualMax) = ComputeResidualMetrics(filteredEdge2Points, fittedEdge2);
            double edge1AngleDegrees = NormalizeLineAngleDegrees(fittedEdge1.End - fittedEdge1.Start);
            double edge2AngleDegrees = NormalizeLineAngleDegrees(fittedEdge2.End - fittedEdge2.Start);
            double parallelismErrorDegrees = Math.Abs(edge1AngleDegrees - edge2AngleDegrees);
            parallelismErrorDegrees = parallelismErrorDegrees > 90 ? 180 - parallelismErrorDegrees : parallelismErrorDegrees;
            double confidence = ComputeConfidence(edge1AverageScore, edge2AverageScore, edge1ResidualRms, edge2ResidualRms, parallelismErrorDegrees, Math.Min(filteredEdge1Points.Length, filteredEdge2Points.Length), caliperCount);

            result = new LineMeasureGradientDetectionResult(
                detectedP1,
                detectedP2,
                [..invalidCaliperCenters],
                [..filteredEdge1Points],
                [..filteredEdge2Points],
                [..rejectedEdge1Points],
                [..rejectedEdge2Points],
                [..filteredEdge1Samples.Select(sample => sample.Score)],
                [..filteredEdge2Samples.Select(sample => sample.Score)],
                new DetectedLineSegment(fittedEdge1.Start, fittedEdge1.End),
                new DetectedLineSegment(fittedEdge2.Start, fittedEdge2.End),
                edge1AverageScore,
                edge2AverageScore,
                edge1ResidualRms,
                edge2ResidualRms,
                edge1ResidualMax,
                edge2ResidualMax,
                Math.Min(filteredEdge1Points.Length, filteredEdge2Points.Length),
                edge1AngleDegrees,
                edge2AngleDegrees,
                parallelismErrorDegrees,
                confidence);

            return true;
        }

        public static bool TryDetectLineCaliperEdges(BitmapSource bitmap, LineCaliperMeasureRoi line, out LineCaliperDetectionResult result)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(line);
            result = default;

            Vector lineDirection = line.P2 - line.P1;
            double lineLength = lineDirection.Length;
            if (lineLength <= 0.5 || line.CaliperSearchRange <= 0)
            {
                return false;
            }

            lineDirection.Normalize();
            Vector measurementDirection = new(-lineDirection.Y, lineDirection.X);

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            int caliperCount = Math.Clamp(line.CaliperCount, 6, 180);
            int minimumValidCalipers = Math.Min(Math.Max(3, line.MinimumValidCalipers), caliperCount);
            List<Point> invalidCaliperCenters = new(caliperCount);
            List<CaliperEdgeSample> edgeSamples = new(caliperCount);

            for (int i = 0; i < caliperCount; i++)
            {
                double lerp = caliperCount == 1 ? 0.5 : (double)i / (caliperCount - 1);
                Point sampleCenter = new(
                    line.P1.X + (line.P2.X - line.P1.X) * lerp,
                    line.P1.Y + (line.P2.Y - line.P1.Y) * lerp);
                if (!TryFindStrongestCircularGradient(
                        pixels,
                        bitmap.PixelWidth,
                        bitmap.PixelHeight,
                        stride,
                        bytesPerPixel,
                        bitmap.Format,
                        sampleCenter,
                        measurementDirection,
                        lineDirection,
                        line.CaliperSearchRange,
                        line.CaliperSamplingHalfWidth,
                        line.CaliperMinimumGradient,
                        line.CaliperEdgePolarity,
                        out CaliperEdgeSample edgeSample))
                {
                    invalidCaliperCenters.Add(sampleCenter);
                    continue;
                }

                edgeSamples.Add(edgeSample);
            }

            if (edgeSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            List<CaliperEdgeSample> filteredSamples = FilterInlierSamples(edgeSamples, lineDirection, lineLength / 2, line.CaliperOutlierThreshold, minimumValidCalipers);
            if (filteredSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            Point[] filteredPoints = [..filteredSamples.Select(sample => sample.Point)];
            Point[] rejectedPoints = [..edgeSamples.Where(sample => !filteredSamples.Contains(sample)).Select(sample => sample.Point)];
            LineSegmentOverlay fittedLine = FitLine(filteredPoints, lineDirection, lineLength / 2);
            Vector fittedDirection = fittedLine.End - fittedLine.Start;
            if (fittedDirection.LengthSquared < 1e-6)
            {
                return false;
            }

            Point detectedP1 = ProjectPointOntoLine(line.P1, fittedLine.Start, fittedDirection);
            Point detectedP2 = ProjectPointOntoLine(line.P2, fittedLine.Start, fittedDirection);
            if (GeometryUtils.Distance(detectedP1, detectedP2) <= 0.5)
            {
                return false;
            }

            double averageScore = filteredSamples.Average(sample => sample.Score);
            (double residualRms, double residualMax) = ComputeResidualMetrics(filteredPoints, fittedLine);
            double angleDegrees = NormalizeLineAngleDegrees(fittedDirection);
            double confidence = ComputeCircularConfidence(averageScore, residualRms, filteredPoints.Length, caliperCount);

            result = new LineCaliperDetectionResult(
                line.P1,
                line.P2,
                detectedP1,
                detectedP2,
                [..invalidCaliperCenters],
                filteredPoints,
                [..rejectedPoints],
                [..filteredSamples.Select(sample => sample.Score)],
                new DetectedLineSegment(fittedLine.Start, fittedLine.End),
                averageScore,
                residualRms,
                residualMax,
                filteredPoints.Length,
                angleDegrees,
                confidence);

            return true;
        }

        public static bool TryDetectCircularCaliperEdges(BitmapSource bitmap, CircularCaliperMeasureRoi caliper, out CircularCaliperDetectionResult result)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(caliper);
            result = default;

            if (caliper is ArcCaliperMeasureRoi arcCaliper)
            {
                return TryDetectArcCaliperEdges(bitmap, arcCaliper, out result);
            }

            if (caliper.Radius <= 0 || caliper.CaliperSearchRange <= 0)
            {
                return false;
            }

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            int caliperCount = Math.Clamp(caliper.CaliperCount, 6, 180);
            int minimumValidCalipers = Math.Min(Math.Max(3, caliper.MinimumValidCalipers), caliperCount);
            List<Point> invalidSamplePoints = new(caliperCount);
            List<CaliperEdgeSample> edgeSamples = new(caliperCount);

            for (int i = 0; i < caliperCount; i++)
            {
                double angleRadians = i * Math.PI * 2 / caliperCount;
                Vector radialDirection = new(Math.Cos(angleRadians), Math.Sin(angleRadians));
                Vector tangentDirection = new(-radialDirection.Y, radialDirection.X);
                Point sampleCenter = caliper.Center + radialDirection * caliper.Radius;
                if (!TryFindStrongestCircularGradient(
                        pixels,
                        bitmap.PixelWidth,
                        bitmap.PixelHeight,
                        stride,
                        bytesPerPixel,
                        bitmap.Format,
                        sampleCenter,
                        radialDirection,
                        tangentDirection,
                        caliper.CaliperSearchRange,
                        caliper.CaliperSamplingHalfWidth,
                        caliper.CaliperMinimumGradient,
                        caliper.CaliperEdgePolarity,
                        out CaliperEdgeSample edgeSample))
                {
                    invalidSamplePoints.Add(sampleCenter);
                    continue;
                }

                edgeSamples.Add(edgeSample);
            }

            if (edgeSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            List<CaliperEdgeSample> filteredSamples = FilterCircularInlierSamples(edgeSamples, caliper.Center, caliper.Radius, caliper.CaliperOutlierThreshold, minimumValidCalipers);
            if (filteredSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            Point[] filteredPoints = [..filteredSamples.Select(sample => sample.Point)];
            Point[] rejectedPoints = [..edgeSamples.Where(sample => !filteredSamples.Contains(sample)).Select(sample => sample.Point)];

            if (!TryFitCircle(filteredPoints, out Point detectedCenter, out double detectedRadius) || detectedRadius <= 0)
            {
                return false;
            }

            (double residualRms, double residualMax) = ComputeCircularResidualMetrics(filteredPoints, detectedCenter, detectedRadius);
            double averageScore = filteredSamples.Average(sample => sample.Score);
            double confidence = ComputeCircularConfidence(averageScore, residualRms, filteredPoints.Length, caliperCount);

            result = new CircularCaliperDetectionResult(
                caliper.Center,
                caliper.Radius,
                detectedCenter,
                detectedRadius,
                [..invalidSamplePoints],
                filteredPoints,
                [..rejectedPoints],
                [..filteredSamples.Select(sample => sample.Score)],
                averageScore,
                residualRms,
                residualMax,
                filteredPoints.Length,
                confidence);

            return true;
        }

        private static bool TryDetectArcCaliperEdges(BitmapSource bitmap, ArcCaliperMeasureRoi caliper, out CircularCaliperDetectionResult result)
        {
            result = default;
            if (caliper.Radius <= 0 || caliper.CaliperSearchRange <= 0 || Math.Abs(caliper.SweepAngle) < 1)
            {
                return false;
            }

            bitmap = NormalizeBitmap(bitmap);
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            int stride = bitmap.PixelWidth * bytesPerPixel;
            byte[] pixels = new byte[bitmap.PixelHeight * stride];
            bitmap.CopyPixels(pixels, stride, 0);

            int caliperCount = Math.Clamp(caliper.CaliperCount, 4, 180);
            int minimumValidCalipers = Math.Min(Math.Max(3, caliper.MinimumValidCalipers), caliperCount);
            List<Point> invalidSamplePoints = new(caliperCount);
            List<CaliperEdgeSample> edgeSamples = new(caliperCount);

            for (int i = 0; i < caliperCount; i++)
            {
                double angleDegrees = caliper.StartAngle + (caliperCount == 1 ? 0 : caliper.SweepAngle * i / (caliperCount - 1));
                double angleRadians = angleDegrees * Math.PI / 180.0;
                Vector radialDirection = new(Math.Cos(angleRadians), Math.Sin(angleRadians));
                Vector tangentDirection = new(-radialDirection.Y, radialDirection.X);
                Point sampleCenter = caliper.Center + radialDirection * caliper.Radius;
                if (!TryFindStrongestCircularGradient(
                        pixels,
                        bitmap.PixelWidth,
                        bitmap.PixelHeight,
                        stride,
                        bytesPerPixel,
                        bitmap.Format,
                        sampleCenter,
                        radialDirection,
                        tangentDirection,
                        caliper.CaliperSearchRange,
                        caliper.CaliperSamplingHalfWidth,
                        caliper.CaliperMinimumGradient,
                        caliper.CaliperEdgePolarity,
                        out CaliperEdgeSample edgeSample))
                {
                    invalidSamplePoints.Add(sampleCenter);
                    continue;
                }

                edgeSamples.Add(edgeSample);
            }

            if (edgeSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            List<CaliperEdgeSample> filteredSamples = FilterCircularInlierSamples(edgeSamples, caliper.Center, caliper.Radius, caliper.CaliperOutlierThreshold, minimumValidCalipers);
            if (filteredSamples.Count < minimumValidCalipers)
            {
                return false;
            }

            Point[] filteredPoints = [..filteredSamples.Select(sample => sample.Point)];
            Point[] rejectedPoints = [..edgeSamples.Where(sample => !filteredSamples.Contains(sample)).Select(sample => sample.Point)];
            if (!TryFitCircle(filteredPoints, out Point detectedCenter, out double detectedRadius) || detectedRadius <= 0)
            {
                return false;
            }

            (double residualRms, double residualMax) = ComputeCircularResidualMetrics(filteredPoints, detectedCenter, detectedRadius);
            double averageScore = filteredSamples.Average(sample => sample.Score);
            double confidence = ComputeCircularConfidence(averageScore, residualRms, filteredPoints.Length, caliperCount);

            result = new CircularCaliperDetectionResult(
                caliper.Center,
                caliper.Radius,
                detectedCenter,
                detectedRadius,
                [..invalidSamplePoints],
                filteredPoints,
                [..rejectedPoints],
                [..filteredSamples.Select(sample => sample.Score)],
                averageScore,
                residualRms,
                residualMax,
                filteredPoints.Length,
                confidence);

            return true;
        }

        public static bool TryCalculateStatistics(BitmapSource bitmap, RoiBase roi, out RoiStatistics statistics)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(roi);

            bitmap = NormalizeBitmap(bitmap);

            statistics = new RoiStatistics();
            Rect bounds = GetRoiBounds(roi);
            if (bounds.IsEmpty)
            {
                return false;
            }

            int minX = Math.Max(0, (int)Math.Floor(bounds.X));
            int minY = Math.Max(0, (int)Math.Floor(bounds.Y));
            int maxX = Math.Min(bitmap.PixelWidth - 1, (int)Math.Ceiling(bounds.Right));
            int maxY = Math.Min(bitmap.PixelHeight - 1, (int)Math.Ceiling(bounds.Bottom));
            if (maxX < minX || maxY < minY)
            {
                return false;
            }

            int roiW = maxX - minX + 1;
            int roiH = maxY - minY + 1;
            int bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
            int stride = roiW * bytesPerPixel;
            byte[] pixels = new byte[roiH * stride];
            bitmap.CopyPixels(new Int32Rect(minX, minY, roiW, roiH), pixels, stride, 0);

            int count = 0;
            long sum = 0;
            double sumSquares = 0;
            byte min = byte.MaxValue;
            byte max = byte.MinValue;

            for (int localY = 0; localY < roiH; localY++)
            {
                for (int localX = 0; localX < roiW; localX++)
                {
                    Point samplePoint = new(minX + localX + 0.5, minY + localY + 0.5);
                    if (!Contains(roi, samplePoint))
                    {
                        continue;
                    }

                    int index = localY * stride + localX * bytesPerPixel;
                    byte value = GetPixelIntensity(pixels, index, bytesPerPixel, bitmap.Format);
                    count++;
                    sum += value;
                    sumSquares += value * value;
                    if (value < min)
                    {
                        min = value;
                    }

                    if (value > max)
                    {
                        max = value;
                    }
                }
            }

            if (count == 0)
            {
                return false;
            }

            double mean = (double)sum / count;
            double variance = Math.Max(0, sumSquares / count - mean * mean);
            statistics = new RoiStatistics
            {
                PixelCount = count,
                Mean = mean,
                Min = min,
                Max = max,
                StandardDeviation = Math.Sqrt(variance)
            };
            return true;
        }

        private static List<Point> GetLinePoints(Point start, Point end)
        {
            int x0 = (int)start.X;
            int y0 = (int)start.Y;
            int x1 = (int)end.X;
            int y1 = (int)end.Y;

            var points = new List<Point>();
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            int cx = x0;
            int cy = y0;
            while (true)
            {
                points.Add(new Point(cx, cy));
                if (cx == x1 && cy == y1)
                {
                    break;
                }

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    cx += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    cy += sy;
                }
            }

            return points;
        }

        private static Rect GetRoiBounds(RoiBase roi)
        {
            return roi switch
            {
                RotatedRect rect => GeometryUtils.GetBoundingBox(new[]
                {
                    GeometryUtils.RotatePoint(new Point(rect.Center.X - rect.Width / 2, rect.Center.Y - rect.Height / 2), rect.Center, rect.Angle),
                    GeometryUtils.RotatePoint(new Point(rect.Center.X + rect.Width / 2, rect.Center.Y - rect.Height / 2), rect.Center, rect.Angle),
                    GeometryUtils.RotatePoint(new Point(rect.Center.X + rect.Width / 2, rect.Center.Y + rect.Height / 2), rect.Center, rect.Angle),
                    GeometryUtils.RotatePoint(new Point(rect.Center.X - rect.Width / 2, rect.Center.Y + rect.Height / 2), rect.Center, rect.Angle)
                }),
                EllipseRoi ellipse => GeometryUtils.GetBoundingBox(new[]
                {
                    GeometryUtils.RotatePoint(new Point(ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y - ellipse.RadiusY), ellipse.Center, ellipse.Angle),
                    GeometryUtils.RotatePoint(new Point(ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y - ellipse.RadiusY), ellipse.Center, ellipse.Angle),
                    GeometryUtils.RotatePoint(new Point(ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y + ellipse.RadiusY), ellipse.Center, ellipse.Angle),
                    GeometryUtils.RotatePoint(new Point(ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y + ellipse.RadiusY), ellipse.Center, ellipse.Angle)
                }),
                CircleRoi circle => new Rect(circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius, circle.Radius * 2, circle.Radius * 2),
                RingRoi ring => new Rect(ring.Center.X - ring.OuterRadius, ring.Center.Y - ring.OuterRadius, ring.OuterRadius * 2, ring.OuterRadius * 2),
                PolygonRoi poly => GeometryUtils.GetBoundingBox(poly.Points),
                _ => Rect.Empty
            };
        }

        private static double GetCircularGradientScore(double gradient, CaliperEdgePolarity polarity)
        {
            return polarity switch
            {
                CaliperEdgePolarity.DarkToLight => Math.Max(gradient, 0),
                CaliperEdgePolarity.LightToDark => Math.Max(-gradient, 0),
                _ => Math.Abs(gradient)
            };
        }

        private static bool TryFindStrongestCircularGradient(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double minimumGradient, CaliperEdgePolarity polarity, out CaliperEdgeSample edgeSample)
        {
            edgeSample = default;
            int sampleCount = searchRange * 2 + 1;
            double[] profile = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int axisOffset = i - searchRange;
                Point sampleCenter = center + measurementDirection * axisOffset;
                profile[i] = SampleAveragedIntensity(pixels, pixelWidth, pixelHeight, stride, bytesPerPixel, format, sampleCenter, averagingDirection, averagingHalfWidth);
            }

            double strongestGradient = 0;
            int bestIndex = -1;
            for (int i = 1; i < sampleCount - 1; i++)
            {
                double gradient = profile[i + 1] - profile[i - 1];
                double score = GetCircularGradientScore(gradient, polarity);
                if (score > strongestGradient)
                {
                    strongestGradient = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0 || strongestGradient < minimumGradient)
            {
                return false;
            }

            edgeSample = new CaliperEdgeSample(center + measurementDirection * (bestIndex - searchRange), strongestGradient);
            return true;
        }

        private static bool Contains(RoiBase roi, Point point)
        {
            switch (roi)
            {
                case RotatedRect rect:
                    var rectMatrix = new Matrix();
                    rectMatrix.RotateAt(-rect.Angle, rect.Center.X, rect.Center.Y);
                    Point rectPoint = rectMatrix.Transform(point);
                    double rectHalfW = rect.Width / 2;
                    double rectHalfH = rect.Height / 2;
                    return rectPoint.X >= rect.Center.X - rectHalfW && rectPoint.X <= rect.Center.X + rectHalfW &&
                           rectPoint.Y >= rect.Center.Y - rectHalfH && rectPoint.Y <= rect.Center.Y + rectHalfH;

                case EllipseRoi ellipse:
                    var ellipseMatrix = new Matrix();
                    ellipseMatrix.RotateAt(-ellipse.Angle, ellipse.Center.X, ellipse.Center.Y);
                    Point ellipsePoint = ellipseMatrix.Transform(point);
                    double dx = ellipsePoint.X - ellipse.Center.X;
                    double dy = ellipsePoint.Y - ellipse.Center.Y;
                    return ellipse.RadiusX > 0 && ellipse.RadiusY > 0 &&
                           (dx * dx) / (ellipse.RadiusX * ellipse.RadiusX) + (dy * dy) / (ellipse.RadiusY * ellipse.RadiusY) <= 1;

                case CircleRoi circle:
                    return GeometryUtils.Distance(circle.Center, point) <= circle.Radius;

                case RingRoi ring:
                    double distance = GeometryUtils.Distance(ring.Center, point);
                    return distance >= ring.InnerRadius && distance <= ring.OuterRadius;

                case PolygonRoi poly:
                    return GeometryUtils.IsPointInPolygon(point, poly.Points);

                default:
                    return false;
            }
        }

        private static byte GetPixelIntensity(byte[] pixels, int index, int bytesPerPixel, PixelFormat format)
        {
            if (format == PixelFormats.Gray8)
            {
                return pixels[index];
            }

            if (bytesPerPixel >= 3)
            {
                byte b = pixels[index];
                byte g = pixels[index + 1];
                byte r = pixels[index + 2];
                return (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            }

            return 0;
        }

        private static bool TryFindStrongestGradientPair(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector measurementDirection, Vector averagingDirection, int searchRange, int averagingHalfWidth, double minimumGradient, CaliperEdgePolarity polarity, out CaliperEdgeSample edge1Sample, out CaliperEdgeSample edge2Sample)
        {
            edge1Sample = default;
            edge2Sample = default;
            int sampleCount = searchRange * 2 + 1;
            double[] profile = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int axisOffset = i - searchRange;
                Point sampleCenter = center + measurementDirection * axisOffset;
                profile[i] = SampleAveragedIntensity(pixels, pixelWidth, pixelHeight, stride, bytesPerPixel, format, sampleCenter, averagingDirection, averagingHalfWidth);
            }

            int middleIndex = searchRange;
            double strongestGradient1 = 0;
            double strongestGradient2 = 0;
            int bestIndex1 = -1;
            int bestIndex2 = -1;
            for (int i = 1; i < sampleCount - 1; i++)
            {
                double gradient = profile[i + 1] - profile[i - 1];
                if (i < middleIndex)
                {
                    double score = GetGradientScore(gradient, polarity, leadingEdge: true);
                    if (score > strongestGradient1)
                    {
                        strongestGradient1 = score;
                        bestIndex1 = i;
                    }
                }
                else if (i > middleIndex)
                {
                    double score = GetGradientScore(gradient, polarity, leadingEdge: false);
                    if (score > strongestGradient2)
                    {
                        strongestGradient2 = score;
                        bestIndex2 = i;
                    }
                }
            }

            if (bestIndex1 < 0 || bestIndex2 < 0 || strongestGradient1 < minimumGradient || strongestGradient2 < minimumGradient)
            {
                return false;
            }

            edge1Sample = new CaliperEdgeSample(center + measurementDirection * (bestIndex1 - searchRange), strongestGradient1);
            edge2Sample = new CaliperEdgeSample(center + measurementDirection * (bestIndex2 - searchRange), strongestGradient2);
            return true;
        }

        private static double GetGradientScore(double gradient, CaliperEdgePolarity polarity, bool leadingEdge)
        {
            return polarity switch
            {
                CaliperEdgePolarity.DarkToLight => leadingEdge ? Math.Max(gradient, 0) : Math.Max(-gradient, 0),
                CaliperEdgePolarity.LightToDark => leadingEdge ? Math.Max(-gradient, 0) : Math.Max(gradient, 0),
                _ => Math.Abs(gradient)
            };
        }

        private static List<CaliperEdgeSample> FilterInlierSamples(List<CaliperEdgeSample> samples, Vector preferredDirection, double fallbackHalfLength, double configuredThreshold, int minimumRequired)
        {
            if (samples.Count <= minimumRequired)
            {
                return samples;
            }

            Point[] points = [..samples.Select(sample => sample.Point)];

            LineSegmentOverlay provisionalFit = FitLine(points, preferredDirection, fallbackHalfLength);
            Vector fitDirection = provisionalFit.End - provisionalFit.Start;
            if (fitDirection.LengthSquared < 1e-6)
            {
                return samples;
            }

            List<(CaliperEdgeSample Sample, double Distance)> distances = new(samples.Count);
            foreach (CaliperEdgeSample sample in samples)
            {
                distances.Add((sample, DistanceToLine(sample.Point, provisionalFit.Start, fitDirection)));
            }

            double threshold = configuredThreshold > 0
                ? configuredThreshold
                : Math.Max(1.0, distances.Select(item => item.Distance).OrderBy(value => value).Skip(distances.Count / 2).FirstOrDefault() * 2.5);

            List<CaliperEdgeSample> filtered = distances
                .Where(item => item.Distance <= threshold)
                .Select(item => item.Sample)
                .ToList();

            return filtered.Count >= minimumRequired ? filtered : samples;
        }

        private static List<CaliperEdgeSample> FilterCircularInlierSamples(List<CaliperEdgeSample> samples, Point fallbackCenter, double fallbackRadius, double configuredThreshold, int minimumRequired)
        {
            if (samples.Count <= minimumRequired)
            {
                return samples;
            }

            Point[] points = [..samples.Select(sample => sample.Point)];
            if (!TryFitCircle(points, out Point center, out double radius) || radius <= 0)
            {
                center = fallbackCenter;
                radius = fallbackRadius;
            }

            List<(CaliperEdgeSample Sample, double Distance)> distances = new(samples.Count);
            foreach (CaliperEdgeSample sample in samples)
            {
                distances.Add((sample, Math.Abs(GeometryUtils.Distance(sample.Point, center) - radius)));
            }

            double threshold = configuredThreshold > 0
                ? configuredThreshold
                : Math.Max(1.0, distances.Select(item => item.Distance).OrderBy(value => value).Skip(distances.Count / 2).FirstOrDefault() * 2.5);

            List<CaliperEdgeSample> filtered = distances
                .Where(item => item.Distance <= threshold)
                .Select(item => item.Sample)
                .ToList();

            return filtered.Count >= minimumRequired ? filtered : samples;
        }

        private static (double Rms, double Max) ComputeResidualMetrics(Point[] points, LineSegmentOverlay fittedLine)
        {
            if (points.Length == 0)
            {
                return (0, 0);
            }

            Vector direction = fittedLine.End - fittedLine.Start;
            double sumSquares = 0;
            double maxResidual = 0;
            foreach (Point point in points)
            {
                double distance = DistanceToLine(point, fittedLine.Start, direction);
                sumSquares += distance * distance;
                maxResidual = Math.Max(maxResidual, distance);
            }

            return (Math.Sqrt(sumSquares / points.Length), maxResidual);
        }

        private static (double Rms, double Max) ComputeCircularResidualMetrics(Point[] points, Point center, double radius)
        {
            if (points.Length == 0)
            {
                return (0, 0);
            }

            double sumSquares = 0;
            double maxResidual = 0;
            foreach (Point point in points)
            {
                double distance = Math.Abs(GeometryUtils.Distance(point, center) - radius);
                sumSquares += distance * distance;
                maxResidual = Math.Max(maxResidual, distance);
            }

            return (Math.Sqrt(sumSquares / points.Length), maxResidual);
        }

        private static double NormalizeLineAngleDegrees(Vector direction)
        {
            if (direction.LengthSquared < 1e-6)
            {
                return 0;
            }

            double angle = Math.Atan2(direction.Y, direction.X) * 180 / Math.PI;
            if (angle < 0)
            {
                angle += 180;
            }

            return angle >= 180 ? angle - 180 : angle;
        }

        private static double ComputeConfidence(double edge1AverageScore, double edge2AverageScore, double edge1ResidualRms, double edge2ResidualRms, double parallelismErrorDegrees, int validCaliperCount, int totalCaliperCount)
        {
            double scoreComponent = Math.Clamp(((edge1AverageScore + edge2AverageScore) / 2) / 64.0, 0, 1);
            double residualComponent = 1.0 / (1.0 + Math.Max(edge1ResidualRms, edge2ResidualRms));
            double parallelComponent = 1.0 / (1.0 + parallelismErrorDegrees / 5.0);
            double validRatio = totalCaliperCount <= 0 ? 0 : (double)validCaliperCount / totalCaliperCount;
            return Math.Clamp(scoreComponent * residualComponent * parallelComponent * validRatio, 0, 1);
        }

        private static double ComputeCircularConfidence(double averageScore, double residualRms, int validCaliperCount, int totalCaliperCount)
        {
            double scoreComponent = Math.Clamp(averageScore / 64.0, 0, 1);
            double residualComponent = 1.0 / (1.0 + residualRms);
            double validRatio = totalCaliperCount <= 0 ? 0 : (double)validCaliperCount / totalCaliperCount;
            return Math.Clamp(scoreComponent * residualComponent * validRatio, 0, 1);
        }

        private static bool TryFitCircle(Point[] points, out Point center, out double radius)
        {
            center = default;
            radius = 0;
            if (points.Length < 3)
            {
                return false;
            }

            double sumX = 0;
            double sumY = 0;
            double sumXX = 0;
            double sumYY = 0;
            double sumXY = 0;
            double sumXr2 = 0;
            double sumYr2 = 0;
            double sumR2 = 0;

            foreach (Point point in points)
            {
                double x = point.X;
                double y = point.Y;
                double r2 = x * x + y * y;
                sumX += x;
                sumY += y;
                sumXX += x * x;
                sumYY += y * y;
                sumXY += x * y;
                sumXr2 += x * r2;
                sumYr2 += y * r2;
                sumR2 += r2;
            }

            double[,] matrix =
            {
                { sumXX, sumXY, sumX },
                { sumXY, sumYY, sumY },
                { sumX, sumY, points.Length }
            };
            double[] rhs =
            {
                -sumXr2,
                -sumYr2,
                -sumR2
            };

            if (!TrySolveLinearSystem3x3(matrix, rhs, out double[] solution))
            {
                return false;
            }

            double d = solution[0];
            double e = solution[1];
            double f = solution[2];
            center = new Point(-d / 2, -e / 2);
            double radiusSquared = center.X * center.X + center.Y * center.Y - f;
            if (radiusSquared <= 0)
            {
                return false;
            }

            radius = Math.Sqrt(radiusSquared);
            return true;
        }

        private static bool TrySolveLinearSystem3x3(double[,] matrix, double[] rhs, out double[] solution)
        {
            solution = new double[3];
            double determinant = Determinant3x3(matrix);
            if (Math.Abs(determinant) < 1e-8)
            {
                return false;
            }

            for (int column = 0; column < 3; column++)
            {
                double[,] working = (double[,])matrix.Clone();
                for (int row = 0; row < 3; row++)
                {
                    working[row, column] = rhs[row];
                }

                solution[column] = Determinant3x3(working) / determinant;
            }

            return true;
        }

        private static double Determinant3x3(double[,] matrix)
        {
            return matrix[0, 0] * (matrix[1, 1] * matrix[2, 2] - matrix[1, 2] * matrix[2, 1])
                 - matrix[0, 1] * (matrix[1, 0] * matrix[2, 2] - matrix[1, 2] * matrix[2, 0])
                 + matrix[0, 2] * (matrix[1, 0] * matrix[2, 1] - matrix[1, 1] * matrix[2, 0]);
        }

        private static double DistanceToLine(Point point, Point linePoint, Vector lineDirection)
        {
            if (lineDirection.LengthSquared < 1e-6)
            {
                return GeometryUtils.Distance(point, linePoint);
            }

            Vector delta = point - linePoint;
            double cross = Math.Abs(delta.X * lineDirection.Y - delta.Y * lineDirection.X);
            return cross / lineDirection.Length;
        }

        private static Point ProjectPointOntoLine(Point point, Point linePoint, Vector lineDirection)
        {
            if (lineDirection.LengthSquared < 1e-6)
            {
                return linePoint;
            }

            double t = ((point.X - linePoint.X) * lineDirection.X + (point.Y - linePoint.Y) * lineDirection.Y) / lineDirection.LengthSquared;
            return linePoint + lineDirection * t;
        }

        private static LineSegmentOverlay FitLine(Point[] points, Vector preferredDirection, double fallbackHalfLength)
        {
            Point centroid = GeometryUtils.GetCentroid(points);
            if (points.Length == 1)
            {
                Vector direction = preferredDirection;
                if (direction.LengthSquared < 1e-6)
                {
                    direction = new Vector(1, 0);
                }

                direction.Normalize();
                return new LineSegmentOverlay(centroid - direction * fallbackHalfLength, centroid + direction * fallbackHalfLength);
            }

            double xx = 0;
            double xy = 0;
            double yy = 0;
            foreach (Point point in points)
            {
                double dx = point.X - centroid.X;
                double dy = point.Y - centroid.Y;
                xx += dx * dx;
                xy += dx * dy;
                yy += dy * dy;
            }

            double angle = 0.5 * Math.Atan2(2 * xy, xx - yy);
            Vector directionVector = new(Math.Cos(angle), Math.Sin(angle));
            if (directionVector.LengthSquared < 1e-6)
            {
                directionVector = preferredDirection;
            }

            if (directionVector.LengthSquared < 1e-6)
            {
                directionVector = new Vector(1, 0);
            }

            directionVector.Normalize();
            double minProjection = double.PositiveInfinity;
            double maxProjection = double.NegativeInfinity;
            foreach (Point point in points)
            {
                double projection = (point.X - centroid.X) * directionVector.X + (point.Y - centroid.Y) * directionVector.Y;
                minProjection = Math.Min(minProjection, projection);
                maxProjection = Math.Max(maxProjection, projection);
            }

            if (maxProjection - minProjection < 1)
            {
                minProjection = -fallbackHalfLength;
                maxProjection = fallbackHalfLength;
            }

            return new LineSegmentOverlay(centroid + directionVector * minProjection, centroid + directionVector * maxProjection);
        }

        private static bool TryIntersectLines(Point p1, Vector d1, Point p2, Vector d2, out Point intersection)
        {
            intersection = default;
            double determinant = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(determinant) < 1e-6)
            {
                return false;
            }

            Vector delta = p2 - p1;
            double t = (delta.X * d2.Y - delta.Y * d2.X) / determinant;
            intersection = p1 + d1 * t;
            return true;
        }

        private static double SampleAveragedIntensity(byte[] pixels, int pixelWidth, int pixelHeight, int stride, int bytesPerPixel, PixelFormat format, Point center, Vector normal, int averagingHalfWidth)
        {
            double sum = 0;
            int count = 0;
            for (int offset = -averagingHalfWidth; offset <= averagingHalfWidth; offset++)
            {
                Point samplePoint = center + normal * offset;
                int x = (int)Math.Round(samplePoint.X);
                int y = (int)Math.Round(samplePoint.Y);
                if (x < 0 || x >= pixelWidth || y < 0 || y >= pixelHeight)
                {
                    continue;
                }

                int index = y * stride + x * bytesPerPixel;
                sum += GetPixelIntensity(pixels, index, bytesPerPixel, format);
                count++;
            }

            return count == 0 ? 0 : sum / count;
        }

        private static BitmapSource NormalizeBitmap(BitmapSource bitmap)
        {
            if (bitmap.Format == PixelFormats.Gray8 ||
                bitmap.Format == PixelFormats.Bgr24 ||
                bitmap.Format == PixelFormats.Bgr32 ||
                bitmap.Format == PixelFormats.Bgra32)
            {
                return bitmap;
            }

            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = bitmap;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            return converted;
        }
    }
}
