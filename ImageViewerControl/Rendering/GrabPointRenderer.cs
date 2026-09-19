using System.Windows;
using System.Windows.Media;
using ImageViewer.Models;

namespace ImageViewer.Rendering
{
    public sealed partial class RoiRenderService
    {
        /// <summary>
        /// 抓取点十字准星渲染器（2026-09-19）：绘制"狙击枪十字"——穿过中心的横竖两条
        /// 长线 + 中心小方框，随缩放保持恒定屏幕尺寸。
        /// </summary>
        private sealed class GrabPointRenderer : IRoiRenderer
        {
            public bool CanRender(RoiBase roi) => roi is GrabPointRoi;

            public void Render(RoiBase roi, RoiRenderContext context, Brush? strokeOverride, bool isSelected)
            {
                var grab = (GrabPointRoi)roi;
                if (!grab.IsVisible) return;

                Brush brush = RoiRenderContext.ResolveStroke(strokeOverride, grab.StrokeColor);
                double half = GrabPointRoi.ScreenExtent / context.Scale;   // 十字半长（屏幕 20px）
                double boxHalf = 6.0 / context.Scale; // 中心方框半宽
                double thickness = (isSelected ? 2.5 : 1.8) / context.Scale;
                var p = grab.Position;

                // 十字横竖线
                context.DrawLineSegment(new Point(p.X - half, p.Y), new Point(p.X + half, p.Y), brush, thickness);
                context.DrawLineSegment(new Point(p.X, p.Y - half), new Point(p.X, p.Y + half), brush, thickness);
                // 中心方框
                context.DrawRectangleOutline(p, boxHalf * 2, boxHalf * 2, 0, brush, thickness);

                if (!string.IsNullOrWhiteSpace(grab.Label))
                {
                    context.DrawInfoText(
                        grab.Label,
                        new Point(p.X, p.Y + half + 2.0 / context.Scale),
                        brush);
                }
            }
        }
    }
}