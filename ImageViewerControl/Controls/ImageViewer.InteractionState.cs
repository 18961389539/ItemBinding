using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ImageViewer.Models;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private const double PixelGridScaleThreshold = 10;
        private const double HandleSize = 8;
        private const double InfoTextOffset = 20;
        private const double AngleArcRadius = 30;
        private const double HitTestTolerance = 5;
        private const double MinimumDrawableSize = 0.1;
        private const double MinimumRoiDimension = 1;
        private const double PolygonVertexHitPadding = 4;
        private const double PolygonResizeHandlePadding = 2;
        private const double PolygonCloseHighlightPadding = 8;
        private const double HandleHitPadding = 6;
        private const double PointAnnotationSize = 6;
        private const double MinimumLineLength = 1.0;

        private RotatedRect? _currentRect;
        private EllipseRoi? _currentEllipse;
        private CircleRoi? _currentCircle;
        private RingRoi? _currentRing;
        private CircularCaliperMeasureRoi? _currentCircularCaliper;
        private LineCaliperMeasureRoi? _currentLineCaliperMeasure;
        private PolygonRoi? _currentPolygon;
        private PolylineRoi? _currentPolyline;
        private LineMeasureRoi? _currentLineMeasure;
        private AngleMeasureRoi? _currentAngleMeasure;
        private ArcMeasureRoi? _currentArcMeasure;
        private PointToLineDistanceRoi? _currentPointToLineMeasure;
        private PointToCircleDistanceRoi? _currentPointToCircleMeasure;
        private ParallelismMeasureRoi? _currentParallelismMeasure;
        private PerpendicularityMeasureRoi? _currentPerpendicularityMeasure;
        private ConcentricityMeasureRoi? _currentConcentricityMeasure;
        private int _arcMeasureStep;
        private int _pointToLineMeasureStep;
        private int _pointToCircleMeasureStep;
        private int _parallelismMeasureStep;
        private int _perpendicularityMeasureStep;
        private int _concentricityMeasureStep;
        private int _ringDrawStep;
        private int _angleMeasureStep;
        private Func<Point, RoiBase?>? _externalPlacementFactory;
        private bool _isPolygonCloseCandidate;
        private bool _isFreehandPolylineMode;
        private bool _isArcCaliperMode;
        private bool _isArrowAnnotationMode;
        private Point _startPoint;
        private Point? _polygonPreviewPoint;
        private Point? _polylinePreviewPoint;
        private BitmapSource? _cachedInfoBitmap;
        private RoiBase? _cachedInfoRoi;
        private double _cachedInfoPixelSize;
        private string? _cachedInfoUnit;
        private string _cachedInfoText = string.Empty;
        private readonly Dictionary<(string Text, double FontSize, double Padding), Size> _infoTextSizeCache = new();
        private static readonly Brush InfoTextBackgroundBrush = CreateInfoTextBackgroundBrush();
        private Path? _pixelGridPath;
        private ImageSource? _cachedPixelGridImageSource;
        private double _cachedPixelGridWidth;
        private double _cachedPixelGridHeight;
        private Canvas ScreenOverlayCanvas => (Canvas)FindName("screenOverlayCanvas");

        private static SolidColorBrush CreateInfoTextBackgroundBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
            brush.Freeze();
            return brush;
        }

        private static bool TryGetPointSelection(RoiBase? roi, out Point point)
        {
            if (roi is PointAnnotationRoi annotation)
            {
                point = annotation.Position;
                return true;
            }

            point = default;
            return false;
        }

        private static bool TryGetLineSelection(RoiBase? roi, out Point p1, out Point p2)
        {
            switch (roi)
            {
                case LineCaliperMeasureRoi lineCaliper:
                    p1 = lineCaliper.P1;
                    p2 = lineCaliper.P2;
                    return true;
                case CaliperMeasureRoi caliper:
                    p1 = caliper.P1;
                    p2 = caliper.P2;
                    return true;
                case LineMeasureRoi line:
                    p1 = line.P1;
                    p2 = line.P2;
                    return true;
                default:
                    p1 = default;
                    p2 = default;
                    return false;
            }
        }

        private static bool TryGetCircleSelection(RoiBase? roi, out Point center, out double radius)
        {
            switch (roi)
            {
                case CircularCaliperMeasureRoi circularCaliper:
                    center = circularCaliper.Center;
                    radius = circularCaliper.Radius;
                    return true;
                case CircleRoi circle:
                    center = circle.Center;
                    radius = circle.Radius;
                    return true;
                default:
                    center = default;
                    radius = 0;
                    return false;
            }
        }

        private IEnumerable<RoiBase> EnumerateActiveRois()
        {
            if (_currentRect != null)
            {
                yield return _currentRect;
            }

            if (_currentEllipse != null)
            {
                yield return _currentEllipse;
            }

            if (_currentCircle != null)
            {
                yield return _currentCircle;
            }

            if (_currentRing != null)
            {
                yield return _currentRing;
            }

            if (_currentCircularCaliper != null)
            {
                yield return _currentCircularCaliper;
            }

            if (_currentLineCaliperMeasure != null)
            {
                yield return _currentLineCaliperMeasure;
            }

            if (_currentPolygon != null && _currentPolygon.Points.Count > 0)
            {
                yield return _currentPolygon;
            }

            if (_currentLineMeasure != null)
            {
                yield return _currentLineMeasure;
            }

            if (_currentPointToLineMeasure != null)
            {
                yield return _currentPointToLineMeasure;
            }

            if (_currentPointToCircleMeasure != null)
            {
                yield return _currentPointToCircleMeasure;
            }

            if (_currentParallelismMeasure != null)
            {
                yield return _currentParallelismMeasure;
            }

            if (_currentPerpendicularityMeasure != null)
            {
                yield return _currentPerpendicularityMeasure;
            }

            if (_currentConcentricityMeasure != null)
            {
                yield return _currentConcentricityMeasure;
            }

            if (_currentPolyline != null && _currentPolyline.Points.Count > 0)
            {
                yield return _currentPolyline;
            }

            if (_currentAngleMeasure != null)
            {
                yield return _currentAngleMeasure;
            }

            if (_currentArcMeasure != null)
            {
                yield return _currentArcMeasure;
            }
        }

        private bool TryCaptureRootGridMouse()
        {
            return rootGrid.IsMouseCaptured || rootGrid.CaptureMouse();
        }

        private void ReleaseRootGridMouseIfCaptured()
        {
            if (rootGrid.IsMouseCaptured)
            {
                rootGrid.ReleaseMouseCapture();
            }
        }

        private void ResetInteractionStepState()
        {
            _arcMeasureStep = 0;
            _pointToLineMeasureStep = 0;
            _pointToCircleMeasureStep = 0;
            _parallelismMeasureStep = 0;
            _perpendicularityMeasureStep = 0;
            _concentricityMeasureStep = 0;
            _ringDrawStep = 0;
            _angleMeasureStep = 0;
        }

        private void ResetTransientInteractionState()
        {
            _currentRect = null;
            _currentEllipse = null;
            _currentCircle = null;
            _currentRing = null;
            _currentCircularCaliper = null;
            _currentLineCaliperMeasure = null;
            _currentPolygon = null;
            _currentPolyline = null;
            _currentLineMeasure = null;
            _currentAngleMeasure = null;
            _currentArcMeasure = null;
            _currentPointToLineMeasure = null;
            _currentPointToCircleMeasure = null;
            _currentParallelismMeasure = null;
            _currentPerpendicularityMeasure = null;
            _currentConcentricityMeasure = null;
            _externalPlacementFactory = null;
            _isPolygonCloseCandidate = false;
            _isFreehandPolylineMode = false;
            _isArcCaliperMode = false;
            _isArrowAnnotationMode = false;
            _polygonPreviewPoint = null;
            _polylinePreviewPoint = null;
            ResetInteractionStepState();
        }

        private void CompleteSingleRoiInteraction<T>(ref T? currentRoi, Func<T, bool> shouldCommit, Action<T>? beforeCommit = null, Action? afterInteraction = null)
            where T : RoiBase
        {
            var vm = ViewModel;
            ReleaseRootGridMouseIfCaptured();

            if (currentRoi is T roi)
            {
                if (shouldCommit(roi))
                {
                    beforeCommit?.Invoke(roi);
                    vm.UndoRedo.Execute(new AddRoiCommand(roi, vm));
                }

                currentRoi = null;
            }

            afterInteraction?.Invoke();
            LeaveInteractionMode();
            DrawRois();
        }
    }
}
