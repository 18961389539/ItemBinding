using System;
using System.Windows;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        /// <summary>
        /// 图像坐标系宿主元素（承载缩放/平移变换的画布）。
        ///
        /// <para>宿主在鼠标事件里调用 <c>e.GetPosition(ImageContainer)</c> 即可得到
        /// <b>图像像素坐标</b> —— 缩放/平移变换作用在该元素自身上，GetPosition 会自动反解。</para>
        ///
        /// <para>2026-09-15 新增：配方页的画面示教（点击图像设置抓取点）需要这个映射。</para>
        /// </summary>
        public System.Windows.FrameworkElement ImageContainer => imageContainer;

        private Point SnapPoint(Point point)
        {
            if (!EnableSnapToGrid || GridSpacing <= 0)
            {
                return point;
            }

            return new Point(
                Math.Round(point.X / GridSpacing) * GridSpacing,
                Math.Round(point.Y / GridSpacing) * GridSpacing);
        }

        private void UpdateCrosshair(double x, double y)
        {
            crosshairH.X1 = 0;
            crosshairH.X2 = ActualWidth;
            crosshairH.Y1 = y;
            crosshairH.Y2 = y;

            crosshairV.X1 = x;
            crosshairV.X2 = x;
            crosshairV.Y1 = 0;
            crosshairV.Y2 = ActualHeight;
        }
    }
}