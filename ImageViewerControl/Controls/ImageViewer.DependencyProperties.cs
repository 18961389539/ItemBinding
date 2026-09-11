using System.Windows;
using System.Windows.Media;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(ImageViewer),
                new PropertyMetadata(null, OnImageSourceChanged));

        public static readonly DependencyProperty IsImageLoadingProperty =
            DependencyProperty.Register(nameof(IsImageLoading), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ImageLoadProgressProperty =
            DependencyProperty.Register(nameof(ImageLoadProgress), typeof(double), typeof(ImageViewer),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty ImageLoadStatusTextProperty =
            DependencyProperty.Register(nameof(ImageLoadStatusText), typeof(string), typeof(ImageViewer),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CanRetryImageLoadProperty =
            DependencyProperty.Register(nameof(CanRetryImageLoad), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ShowPixelGridProperty =
            DependencyProperty.Register(nameof(ShowPixelGrid), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnShowPixelGridChanged));

        public static readonly DependencyProperty ShowCrosshairProperty =
            DependencyProperty.Register(nameof(ShowCrosshair), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnShowCrosshairChanged));

        public static readonly DependencyProperty ShowInfoPanelProperty =
            DependencyProperty.Register(nameof(ShowInfoPanel), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnShowInfoPanelChanged));

        public static readonly DependencyProperty ShowCaliperScoresProperty =
            DependencyProperty.Register(nameof(ShowCaliperScores), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(true, OnShowCaliperScoresChanged));

        public static readonly DependencyProperty ShowHistogramProperty =
            DependencyProperty.Register(nameof(ShowHistogram), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnShowHistogramChanged));

        public static readonly DependencyProperty ShowProfileProperty =
            DependencyProperty.Register(nameof(ShowProfile), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnShowProfileChanged));

        public static readonly DependencyProperty ShowScaleBarProperty =
            DependencyProperty.Register(nameof(ShowScaleBar), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnShowScaleBarChanged));

        public static readonly DependencyProperty ShowRoiListProperty =
            DependencyProperty.Register(nameof(ShowRoiList), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnShowRoiListChanged));

        public static readonly DependencyProperty ShowSnapGridProperty =
            DependencyProperty.Register(nameof(ShowSnapGrid), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnShowSnapGridChanged));

        public static readonly DependencyProperty EnableSnapToGridProperty =
            DependencyProperty.Register(nameof(EnableSnapToGrid), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false));

        public static readonly DependencyProperty GridSpacingProperty =
            DependencyProperty.Register(nameof(GridSpacing), typeof(double), typeof(ImageViewer),
                new PropertyMetadata(10.0, OnShowSnapGridChanged));

        public static readonly DependencyProperty EnableGpuRenderingProperty =
            DependencyProperty.Register(nameof(EnableGpuRendering), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, OnEnableGpuRenderingChanged));

        public static readonly DependencyProperty PseudoColorPaletteProperty =
            DependencyProperty.Register(nameof(PseudoColorPalette), typeof(PseudoColorPalette), typeof(ImageViewer),
                new PropertyMetadata(PseudoColorPalette.None, OnPseudoColorPaletteChanged));

        public static readonly DependencyProperty ScaleProperty =
            DependencyProperty.Register(nameof(Scale), typeof(double), typeof(ImageViewer),
                new PropertyMetadata(1.0, OnScaleChanged));

        public static readonly DependencyProperty PixelSizeProperty =
            DependencyProperty.Register(nameof(PixelSize), typeof(double), typeof(ImageViewer),
                new PropertyMetadata(1.0, OnCalibrationChanged));

        public static readonly DependencyProperty PhysicalUnitProperty =
            DependencyProperty.Register(nameof(PhysicalUnit), typeof(string), typeof(ImageViewer),
                new PropertyMetadata("px", OnCalibrationChanged));

        public static readonly DependencyProperty MenuStateProperty =
            DependencyProperty.Register(nameof(MenuState), typeof(object), typeof(ImageViewer),
                new PropertyMetadata(ImageViewerMenuStateSnapshot.Empty));

        public ImageSource? ImageSource
        {
            get => (ImageSource?)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        public bool IsImageLoading
        {
            get => (bool)GetValue(IsImageLoadingProperty);
            set => SetValue(IsImageLoadingProperty, value);
        }

        public double ImageLoadProgress
        {
            get => (double)GetValue(ImageLoadProgressProperty);
            set => SetValue(ImageLoadProgressProperty, value);
        }

        public string ImageLoadStatusText
        {
            get => (string)GetValue(ImageLoadStatusTextProperty);
            set => SetValue(ImageLoadStatusTextProperty, value);
        }

        public bool CanRetryImageLoad
        {
            get => (bool)GetValue(CanRetryImageLoadProperty);
            set => SetValue(CanRetryImageLoadProperty, value);
        }

        public bool ShowPixelGrid
        {
            get => (bool)GetValue(ShowPixelGridProperty);
            set => SetValue(ShowPixelGridProperty, value);
        }

        public bool ShowCrosshair
        {
            get => (bool)GetValue(ShowCrosshairProperty);
            set => SetValue(ShowCrosshairProperty, value);
        }

        public bool ShowInfoPanel
        {
            get => (bool)GetValue(ShowInfoPanelProperty);
            set => SetValue(ShowInfoPanelProperty, value);
        }

        public bool ShowCaliperScores
        {
            get => (bool)GetValue(ShowCaliperScoresProperty);
            set => SetValue(ShowCaliperScoresProperty, value);
        }

        public bool ShowHistogram
        {
            get => (bool)GetValue(ShowHistogramProperty);
            set => SetValue(ShowHistogramProperty, value);
        }

        public bool ShowProfile
        {
            get => (bool)GetValue(ShowProfileProperty);
            set => SetValue(ShowProfileProperty, value);
        }

        public bool ShowScaleBar
        {
            get => (bool)GetValue(ShowScaleBarProperty);
            set => SetValue(ShowScaleBarProperty, value);
        }

        public bool ShowRoiList
        {
            get => (bool)GetValue(ShowRoiListProperty);
            set => SetValue(ShowRoiListProperty, value);
        }

        public bool ShowSnapGrid
        {
            get => (bool)GetValue(ShowSnapGridProperty);
            set => SetValue(ShowSnapGridProperty, value);
        }

        public bool EnableSnapToGrid
        {
            get => (bool)GetValue(EnableSnapToGridProperty);
            set => SetValue(EnableSnapToGridProperty, value);
        }

        public double GridSpacing
        {
            get => (double)GetValue(GridSpacingProperty);
            set => SetValue(GridSpacingProperty, value);
        }

        public bool EnableGpuRendering
        {
            get => (bool)GetValue(EnableGpuRenderingProperty);
            set => SetValue(EnableGpuRenderingProperty, value);
        }

        public PseudoColorPalette PseudoColorPalette
        {
            get => (PseudoColorPalette)GetValue(PseudoColorPaletteProperty);
            set => SetValue(PseudoColorPaletteProperty, value);
        }

        public double Scale
        {
            get => (double)GetValue(ScaleProperty);
            set => SetValue(ScaleProperty, value);
        }

        public double PixelSize
        {
            get => (double)GetValue(PixelSizeProperty);
            set => SetValue(PixelSizeProperty, value);
        }

        public string PhysicalUnit
        {
            get => (string)GetValue(PhysicalUnitProperty);
            set => SetValue(PhysicalUnitProperty, value);
        }

        public object MenuState
        {
            get => GetValue(MenuStateProperty);
            private set => SetValue(MenuStateProperty, value);
        }

        private static void WithViewer(DependencyObject dependencyObject, Action<ImageViewer> callback)
        {
            if (dependencyObject is ImageViewer viewer)
            {
                callback(viewer);
            }
        }

        private static void WithViewer<TValue>(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e, Action<ImageViewer, TValue> callback)
        {
            if (dependencyObject is ImageViewer viewer && e.NewValue is TValue value)
            {
                callback(viewer, value);
            }
        }

        private static void OnShowRoiListChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer<bool>(d, e, (viewer, value) => viewer._imageViewStateController.HandleShowRoiListChanged(value));
        }

        private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer(d, viewer => viewer._imageSourceController.HandleImageSourceChanged(e.NewValue as ImageSource));
        }

        private static void OnCalibrationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer(d, viewer => viewer._imageViewStateController.HandleCalibrationChanged());
        }

        private static void OnShowPixelGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer(d, viewer => viewer._imageViewStateController.HandleShowPixelGridChanged());
        }

        private static void OnShowSnapGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer(d, viewer => viewer._imageViewStateController.HandleShowSnapGridChanged());
        }

        private static void OnShowCrosshairChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer<bool>(d, e, (viewer, value) => viewer._imageViewStateController.HandleShowCrosshairChanged(value));
        }

        private static void OnShowInfoPanelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer<bool>(d, e, (viewer, value) => viewer._imageViewStateController.HandleShowInfoPanelChanged(value));
        }

        private static void OnShowCaliperScoresChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer(d, viewer => viewer.DrawRois());
        }

        private static void OnShowScaleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer<bool>(d, e, (viewer, value) => viewer._imageViewStateController.HandleShowScaleBarChanged(value));
        }

        private static void OnScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            WithViewer<double>(d, e, (viewer, value) => viewer._imageViewStateController.HandleScaleChanged(value));
        }
    }
}