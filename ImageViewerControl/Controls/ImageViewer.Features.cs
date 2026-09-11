using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.Utils;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private const double ViewportPadding = 16;

        private delegate bool BitmapAnalysisDelegate<TTarget, TResult>(BitmapSource bitmap, TTarget target, out TResult result);

        public void ShowRoiProperties(RoiBase roi) => _dialogWorkflowService.ShowRoiProperties(roi);

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use ShowRoiProperties(RoiBase) instead.", false)]
        public void ShowRoiPropertiesDialog(RoiBase roi) => ShowRoiProperties(roi);

        private TResultRoi? CreateRoiFromSelectionCore<TResultRoi>(Point position, Func<RoiBase, TResultRoi?> createFromSource)
            where TResultRoi : RoiBase
        {
            RoiBase? source = HitTest(position);
            return source == null ? null : createFromSource(source);
        }

        private static FittedEllipseRoi? CreateFittedEllipseFromSource(RoiBase source)
        {
            if (!TryGetEllipseFitSourcePoints(source, out IReadOnlyList<Point>? points) || points == null)
            {
                return null;
            }

            if (!GeometryUtils.TryFitEllipse(points, out Point center, out double radiusX, out double radiusY, out double angleDegrees))
            {
                return null;
            }

            return new FittedEllipseRoi
            {
                Center = center,
                RadiusX = radiusX,
                RadiusY = radiusY,
                Angle = angleDegrees,
                SourcePointCount = points.Count,
                Label = string.IsNullOrWhiteSpace(source.Label) ? "Fit" : $"Fit {source.Label}"
            };
        }

        private static bool TryGetEllipseFitSourcePoints(RoiBase source, out IReadOnlyList<Point>? points)
        {
            points = source switch
            {
                PolygonRoi polygon when polygon.Points.Count >= 3 => polygon.Points,
                PolylineRoi polyline when polyline.Points.Count >= 3 => polyline.Points,
                _ => null
            };

            return points != null;
        }

        private static ArcMeasureRoi CreateArcMeasureFromPoints(Point startPoint, Point endPoint, Point arcPoint)
        {
            return new ArcMeasureRoi
            {
                StartPoint = startPoint,
                EndPoint = endPoint,
                ArcPoint = arcPoint
            };
        }

        private bool TryApplyCaliperDetection(CaliperMeasureRoi line)
        {
            return TryApplyBitmapAnalysisCore<CaliperMeasureRoi, LineMeasureGradientDetectionResult>(
                line,
                static roi => roi.EnsureCaliperRegion(),
                static roi => roi.ClearDetectedEdges(),
                ImageAnalysisService.TryDetectLineMeasureEdges,
                RoiDetectionResultMapper.Apply);
        }

        private bool TryApplyLineCaliperDetection(LineCaliperMeasureRoi line)
        {
            return TryApplyBitmapAnalysisCore<LineCaliperMeasureRoi, LineCaliperDetectionResult>(
                line,
                prepare: null,
                static roi => roi.ClearDetectedLine(),
                ImageAnalysisService.TryDetectLineCaliperEdges,
                RoiDetectionResultMapper.Apply);
        }

        private bool TryApplyCircularCaliperDetection(CircularCaliperMeasureRoi caliper)
        {
            return TryApplyBitmapAnalysisCore<CircularCaliperMeasureRoi, CircularCaliperDetectionResult>(
                caliper,
                prepare: null,
                static roi => roi.ClearDetectedEdges(),
                ImageAnalysisService.TryDetectCircularCaliperEdges,
                RoiDetectionResultMapper.Apply);
        }

        private bool TryApplyArcCaliperDetection(ArcCaliperMeasureRoi caliper)
        {
            return TryApplyCircularCaliperDetection(caliper);
        }

        private bool TryRefreshCaliperDetection(RoiBase? roi)
        {
            return roi switch
            {
                CaliperMeasureRoi line => TryApplyCaliperDetection(line),
                LineCaliperMeasureRoi lineCaliper => TryApplyLineCaliperDetection(lineCaliper),
                CircularCaliperMeasureRoi circular => TryApplyCircularCaliperDetection(circular),
                BlobAnalysisRoi blob => TryApplyBlobAnalysis(blob),
                _ => false
            };
        }

        private bool TryApplyBlobAnalysis(BlobAnalysisRoi blobRoi)
        {
            return TryApplyBitmapAnalysisCore<BlobAnalysisRoi, System.Collections.Generic.List<BlobFeature>>(
                blobRoi,
                prepare: null,
                static roi => roi.DetectedBlobs?.Clear(),
                TryDetectBlobFeatures,
                ApplyBlobAnalysisResult);
        }

        private bool TryApplyBitmapAnalysisCore<TTarget, TResult>(
            TTarget target,
            Action<TTarget>? prepare,
            Action<TTarget> clearResult,
            BitmapAnalysisDelegate<TTarget, TResult> detector,
            Action<TTarget, TResult> applyDetectionResult)
        {
            prepare?.Invoke(target);
            if (GetAnalysisBitmapSource() is not BitmapSource bitmap)
            {
                clearResult(target);
                return false;
            }

            if (!detector(bitmap, target, out TResult detectionResult))
            {
                clearResult(target);
                return false;
            }

            applyDetectionResult(target, detectionResult);
            return true;
        }

        private static bool TryDetectBlobFeatures(BitmapSource bitmap, BlobAnalysisRoi blobRoi, out System.Collections.Generic.List<BlobFeature> blobs)
        {
            blobs = BlobAnalysisService.DetectBlobs(bitmap, GetBlobAnalysisBounds(blobRoi), blobRoi.UseOtsu, blobRoi.ManualThreshold, blobRoi.DetectDark, blobRoi.MinArea);
            return true;
        }

        private static void ApplyBlobAnalysisResult(BlobAnalysisRoi blobRoi, System.Collections.Generic.List<BlobFeature> blobs)
        {
            blobRoi.DetectedBlobs = blobs;
        }

        private static Rect GetBlobAnalysisBounds(BlobAnalysisRoi blobRoi)
        {
            var rect = new Rect(
                blobRoi.Center.X - blobRoi.Width / 2,
                blobRoi.Center.Y - blobRoi.Height / 2,
                blobRoi.Width,
                blobRoi.Height);

            if (blobRoi.Angle == 0)
            {
                return rect;
            }

            return Utils.GeometryUtils.GetBoundingBox(new[]
            {
                Utils.GeometryUtils.RotatePoint(new Point(rect.Left, rect.Top), blobRoi.Center, blobRoi.Angle),
                Utils.GeometryUtils.RotatePoint(new Point(rect.Right, rect.Top), blobRoi.Center, blobRoi.Angle),
                Utils.GeometryUtils.RotatePoint(new Point(rect.Right, rect.Bottom), blobRoi.Center, blobRoi.Angle),
                Utils.GeometryUtils.RotatePoint(new Point(rect.Left, rect.Bottom), blobRoi.Center, blobRoi.Angle)
            });
        }

        private void RefreshAllCaliperDetections()
        {
            foreach (RoiBase roi in ViewModel.AllRois)
            {
                TryRefreshCaliperDetection(roi);
            }
        }

        private async void OnFeatureMenuCommandClick(object sender, RoutedEventArgs e)
        {
            if (TryGetTaggedCommand(sender, out ImageViewerFeatureMenuCommand command))
            {
                await _featureMenuCommandController.ExecuteAsync(command);
            }
        }

    }
}
