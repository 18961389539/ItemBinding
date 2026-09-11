using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ImageViewer.Models;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void StartInteractionMode(InteractionMode mode, Cursor cursor, Action? resetState = null)
        {
            ExitCurrentMode();
            resetState?.Invoke();
            EnterInteractionMode(mode, cursor);
        }

        public void ExitCurrentMode()
        {
            ReleaseRootGridMouseIfCaptured();

            if (_currentPolygon != null)
            {
                EndPolygonDrawing();
            }

            ResetTransientInteractionState();

            LeaveInteractionMode();
            DrawRois();
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use StartLineMeasureMode() or StartCaliperMeasureMode() for explicit measure-mode entry.", false)]
        public void StartMeasureMode()
        {
            StartCaliperMeasureMode();
        }

        public void StartRoiMode()
        {
            StartInteractionMode(InteractionMode.DrawRectangle, Cursors.Cross);
        }

        public void StartBlobAnalysisMode()
        {
            StartInteractionMode(InteractionMode.DrawBlobAnalysis, Cursors.Cross);
        }

        public void StartCircleRoiMode()
        {
            StartInteractionMode(InteractionMode.DrawCircle, Cursors.Cross, () => _isArcCaliperMode = false);
        }

        public void StartRingRoiMode()
        {
            StartInteractionMode(InteractionMode.DrawRing, Cursors.Cross, () => _ringDrawStep = 0);
        }

        public void StartCircularCaliperMeasureMode()
        {
            StartInteractionMode(InteractionMode.DrawCircularCaliper, Cursors.Cross, () => _isArcCaliperMode = false);
        }

        public void StartArcCaliperMeasureMode()
        {
            StartInteractionMode(InteractionMode.DrawCircularCaliper, Cursors.Cross, () => _isArcCaliperMode = true);
        }

        public void StartLineCaliperMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasureLineCaliper, Cursors.Pen);
        }

        public void StartPointAnnotationMode()
        {
            StartInteractionMode(InteractionMode.DrawPointAnnotation, Cursors.Cross);
        }

        public void StartArrowAnnotationMode()
        {
            StartInteractionMode(InteractionMode.MeasureLine, Cursors.Pen, () => _isArrowAnnotationMode = true);
        }

        public void StartTextAnnotationMode()
        {
            StartInteractionMode(InteractionMode.DrawTextAnnotation, Cursors.IBeam);
        }

        public void StartPolylineRoiMode(bool freehand)
        {
            StartInteractionMode(
                freehand ? InteractionMode.DrawFreehandPolyline : InteractionMode.DrawPolyline,
                Cursors.Pen,
                () =>
                {
                    _currentPolyline = new PolylineRoi { IsFreehand = freehand };
                    _isFreehandPolylineMode = freehand;
                });
        }

        public void StartPlacementMode(Func<Point, RoiBase?> createRoi, Cursor? cursor = null)
        {
            ArgumentNullException.ThrowIfNull(createRoi);

            StartInteractionMode(InteractionMode.PlaceExternalRoi, cursor ?? Cursors.Cross, () => _externalPlacementFactory = createRoi);
        }

        public void StartFitEllipseMode()
        {
            StartPlacementMode(CreateFittedEllipseAt, Cursors.UpArrow);
        }

        public void StartLineMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasureLine, Cursors.Pen, () => _isArrowAnnotationMode = false);
        }

        public void StartCaliperMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasureCaliper, Cursors.Pen);
        }

        public void StartAngleMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasureAngle, Cursors.Pen, () => _angleMeasureStep = 0);
        }

        public void StartArcMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasureArc, Cursors.Pen, () => _arcMeasureStep = 0);
        }

        public void StartPointToLineMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasurePointToLine, Cursors.Pen, () =>
            {
                _currentPointToLineMeasure = null;
                _pointToLineMeasureStep = 0;
            });
        }

        public void StartPointToCircleMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasurePointToCircle, Cursors.Pen, () =>
            {
                _currentPointToCircleMeasure = null;
                _pointToCircleMeasureStep = 0;
            });
        }

        public void StartParallelismMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasureParallelism, Cursors.Pen, () =>
            {
                _currentParallelismMeasure = null;
                _parallelismMeasureStep = 0;
            });
        }

        public void StartPerpendicularityMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasurePerpendicularity, Cursors.Pen, () =>
            {
                _currentPerpendicularityMeasure = null;
                _perpendicularityMeasureStep = 0;
            });
        }

        public void StartConcentricityMeasureMode()
        {
            StartInteractionMode(InteractionMode.MeasureConcentricity, Cursors.Pen, () =>
            {
                _currentConcentricityMeasure = null;
                _concentricityMeasureStep = 0;
            });
        }

    }
}
