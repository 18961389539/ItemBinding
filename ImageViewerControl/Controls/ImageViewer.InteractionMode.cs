using System.Windows.Input;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private enum InteractionMode
        {
            None,
            DrawRectangle,
            DrawEllipse,
            DrawCircle,
            DrawRing,
            DrawCircularCaliper,
            DrawPolygon,
            DrawPolyline,
            DrawFreehandPolyline,
            DrawPointAnnotation,
            DrawTextAnnotation,
            PlaceExternalRoi,
            MeasureLine,
            MeasureLineCaliper,
            MeasureCaliper,
            MeasureAngle,
            MeasureArc,
            MeasurePointToLine,
            MeasurePointToCircle,
            MeasureParallelism,
            MeasurePerpendicularity,
            MeasureConcentricity,
            DrawBlobAnalysis
        }

        private InteractionMode _interactionMode = InteractionMode.None;

        private bool IsToolInteractionActive => _interactionMode != InteractionMode.None;

        private void EnterInteractionMode(InteractionMode mode, Cursor cursor)
        {
            _interactionMode = mode;
            rootGrid.Cursor = cursor;

            // Attach generic tool event dispatchers
            rootGrid.MouseDown += OnToolMouseDown;
            rootGrid.MouseMove += OnToolMouseMove;
            rootGrid.MouseUp += OnToolMouseUp;
        }

        private void LeaveInteractionMode()
        {
            _interactionMode = InteractionMode.None;
            rootGrid.Cursor = Cursors.Arrow;

            // Detach generic dispatchers
            rootGrid.MouseDown -= OnToolMouseDown;
            rootGrid.MouseMove -= OnToolMouseMove;
            rootGrid.MouseUp -= OnToolMouseUp;
        }

        // Generic dispatchers allow centralized handling of tool mouse events and reduce per-tool event attachments
        private void OnToolMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Redirect to specific handlers depending on the interaction mode
            switch (_interactionMode)
            {
                case InteractionMode.DrawRectangle:
                case InteractionMode.DrawBlobAnalysis:
                    OnRoiMouseDown(sender, e);
                    break;
                case InteractionMode.DrawEllipse:
                    OnEllipseMouseDown(sender, e);
                    break;
                case InteractionMode.DrawCircle:
                    OnCircleMouseDown(sender, e);
                    break;
                case InteractionMode.DrawRing:
                    OnRingMouseDown(sender, e);
                    break;
                case InteractionMode.DrawCircularCaliper:
                    OnCircularCaliperMouseDown(sender, e);
                    break;
                case InteractionMode.DrawPolygon:
                    OnPolygonMouseDown(sender, e);
                    break;
                case InteractionMode.DrawPolyline:
                case InteractionMode.DrawFreehandPolyline:
                    OnPolylineMouseDown(sender, e);
                    break;
                case InteractionMode.DrawPointAnnotation:
                    OnPointAnnotationMouseDown(sender, e);
                    break;
                case InteractionMode.DrawTextAnnotation:
                    OnTextAnnotationMouseDown(sender, e);
                    break;
                case InteractionMode.PlaceExternalRoi:
                    OnExternalPlacementMouseDown(sender, e);
                    break;
                case InteractionMode.MeasureLine:
                    OnMeasureMouseDown(sender, e, useCaliper: false);
                    break;
                case InteractionMode.MeasureLineCaliper:
                    OnLineCaliperMouseDown(sender, e);
                    break;
                case InteractionMode.MeasureCaliper:
                    OnMeasureMouseDown(sender, e, useCaliper: true);
                    break;
                case InteractionMode.MeasureAngle:
                    OnAngleMeasureMouseDown(sender, e);
                    break;
                case InteractionMode.MeasureArc:
                    OnArcMeasureMouseDown(sender, e);
                    break;
                case InteractionMode.MeasurePointToLine:
                    OnPointToLineMeasureMouseDown(sender, e);
                    break;
                case InteractionMode.MeasurePointToCircle:
                    OnPointToCircleMeasureMouseDown(sender, e);
                    break;
                case InteractionMode.MeasureParallelism:
                    OnParallelismMeasureMouseDown(sender, e);
                    break;
                case InteractionMode.MeasurePerpendicularity:
                    OnPerpendicularityMeasureMouseDown(sender, e);
                    break;
                case InteractionMode.MeasureConcentricity:
                    OnConcentricityMeasureMouseDown(sender, e);
                    break;
            }
        }

        private void OnToolMouseMove(object sender, MouseEventArgs e)
        {
            switch (_interactionMode)
            {
                case InteractionMode.DrawRectangle:
                case InteractionMode.DrawBlobAnalysis:
                    OnRoiMouseMove(sender, e);
                    break;
                case InteractionMode.DrawEllipse:
                    OnEllipseMouseMove(sender, e);
                    break;
                case InteractionMode.DrawCircle:
                    OnCircleMouseMove(sender, e);
                    break;
                case InteractionMode.DrawRing:
                    OnRingMouseMove(sender, e);
                    break;
                case InteractionMode.DrawCircularCaliper:
                    OnCircularCaliperMouseMove(sender, e);
                    break;
                case InteractionMode.DrawPolygon:
                    OnPolygonMouseMove(sender, e);
                    break;
                case InteractionMode.DrawPolyline:
                case InteractionMode.DrawFreehandPolyline:
                    OnPolylineMouseMove(sender, e);
                    break;
                case InteractionMode.MeasureLine:
                case InteractionMode.MeasureCaliper:
                    OnMeasureMouseMove(sender, e);
                    break;
                case InteractionMode.MeasureLineCaliper:
                    OnLineCaliperMouseMove(sender, e);
                    break;
                case InteractionMode.MeasureAngle:
                    OnAngleMeasureMouseMove(sender, e);
                    break;
                case InteractionMode.MeasureArc:
                    OnArcMeasureMouseMove(sender, e);
                    break;
                case InteractionMode.MeasurePointToLine:
                    OnPointToLineMeasureMouseMove(sender, e);
                    break;
                case InteractionMode.MeasurePointToCircle:
                    OnPointToCircleMeasureMouseMove(sender, e);
                    break;
                case InteractionMode.MeasureParallelism:
                    OnParallelismMeasureMouseMove(sender, e);
                    break;
                case InteractionMode.MeasurePerpendicularity:
                    OnPerpendicularityMeasureMouseMove(sender, e);
                    break;
                case InteractionMode.MeasureConcentricity:
                    OnConcentricityMeasureMouseMove(sender, e);
                    break;
            }
        }

        private void OnToolMouseUp(object sender, MouseButtonEventArgs e)
        {
            switch (_interactionMode)
            {
                case InteractionMode.DrawRectangle:
                case InteractionMode.DrawBlobAnalysis:
                    OnRoiMouseUp(sender, e);
                    break;
                case InteractionMode.DrawEllipse:
                    OnEllipseMouseUp(sender, e);
                    break;
                case InteractionMode.DrawCircle:
                    OnCircleMouseUp(sender, e);
                    break;
                case InteractionMode.DrawRing:
                    OnRingMouseUp(sender, e);
                    break;
                case InteractionMode.DrawCircularCaliper:
                    OnCircularCaliperMouseUp(sender, e);
                    break;
                case InteractionMode.DrawPolygon:
                    // polygon completes on mouse down (double-click/right) usually
                    break;
                case InteractionMode.DrawPolyline:
                case InteractionMode.DrawFreehandPolyline:
                    OnPolylineMouseUp(sender, e);
                    break;
                case InteractionMode.MeasureLine:
                case InteractionMode.MeasureCaliper:
                    OnMeasureMouseUp(sender, e);
                    break;
                case InteractionMode.MeasureLineCaliper:
                    OnLineCaliperMouseUp(sender, e);
                    break;
                case InteractionMode.MeasureAngle:
                    // angle uses step-based flow handled in mouse down
                    break;
            }
        }
    }
}
