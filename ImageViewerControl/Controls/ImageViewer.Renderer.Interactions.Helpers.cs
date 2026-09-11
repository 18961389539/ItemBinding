using System;
using System.Windows;
using System.Windows.Input;
using ImageViewer.Models;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private static bool IsPrimaryMouseButtonDown(MouseButtonEventArgs e)
        {
            return !e.Handled && e.LeftButton == MouseButtonState.Pressed;
        }

        private Point GetSnappedMousePosition(MouseEventArgs e)
        {
            return SnapPoint(e.GetPosition(imageContainer));
        }

        private bool TryBeginMouseDrawing(MouseButtonEventArgs e, out Point startPoint)
        {
            startPoint = default;

            if (!IsPrimaryMouseButtonDown(e))
            {
                return false;
            }

            if (!TryCaptureRootGridMouse())
            {
                return false;
            }

            startPoint = GetSnappedMousePosition(e);
            return true;
        }

        private bool TryBeginMouseDrawing<T>(MouseButtonEventArgs e, ref T? currentRoi, Func<Point, T> createRoi, out Point startPoint)
            where T : RoiBase
        {
            startPoint = default;

            if (currentRoi != null || !TryBeginMouseDrawing(e, out startPoint))
            {
                return false;
            }

            currentRoi = createRoi(startPoint);
            return true;
        }

        private bool HandleStepSelectionMouseDown<T>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            int finalStep,
            Func<Point, RoiBase?, T?> createInitial,
            Func<T, Point, RoiBase?, bool> tryComplete,
            Action<T> commit)
            where T : class
        {
            return HandleStepSelectionMouseDownCore(
                e,
                ref currentRoi,
                ref step,
                finalStep,
                createInitial,
                tryComplete,
                commit,
                null);
        }

        private bool HandleStepSelectionMouseDown<T>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            int finalStep,
            Func<Point, RoiBase?, T?> createInitial,
            Action<T, int, Point, RoiBase?> updateStep,
            Func<T, Point, RoiBase?, bool> tryComplete,
            Action<T> commit)
            where T : class
        {
            return HandleStepSelectionMouseDownCore(
                e,
                ref currentRoi,
                ref step,
                finalStep,
                createInitial,
                tryComplete,
                commit,
                updateStep);
        }

        private bool HandleStepSelectionMouseDownCore<T>(
            MouseButtonEventArgs e,
            ref T? currentRoi,
            ref int step,
            int finalStep,
            Func<Point, RoiBase?, T?> createInitial,
            Func<T, Point, RoiBase?, bool> tryComplete,
            Action<T> commit,
            Action<T, int, Point, RoiBase?>? updateStep)
            where T : class
        {
            if (e.Handled)
            {
                return false;
            }

            if (e.RightButton == MouseButtonState.Pressed)
            {
                ResetStepInteraction(ref currentRoi, ref step);
                e.Handled = true;
                return true;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return false;
            }

            Point position = GetSnappedMousePosition(e);
            RoiBase? hitRoi = HitTest(position);

            if (step == 0)
            {
                T? created = createInitial(position, hitRoi);
                if (created == null)
                {
                    return false;
                }

                if (!TryCaptureRootGridMouse())
                {
                    return false;
                }

                currentRoi = created;
                step = finalStep == 0 ? 0 : 1;
                DrawRois();
                e.Handled = true;
                return true;
            }

            if (currentRoi == null)
            {
                return false;
            }

            if (step < finalStep)
            {
                updateStep?.Invoke(currentRoi, step, position, hitRoi);
                step++;
                DrawRois();
                e.Handled = true;
                return true;
            }

            if (step == finalStep)
            {
                if (tryComplete(currentRoi, position, hitRoi))
                {
                    commit(currentRoi);
                    ResetStepInteraction(ref currentRoi, ref step);
                }
                else
                {
                    DrawRois();
                }

                e.Handled = true;
                return true;
            }

            return false;
        }

        private void UpdateStepSelectionMouseMove<T>(
            T? currentRoi,
            int step,
            MouseEventArgs e,
            Action<RoiBase?> updateIdleCursor,
            Action<T, int, Point, RoiBase?> updateActivePreview)
            where T : class
        {
            Point position = GetSnappedMousePosition(e);
            RoiBase? hitRoi = HitTest(position);

            if (step == 0)
            {
                updateIdleCursor(hitRoi);
                return;
            }

            if (currentRoi != null)
            {
                updateActivePreview(currentRoi, step, position, hitRoi);
                DrawRois();
            }
        }

        private void UpdateMouseDrawing<T>(T? currentRoi, MouseEventArgs e, Action<T, Point> update)
            where T : class
        {
            if (currentRoi == null)
            {
                return;
            }

            update(currentRoi, GetSnappedMousePosition(e));
            DrawRois();
        }

        private static Point GetMidPoint(Point startPoint, Point currentPoint)
        {
            return new Point((startPoint.X + currentPoint.X) / 2, (startPoint.Y + currentPoint.Y) / 2);
        }

        private void CommitRoi(RoiBase roi)
        {
            var vm = ViewModel;
            vm.UndoRedo.Execute(new AddRoiCommand(roi, vm));
        }

        private void ResetStepInteraction<T>(ref T? currentRoi, ref int step)
            where T : class
        {
            currentRoi = null;
            step = 0;
            ReleaseRootGridMouseIfCaptured();
            LeaveInteractionMode();
            DrawRois();
        }

        private void CommitRoiAndLeaveInteraction(RoiBase roi)
        {
            CommitRoi(roi);
            LeaveInteractionMode();
            DrawRois();
        }

        private void CommitRoiAndExitCurrentMode(RoiBase roi)
        {
            CommitRoi(roi);
            ExitCurrentMode();
            DrawRois();
        }

        private void ApplyCircularCaliperDetection(CircularCaliperMeasureRoi caliper)
        {
            if (caliper is ArcCaliperMeasureRoi arcCaliper)
            {
                TryApplyArcCaliperDetection(arcCaliper);
                return;
            }

            TryApplyCircularCaliperDetection(caliper);
        }

        private void UpdateCircularCaliperDrawing(CircularCaliperMeasureRoi caliper, Point currentPos)
        {
            caliper.Center = _startPoint;
            caliper.Radius = GeometryUtils.Distance(_startPoint, currentPos);

            if (caliper.Radius > MinimumLineLength)
            {
                ApplyCircularCaliperDetection(caliper);
            }
            else
            {
                caliper.ClearDetectedEdges();
            }
        }
    }
}
