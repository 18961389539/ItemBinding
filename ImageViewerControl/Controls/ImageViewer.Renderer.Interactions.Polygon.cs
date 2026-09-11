using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.Utils;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        public void StartPolygonRoiMode()
        {
            ExitCurrentMode();
            _currentPolygon = new PolygonRoi();
            _isPolygonCloseCandidate = false;
            EnterInteractionMode(InteractionMode.DrawPolygon, Cursors.Pen);
        }

        private void OnPolygonMouseDown(object sender, MouseButtonEventArgs e)
        {
            HandlePolygonMouseDown(e);
        }

        private void OnPolygonMouseMove(object sender, MouseEventArgs e)
        {
            HandlePolygonMouseMove(e);
        }

        private void EndPolygonDrawing()
        {
            if (_currentPolygon == null)
            {
                return;
            }

            var vm = ViewModel;
            if (_currentPolygon.Points.Count >= 3)
            {
                _currentPolygon.IsClosed = true;
                vm.UndoRedo.Execute(new AddRoiCommand(_currentPolygon, vm));
            }

            ResetPolygonDrawing();
        }

        private void HandlePolygonMouseDown(MouseButtonEventArgs e)
        {
            if (e.Handled || _currentPolygon == null)
            {
                return;
            }

            if (e.RightButton == MouseButtonState.Pressed || e.ClickCount >= 2)
            {
                EndPolygonDrawing();
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (!TryCaptureRootGridMouse())
            {
                return;
            }

            Point pos = GetSnappedMousePosition(e);
            if (_currentPolygon.Points.Count >= 3 &&
                RoiGeometryService.ShouldClosePolygon(pos, _currentPolygon.Points[0], HitTestTolerance, Scale))
            {
                EndPolygonDrawing();
                return;
            }

            _currentPolygon.Points.Add(pos);
            _polygonPreviewPoint = null;
            DrawRois();
        }

        private void HandlePolygonMouseMove(MouseEventArgs e)
        {
            if (_currentPolygon == null || _currentPolygon.Points.Count == 0)
            {
                return;
            }

            Point currentPos = GetSnappedMousePosition(e);
            _polygonPreviewPoint = currentPos;
            _isPolygonCloseCandidate = _currentPolygon.Points.Count >= 3 &&
                RoiGeometryService.ShouldClosePolygon(currentPos, _currentPolygon.Points[0], HitTestTolerance, Scale);

            rootGrid.Cursor = _isPolygonCloseCandidate ? Cursors.Hand : Cursors.Pen;
            DrawRois();
        }

        private void ResetPolygonDrawing()
        {
            ReleaseRootGridMouseIfCaptured();
            _currentPolygon = null;
            _isPolygonCloseCandidate = false;
            _polygonPreviewPoint = null;
            LeaveInteractionMode();
            DrawRois();
        }

        /// <summary>
        /// 获取指定点位于多边形的哪一条边（段）上。
        /// Chinese: 如果点靠近某条多边形边，则返回边的起始点索引，否则返回 -1。
        /// English: Returns the index of the polygon segment (edge) that the given point is near,
        /// or -1 when none found.
        /// </summary>
        /// <param name="point">要检测的点（图像坐标） / Point to test (in image coordinates)</param>
        /// <returns>命中的边的起始顶点索引，未命中返回 -1 / The index of the segment start vertex, or -1 if none.</returns>
        public int GetPolygonSegmentAt(Point point)
        {
            return RoiInteraction.GetPolygonSegmentAt(ViewModel.SelectedRoi, point, Scale, HitTestTolerance);
        }

        /// <summary>
        /// 获取指定点对应的多边形顶点索引（如果有的话）。
        /// Chinese: 检查点是否在某个顶点控制点区域内，返回该顶点的索引或 -1。
        /// English: Returns the index of a polygon vertex at the given point or -1 if none.
        /// </summary>
        /// <param name="point">要检测的点（图像坐标） / Point to test (in image coordinates)</param>
        /// <returns>顶点索引或 -1 / The vertex index or -1 if none.</returns>
        public int GetPolygonPointIndexAt(Point point)
        {
            return RoiInteraction.GetPolygonPointIndexAt(ViewModel.SelectedRoi, point, Scale, HandleSize, PolygonVertexHitPadding);
        }
    }
}
