using Microsoft.Xaml.Behaviors;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace MainAPP.Interactivity
{
    /// <summary>
    /// 为 FrameworkElement 添加 Material Design 风格的水波纹点击效果。
    /// 推荐用法：放在 Button 的 ControlTemplate 根元素（Border）上，
    /// 通过 xmlns:i="http://schemas.microsoft.com/xaml/behaviors" 的
    /// i:Interaction.Behaviors 附加。
    /// 嵌入 ControlTemplate 而非 Style.Setter，可避免 BehaviorCollection
    /// 在多个按钮实例间共享导致只生效一次的问题。
    /// </summary>
    public class RippleBehavior : Behavior<FrameworkElement>
    {
        #region 依赖属性

        /// <summary>水波纹颜色（默认半透明白）</summary>
        public static readonly DependencyProperty RippleColorProperty =
            DependencyProperty.Register(
                nameof(RippleColor),
                typeof(Color),
                typeof(RippleBehavior),
                new PropertyMetadata(Color.FromArgb(80, 255, 255, 255)));

        /// <summary>水波纹动画时长（毫秒）</summary>
        public static readonly DependencyProperty DurationMsProperty =
            DependencyProperty.Register(
                nameof(DurationMs),
                typeof(int),
                typeof(RippleBehavior),
                new PropertyMetadata(500));

        /// <summary>水波纹最大尺寸相对于元素最大边长的倍数</summary>
        public static readonly DependencyProperty ScaleFactorProperty =
            DependencyProperty.Register(
                nameof(ScaleFactor),
                typeof(double),
                typeof(RippleBehavior),
                new PropertyMetadata(2.0));

        /// <summary>圆角半径（用于裁剪水波纹，匹配 Button 模板的 CornerRadius）</summary>
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(
                nameof(CornerRadius),
                typeof(double),
                typeof(RippleBehavior),
                new PropertyMetadata(4.0));

        #endregion

        #region 属性访问器

        public Color RippleColor
        {
            get => (Color)GetValue(RippleColorProperty);
            set => SetValue(RippleColorProperty, value);
        }

        public int DurationMs
        {
            get => (int)GetValue(DurationMsProperty);
            set => SetValue(DurationMsProperty, value);
        }

        public double ScaleFactor
        {
            get => (double)GetValue(ScaleFactorProperty);
            set => SetValue(ScaleFactorProperty, value);
        }

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        #endregion

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PreviewMouseLeftButtonDown += OnMouseDown;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.PreviewMouseLeftButtonDown -= OnMouseDown;
            base.OnDetaching();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element) return;

            // 若 TemplatedParent 是 ButtonBase 且按钮被禁用，则跳过；
            // 实际上 IsEnabled 会阻断事件，这里仅作保险。
            if (element.TemplatedParent is ButtonBase buttonBase && !buttonBase.IsEnabled)
                return;

            var position = e.GetPosition(element);

            var adornerLayer = AdornerLayer.GetAdornerLayer(element);
            if (adornerLayer == null) return;

            var adorner = new RippleAdorner(
                element,
                position,
                RippleColor,
                DurationMs,
                ScaleFactor,
                CornerRadius);

            adornerLayer.Add(adorner);
        }
    }

    /// <summary>
    /// 承载水波纹 Ellipse 的 Adorner，绘制在被装饰元素之上。
    /// 使用 ScaleTransform 动画（从 0 缩放到 1），配合 RenderTransformOrigin=(0.5,0.5)
    /// 实现以点击位置为中心向外扩散的效果。
    /// </summary>
    internal sealed class RippleAdorner : Adorner
    {
        private readonly Ellipse _ripple;
        private readonly Point _position;
        private readonly int _durationMs;
        private readonly double _scaleFactor;
        private readonly double _cornerRadius;
        private bool _animationStarted;

        public RippleAdorner(
            UIElement adornedElement,
            Point position,
            Color color,
            int durationMs,
            double scaleFactor,
            double cornerRadius)
            : base(adornedElement)
        {
            _position = position;
            _durationMs = durationMs;
            _scaleFactor = scaleFactor;
            _cornerRadius = cornerRadius;

            _ripple = new Ellipse
            {
                Fill = new SolidColorBrush(color),
                IsHitTestVisible = false
            };
            AddVisualChild(_ripple);
            IsHitTestVisible = false;
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return _ripple;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // 用圆角矩形裁剪 Adorner，使水波纹不溢出按钮边界
            var clipRect = new Rect(0, 0, finalSize.Width, finalSize.Height);
            Clip = new RectangleGeometry(clipRect, _cornerRadius, _cornerRadius);

            // 计算水波纹最大尺寸
            var maxSize = Math.Max(finalSize.Width, finalSize.Height) * _scaleFactor;

            // 安排 Ellipse，使其几何中心位于点击位置；
            // RenderTransformOrigin=(0.5,0.5) 使 ScaleTransform 以 Ellipse 中心为缩放原点，
            // 因此缩放过程中 Ellipse 始终以 _position 为中心。
            _ripple.Width = maxSize;
            _ripple.Height = maxSize;
            var transform = new ScaleTransform(0, 0);
            _ripple.RenderTransform = transform;
            _ripple.RenderTransformOrigin = new Point(0.5, 0.5);

            var x = _position.X - maxSize / 2;
            var y = _position.Y - maxSize / 2;
            _ripple.Arrange(new Rect(x, y, maxSize, maxSize));

            if (!_animationStarted)
            {
                _animationStarted = true;
                StartAnimation(transform);
            }

            return finalSize;
        }

        private void StartAnimation(ScaleTransform transform)
        {
            var duration = TimeSpan.FromMilliseconds(_durationMs);

            var scaleAnim = new DoubleAnimation(0, 1, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnim = new DoubleAnimation(0.8, 0, duration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            scaleAnim.Completed += (s, e) =>
            {
                var layer = AdornerLayer.GetAdornerLayer(AdornedElement);
                layer?.Remove(this);
            };

            transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            _ripple.BeginAnimation(OpacityProperty, opacityAnim);
        }
    }
}
