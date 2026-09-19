using System.Windows;
using ImageViewer.Common;

namespace ImageViewer.Models
{
    /// <summary>
    /// 抓取点十字准星（2026-09-19）：用于配方页「抓取点示教」。
    /// 在图像上以狙击枪十字样式显示抓取点位置，用户可按住拖动实时设置
    /// 长/短轴偏移（宿主订阅 <see cref="Position"/> 变化后到反算）。
    /// 坐标为图像像素（与配方页 SetGrabPoint 同坐标系）。
    /// </summary>
    public sealed class GrabPointRoi : RoiBase
    {
        /// <summary>
        /// 十字/命中区域半长（屏幕像素，随缩放换算到图像坐标）。
        /// 渲染器与命中测试共用此常量，保证"看得见的部分"与"可拖动的区域"一致。
        /// </summary>
        public const double ScreenExtent = 20.0;

        private Point _position;

        public GrabPointRoi()
        {
            StrokeColor = System.Windows.Media.Colors.Magenta;
        }

        public Point Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        public override RoiBase Clone()
        {
            return new GrabPointRoi
            {
                Position = Position,
                Label = Label,
                StrokeColor = StrokeColor,
                StrokeThickness = StrokeThickness,
                IsVisible = IsVisible,
                IsLocked = IsLocked
            };
        }

        public override void ApplyFrom(RoiBase source)
        {
            if (source is not GrabPointRoi grab)
            {
                throw new ArgumentException($"Cannot apply state from {source.GetType().Name} to {nameof(GrabPointRoi)}.", nameof(source));
            }

            Position = grab.Position;
            ApplyCommonState(grab);
        }
    }
}