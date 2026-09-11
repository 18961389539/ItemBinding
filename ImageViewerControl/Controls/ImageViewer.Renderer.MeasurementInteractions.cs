using System.Windows;
using System.Windows.Input;
using ImageViewer.Models;
using ImageViewer.Utils;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private delegate bool RoiSelectionResolver<TSelection>(RoiBase? hitRoi, out TSelection selection);

        private readonly record struct LineSelection(Point P1, Point P2);

        private readonly record struct CircleSelection(Point Center, double Radius);

        private readonly record struct PointToSelectionMeasureAdapter<T, TSelection>(
            Func<Point, T> CreateInitial,
            RoiSelectionResolver<TSelection> TryResolveSelection,
            Action<T, TSelection> ApplySelection,
            Action<T> ApplyFallback)
            where T : RoiBase;

        private readonly record struct PairedLineMeasureAdapter<T>(
            Func<Point, Point, T> CreateInitial,
            Func<T, (Point P1, Point P2)> GetPrimaryLine,
            Action<T, Point, Point> SetPrimaryLine,
            Action<T, Point, Point> SetSecondaryLine)
            where T : RoiBase;

        private readonly record struct PairedCircleMeasureAdapter<T>(
            Func<CircleSelection, T> CreateInitial,
            Func<T, CircleSelection> GetPrimaryCircle,
            Action<T, CircleSelection> SetPrimaryCircle,
            Action<T, CircleSelection> SetSecondaryCircle)
            where T : RoiBase;

        private static readonly PointToSelectionMeasureAdapter<PointToLineDistanceRoi, LineSelection> PointToLineMeasureAdapter = new(
            static point => new PointToLineDistanceRoi
            {
                Point = point,
                LineP1 = point,
                LineP2 = point
            },
            TryGetLineSelectionValue,
            static (roi, selection) =>
            {
                roi.LineP1 = selection.P1;
                roi.LineP2 = selection.P2;
            },
            static roi =>
            {
                roi.LineP1 = roi.Point;
                roi.LineP2 = roi.Point;
            });

        private static readonly PointToSelectionMeasureAdapter<PointToCircleDistanceRoi, CircleSelection> PointToCircleMeasureAdapter = new(
            static point => new PointToCircleDistanceRoi
            {
                Point = point,
                Center = point,
                Radius = 1
            },
            TryGetCircleSelectionValue,
            static (roi, selection) =>
            {
                roi.Center = selection.Center;
                roi.Radius = selection.Radius;
            },
            static roi =>
            {
                roi.Center = roi.Point;
                roi.Radius = 1;
            });

        private static readonly PairedLineMeasureAdapter<ParallelismMeasureRoi> ParallelismLineAdapter = new(
            static (lineP1, lineP2) => new ParallelismMeasureRoi
            {
                Line1P1 = lineP1,
                Line1P2 = lineP2,
                Line2P1 = lineP1,
                Line2P2 = lineP2
            },
            static roi => (roi.Line1P1, roi.Line1P2),
            static (roi, lineP1, lineP2) =>
            {
                roi.Line1P1 = lineP1;
                roi.Line1P2 = lineP2;
            },
            static (roi, lineP1, lineP2) =>
            {
                roi.Line2P1 = lineP1;
                roi.Line2P2 = lineP2;
            });

        private static readonly PairedLineMeasureAdapter<PerpendicularityMeasureRoi> PerpendicularityLineAdapter = new(
            static (lineP1, lineP2) => new PerpendicularityMeasureRoi
            {
                Line1P1 = lineP1,
                Line1P2 = lineP2,
                Line2P1 = lineP1,
                Line2P2 = lineP2
            },
            static roi => (roi.Line1P1, roi.Line1P2),
            static (roi, lineP1, lineP2) =>
            {
                roi.Line1P1 = lineP1;
                roi.Line1P2 = lineP2;
            },
            static (roi, lineP1, lineP2) =>
            {
                roi.Line2P1 = lineP1;
                roi.Line2P2 = lineP2;
            });

        private static readonly PairedCircleMeasureAdapter<ConcentricityMeasureRoi> ConcentricityCircleAdapter = new(
            static selection => new ConcentricityMeasureRoi
            {
                Center1 = selection.Center,
                Radius1 = selection.Radius,
                Center2 = selection.Center,
                Radius2 = selection.Radius
            },
            static roi => new CircleSelection(roi.Center1, roi.Radius1),
            static (roi, selection) =>
            {
                roi.Center1 = selection.Center;
                roi.Radius1 = selection.Radius;
            },
            static (roi, selection) =>
            {
                roi.Center2 = selection.Center;
                roi.Radius2 = selection.Radius;
            });

        private void OnLineCaliperMouseDown(object sender, MouseButtonEventArgs e)
        {
            TryBeginMouseDrawing(e, ref _currentLineCaliperMeasure, startPoint => new LineCaliperMeasureRoi
            {
                P1 = startPoint,
                P2 = startPoint
            }, out _startPoint);
        }

        private void OnLineCaliperMouseMove(object sender, MouseEventArgs e)
        {
            UpdateMouseDrawing(_currentLineCaliperMeasure, e, (lineCaliper, currentPos) =>
            {
                var previewLine = (LineCaliperMeasureRoi)lineCaliper.Clone();
                previewLine.P1 = _startPoint;
                previewLine.P2 = currentPos;

                if (GeometryUtils.Distance(previewLine.P1, previewLine.P2) > MinimumLineLength && TryApplyLineCaliperDetection(previewLine))
                {
                    lineCaliper.ApplyFrom(previewLine);
                }
                else
                {
                    lineCaliper.P1 = _startPoint;
                    lineCaliper.P2 = currentPos;
                    lineCaliper.ClearDetectedLine();
                }
            });
        }

        private void OnLineCaliperMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndLineCaliperDrawing();
        }

        private void EndLineCaliperDrawing()
        {
            CompleteSingleRoiInteraction(
                ref _currentLineCaliperMeasure,
                lineCaliper => GeometryUtils.Distance(lineCaliper.P1, lineCaliper.P2) > MinimumDrawableSize);
        }

        private void OnMeasureMouseDown(object sender, MouseButtonEventArgs e, bool useCaliper)
        {
            if (!TryBeginMouseDrawing(e, ref _currentLineMeasure, startPoint => useCaliper
                ? new CaliperMeasureRoi { P1 = startPoint, P2 = startPoint }
                : _isArrowAnnotationMode
                    ? new ArrowAnnotationRoi { P1 = startPoint, P2 = startPoint }
                    : new LineMeasureRoi { P1 = startPoint, P2 = startPoint }, out _startPoint))
            {
                return;
            }

            if (_currentLineMeasure is CaliperMeasureRoi caliper)
            {
                caliper.SyncCaliperRegionFromMeasurementLine(updateSearchRange: false);
            }
        }

        private void OnMeasureMouseMove(object sender, MouseEventArgs e)
        {
            UpdateMouseDrawing(_currentLineMeasure, e, (measure, currentPos) =>
            {
                measure.P2 = currentPos;

                if (measure is CaliperMeasureRoi caliper)
                {
                    caliper.SyncCaliperRegionFromMeasurementLine();
                    if (GeometryUtils.Distance(caliper.P1, caliper.P2) > MinimumLineLength)
                    {
                        TryApplyCaliperDetection(caliper);
                    }
                    else
                    {
                        caliper.ClearDetectedEdges();
                    }
                }
            });
        }

        private void OnMeasureMouseUp(object sender, MouseButtonEventArgs e)
        {
            CompleteSingleRoiInteraction(
                ref _currentLineMeasure,
                line => GeometryUtils.Distance(line.P1, line.P2) > MinimumDrawableSize,
                line =>
                {
                    if (line is CaliperMeasureRoi caliper)
                    {
                        TryApplyCaliperDetection(caliper);
                    }
                });
        }

        private void OnAngleMeasureMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandleThreePointMeasureMouseDown(
                e,
                ref _currentAngleMeasure,
                ref _angleMeasureStep,
                createInitial: static startPoint => new AngleMeasureRoi { P1 = startPoint, Vertex = startPoint, P2 = startPoint },
                applySecondPoint: static (roi, pos) => roi.Vertex = pos,
                applyThirdPoint: static (roi, pos) => roi.P2 = pos);
        }

        private void OnAngleMeasureMouseMove(object sender, MouseEventArgs e)
        {
            UpdateThreePointMeasureMouseMove(
                _currentAngleMeasure,
                _angleMeasureStep,
                e,
                applySecondPoint: static (roi, pos) => roi.Vertex = pos,
                applyThirdPoint: static (roi, pos) => roi.P2 = pos);
        }

        private void OnArcMeasureMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandleThreePointMeasureMouseDown(
                e,
                ref _currentArcMeasure,
                ref _arcMeasureStep,
                createInitial: pos => CreateArcMeasureFromPoints(pos, pos, pos),
                applySecondPoint: static (roi, pos) => roi.EndPoint = pos,
                applyThirdPoint: static (roi, pos) => roi.ArcPoint = pos);
        }

        private void OnArcMeasureMouseMove(object sender, MouseEventArgs e)
        {
            UpdateThreePointMeasureMouseMove(
                _currentArcMeasure,
                _arcMeasureStep,
                e,
                applySecondPoint: static (roi, pos) => roi.EndPoint = pos,
                applyThirdPoint: static (roi, pos) => roi.ArcPoint = pos);
        }

        private void OnPointToLineMeasureMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandlePointToSelectionMeasureMouseDown(e, ref _currentPointToLineMeasure, ref _pointToLineMeasureStep, PointToLineMeasureAdapter);
        }

        private void OnPointToLineMeasureMouseMove(object sender, MouseEventArgs e)
        {
            UpdatePointToSelectionMeasureMouseMove(_currentPointToLineMeasure, _pointToLineMeasureStep, e, PointToLineMeasureAdapter);
        }

        private void OnPointToCircleMeasureMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandlePointToSelectionMeasureMouseDown(e, ref _currentPointToCircleMeasure, ref _pointToCircleMeasureStep, PointToCircleMeasureAdapter);
        }

        private void OnPointToCircleMeasureMouseMove(object sender, MouseEventArgs e)
        {
            UpdatePointToSelectionMeasureMouseMove(_currentPointToCircleMeasure, _pointToCircleMeasureStep, e, PointToCircleMeasureAdapter);
        }

        private void OnParallelismMeasureMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandlePairedLineMeasureMouseDown(e, ref _currentParallelismMeasure, ref _parallelismMeasureStep, ParallelismLineAdapter);
        }

        private void OnParallelismMeasureMouseMove(object sender, MouseEventArgs e)
        {
            UpdatePairedLineMeasureMouseMove(_currentParallelismMeasure, _parallelismMeasureStep, e, ParallelismLineAdapter);
        }

        private void OnPerpendicularityMeasureMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandlePairedLineMeasureMouseDown(e, ref _currentPerpendicularityMeasure, ref _perpendicularityMeasureStep, PerpendicularityLineAdapter);
        }

        private void OnPerpendicularityMeasureMouseMove(object sender, MouseEventArgs e)
        {
            UpdatePairedLineMeasureMouseMove(_currentPerpendicularityMeasure, _perpendicularityMeasureStep, e, PerpendicularityLineAdapter);
        }

        private void OnConcentricityMeasureMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandlePairedCircleMeasureMouseDown(e, ref _currentConcentricityMeasure, ref _concentricityMeasureStep, ConcentricityCircleAdapter);
        }

        private void OnConcentricityMeasureMouseMove(object sender, MouseEventArgs e)
        {
            UpdatePairedCircleMeasureMouseMove(_currentConcentricityMeasure, _concentricityMeasureStep, e, ConcentricityCircleAdapter);
        }

        private static Point GetSelectedPointOrFallback(RoiBase? hitRoi, Point fallback)
        {
            return TryGetPointSelection(hitRoi, out Point selectedPoint) ? selectedPoint : fallback;
        }

        private void HandlePointToSelectionMeasureMouseDown<T, TSelection>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            Func<Point, T> createInitial,
            RoiSelectionResolver<TSelection> tryResolveSelection,
            Action<T, TSelection> applySelection)
            where T : RoiBase
        {
            HandleStepSelectionMouseDown(
                e,
                ref currentRoi,
                ref step,
                finalStep: 1,
                createInitial: (pos, hitRoi) => createInitial(GetSelectedPointOrFallback(hitRoi, pos)),
                tryComplete: (roi, pos, hitRoi) => TryApplyResolvedSelection(roi, hitRoi, tryResolveSelection, applySelection),
                commit: CommitRoi);
        }

        private void HandlePointToSelectionMeasureMouseDown<T, TSelection>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            PointToSelectionMeasureAdapter<T, TSelection> adapter)
            where T : RoiBase
        {
            HandlePointToSelectionMeasureMouseDown(
                e,
                ref currentRoi,
                ref step,
                adapter.CreateInitial,
                adapter.TryResolveSelection,
                adapter.ApplySelection);
        }

        private void UpdatePointToSelectionMeasureMouseMove<T, TSelection>(
            T? currentRoi,
            int step,
            MouseEventArgs e,
            RoiSelectionResolver<TSelection> tryResolveSelection,
            Action<T, TSelection> applySelection,
            Action<T> applyFallback)
            where T : RoiBase
        {
            UpdateStepSelectionMouseMove(
                currentRoi,
                step,
                e,
                updateIdleCursor: hitRoi => SetSelectionCursor(tryResolveSelection(hitRoi, out _)),
                updateActivePreview: (roi, currentStep, pos, hitRoi) => UpdateResolvedSelectionPreview(
                    roi,
                    currentStep,
                    hitRoi,
                    tryResolveSelection,
                    applySelection,
                    applyFallback,
                    SetSelectionCursor));
        }

        private void UpdatePointToSelectionMeasureMouseMove<T, TSelection>(
            T? currentRoi,
            int step,
            MouseEventArgs e,
            PointToSelectionMeasureAdapter<T, TSelection> adapter)
            where T : RoiBase
        {
            UpdatePointToSelectionMeasureMouseMove(
                currentRoi,
                step,
                e,
                adapter.TryResolveSelection,
                adapter.ApplySelection,
                adapter.ApplyFallback);
        }

        private void HandleThreePointMeasureMouseDown<T>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            Func<Point, T> createInitial,
            Action<T, Point> applySecondPoint,
            Action<T, Point> applyThirdPoint)
            where T : RoiBase
        {
            HandleStepSelectionMouseDown(
                e,
                ref currentRoi,
                ref step,
                finalStep: 2,
                createInitial: (pos, hitRoi) => createInitial(pos),
                updateStep: (roi, currentStep, pos, hitRoi) =>
                {
                    if (currentStep == 1)
                    {
                        applySecondPoint(roi, pos);
                        applyThirdPoint(roi, pos);
                    }
                },
                tryComplete: (roi, pos, hitRoi) =>
                {
                    applyThirdPoint(roi, pos);
                    return true;
                },
                commit: CommitRoi);
        }

        private void UpdateThreePointMeasureMouseMove<T>(
            T? currentRoi,
            int step,
            MouseEventArgs e,
            Action<T, Point> applySecondPoint,
            Action<T, Point> applyThirdPoint)
            where T : RoiBase
        {
            UpdateStepSelectionMouseMove(
                currentRoi,
                step,
                e,
                updateIdleCursor: static hitRoi => { },
                updateActivePreview: (roi, currentStep, pos, hitRoi) =>
                {
                    if (currentStep == 1)
                    {
                        applySecondPoint(roi, pos);
                        applyThirdPoint(roi, pos);
                    }
                    else if (currentStep == 2)
                    {
                        applyThirdPoint(roi, pos);
                    }
                });
        }

        private void HandlePairedLineMeasureMouseDown<T>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            Func<Point, Point, T> createInitial,
            Action<T, Point, Point> applyInitialSelection,
            Action<T, Point, Point> applyCompletedSelection)
            where T : RoiBase
        {
            HandleStepSelectionMouseDown(
                e,
                ref currentRoi,
                ref step,
                finalStep: 2,
                createInitial: (pos, hitRoi) =>
                {
                    if (TryGetLineSelection(hitRoi, out Point lineP1, out Point lineP2))
                    {
                        return createInitial(lineP1, lineP2);
                    }

                    return null;
                },
                updateStep: (roi, currentStep, pos, hitRoi) =>
                {
                    if (currentStep == 1 && TryGetLineSelection(hitRoi, out Point initialLineP1, out Point initialLineP2))
                    {
                        applyInitialSelection(roi, initialLineP1, initialLineP2);
                    }
                },
                tryComplete: (roi, pos, hitRoi) =>
                {
                    if (!TryGetLineSelection(hitRoi, out Point completedLineP1, out Point completedLineP2))
                    {
                        return false;
                    }

                    applyCompletedSelection(roi, completedLineP1, completedLineP2);
                    return true;
                },
                commit: CommitRoi);
        }

        private void HandlePairedLineMeasureMouseDown<T>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            PairedLineMeasureAdapter<T> adapter)
            where T : RoiBase
        {
            HandlePairedLineMeasureMouseDown(
                e,
                ref currentRoi,
                ref step,
                adapter.CreateInitial,
                (roi, lineP1, lineP2) =>
                {
                    adapter.SetPrimaryLine(roi, lineP1, lineP2);
                    adapter.SetSecondaryLine(roi, lineP1, lineP2);
                },
                adapter.SetSecondaryLine);
        }

        private void HandlePairedCircleMeasureMouseDown<T>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            Func<Point, double, T> createInitial,
            Action<T, Point, double> applyInitialSelection,
            Action<T, Point, double> applyCompletedSelection)
            where T : RoiBase
        {
            HandleStepSelectionMouseDown(
                e,
                ref currentRoi,
                ref step,
                finalStep: 2,
                createInitial: (pos, hitRoi) =>
                {
                    if (TryGetCircleSelection(hitRoi, out Point center, out double radius))
                    {
                        return createInitial(center, radius);
                    }

                    return null;
                },
                updateStep: (roi, currentStep, pos, hitRoi) =>
                {
                    if (currentStep == 1 && TryGetCircleSelection(hitRoi, out Point initialCenter, out double initialRadius))
                    {
                        applyInitialSelection(roi, initialCenter, initialRadius);
                    }
                },
                tryComplete: (roi, pos, hitRoi) =>
                {
                    if (!TryGetCircleSelection(hitRoi, out Point completedCenter, out double completedRadius))
                    {
                        return false;
                    }

                    applyCompletedSelection(roi, completedCenter, completedRadius);
                    return true;
                },
                commit: CommitRoi);
        }

        private void HandlePairedCircleMeasureMouseDown<T>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            PairedCircleMeasureAdapter<T> adapter)
            where T : RoiBase
        {
            HandlePairedCircleMeasureMouseDown(
                e,
                ref currentRoi,
                ref step,
                (center, radius) => adapter.CreateInitial(new CircleSelection(center, radius)),
                (roi, center, radius) =>
                {
                    CircleSelection selection = new(center, radius);
                    adapter.SetPrimaryCircle(roi, selection);
                    adapter.SetSecondaryCircle(roi, selection);
                },
                (roi, center, radius) => adapter.SetSecondaryCircle(roi, new CircleSelection(center, radius)));
        }

        private void UpdatePairedLineMeasureMouseMove<T>(
            T? currentRoi,
            int step,
            MouseEventArgs e,
            Action<T, Point, Point> applyInitialSelection,
            Action<T, Point, Point> applyPreviewSelection,
            Action<T> applyFallback)
            where T : RoiBase
        {
            UpdateStepSelectionMouseMove(
                currentRoi,
                step,
                e,
                updateIdleCursor: hitRoi => SetSelectionCursor(TryGetLineSelection(hitRoi, out _, out _)),
                updateActivePreview: (roi, currentStep, pos, hitRoi) => UpdatePairedLineSelectionPreview(
                    roi,
                    currentStep,
                    hitRoi,
                    applyInitialSelection,
                    applyPreviewSelection,
                    applyFallback,
                    SetSelectionCursor));
        }

        private void UpdatePairedLineMeasureMouseMove<T>(
            T? currentRoi,
            int step,
            MouseEventArgs e,
            PairedLineMeasureAdapter<T> adapter)
            where T : RoiBase
        {
            UpdatePairedLineMeasureMouseMove(
                currentRoi,
                step,
                e,
                (roi, lineP1, lineP2) =>
                {
                    adapter.SetPrimaryLine(roi, lineP1, lineP2);
                    adapter.SetSecondaryLine(roi, lineP1, lineP2);
                },
                adapter.SetSecondaryLine,
                roi =>
                {
                    (Point primaryP1, Point primaryP2) = adapter.GetPrimaryLine(roi);
                    adapter.SetSecondaryLine(roi, primaryP1, primaryP2);
                });
        }

        private void UpdatePairedCircleMeasureMouseMove<T>(
            T? currentRoi,
            int step,
            MouseEventArgs e,
            Action<T, Point, double> applyInitialSelection,
            Action<T, Point, double> applyPreviewSelection,
            Action<T> applyFallback)
            where T : RoiBase
        {
            UpdateStepSelectionMouseMove(
                currentRoi,
                step,
                e,
                updateIdleCursor: hitRoi => SetSelectionCursor(TryGetCircleSelection(hitRoi, out _, out _)),
                updateActivePreview: (roi, currentStep, pos, hitRoi) => UpdatePairedCircleSelectionPreview(
                    roi,
                    currentStep,
                    hitRoi,
                    applyInitialSelection,
                    applyPreviewSelection,
                    applyFallback,
                    SetSelectionCursor));
        }

        private void UpdatePairedCircleMeasureMouseMove<T>(
            T? currentRoi,
            int step,
            MouseEventArgs e,
            PairedCircleMeasureAdapter<T> adapter)
            where T : RoiBase
        {
            UpdatePairedCircleMeasureMouseMove(
                currentRoi,
                step,
                e,
                (roi, center, radius) =>
                {
                    CircleSelection selection = new(center, radius);
                    adapter.SetPrimaryCircle(roi, selection);
                    adapter.SetSecondaryCircle(roi, selection);
                },
                (roi, center, radius) => adapter.SetSecondaryCircle(roi, new CircleSelection(center, radius)),
                roi => adapter.SetSecondaryCircle(roi, adapter.GetPrimaryCircle(roi)));
        }

        private void SetSelectionCursor(bool hasSelection)
        {
            rootGrid.Cursor = hasSelection ? Cursors.Hand : Cursors.Pen;
        }

        private static bool TryGetLineSelectionValue(RoiBase? hitRoi, out LineSelection selection)
        {
            if (TryGetLineSelection(hitRoi, out Point p1, out Point p2))
            {
                selection = new LineSelection(p1, p2);
                return true;
            }

            selection = default;
            return false;
        }

        private static bool TryGetCircleSelectionValue(RoiBase? hitRoi, out CircleSelection selection)
        {
            if (TryGetCircleSelection(hitRoi, out Point center, out double radius))
            {
                selection = new CircleSelection(center, radius);
                return true;
            }

            selection = default;
            return false;
        }

        private static bool TryApplyResolvedSelection<T, TSelection>(
            T roi,
            RoiBase? hitRoi,
            RoiSelectionResolver<TSelection> tryResolveSelection,
            Action<T, TSelection> applySelection)
            where T : class
        {
            if (!tryResolveSelection(hitRoi, out TSelection selection))
            {
                return false;
            }

            applySelection(roi, selection);
            return true;
        }

        private static void UpdateResolvedSelectionPreview<T, TSelection>(
            T roi,
            int step,
            RoiBase? hitRoi,
            RoiSelectionResolver<TSelection> tryResolveSelection,
            Action<T, TSelection> applySelection,
            Action<T> applyFallback,
            Action<bool> setCursor)
            where T : class
        {
            if (step != 1)
            {
                return;
            }

            if (tryResolveSelection(hitRoi, out TSelection selection))
            {
                applySelection(roi, selection);
                setCursor(true);
                return;
            }

            applyFallback(roi);
            setCursor(false);
        }

        private static void UpdatePairedLineSelectionPreview<T>(
            T roi,
            int step,
            RoiBase? hitRoi,
            Action<T, Point, Point> applyInitialSelection,
            Action<T, Point, Point> applyPreviewSelection,
            Action<T> applyFallback,
            Action<bool> setCursor)
            where T : class
        {
            if (TryGetLineSelection(hitRoi, out Point lineP1, out Point lineP2))
            {
                if (step == 1)
                {
                    applyInitialSelection(roi, lineP1, lineP2);
                    setCursor(true);
                }
                else if (step == 2)
                {
                    applyPreviewSelection(roi, lineP1, lineP2);
                    setCursor(true);
                }
            }
            else if (step == 2)
            {
                applyFallback(roi);
                setCursor(false);
            }
        }

        private static void UpdatePairedCircleSelectionPreview<T>(
            T roi,
            int step,
            RoiBase? hitRoi,
            Action<T, Point, double> applyInitialSelection,
            Action<T, Point, double> applyPreviewSelection,
            Action<T> applyFallback,
            Action<bool> setCursor)
            where T : class
        {
            if (TryGetCircleSelection(hitRoi, out Point center, out double radius))
            {
                if (step == 1)
                {
                    applyInitialSelection(roi, center, radius);
                    setCursor(true);
                }
                else if (step == 2)
                {
                    applyPreviewSelection(roi, center, radius);
                    setCursor(true);
                }
            }
            else if (step == 2)
            {
                applyFallback(roi);
                setCursor(false);
            }
        }

    }
}
