using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MainAPP.Controls
{
    /// <summary>
    /// 内容切换时播放过渡动画的 ContentControl。
    /// 当 Content 变化或控件被加载到视觉树时，触发 Fade + Slide In 动画。
    /// 用于 TabControl 切换 TabItem 时的页面过渡效果。
    /// </summary>
    public class TransitioningContentControl : ContentControl
    {
        public static readonly DependencyProperty TransitionDurationProperty =
            DependencyProperty.Register(
                nameof(TransitionDuration),
                typeof(Duration),
                typeof(TransitioningContentControl),
                new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(300))));

        /// <summary>
        /// 过渡动画时长，默认 300ms
        /// </summary>
        public Duration TransitionDuration
        {
            get => (Duration)GetValue(TransitionDurationProperty);
            set => SetValue(TransitionDurationProperty, value);
        }

        static TransitioningContentControl()
        {
            // 指定默认样式键，使 WPF 在 Themes/Generic.xaml 中查找默认模板
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(TransitioningContentControl),
                new FrameworkPropertyMetadata(typeof(TransitioningContentControl)));
        }

        public TransitioningContentControl()
        {
            // 监听 Loaded 事件：TabControl 切换 TabItem 时，新选中的内容会被加载到视觉树，触发过渡动画
            Loaded += OnLoaded;
        }

        /// <summary>
        /// 控件加载到视觉树时播放过渡动画（适用于 TabControl 切换 TabItem 场景）。
        /// WPF 原生 TabControl 在切换 TabItem 时，会将旧内容移出视觉树（Unloaded），
        /// 将新内容加入视觉树（Loaded），因此 Loaded 是触发过渡动画的天然时机。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PlayTransition();
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);

            // 仅当 Content 真正从一个非空对象变为另一个非空对象时播放动画
            // （适用于 ContentTemplate 动态切换场景，例如直接替换 Content 属性）
            if (oldContent == null || newContent == null) return;

            PlayTransition();
        }

        /// <summary>
        /// 播放 Fade + Slide In 过渡动画：
        /// - 透明度从 0 → 1
        /// - X 方向从 40px → 0（从右侧滑入）
        /// - 缓动函数：QuadraticEase EaseOut
        /// - 时长：TransitionDuration（默认 300ms）
        /// </summary>
        private void PlayTransition()
        {
            if (Content is not FrameworkElement fe) return;

            // 确保使用 TranslateTransform 以支持 X 方向滑入动画
            fe.RenderTransform = new TranslateTransform { X = 40 };
            fe.Opacity = 0;

            // 淡入动画：透明度 0 → 1
            var fadeIn = new DoubleAnimation(0, 1, TransitionDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // 滑入动画：X 从 40 → 0
            var slideIn = new DoubleAnimation(40, 0, TransitionDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            fe.BeginAnimation(OpacityProperty, fadeIn);
            fe.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }
    }
}
