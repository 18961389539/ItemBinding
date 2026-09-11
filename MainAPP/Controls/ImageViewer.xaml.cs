using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MainAPP.Services;
using Microsoft.Win32;
using OpenCvSharp.WpfExtensions;

// VSTHRD001: 使用 Dispatcher.InvokeAsync 切换到 UI 线程是 WPF 标准模式，无需 JoinableTaskFactory
#pragma warning disable VSTHRD001

namespace MainAPP.Controls
{
    public partial class ImageViewer : UserControl, IDisposable
    {
        #region 常量和字段

        private const double MinScale = 0.01, MaxScale = 50.0, PixelGridThreshold = 8.0;
        private const double ZoomFactor = 1.25, WheelZoomFactor = 1.1, FitViewMargin = 0.95;
        private const double MinRoiSize = 2.0, MinEllipseRadius = 2.0, MinResizeSize = 10.0, MinVisibleRatio = 0.5;
        private const double HandleSize = 12.0, RotateHandleOffset = 25.0, ClosePolygonDistance = 10.0;
        private const int MaxPixelGridCount = 10000, PanStep = 20;
        private const double MinVisibleBase = 50.0, DegToRad = Math.PI / 180.0, RadToDeg = 180.0 / Math.PI;

        // 缓存的画刷 - 避免重复创建
        private static readonly Brush s_gridBrush = CreateFrozenBrush(Color.FromArgb(80, 128, 128, 128));
        private static readonly Brush s_rectRoiFill = CreateFrozenBrush(Color.FromArgb(32, 0, 255, 0));
        private static readonly Brush s_ellipseRoiFill = CreateFrozenBrush(Color.FromArgb(32, 0, 0, 255));
        private static readonly Brush s_polygonFill = CreateFrozenBrush(Color.FromArgb(32, 255, 0, 255));
        private static readonly Brush s_polygonSelectedFill = CreateFrozenBrush(Color.FromArgb(48, 0, 255, 255));
        private static readonly Brush s_angleFill = CreateFrozenBrush(Color.FromArgb(64, 255, 165, 0));
        private static readonly DoubleCollection s_dashArray = CreateFrozenDashArray([4, 2]);
        private static readonly DoubleCollection s_smallDashArray = CreateFrozenDashArray([2, 2]);

        private double _currentScale = 1.0;
        private Point _lastMousePosition, _roiDragStart;
        // L272: _isDisposed 改为 volatile，确保跨线程可见性
        private volatile bool _isDisposed;
        private bool _isPanning, _hasImageDisplayed;
        private bool _isMeasuring, _isAngleMeasuring;
        private bool _isDraggingMeasurePoint, _isDraggingAnglePoint;


        // 视频流性能优化字段
        private bool _isVideoStreamMode;
        private long _lastFrameUpdateTicks;
        private const long FrameIntervalTicks = 166667; // ~16.6ms (60fps) 使用 Stopwatch ticks
        private int _selectedDistanceMeasurementIndex = -1, _selectedAngleMeasurementIndex = -1;
        private int _draggingPointIndex = -1; // 0: P1, 1: P2 for distance; 0: P1, 1: Vertex, 2: P2 for angle
        private bool _isRoiMode, _isDrawingRoi, _isDraggingRoi, _isResizingRoi, _isRotatingRoi;
        private bool _isEllipseRoiMode, _isDrawingEllipseRoi, _isDraggingEllipseRoi, _isResizingEllipseRoi, _isRotatingEllipseRoi;
        private bool _isPolygonRoiMode, _isDrawingPolygonRoi, _isDraggingPolygonRoi, _isDraggingPolygonVertex;
        private int _selectedRectRoiIndex = -1, _selectedEllipseRoiIndex = -1, _selectedPolygonRoiIndex = -1;
        private int _draggingVertexIndex = -1;
        private int _resizeHandleIndex = -1; // 0-3: corners, 4-7: edges
        private double _initialRotationAngle;

        private WriteableBitmap? _currentWriteableBitmap;
        private BitmapSource? _cachedBitmapSource;
        private byte[]? _pixelCache;
        private int _pixelCacheStride, _pixelCacheChannels, _pixelCacheWidth, _pixelCacheHeight;
        // M222: 像素缓存更新的取消令牌，新一次更新时取消上一次未完成的任务，避免并发覆盖
        private CancellationTokenSource? _pixelCacheCts;

        private readonly object _imageLock = new();

        private Point? _measurePoint1, _measurePoint2;
        private RotatedRect? _currentRoi, _roiBeforeEdit;
        private EllipseRoi? _currentEllipseRoi, _ellipseRoiBeforeEdit;
        private PolygonRoi? _polygonRoiBeforeEdit;

        private readonly List<(Point P1, Point P2)> _distanceMeasurements = [];
        private readonly List<(Point P1, Point Vertex, Point P2)> _angleMeasurements = [];
        private readonly List<Point> _anglePoints = [], _polygonPoints = [];
        private readonly List<RotatedRect> _rectRois = [];
        private readonly List<EllipseRoi> _ellipseRois = [];
        private readonly List<PolygonRoi> _polygonRois = [];

        private double _pixelsPerUnit = 1.0;
        private string _physicalUnit = "px";

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static DoubleCollection CreateFrozenDashArray(double[] values)
        {
            var collection = new DoubleCollection(values);
            collection.Freeze();
            return collection;
        }

        #endregion

        #region 构造函数

        public ImageViewer()
        {
            InitializeComponent();
            // L255: 移除未使用的 _syncContext 字段
            InitializeEventHandlers();
            InitializeContextMenu();
        }

        private void InitializeEventHandlers()
        {
            rootGrid.MouseWheel += OnMouseWheel;
            rootGrid.MouseDown += OnMouseDown;
            rootGrid.MouseUp += OnMouseUp;
            rootGrid.MouseMove += OnMouseMove;
            rootGrid.MouseLeave += OnMouseLeave;
            rootGrid.SizeChanged += OnSizeChanged;
            Focusable = true;
            KeyDown += OnKeyDown;
            AllowDrop = true;
            Drop += OnDrop;
            DragOver += OnDragOver;
        }

        private void InitializeContextMenu()
        {
            MenuItem CreateItem(string header, string gesture, Action action)
            {
                var item = new MenuItem { Header = header, InputGestureText = gesture };
                item.Click += (_, _) => action();
                return item;
            }

            var roiMenu = new MenuItem { Header = "ROI 选择" };
            roiMenu.Items.Add(CreateItem("矩形 ROI (O)", "O", ToggleRoiMode));
            roiMenu.Items.Add(CreateItem("椭圆 ROI (E)", "E", ToggleEllipseRoiMode));
            roiMenu.Items.Add(CreateItem("多边形 ROI (P)", "P", TogglePolygonRoiMode));
            roiMenu.Items.Add(new Separator());
            roiMenu.Items.Add(CreateItem("清除所有 ROI (Del)", "Del", ClearAllRoi));

            var contextMenu = new ContextMenu();
            foreach (var item in new object[] {
                CreateItem("重置视图 (R)", "R", ResetView),
                CreateItem("适应窗口 (F)", "F", FitToView),
                CreateItem("实际大小 (1)", "1", () => SetScaleAnimated(1.0)),
                new Separator(),
                CreateItem("复制图像 (Ctrl+C)", "Ctrl+C", CopyImageToClipboard),
                CreateItem("保存图像 (Ctrl+S)", "Ctrl+S", SaveImageToFile),
                new Separator(),
                CreateItem("放大 (+)", "+", ZoomIn),
                CreateItem("缩小 (-)", "-", ZoomOut),
                new Separator(),
                CreateItem("距离测量 (M)", "M", ToggleMeasureMode),
                CreateItem("角度测量 (A)", "A", ToggleAngleMeasureMode),
                roiMenu,
                CreateItem("图像属性 (I)", "I", ToggleInfoPanel),
                CreateItem("十字准线 (X)", "X", () => ShowCrosshair = !ShowCrosshair)
            }) contextMenu.Items.Add(item);

            rootGrid.ContextMenu = contextMenu;
        }

        #endregion

        #region 依赖属性

        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(ImageViewer),
                new PropertyMetadata(null, OnImageSourceChanged));

        public ImageSource ImageSource
        {
            get => (ImageSource)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        public static readonly DependencyProperty ShowCrosshairProperty =
            DependencyProperty.Register(nameof(ShowCrosshair), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false));

        public bool ShowCrosshair
        {
            get => (bool)GetValue(ShowCrosshairProperty);
            set => SetValue(ShowCrosshairProperty, value);
        }

        public static readonly DependencyProperty ShowPixelGridProperty =
            DependencyProperty.Register(nameof(ShowPixelGrid), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(true, (d, _) => (d as ImageViewer)?.UpdatePixelGrid()));

        public bool ShowPixelGrid
        {
            get => (bool)GetValue(ShowPixelGridProperty);
            set => SetValue(ShowPixelGridProperty, value);
        }

        public static readonly DependencyProperty ShowInfoPanelProperty =
            DependencyProperty.Register(nameof(ShowInfoPanel), typeof(bool), typeof(ImageViewer),
                new PropertyMetadata(false, (d, e) =>
                {
                    if (d is ImageViewer v) { v.infoPanel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed; if ((bool)e.NewValue) v.UpdateInfoPanel(); }
                }));

        public bool ShowInfoPanel
        {
            get => (bool)GetValue(ShowInfoPanelProperty);
            set => SetValue(ShowInfoPanelProperty, value);
        }

        #endregion

        #region ImageSource 处理

        private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => (d as ImageViewer)?.OnImageSourceChanged(e);

        private void OnImageSourceChanged(DependencyPropertyChangedEventArgs e)
        {
            if (_isDisposed) return;
            // 1. 尝试安全地移除旧事件处理程序
            if (e.OldValue is WriteableBitmap old)
            {
                // 跨线程访问 IsFrozen 可能抛出异常
                if (!old.IsFrozen)
                {
                    old.Changed -= OnWriteableBitmapChanged;
                }
            }
            if (e.NewValue is WriteableBitmap wb)
            {
                _currentWriteableBitmap = wb;
                try
                {
                    if (!wb.IsFrozen)
                    {
                        wb.Changed += OnWriteableBitmapChanged;
                    }
                }
                // L26: 记录注册图像事件失败的异常，便于排查显示异常
                catch (Exception ex) { LogService.Instance.Warning($"注册图像事件失败: {ex}"); }
                UpdateImage(wb, !_hasImageDisplayed);
            }
            else if (e.NewValue is BitmapSource bs)
            {
                _currentWriteableBitmap = null;
                UpdateImage(bs, !_hasImageDisplayed);
            }
            else
            {
                _currentWriteableBitmap = null;
                ClearImage();
            }
        }

        private void OnWriteableBitmapChanged(object? sender, EventArgs e)
        {
            // L268: 已释放时直接返回，避免在 Dispose 后处理回调
            if (_isDisposed) return;
            if (sender is not WriteableBitmap wb || wb != _currentWriteableBitmap) return;

            // 视频流模式下进行帧率限制，避免过于频繁的更新
            if (_isVideoStreamMode)
            {
                long currentTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                if (currentTicks - _lastFrameUpdateTicks < FrameIntervalTicks)
                    return;
                _lastFrameUpdateTicks = currentTicks;

                // 视频流模式：只更新图像显示，跳过像素缓存
                // M238: InvokeAsync 本身可能因 Dispatcher 关闭抛异常，外层 try-catch 保护
                try
                {
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (_isDisposed) return;

                            // 检查尺寸是否变化
                            bool sizeChanged = _cachedBitmapSource == null ||
                                               _cachedBitmapSource.PixelWidth != wb.PixelWidth ||
                                               _cachedBitmapSource.PixelHeight != wb.PixelHeight;

                            _cachedBitmapSource = wb;
                            image.Source = wb;

                            if (sizeChanged)
                            {
                                overlayCanvas.Width = wb.PixelWidth;
                                overlayCanvas.Height = wb.PixelHeight;
                            }

                            if (!_hasImageDisplayed)
                            {
                                _hasImageDisplayed = true;
                                FitToView();
                            }
                        }
                        // L26: 记录渲染时失败的异常，便于排查显示异常
                        catch (Exception ex) { LogService.Instance.Warning($"渲染图像失败: {ex}"); }
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }
                catch (Exception ex) { LogService.Instance.Warning($"Dispatcher.InvokeAsync 派发渲染任务失败: {ex}"); }
            }
            else
            {
                // M238: InvokeAsync 本身可能因 Dispatcher 关闭抛异常，外层 try-catch 保护
                try
                {
                    _ = Dispatcher.InvokeAsync(() => UpdateImage(wb, false));
                }
                catch (Exception ex) { LogService.Instance.Warning($"Dispatcher.InvokeAsync 派发更新图像任务失败: {ex}"); }
            }
        }

        private void UpdateImage(BitmapSource? source, bool resetView)
        {
            if (source == null) { ClearImage(); return; }
            if (_isDisposed) return;

            try
            {
                // 确保尺寸有效
                if (source.PixelWidth <= 0 || source.PixelHeight <= 0) return;

                _cachedBitmapSource = source;
                image.Source = source;

                // 设置 overlayCanvas 尺寸以匹配图像
                overlayCanvas.Width = source.PixelWidth;
                overlayCanvas.Height = source.PixelHeight;

                // 视频流模式下跳过像素缓存更新
                if (!_isVideoStreamMode)
                    UpdatePixelCacheAsync(source);

                UpdateScaleDisplay();

                // 只有在有尺寸时才尝试 FitToView，否则延迟执行
                if (resetView || !_hasImageDisplayed)
                {
                    _hasImageDisplayed = true;
                    if (rootGrid.ActualWidth > 0 && rootGrid.ActualHeight > 0)
                        FitToView();
                    else
                        _ = Dispatcher.InvokeAsync(FitToView, System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else
                {
                    _hasImageDisplayed = true;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"ImageViewer UpdateImage 异常: {ex}");
            }
        }

        // VSTHRD100: 该方法由同步代码调用（UpdateImage/IsVideoStreamMode setter），无法改为 async Task；
        // 整个方法体已包 try-catch，异常不会导致进程崩溃
#pragma warning disable VSTHRD100
        private async void UpdatePixelCacheAsync(BitmapSource source)
        {
            // M26: async void 非 EventHandler，需顶层 try-catch 防止异常直接终止进程
            try
            {
                // M222: 取消上一次未完成的像素缓存更新，避免并发覆盖
                _pixelCacheCts?.Cancel();
                var cts = new CancellationTokenSource();
                _pixelCacheCts = cts;
                try
                {
                    await UpdatePixelCacheCoreAsync(source, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // 取消是预期行为，不记录日志
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"UpdatePixelCacheAsync 异常: {ex}");
                }
                finally
                {
                    if (ReferenceEquals(_pixelCacheCts, cts))
                    {
                        _pixelCacheCts = null;
                    }
                    cts.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"UpdatePixelCacheAsync 顶层异常: {ex}");
            }
        }
#pragma warning restore VSTHRD100

        private async Task UpdatePixelCacheCoreAsync(BitmapSource source, CancellationToken cancellationToken)
        {
            if (source == null || _isVideoStreamMode) return;

            byte[]? pixelData = null;
            int channels = 0, stride = 0, width = 0, height = 0;

            // 尝试直接从 BitmapSource 获取像素数据
            width = source.PixelWidth;
            height = source.PixelHeight;

            // 确定通道数
            channels = source.Format.BitsPerPixel / 8;
            if (channels == 0) channels = 1;

            stride = width * channels;
            // 确保 stride 是 4 的倍数（某些格式需要）
            int padding = stride % 4;
            if (padding != 0) stride += (4 - padding);

            int dataSize = height * stride;
            if (dataSize <= 0) return;

            pixelData = new byte[dataSize];

            // WriteableBitmap 必须在 UI 线程上访问
            if (source is WriteableBitmap wb)
            {
                wb.CopyPixels(pixelData, stride, 0);
            }
            else
            {
                // 非 WriteableBitmap 可以冻结后在后台线程处理
                if (!source.IsFrozen)
                {
                    source.Freeze();
                }

                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        source.CopyPixels(pixelData, stride, 0);
                    }
                    // L25: 记录 CopyPixels 失败原因，便于排查测量功能静默失效
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning($"CopyPixels 失败: {ex}");
                    }

                }, cancellationToken).ConfigureAwait(false);
            }

            // M222: 后台任务完成后若已被取消，则不更新缓存
            cancellationToken.ThrowIfCancellationRequested();

            if (pixelData == null) return;

            // 在锁内更新缓存数据
            lock (_imageLock)
            {
                _pixelCache = pixelData;
                _pixelCacheChannels = channels;
                _pixelCacheStride = stride;
                _pixelCacheWidth = width;
                _pixelCacheHeight = height;
            }
        }

        private void ClearImage()
        {
            image.Source = null; _cachedBitmapSource = null; _hasImageDisplayed = false; _currentScale = 1.0;
            _pixelCache = null; _pixelCacheChannels = _pixelCacheWidth = _pixelCacheHeight = 0;
            scaleTransform.ScaleX = scaleTransform.ScaleY = 1.0;
            translateTransform.X = translateTransform.Y = 0;
            UpdateScaleDisplay(); coordTextBlock.Text = "";
            ClearMeasurement(); ClearAllRoi(); ClearAngleMeasurement();
            pixelGridCanvas.Children.Clear();
        }

        #endregion

        #region 鼠标事件处理

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_cachedBitmapSource == null || _isDisposed) return;
            if (!EnsureTransformReady()) return;
            var mousePos = e.GetPosition(rootGrid);
            double factor = e.Delta > 0 ? WheelZoomFactor : 1.0 / WheelZoomFactor;
            double newScale = Math.Clamp(_currentScale * factor, MinScale, MaxScale);
            if (Math.Abs(newScale - _currentScale) >= double.Epsilon)
            {
                ZoomToPoint(newScale, mousePos);
                e.Handled = true;
            }
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_cachedBitmapSource == null || _isDisposed) return;
            if (!EnsureTransformReady()) return;

            var pos = e.GetPosition(rootGrid);

            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                FitToView();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                if (_isRoiMode) { HandleRoiMouseDown(pos); e.Handled = true; return; }
                if (_isEllipseRoiMode) { HandleEllipseRoiMouseDown(pos); e.Handled = true; return; }
                if (_isPolygonRoiMode) { HandlePolygonRoiMouseDown(pos); e.Handled = true; return; }
                if (_isAngleMeasuring) { HandleAngleMeasureClick(pos); e.Handled = true; return; }
                if (_isMeasuring) { HandleMeasureClick(pos); e.Handled = true; return; }
            }

            if (e.ChangedButton is MouseButton.Middle or MouseButton.Left)
            {
                _isPanning = true;
                _lastMousePosition = pos;
                rootGrid.CaptureMouse();
                Cursor = Cursors.Hand;
            }

            Focus();
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (_isRoiMode) { HandleRoiMouseUp(); e.Handled = true; return; }
                if (_isEllipseRoiMode) { HandleEllipseRoiMouseUp(); e.Handled = true; return; }
                if (_isPolygonRoiMode && (_isDraggingPolygonRoi || _isDraggingPolygonVertex)) { HandlePolygonRoiMouseUp(); e.Handled = true; return; }
                if (_isMeasuring && _isDraggingMeasurePoint) { HandleMeasureMouseUp(); e.Handled = true; return; }
                if (_isAngleMeasuring && _isDraggingAnglePoint) { HandleAngleMeasureMouseUp(); e.Handled = true; return; }
            }
            if (_isPanning && e.ChangedButton is MouseButton.Middle or MouseButton.Left)
            { _isPanning = false; rootGrid.ReleaseMouseCapture(); Cursor = Cursors.Arrow; e.Handled = true; }
        }


        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_cachedBitmapSource == null || _isDisposed)
            {
                coordTextBlock.Text = "";
                return;
            }
            if (!EnsureTransformReady()) return;
            var mousePos = e.GetPosition(rootGrid);

            // 视频流模式下只处理平移，跳过其他昂贵的更新
            if (_isVideoStreamMode)
            {
                if (_isPanning)
                {
                    var delta = mousePos - _lastMousePosition;
                    translateTransform.X += delta.X;
                    translateTransform.Y += delta.Y;
                    _lastMousePosition = mousePos;
                    ClampTranslation();
                }
                return;
            }

            if (_isRoiMode && (_isDrawingRoi || _isDraggingRoi || _isResizingRoi || _isRotatingRoi || _currentRoi.HasValue))
                HandleRoiMouseMove(mousePos);
            if (_isEllipseRoiMode && (_isDrawingEllipseRoi || _isDraggingEllipseRoi || _isResizingEllipseRoi || _isRotatingEllipseRoi))
                HandleEllipseRoiMouseMove(mousePos);
            if (_isPolygonRoiMode && (_isDrawingPolygonRoi || _isDraggingPolygonRoi || _isDraggingPolygonVertex))
                HandlePolygonRoiMouseMove(mousePos);
            if (_isMeasuring && _isDraggingMeasurePoint)
                HandleMeasureMouseMove(mousePos);
            if (_isAngleMeasuring)
            {
                if (_isDraggingAnglePoint)
                    HandleAngleMeasureMouseMoveForDrag(mousePos);
                else
                    HandleAngleMeasureMove(mousePos);
            }

            if (_isPanning)
            {
                var delta = mousePos - _lastMousePosition;
                translateTransform.X += delta.X;
                translateTransform.Y += delta.Y;
                _lastMousePosition = mousePos;
                ClampTranslation();
            }

            UpdateCoordinateDisplay(mousePos);
            UpdateCrosshair(mousePos);
            UpdatePixelGrid();

            if (_isMeasuring && _measurePoint1.HasValue && !_measurePoint2.HasValue)
                UpdateMeasureLinePreview(mousePos);
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (_isPanning) return;
            coordTextBlock.Text = "";
            crosshairH.Visibility = crosshairV.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region 键盘事件处理

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_cachedBitmapSource == null) return;
            e.Handled = true;
            switch (e.Key)
            {
                case Key.R: ResetView(); break;
                case Key.F: FitToView(); break;
                case Key.D1 or Key.NumPad1: SetScaleAnimated(1.0); break;
                case Key.D2 or Key.NumPad2: SetScaleAnimated(2.0); break;
                case Key.Add or Key.OemPlus: ZoomIn(); break;
                case Key.Subtract or Key.OemMinus: ZoomOut(); break;
                case Key.C when Keyboard.Modifiers == ModifierKeys.Control: CopyImageToClipboard(); break;
                case Key.S when Keyboard.Modifiers == ModifierKeys.Control: SaveImageToFile(); break;
                case Key.Left: Pan(-PanStep, 0); break;
                case Key.Right: Pan(PanStep, 0); break;
                case Key.Up: Pan(0, -PanStep); break;
                case Key.Down: Pan(0, PanStep); break;
                case Key.M: ToggleMeasureMode(); break;
                case Key.A: ToggleAngleMeasureMode(); break;
                case Key.I: ToggleInfoPanel(); break;
                case Key.X: ShowCrosshair = !ShowCrosshair; break;
                case Key.O: ToggleRoiMode(); break;
                case Key.E: ToggleEllipseRoiMode(); break;
                case Key.P: TogglePolygonRoiMode(); break;
                case Key.Delete: DeleteSelectedOrClearAll(); break;
                case Key.Escape: ExitAllModes(); break;
                case Key.Enter when _isDrawingPolygonRoi: FinishPolygonRoi(); break;
                default: e.Handled = false; break;
            }
        }

        #endregion

        #region 拖放支持

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && IsImageFile(files[0]))
                LoadImageFromFile(files[0]);
        }

        private static bool IsImageFile(string filePath) =>
            System.IO.Path.GetExtension(filePath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tiff" or ".tif";

        #endregion

        #region 缩放和平移

        private void ZoomToPoint(double newScale, Point centerPoint)
        {
            var imagePos = GetImagePositionFromMouse(centerPoint);
            _currentScale = newScale;
            scaleTransform.ScaleX = scaleTransform.ScaleY = _currentScale;
            translateTransform.X = centerPoint.X - imagePos.X * _currentScale;
            translateTransform.Y = centerPoint.Y - imagePos.Y * _currentScale;
            ClampTranslation(); UpdateScaleDisplay(); UpdatePixelGrid(); UpdateOverlayScale();
        }

        private bool EnsureTransformReady()
        {
            if (_cachedBitmapSource == null || _isDisposed) return false;

            bool invalidScale = _currentScale <= 0 || double.IsNaN(_currentScale) || double.IsInfinity(_currentScale);
            bool invalidTransform =
                double.IsNaN(scaleTransform.ScaleX) || double.IsInfinity(scaleTransform.ScaleX) || scaleTransform.ScaleX <= 0 ||
                double.IsNaN(scaleTransform.ScaleY) || double.IsInfinity(scaleTransform.ScaleY) || scaleTransform.ScaleY <= 0 ||
                double.IsNaN(translateTransform.X) || double.IsInfinity(translateTransform.X) ||
                double.IsNaN(translateTransform.Y) || double.IsInfinity(translateTransform.Y);

            if (invalidScale || invalidTransform)
            {
                _currentScale = 1.0;
                scaleTransform.ScaleX = scaleTransform.ScaleY = 1.0;
                translateTransform.X = translateTransform.Y = 0;
                FitToView();
                return false;
            }

            return true;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_cachedBitmapSource == null) return;
            if (!_hasImageDisplayed) FitToView();
            else { ClampTranslation(); UpdatePixelGrid(); UpdateOverlayScale(); }
        }

        private void FitToView()
        {
            if (_cachedBitmapSource == null || _isDisposed) return;
            if (rootGrid.ActualWidth <= 0 || rootGrid.ActualHeight <= 0) return;
            if (_cachedBitmapSource.PixelWidth <= 0 || _cachedBitmapSource.PixelHeight <= 0) return;

            double scaleX = rootGrid.ActualWidth / _cachedBitmapSource.PixelWidth;
            double scaleY = rootGrid.ActualHeight / _cachedBitmapSource.PixelHeight;
            double targetScale = Math.Min(scaleX, scaleY) * FitViewMargin;

            if (double.IsNaN(targetScale) || double.IsInfinity(targetScale)) targetScale = 1.0;

            _currentScale = Math.Clamp(targetScale, MinScale, MaxScale);

            double scaledWidth = _cachedBitmapSource.PixelWidth * _currentScale;
            double scaledHeight = _cachedBitmapSource.PixelHeight * _currentScale;

            scaleTransform.ScaleX = scaleTransform.ScaleY = _currentScale;
            translateTransform.X = (rootGrid.ActualWidth - scaledWidth) / 2;
            translateTransform.Y = (rootGrid.ActualHeight - scaledHeight) / 2;

            UpdateScaleDisplay();
            UpdatePixelGrid();
            UpdateOverlayScale();
        }

        private void ClampTranslation()
        {
            if (_cachedBitmapSource == null || _isDisposed) return;
            if (rootGrid.ActualWidth <= 0 || rootGrid.ActualHeight <= 0) return;

            double scaledWidth = _cachedBitmapSource.PixelWidth * _currentScale;
            double scaledHeight = _cachedBitmapSource.PixelHeight * _currentScale;
            double viewWidth = rootGrid.ActualWidth, viewHeight = rootGrid.ActualHeight;
            double minVisibleSize = Math.Min(MinVisibleBase, Math.Min(scaledWidth, scaledHeight) * MinVisibleRatio);

            double minX = viewWidth - scaledWidth - minVisibleSize, maxX = minVisibleSize;
            double minY = viewHeight - scaledHeight - minVisibleSize, maxY = minVisibleSize;

            if (minX > maxX)
            {
                double c = (viewWidth - scaledWidth) / 2;
                minX = Math.Min(0, c);
                maxX = Math.Max(viewWidth - scaledWidth, c);
            }
            if (minY > maxY)
            {
                double c = (viewHeight - scaledHeight) / 2;
                minY = Math.Min(0, c);
                maxY = Math.Max(viewHeight - scaledHeight, c);
            }

            translateTransform.X = Math.Clamp(translateTransform.X, minX, maxX);
            translateTransform.Y = Math.Clamp(translateTransform.Y, minY, maxY);
        }

        private Point GetImagePositionFromMouse(Point mousePos) =>
            new((mousePos.X - translateTransform.X) / _currentScale, (mousePos.Y - translateTransform.Y) / _currentScale);

        #endregion

        #region 坐标显示

        private void UpdateCoordinateDisplay(Point mousePos)
        {
            if (_cachedBitmapSource == null) { coordTextBlock.Text = ""; return; }
            var imagePos = GetImagePositionFromMouse(mousePos);
            int x = (int)Math.Floor(imagePos.X), y = (int)Math.Floor(imagePos.Y);
            coordTextBlock.Text = (x < 0 || x >= _cachedBitmapSource.PixelWidth || y < 0 || y >= _cachedBitmapSource.PixelHeight)
                ? $"缩放: {_currentScale:P0}" : $"{GetPixelInfo(x, y)} | {_currentScale:P0}";
        }

        private string GetPixelInfo(int x, int y)
        {
            string coordStr = $"({x},{y})";
            if (!IsValidPixelCoordinate(x, y)) return coordStr;

            lock (_imageLock)
            {
                if (_pixelCache == null || _pixelCacheStride <= 0 || _pixelCacheChannels <= 0)
                    return coordStr;

                int index = y * _pixelCacheStride + x * _pixelCacheChannels;
                if (index < 0 || index + _pixelCacheChannels > _pixelCache.Length)
                    return coordStr;

                return _pixelCacheChannels switch
                {
                    1 => $"{coordStr} Gray:{_pixelCache[index]}",
                    3 => $"{coordStr} B:{_pixelCache[index]} G:{_pixelCache[index + 1]} R:{_pixelCache[index + 2]}",
                    4 => $"{coordStr} B:{_pixelCache[index]} G:{_pixelCache[index + 1]} R:{_pixelCache[index + 2]} A:{_pixelCache[index + 3]}",
                    _ => coordStr
                };
            }
        }

        private bool IsValidPixelCoordinate(int x, int y) =>
            _cachedBitmapSource != null && x >= 0 && x < _cachedBitmapSource.PixelWidth && y >= 0 && y < _cachedBitmapSource.PixelHeight;

        private void UpdateScaleDisplay()
        {
            if (_cachedBitmapSource == null) coordTextBlock.Text = "";
            else if (string.IsNullOrEmpty(coordTextBlock.Text) || !coordTextBlock.Text.Contains('('))
                coordTextBlock.Text = $"缩放: {_currentScale:P0}";
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 启用或禁用视频流模式。启用后会完全跳过像素缓存、直方图计算和鼠标移动时的UI更新以最大化性能。
        /// </summary>
        public bool IsVideoStreamMode
        {
            get => _isVideoStreamMode;
            set
            {
                if (_isVideoStreamMode == value) return;
                _isVideoStreamMode = value;

                if (value)
                {
                    // 视频流模式下自动禁用高开销功能
                    ShowCrosshair = false;
                    ShowPixelGrid = false;

                    // 清除已有的覆盖层元素
                    pixelGridCanvas.Children.Clear();
                    crosshairH.Visibility = crosshairV.Visibility = Visibility.Collapsed;
                    coordTextBlock.Text = "";

                    // 重置时间戳
                    _lastFrameUpdateTicks = 0;
                }
                else
                {
                    // 退出视频流模式时，恢复像素缓存
                    if (_cachedBitmapSource != null)
                        UpdatePixelCacheAsync(_cachedBitmapSource);
                }
            }
        }

        public void ResetView() { _hasImageDisplayed = false; FitToView(); _hasImageDisplayed = true; }
        public void SetScaleAnimated(double scale) { if (_cachedBitmapSource != null) ZoomToPoint(Math.Clamp(scale, MinScale, MaxScale), new Point(rootGrid.ActualWidth / 2, rootGrid.ActualHeight / 2)); }
        public double GetScale() => _currentScale;
        public void ZoomIn() => ZoomToPoint(Math.Min(_currentScale * ZoomFactor, MaxScale), GetViewCenter());
        public void ZoomOut() => ZoomToPoint(Math.Max(_currentScale / ZoomFactor, MinScale), GetViewCenter());


        private Point GetViewCenter() => new(rootGrid.ActualWidth / 2, rootGrid.ActualHeight / 2);

        public void SetScale(double scale)
        {
            if (_cachedBitmapSource == null) return;
            _currentScale = Math.Clamp(scale, MinScale, MaxScale);
            scaleTransform.ScaleX = scaleTransform.ScaleY = _currentScale;
            ClampTranslation(); UpdateScaleDisplay(); UpdatePixelGrid();
        }

        public void Pan(double deltaX, double deltaY)
        {
            if (_cachedBitmapSource == null) return;
            translateTransform.X += deltaX; translateTransform.Y += deltaY; ClampTranslation();
        }

        // M189: 剪贴板操作加 try-catch，避免剪贴板被占用等异常导致崩溃
        public void CopyImageToClipboard()
        {
            if (_cachedBitmapSource == null) return;
            try
            {
                Clipboard.SetImage(_cachedBitmapSource);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"复制图像到剪贴板失败: {ex}");
            }
        }

        public void SaveImageToFile()
        {
            if (_cachedBitmapSource == null) return;
            var dialog = new SaveFileDialog { Filter = "PNG 图像|*.png|JPEG 图像|*.jpg|BMP 图像|*.bmp|所有文件|*.*", DefaultExt = ".png" };
            if (dialog.ShowDialog() == true) SaveImage(dialog.FileName);
        }

        private void SaveImage(string filePath)
        {
            if (_cachedBitmapSource == null) return;
            BitmapEncoder encoder = System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                ".bmp" => new BmpBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                ".tiff" or ".tif" => new TiffBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
            encoder.Frames.Add(BitmapFrame.Create(_cachedBitmapSource));
            using var stream = File.Create(filePath);
            encoder.Save(stream);
        }

        // M190: LoadImageFromFile 加 try-catch，记录加载失败原因
        public void LoadImageFromFile(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("图像文件不存在", filePath);
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.EndInit(); bitmap.Freeze();
                ImageSource = bitmap;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"从文件加载图像失败: {filePath}: {ex}");
                throw;
            }
        }

        public Point? GetImageCoordinate(Point mousePosition)
        {
            if (_cachedBitmapSource == null) return null;
            var imagePos = GetImagePositionFromMouse(mousePosition);
            int x = (int)Math.Floor(imagePos.X), y = (int)Math.Floor(imagePos.Y);
            return IsValidPixelCoordinate(x, y) ? new Point(x, y) : null;
        }

        #endregion

        #region 十字准线

        private void UpdateCrosshair(Point mousePos)
        {
            if (!ShowCrosshair || !GetImageCoordinate(mousePos).HasValue)
            { crosshairH.Visibility = crosshairV.Visibility = Visibility.Collapsed; return; }
            crosshairH.X1 = 0; crosshairH.X2 = rootGrid.ActualWidth; crosshairH.Y1 = crosshairH.Y2 = mousePos.Y;
            crosshairV.Y1 = 0; crosshairV.Y2 = rootGrid.ActualHeight; crosshairV.X1 = crosshairV.X2 = mousePos.X;
            crosshairH.Visibility = crosshairV.Visibility = Visibility.Visible;
        }

        #endregion


        #region 像素网格

        private void UpdatePixelGrid()
        {
            pixelGridCanvas.Children.Clear();
            // 视频流模式下跳过像素网格绘制以提升性能
            if (_isVideoStreamMode || !ShowPixelGrid || _cachedBitmapSource == null || _isDisposed || _currentScale < PixelGridThreshold)
                return;

            var (pixelStartX, pixelStartY, pixelEndX, pixelEndY) = GetVisiblePixelRange();
            if (pixelEndX < 0 || pixelEndY < 0) return;

            long pixelCount = (long)(pixelEndX - pixelStartX) * (pixelEndY - pixelStartY);
            if (pixelCount > MaxPixelGridCount || pixelCount < 0) return;

            double viewWidth = rootGrid.ActualWidth, viewHeight = rootGrid.ActualHeight;
            double yMin = Math.Max(0, translateTransform.Y + pixelStartY * _currentScale);
            double yMax = Math.Min(viewHeight, translateTransform.Y + pixelEndY * _currentScale);
            double xMin = Math.Max(0, translateTransform.X + pixelStartX * _currentScale);
            double xMax = Math.Min(viewWidth, translateTransform.X + pixelEndX * _currentScale);

            for (int x = pixelStartX; x <= pixelEndX; x++)
            {
                double screenX = translateTransform.X + x * _currentScale;
                if (screenX >= 0 && screenX <= viewWidth)
                    pixelGridCanvas.Children.Add(CreateGridLine(screenX, screenX, yMin, yMax, s_gridBrush));
            }
            for (int y = pixelStartY; y <= pixelEndY; y++)
            {
                double screenY = translateTransform.Y + y * _currentScale;
                if (screenY >= 0 && screenY <= viewHeight)
                    pixelGridCanvas.Children.Add(CreateGridLine(xMin, xMax, screenY, screenY, s_gridBrush));
            }
        }

        private static Line CreateGridLine(double x1, double x2, double y1, double y2, Brush stroke) =>
            new() { X1 = x1, X2 = x2, Y1 = y1, Y2 = y2, Stroke = stroke, StrokeThickness = 0.5, IsHitTestVisible = false };

        private (int startX, int startY, int endX, int endY) GetVisiblePixelRange()
        {
            if (_cachedBitmapSource == null || _currentScale <= 0)
                return (-1, -1, -1, -1);

            double viewWidth = rootGrid.ActualWidth, viewHeight = rootGrid.ActualHeight;
            int pixelStartX = (int)Math.Floor(Math.Max(0, -translateTransform.X / _currentScale));
            int pixelStartY = (int)Math.Floor(Math.Max(0, -translateTransform.Y / _currentScale));
            int pixelEndX = (int)Math.Ceiling(Math.Min(_cachedBitmapSource.PixelWidth, (viewWidth - translateTransform.X) / _currentScale));
            int pixelEndY = (int)Math.Ceiling(Math.Min(_cachedBitmapSource.PixelHeight, (viewHeight - translateTransform.Y) / _currentScale));
            return (pixelStartX, pixelStartY, pixelEndX, pixelEndY);
        }

        #endregion

        #region 距离测量

        public void ToggleMeasureMode()
        {
            ExitAllModes(); _isMeasuring = !_isMeasuring;
            Cursor = _isMeasuring ? Cursors.Cross : Cursors.Arrow;
            _measurePoint1 = _measurePoint2 = null;
            _selectedDistanceMeasurementIndex = -1;
        }

        private void HandleMeasureClick(Point screenPos)
        {
            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;

            double handleSize = GetScaledHandleSize();

            // 检查是否点击了已有测量的端点（用于拖动编辑）
            for (int i = 0; i < _distanceMeasurements.Count; i++)
            {
                var m = _distanceMeasurements[i];
                if (IsWithinHandleDistance(imageCoord.Value, m.P1, handleSize))
                {
                    _selectedDistanceMeasurementIndex = i;
                    _isDraggingMeasurePoint = true;
                    _draggingPointIndex = 0;
                    _roiDragStart = imageCoord.Value;
                    UpdateAllMeasurementsDisplay();
                    return;
                }
                if (IsWithinHandleDistance(imageCoord.Value, m.P2, handleSize))
                {
                    _selectedDistanceMeasurementIndex = i;
                    _isDraggingMeasurePoint = true;
                    _draggingPointIndex = 1;
                    _roiDragStart = imageCoord.Value;
                    UpdateAllMeasurementsDisplay();
                    return;
                }
            }

            // 检查是否点击了测量线（用于选中）
            for (int i = 0; i < _distanceMeasurements.Count; i++)
            {
                var m = _distanceMeasurements[i];
                if (IsPointNearLine(imageCoord.Value, m.P1, m.P2, handleSize))
                {
                    _selectedDistanceMeasurementIndex = i;
                    _measurePoint1 = _measurePoint2 = null;
                    UpdateAllMeasurementsDisplay();
                    return;
                }
            }

            // 否则进行正常的测量绘制
            _selectedDistanceMeasurementIndex = -1;
            if (!_measurePoint1.HasValue) _measurePoint1 = imageCoord.Value;
            else { _distanceMeasurements.Add((_measurePoint1.Value, imageCoord.Value)); _measurePoint1 = _measurePoint2 = null; }
            UpdateAllMeasurementsDisplay();
        }

        private void HandleMeasureMouseUp()
        {
            _isDraggingMeasurePoint = false;
            _draggingPointIndex = -1;
        }

        private void HandleMeasureMouseMove(Point screenPos)
        {
            if (!_isDraggingMeasurePoint || _selectedDistanceMeasurementIndex < 0) return;

            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;

            var m = _distanceMeasurements[_selectedDistanceMeasurementIndex];
            if (_draggingPointIndex == 0)
                _distanceMeasurements[_selectedDistanceMeasurementIndex] = (imageCoord.Value, m.P2);
            else
                _distanceMeasurements[_selectedDistanceMeasurementIndex] = (m.P1, imageCoord.Value);

            UpdateAllMeasurementsDisplay();
            Cursor = Cursors.Hand;
        }

        private static bool IsPointNearLine(Point point, Point lineStart, Point lineEnd, double threshold)
        {
            double lineLength = GetDistance(lineStart, lineEnd);
            if (lineLength < 0.001) return GetDistance(point, lineStart) < threshold;

            double t = Math.Clamp(((point.X - lineStart.X) * (lineEnd.X - lineStart.X) + (point.Y - lineStart.Y) * (lineEnd.Y - lineStart.Y)) / (lineLength * lineLength), 0, 1);
            var closest = new Point(lineStart.X + t * (lineEnd.X - lineStart.X), lineStart.Y + t * (lineEnd.Y - lineStart.Y));
            return GetDistance(point, closest) < threshold;
        }

        private void UpdateMeasureLinePreview(Point mousePos)
        {
            if (!_measurePoint1.HasValue) return;
            var imageCoord = GetImageCoordinate(mousePos);
            if (!imageCoord.HasValue) return;
            double dist = Math.Sqrt(Math.Pow(imageCoord.Value.X - _measurePoint1.Value.X, 2) + Math.Pow(imageCoord.Value.Y - _measurePoint1.Value.Y, 2)) / _pixelsPerUnit;
            measureTextBlock.Text = $"距离: {dist:F2} {_physicalUnit}";
            measureTextBlock.Visibility = Visibility.Visible;
            UpdateAllMeasurementsDisplay();
        }

        private Point GetScreenPositionFromImage(Point imagePos) => new(translateTransform.X + imagePos.X * _currentScale, translateTransform.Y + imagePos.Y * _currentScale);

        private void UpdateAllMeasurementsDisplay()
        {
            RemoveOverlayByTag("measure_");
            double lineThickness = 2 / _currentScale, pointSize = 8 / _currentScale;

            for (int i = 0; i < _distanceMeasurements.Count; i++)
                DrawMeasurementLine(_distanceMeasurements[i].P1, _distanceMeasurements[i].P2, i, lineThickness, pointSize, i == _selectedDistanceMeasurementIndex);

            if (_measurePoint1.HasValue)
            {
                overlayCanvas.Children.Add(CreatePoint(_measurePoint1.Value, pointSize, Brushes.Red, "measure_current_p1"));
                var endPoint = GetImageCoordinate(Mouse.GetPosition(rootGrid));
                if (endPoint.HasValue)
                    overlayCanvas.Children.Add(new Line { X1 = _measurePoint1.Value.X, Y1 = _measurePoint1.Value.Y, X2 = endPoint.Value.X, Y2 = endPoint.Value.Y, Stroke = Brushes.Yellow, StrokeThickness = lineThickness, StrokeDashArray = [4, 2], Tag = "measure_current_line", IsHitTestVisible = false });
            }

            if (_distanceMeasurements.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _distanceMeasurements.Count; i++)
                {
                    var m = _distanceMeasurements[i];
                    sb.AppendLine($"#{i + 1}: {Math.Sqrt(Math.Pow(m.P2.X - m.P1.X, 2) + Math.Pow(m.P2.Y - m.P1.Y, 2)) / _pixelsPerUnit:F2}{_physicalUnit}");
                }
                measureTextBlock.Text = sb.ToString().TrimEnd();
                measureTextBlock.Visibility = Visibility.Visible;
            }
        }

        private void DrawMeasurementLine(Point p1, Point p2, int index, double lineThickness, double pointSize, bool isSelected)
        {
            var lineColor = isSelected ? Brushes.Cyan : Brushes.Yellow;
            var pointColor = isSelected ? Brushes.Lime : Brushes.Red;
            var pointSizeActual = isSelected ? pointSize * 1.3 : pointSize;

            overlayCanvas.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = p2.X, Y2 = p2.Y, Stroke = lineColor, StrokeThickness = isSelected ? lineThickness * 1.5 : lineThickness, Tag = $"measure_{index}_line", IsHitTestVisible = false });
            overlayCanvas.Children.Add(CreatePoint(p1, pointSizeActual, pointColor, $"measure_{index}_p1"));
            overlayCanvas.Children.Add(CreatePoint(p2, pointSizeActual, pointColor, $"measure_{index}_p2"));
        }

        private Ellipse CreatePoint(Point pos, double size, Brush fill, string tag)
        {
            var point = new Ellipse { Width = size, Height = size, Fill = fill, Tag = tag, IsHitTestVisible = false };
            Canvas.SetLeft(point, pos.X - size / 2); Canvas.SetTop(point, pos.Y - size / 2);
            return point;
        }

        private void RemoveOverlayByTag(string prefix)
        {
            foreach (var e in overlayCanvas.Children.OfType<FrameworkElement>().Where(e => e.Tag is string s && s.StartsWith(prefix)).ToList())
                overlayCanvas.Children.Remove(e);
        }

        private void UpdateOverlayScale() { UpdateAllMeasurementsDisplay(); UpdateAllRoiDisplay(); UpdateAllAngleMeasurementsDisplay(); }

        private void ClearMeasurement()
        {
            _measurePoint1 = _measurePoint2 = null; _distanceMeasurements.Clear();
            _selectedDistanceMeasurementIndex = -1;
            measureTextBlock.Visibility = Visibility.Collapsed; UpdateAllMeasurementsDisplay();
        }

        private void DeleteSelectedMeasurement()
        {
            if (_selectedDistanceMeasurementIndex >= 0 && _selectedDistanceMeasurementIndex < _distanceMeasurements.Count)
            {
                _distanceMeasurements.RemoveAt(_selectedDistanceMeasurementIndex);
                _selectedDistanceMeasurementIndex = -1;
                UpdateAllMeasurementsDisplay();
            }
        }

        private void DeleteSelectedAngleMeasurement()
        {
            if (_selectedAngleMeasurementIndex >= 0 && _selectedAngleMeasurementIndex < _angleMeasurements.Count)
            {
                _angleMeasurements.RemoveAt(_selectedAngleMeasurementIndex);
                _selectedAngleMeasurementIndex = -1;
                UpdateAllAngleMeasurementsDisplay();
            }
        }

        private void DeleteSelectedOrClearAll()
        {
            // 优先删除选中的单个测量或ROI
            if (_selectedDistanceMeasurementIndex >= 0)
            {
                DeleteSelectedMeasurement();
                return;
            }
            if (_selectedAngleMeasurementIndex >= 0)
            {
                DeleteSelectedAngleMeasurement();
                return;
            }
            if (_selectedRectRoiIndex >= 0 && _selectedRectRoiIndex < _rectRois.Count)
            {
                _rectRois.RemoveAt(_selectedRectRoiIndex);
                _selectedRectRoiIndex = -1;
                UpdateAllRoiDisplay();
                RoiChanged?.Invoke(this, _rectRois.AsReadOnly());
                return;
            }
            if (_selectedEllipseRoiIndex >= 0 && _selectedEllipseRoiIndex < _ellipseRois.Count)
            {
                _ellipseRois.RemoveAt(_selectedEllipseRoiIndex);
                _selectedEllipseRoiIndex = -1;
                UpdateAllRoiDisplay();
                EllipseRoiChanged?.Invoke(this, _ellipseRois.AsReadOnly());
                return;
            }
            if (_selectedPolygonRoiIndex >= 0 && _selectedPolygonRoiIndex < _polygonRois.Count)
            {
                _polygonRois.RemoveAt(_selectedPolygonRoiIndex);
                _selectedPolygonRoiIndex = -1;
                UpdateAllRoiDisplay();
                PolygonRoiChanged?.Invoke(this, _polygonRois.AsReadOnly());
                return;
            }
            // 否则清除所有
            ClearAllRoi();
            ClearAngleMeasurement();
        }

        public IReadOnlyList<(Point P1, Point P2)> GetDistanceMeasurements() => _distanceMeasurements.AsReadOnly();

        #endregion

        #region 图像属性面板

        public void ToggleInfoPanel() => ShowInfoPanel = !ShowInfoPanel;

        private void UpdateInfoPanel()
        {
            if (!ShowInfoPanel || _cachedBitmapSource == null) return;
            string channels = _pixelCacheChannels switch { 1 => "灰度", 3 => "BGR", 4 => "BGRA", _ => $"{_pixelCacheChannels}通道" };
            infoText.Text = $"尺寸: {_cachedBitmapSource.PixelWidth} × {_cachedBitmapSource.PixelHeight}\n格式: {_cachedBitmapSource.Format}\n通道: {channels}\nDPI: {_cachedBitmapSource.DpiX:F0} × {_cachedBitmapSource.DpiY:F0}\n深度: {_cachedBitmapSource.Format.BitsPerPixel} bpp";
        }

        #endregion



        #region ROI 结构定义

        public struct RotatedRect(double centerX, double centerY, double width, double height, double angle = 0)
        {
            public double CenterX { get; set; } = centerX;
            public double CenterY { get; set; } = centerY;
            public double Width { get; set; } = width;
            public double Height { get; set; } = height;
            public double Angle { get; set; } = angle;

            public readonly Point[] GetCorners()
            {
                double rad = Angle * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
                double hw = Width / 2, hh = Height / 2;
                var corners = new[] { new Point(-hw, -hh), new Point(hw, -hh), new Point(hw, hh), new Point(-hw, hh) };
                for (int i = 0; i < 4; i++) corners[i] = new Point(corners[i].X * cos - corners[i].Y * sin + CenterX, corners[i].X * sin + corners[i].Y * cos + CenterY);
                return corners;
            }

            public readonly bool ContainsPoint(Point point)
            {
                double rad = -Angle * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
                double dx = point.X - CenterX, dy = point.Y - CenterY;
                return Math.Abs(dx * cos - dy * sin) <= Width / 2 && Math.Abs(dx * sin + dy * cos) <= Height / 2;
            }

            public override readonly string ToString() => $"中心:({CenterX:F1},{CenterY:F1}) 尺寸:{Width:F1}×{Height:F1} 角度:{Angle:F1}°";
        }

        public struct EllipseRoi(double centerX, double centerY, double radiusX, double radiusY, double angle = 0)
        {
            public double CenterX { get; set; } = centerX;
            public double CenterY { get; set; } = centerY;
            public double RadiusX { get; set; } = radiusX;
            public double RadiusY { get; set; } = radiusY;
            public double Angle { get; set; } = angle;

            public readonly double GetArea() => Math.PI * RadiusX * RadiusY;

            public readonly bool ContainsPoint(Point point)
            {
                double rad = -Angle * Math.PI / 180.0, cos = Math.Cos(rad), sin = Math.Sin(rad);
                double dx = point.X - CenterX, dy = point.Y - CenterY;
                double localX = dx * cos - dy * sin, localY = dx * sin + dy * cos;
                return (localX * localX) / (RadiusX * RadiusX) + (localY * localY) / (RadiusY * RadiusY) <= 1;
            }

            public override readonly string ToString() => $"中心:({CenterX:F1},{CenterY:F1}) 半径:{RadiusX:F1}×{RadiusY:F1} 角度:{Angle:F1}° 面积:{GetArea():F1}px²";
        }

        public struct PolygonRoi(Point[] points)
        {
            public Point[] Points { get; set; } = points;

            public readonly double GetArea()
            {
                if (Points == null || Points.Length < 3) return 0;
                double area = 0; int n = Points.Length;
                for (int i = 0; i < n; i++) { int j = (i + 1) % n; area += Points[i].X * Points[j].Y - Points[j].X * Points[i].Y; }
                return Math.Abs(area) / 2;
            }

            public readonly double GetPerimeter()
            {
                if (Points == null || Points.Length < 2) return 0;
                double perimeter = 0;
                for (int i = 0; i < Points.Length; i++) { int j = (i + 1) % Points.Length; perimeter += Math.Sqrt(Math.Pow(Points[j].X - Points[i].X, 2) + Math.Pow(Points[j].Y - Points[i].Y, 2)); }
                return perimeter;
            }

            public override readonly string ToString() => $"顶点数:{Points?.Length ?? 0} 面积:{GetArea():F1}px² 周长:{GetPerimeter():F1}px";
        }

        #endregion

        #region ROI 事件和方法

        public event EventHandler<IReadOnlyList<RotatedRect>>? RoiChanged;
        public event EventHandler<IReadOnlyList<EllipseRoi>>? EllipseRoiChanged;
        public event EventHandler<IReadOnlyList<PolygonRoi>>? PolygonRoiChanged;

        public void ToggleRoiMode() { ExitAllModes(); _isRoiMode = !_isRoiMode; if (_isRoiMode) { Cursor = Cursors.Cross; _currentRoi = null; _selectedRectRoiIndex = -1; } }
        public void ToggleEllipseRoiMode() { ExitAllModes(); _isEllipseRoiMode = !_isEllipseRoiMode; if (_isEllipseRoiMode) { Cursor = Cursors.Cross; _currentEllipseRoi = null; _selectedEllipseRoiIndex = -1; } }
        public void TogglePolygonRoiMode() { ExitAllModes(); _isPolygonRoiMode = !_isPolygonRoiMode; if (_isPolygonRoiMode) { Cursor = Cursors.Cross; _polygonPoints.Clear(); _isDrawingPolygonRoi = true; _selectedPolygonRoiIndex = -1; } }

        public IReadOnlyList<RotatedRect> GetRectRois() => _rectRois.AsReadOnly();
        public IReadOnlyList<EllipseRoi> GetEllipseRois() => _ellipseRois.AsReadOnly();
        public IReadOnlyList<PolygonRoi> GetPolygonRois() => _polygonRois.AsReadOnly();

        public void AddRectRoi(RotatedRect roi) { _rectRois.Add(roi); UpdateAllRoiDisplay(); RoiChanged?.Invoke(this, _rectRois.AsReadOnly()); }

        public void ClearRoi() { _currentRoi = null; _rectRois.Clear(); _selectedRectRoiIndex = -1; _isDrawingRoi = _isDraggingRoi = _isResizingRoi = _isRotatingRoi = false; RoiChanged?.Invoke(this, _rectRois.AsReadOnly()); }
        public void ClearEllipseRoi() { _currentEllipseRoi = null; _ellipseRois.Clear(); _selectedEllipseRoiIndex = -1; _isDrawingEllipseRoi = _isDraggingEllipseRoi = _isResizingEllipseRoi = _isRotatingEllipseRoi = false; EllipseRoiChanged?.Invoke(this, _ellipseRois.AsReadOnly()); }
        public void ClearPolygonRoi() { _polygonRois.Clear(); _polygonPoints.Clear(); _isDrawingPolygonRoi = _isDraggingPolygonRoi = _isDraggingPolygonVertex = false; _selectedPolygonRoiIndex = -1; PolygonRoiChanged?.Invoke(this, _polygonRois.AsReadOnly()); }

        private void HandleRoiMouseDown(Point screenPos)
        {
            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;
            _roiDragStart = imageCoord.Value;

            double handleSize = GetScaledHandleSize();

            // 检查是否点击了已选中ROI的控制手柄
            if (_selectedRectRoiIndex >= 0 && _selectedRectRoiIndex < _rectRois.Count)
            {
                var roi = _rectRois[_selectedRectRoiIndex];

                // 检查旋转手柄
                var rotateHandle = GetRotateHandlePosition(roi);
                if (IsWithinHandleDistance(imageCoord.Value, rotateHandle, handleSize))
                {
                    _isRotatingRoi = true;
                    _roiBeforeEdit = roi;
                    _initialRotationAngle = Math.Atan2(imageCoord.Value.Y - roi.CenterY, imageCoord.Value.X - roi.CenterX) * RadToDeg;
                    return;
                }

                // 检查调整大小手柄 (角点)
                var corners = roi.GetCorners();
                for (int j = 0; j < 4; j++)
                {
                    if (IsWithinHandleDistance(imageCoord.Value, corners[j], handleSize))
                    {
                        _isResizingRoi = true;
                        _resizeHandleIndex = j;
                        _roiBeforeEdit = roi;
                        return;
                    }
                }

                // 检查边缘中点手柄
                for (int j = 0; j < 4; j++)
                {
                    var edgeMid = GetMidPoint(corners[j], corners[(j + 1) % 4]);
                    if (IsWithinHandleDistance(imageCoord.Value, edgeMid, handleSize))
                    {
                        _isResizingRoi = true;
                        _resizeHandleIndex = j + 4;
                        _roiBeforeEdit = roi;
                        return;
                    }
                }
            }

            // 检查是否点击了ROI内部(拖动)或选择新ROI
            for (int i = _rectRois.Count - 1; i >= 0; i--)
            {
                if (_rectRois[i].ContainsPoint(imageCoord.Value))
                {
                    _selectedRectRoiIndex = i;
                    _currentRoi = _rectRois[i];
                    _isDraggingRoi = true;
                    _roiBeforeEdit = _currentRoi;
                    UpdateAllRoiDisplay();
                    return;
                }
            }

            _selectedRectRoiIndex = -1;
            _isDrawingRoi = true;
            _currentRoi = new RotatedRect(imageCoord.Value.X, imageCoord.Value.Y, 0, 0, 0);
        }

        private double GetScaledHandleSize() => _currentScale > 0 ? HandleSize / _currentScale : HandleSize;

        private static bool IsWithinHandleDistance(Point p1, Point p2, double threshold) => GetDistance(p1, p2) < threshold;

        private static Point GetMidPoint(Point p1, Point p2) => new((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);

        private static double GetDistance(Point p1, Point p2)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Point GetRotateHandlePosition(RotatedRect roi)
        {
            double rad = roi.Angle * DegToRad;
            double offsetY = -roi.Height / 2 - RotateHandleOffset;
            return new Point(roi.CenterX - offsetY * Math.Sin(rad), roi.CenterY + offsetY * Math.Cos(rad));
        }

        private void HandleRoiMouseMove(Point screenPos)
        {
            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;

            if (_isDrawingRoi && _currentRoi.HasValue)
            {
                double w = Math.Abs(imageCoord.Value.X - _roiDragStart.X), h = Math.Abs(imageCoord.Value.Y - _roiDragStart.Y);
                _currentRoi = new RotatedRect((_roiDragStart.X + imageCoord.Value.X) / 2, (_roiDragStart.Y + imageCoord.Value.Y) / 2, w, h, 0);
                UpdateAllRoiDisplay();
            }
            else if (_isRotatingRoi && _roiBeforeEdit.HasValue && _selectedRectRoiIndex >= 0)
            {
                var r = _roiBeforeEdit.Value;
                double currentAngle = Math.Atan2(imageCoord.Value.Y - r.CenterY, imageCoord.Value.X - r.CenterX) * RadToDeg;
                double deltaAngle = currentAngle - _initialRotationAngle;
                _currentRoi = new RotatedRect(r.CenterX, r.CenterY, r.Width, r.Height, r.Angle + deltaAngle);
                _rectRois[_selectedRectRoiIndex] = _currentRoi.Value;
                UpdateAllRoiDisplay();
                Cursor = Cursors.Hand;
            }
            else if (_isResizingRoi && _roiBeforeEdit.HasValue && _selectedRectRoiIndex >= 0)
            {
                var r = _roiBeforeEdit.Value;
                var corners = r.GetCorners(); // 0:左上, 1:右上, 2:右下, 3:左下
                double rad = r.Angle * DegToRad, cos = Math.Cos(rad), sin = Math.Sin(rad);

                if (_resizeHandleIndex < 4) // 角点拖动 - 对角固定
                {
                    // 获取固定的对角点
                    int oppositeIndex = (_resizeHandleIndex + 2) % 4;
                    var fixedCorner = corners[oppositeIndex];
                    var draggedCorner = imageCoord.Value;

                    // 计算新的中心点
                    double newCenterX = (fixedCorner.X + draggedCorner.X) / 2;
                    double newCenterY = (fixedCorner.Y + draggedCorner.Y) / 2;

                    // 计算固定点和拖动点在局部坐标系中的位置
                    double dx = draggedCorner.X - fixedCorner.X;
                    double dy = draggedCorner.Y - fixedCorner.Y;
                    double localDx = dx * cos + dy * sin;
                    double localDy = -dx * sin + dy * cos;

                    double newWidth = Math.Max(MinResizeSize, Math.Abs(localDx));
                    double newHeight = Math.Max(MinResizeSize, Math.Abs(localDy));

                    _currentRoi = new RotatedRect(newCenterX, newCenterY, newWidth, newHeight, r.Angle);
                }
                else // 边缘中点拖动 - 对边固定
                {
                    int edge = _resizeHandleIndex - 4; // 0:上, 1:右, 2:下, 3:左

                    // 将鼠标位置转换到局部坐标系
                    double dx = imageCoord.Value.X - r.CenterX;
                    double dy = imageCoord.Value.Y - r.CenterY;
                    double localX = dx * cos + dy * sin;
                    double localY = -dx * sin + dy * cos;

                    double newWidth = r.Width, newHeight = r.Height;
                    double newCenterX = r.CenterX, newCenterY = r.CenterY;
                    double offsetX = 0, offsetY = 0;

                    if (edge == 0) // 上边 - 下边固定
                    {
                        double bottomY = r.Height / 2;
                        newHeight = Math.Max(MinResizeSize, bottomY - localY);
                        offsetY = (r.Height - newHeight) / 2;
                    }
                    else if (edge == 2) // 下边 - 上边固定
                    {
                        double topY = -r.Height / 2;
                        newHeight = Math.Max(MinResizeSize, localY - topY);
                        offsetY = (newHeight - r.Height) / 2;
                    }
                    else if (edge == 1) // 右边 - 左边固定
                    {
                        double leftX = -r.Width / 2;
                        newWidth = Math.Max(MinResizeSize, localX - leftX);
                        offsetX = (newWidth - r.Width) / 2;
                    }
                    else // 左边 - 右边固定
                    {
                        double rightX = r.Width / 2;
                        newWidth = Math.Max(MinResizeSize, rightX - localX);
                        offsetX = (r.Width - newWidth) / 2;
                    }

                    // 将偏移转换回世界坐标
                    newCenterX = r.CenterX + offsetX * cos - offsetY * sin;
                    newCenterY = r.CenterY + offsetX * sin + offsetY * cos;

                    _currentRoi = new RotatedRect(newCenterX, newCenterY, newWidth, newHeight, r.Angle);
                }

                _rectRois[_selectedRectRoiIndex] = _currentRoi.Value;
                UpdateAllRoiDisplay();
                Cursor = GetResizeCursor(_resizeHandleIndex, r.Angle);
            }
            else if (_isDraggingRoi && _roiBeforeEdit.HasValue && _selectedRectRoiIndex >= 0)
            {
                var r = _roiBeforeEdit.Value;
                _currentRoi = new RotatedRect(r.CenterX + imageCoord.Value.X - _roiDragStart.X, r.CenterY + imageCoord.Value.Y - _roiDragStart.Y, r.Width, r.Height, r.Angle);
                _rectRois[_selectedRectRoiIndex] = _currentRoi.Value;
                UpdateAllRoiDisplay();
                Cursor = Cursors.SizeAll;
            }
            else if (_selectedRectRoiIndex >= 0)
            {
                // 更新光标样式
                UpdateRoiCursor(imageCoord.Value);
            }
        }

        private void UpdateRoiCursor(Point imageCoord)
        {
            if (_selectedRectRoiIndex < 0 || _selectedRectRoiIndex >= _rectRois.Count) { Cursor = Cursors.Cross; return; }
            var roi = _rectRois[_selectedRectRoiIndex];
            // L256: 使用 HandleSize 常量替代魔法数字 12
            double handleSize = HandleSize / _currentScale;

            var rotateHandle = GetRotateHandlePosition(roi);
            if (GetDistance(imageCoord, rotateHandle) < handleSize) { Cursor = Cursors.Hand; return; }

            var corners = roi.GetCorners();
            for (int j = 0; j < 4; j++)
            {
                if (GetDistance(imageCoord, corners[j]) < handleSize) { Cursor = GetResizeCursor(j, roi.Angle); return; }
            }
            for (int j = 0; j < 4; j++)
            {
                var edgeMid = new Point((corners[j].X + corners[(j + 1) % 4].X) / 2, (corners[j].Y + corners[(j + 1) % 4].Y) / 2);
                if (GetDistance(imageCoord, edgeMid) < handleSize) { Cursor = GetResizeCursor(j + 4, roi.Angle); return; }
            }
            Cursor = roi.ContainsPoint(imageCoord) ? Cursors.SizeAll : Cursors.Cross;
        }

        private static Cursor GetResizeCursor(int handleIndex, double angle)
        {
            double[] baseAngles = handleIndex < 4
                ? [315, 45, 135, 225] // 角点: 左上, 右上, 右下, 左下
                : [270, 0, 90, 180];   // 边缘: 上, 右, 下, 左
            double adjustedAngle = (baseAngles[handleIndex % 4] + angle + 360) % 360;
            return adjustedAngle switch
            {
                >= 337.5 or < 22.5 => Cursors.SizeWE,
                >= 22.5 and < 67.5 => Cursors.SizeNWSE,
                >= 67.5 and < 112.5 => Cursors.SizeNS,
                >= 112.5 and < 157.5 => Cursors.SizeNESW,
                >= 157.5 and < 202.5 => Cursors.SizeWE,
                >= 202.5 and < 247.5 => Cursors.SizeNWSE,
                >= 247.5 and < 292.5 => Cursors.SizeNS,
                _ => Cursors.SizeNESW
            };
        }

        private void HandleRoiMouseUp()
        {
            if (_isDrawingRoi && _currentRoi is { Width: >= MinRoiSize, Height: >= MinRoiSize })
            {
                _rectRois.Add(_currentRoi.Value);
                _selectedRectRoiIndex = _rectRois.Count - 1;
                RoiChanged?.Invoke(this, _rectRois.AsReadOnly());
            }
            else if ((_isDraggingRoi || _isResizingRoi || _isRotatingRoi) && _selectedRectRoiIndex >= 0)
            {
                RoiChanged?.Invoke(this, _rectRois.AsReadOnly());
            }
            _isDrawingRoi = _isDraggingRoi = _isResizingRoi = _isRotatingRoi = false;
            _resizeHandleIndex = -1;
            _currentRoi = _roiBeforeEdit = null;
            Cursor = Cursors.Cross;
            UpdateAllRoiDisplay();
        }

        private void HandleEllipseRoiMouseDown(Point screenPos)
        {
            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;
            _roiDragStart = imageCoord.Value;

            double handleSize = GetScaledHandleSize();

            // 检查是否点击了已选中椭圆ROI的控制手柄
            if (_selectedEllipseRoiIndex >= 0 && _selectedEllipseRoiIndex < _ellipseRois.Count)
            {
                var roi = _ellipseRois[_selectedEllipseRoiIndex];

                // 检查旋转手柄
                var rotateHandle = GetEllipseRotateHandlePosition(roi);
                if (IsWithinHandleDistance(imageCoord.Value, rotateHandle, handleSize))
                {
                    _isRotatingEllipseRoi = true;
                    _ellipseRoiBeforeEdit = roi;
                    _initialRotationAngle = Math.Atan2(imageCoord.Value.Y - roi.CenterY, imageCoord.Value.X - roi.CenterX) * RadToDeg;
                    return;
                }

                // 检查调整大小手柄 (四个方向)
                var handles = GetEllipseHandles(roi);
                for (int j = 0; j < 4; j++)
                {
                    if (IsWithinHandleDistance(imageCoord.Value, handles[j], handleSize))
                    {
                        _isResizingEllipseRoi = true;
                        _resizeHandleIndex = j;
                        _ellipseRoiBeforeEdit = roi;
                        return;
                    }
                }
            }

            // 检查是否点击了椭圆内部
            for (int i = _ellipseRois.Count - 1; i >= 0; i--)
            {
                if (_ellipseRois[i].ContainsPoint(imageCoord.Value))
                {
                    _selectedEllipseRoiIndex = i;
                    _currentEllipseRoi = _ellipseRois[i];
                    _isDraggingEllipseRoi = true;
                    _ellipseRoiBeforeEdit = _currentEllipseRoi;
                    UpdateAllRoiDisplay();
                    return;
                }
            }
            _selectedEllipseRoiIndex = -1;
            _isDrawingEllipseRoi = true;
            _currentEllipseRoi = new EllipseRoi(imageCoord.Value.X, imageCoord.Value.Y, 0, 0, 0);
        }

        private static Point GetEllipseRotateHandlePosition(EllipseRoi roi)
        {
            double rad = roi.Angle * DegToRad;
            double offsetY = -roi.RadiusY - RotateHandleOffset;
            return new Point(roi.CenterX - offsetY * Math.Sin(rad), roi.CenterY + offsetY * Math.Cos(rad));
        }

        private static Point[] GetEllipseHandles(EllipseRoi roi)
        {
            double rad = roi.Angle * DegToRad, cos = Math.Cos(rad), sin = Math.Sin(rad);
            return [
                new Point(roi.CenterX - roi.RadiusY * sin, roi.CenterY + roi.RadiusY * cos),     // 上
                new Point(roi.CenterX + roi.RadiusX * cos, roi.CenterY + roi.RadiusX * sin),     // 右
                new Point(roi.CenterX + roi.RadiusY * sin, roi.CenterY - roi.RadiusY * cos),     // 下
                new Point(roi.CenterX - roi.RadiusX * cos, roi.CenterY - roi.RadiusX * sin)      // 左
            ];
        }

        private void HandleEllipseRoiMouseMove(Point screenPos)
        {
            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;

            if (_isDrawingEllipseRoi && _currentEllipseRoi.HasValue)
            {
                double rx = Math.Abs(imageCoord.Value.X - _roiDragStart.X) / 2, ry = Math.Abs(imageCoord.Value.Y - _roiDragStart.Y) / 2;
                _currentEllipseRoi = new EllipseRoi((_roiDragStart.X + imageCoord.Value.X) / 2, (_roiDragStart.Y + imageCoord.Value.Y) / 2, rx, ry, 0);
                UpdateAllRoiDisplay();
            }
            else if (_isRotatingEllipseRoi && _ellipseRoiBeforeEdit.HasValue && _selectedEllipseRoiIndex >= 0)
            {
                var e = _ellipseRoiBeforeEdit.Value;
                double currentAngle = Math.Atan2(imageCoord.Value.Y - e.CenterY, imageCoord.Value.X - e.CenterX) * RadToDeg;
                double deltaAngle = currentAngle - _initialRotationAngle;
                _currentEllipseRoi = new EllipseRoi(e.CenterX, e.CenterY, e.RadiusX, e.RadiusY, e.Angle + deltaAngle);
                _ellipseRois[_selectedEllipseRoiIndex] = _currentEllipseRoi.Value;
                UpdateAllRoiDisplay();
                Cursor = Cursors.Hand;
            }
            else if (_isResizingEllipseRoi && _ellipseRoiBeforeEdit.HasValue && _selectedEllipseRoiIndex >= 0)
            {
                var e = _ellipseRoiBeforeEdit.Value;
                double rad = -e.Angle * DegToRad, cos = Math.Cos(rad), sin = Math.Sin(rad);
                double dx = imageCoord.Value.X - e.CenterX, dy = imageCoord.Value.Y - e.CenterY;
                double localX = dx * cos - dy * sin, localY = dx * sin + dy * cos;

                double newRadiusX = e.RadiusX, newRadiusY = e.RadiusY;
                if (_resizeHandleIndex is 0 or 2) // 上/下
                    newRadiusY = Math.Max(MinEllipseRadius, Math.Abs(localY));
                else // 左/右
                    newRadiusX = Math.Max(MinEllipseRadius, Math.Abs(localX));

                _currentEllipseRoi = new EllipseRoi(e.CenterX, e.CenterY, newRadiusX, newRadiusY, e.Angle);
                _ellipseRois[_selectedEllipseRoiIndex] = _currentEllipseRoi.Value;
                UpdateAllRoiDisplay();
                Cursor = _resizeHandleIndex is 0 or 2 ? Cursors.SizeNS : Cursors.SizeWE;
            }
            else if (_isDraggingEllipseRoi && _ellipseRoiBeforeEdit.HasValue && _selectedEllipseRoiIndex >= 0)
            {
                var e = _ellipseRoiBeforeEdit.Value;
                _currentEllipseRoi = new EllipseRoi(e.CenterX + imageCoord.Value.X - _roiDragStart.X, e.CenterY + imageCoord.Value.Y - _roiDragStart.Y, e.RadiusX, e.RadiusY, e.Angle);
                _ellipseRois[_selectedEllipseRoiIndex] = _currentEllipseRoi.Value;
                UpdateAllRoiDisplay();
                Cursor = Cursors.SizeAll;
            }
            else if (_selectedEllipseRoiIndex >= 0)
            {
                UpdateEllipseRoiCursor(imageCoord.Value);
            }
        }

        private void UpdateEllipseRoiCursor(Point imageCoord)
        {
            if (_selectedEllipseRoiIndex < 0 || _selectedEllipseRoiIndex >= _ellipseRois.Count) { Cursor = Cursors.Cross; return; }
            var roi = _ellipseRois[_selectedEllipseRoiIndex];
            double handleSize = GetScaledHandleSize();

            var rotateHandle = GetEllipseRotateHandlePosition(roi);
            if (IsWithinHandleDistance(imageCoord, rotateHandle, handleSize)) { Cursor = Cursors.Hand; return; }

            var handles = GetEllipseHandles(roi);
            for (int j = 0; j < 4; j++)
            {
                if (IsWithinHandleDistance(imageCoord, handles[j], handleSize))
                {
                    Cursor = j is 0 or 2 ? Cursors.SizeNS : Cursors.SizeWE;
                    return;
                }
            }
            Cursor = roi.ContainsPoint(imageCoord) ? Cursors.SizeAll : Cursors.Cross;
        }

        private void HandleEllipseRoiMouseUp()
        {
            if (_isDrawingEllipseRoi && _currentEllipseRoi is { RadiusX: >= MinEllipseRadius, RadiusY: >= MinEllipseRadius })
            {
                _ellipseRois.Add(_currentEllipseRoi.Value);
                _selectedEllipseRoiIndex = _ellipseRois.Count - 1;
                EllipseRoiChanged?.Invoke(this, _ellipseRois.AsReadOnly());
            }
            else if ((_isDraggingEllipseRoi || _isResizingEllipseRoi || _isRotatingEllipseRoi) && _selectedEllipseRoiIndex >= 0)
            {
                EllipseRoiChanged?.Invoke(this, _ellipseRois.AsReadOnly());
            }
            _isDrawingEllipseRoi = _isDraggingEllipseRoi = _isResizingEllipseRoi = _isRotatingEllipseRoi = false;
            _resizeHandleIndex = -1;
            _currentEllipseRoi = _ellipseRoiBeforeEdit = null;
            Cursor = Cursors.Cross;
            UpdateAllRoiDisplay();
        }

        private void HandlePolygonRoiMouseDown(Point screenPos)
        {
            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;

            double handleSize = GetScaledHandleSize();

            // 检查是否点击了已选中多边形的顶点（用于拖动顶点）
            if (_selectedPolygonRoiIndex >= 0 && _selectedPolygonRoiIndex < _polygonRois.Count)
            {
                var roi = _polygonRois[_selectedPolygonRoiIndex];
                if (roi.Points != null)
                {
                    for (int j = 0; j < roi.Points.Length; j++)
                    {
                        if (IsWithinHandleDistance(imageCoord.Value, roi.Points[j], handleSize))
                        {
                            _isDraggingPolygonVertex = true;
                            _draggingVertexIndex = j;
                            _roiDragStart = imageCoord.Value;
                            _polygonRoiBeforeEdit = roi;
                            return;
                        }
                    }
                }
            }

            // 检查是否点击了多边形内部或边线（用于选中或拖动整体）
            for (int i = _polygonRois.Count - 1; i >= 0; i--)
            {
                var roi = _polygonRois[i];
                if (roi.Points == null || roi.Points.Length < 3) continue;

                // 检查是否点击了顶点
                for (int j = 0; j < roi.Points.Length; j++)
                {
                    if (IsWithinHandleDistance(imageCoord.Value, roi.Points[j], handleSize))
                    {
                        _selectedPolygonRoiIndex = i;
                        _isDraggingPolygonVertex = true;
                        _draggingVertexIndex = j;
                        _roiDragStart = imageCoord.Value;
                        _polygonRoiBeforeEdit = roi;
                        UpdateAllRoiDisplay();
                        return;
                    }
                }

                // 检查是否点击了边线
                for (int j = 0; j < roi.Points.Length; j++)
                {
                    int next = (j + 1) % roi.Points.Length;
                    if (IsPointNearLine(imageCoord.Value, roi.Points[j], roi.Points[next], handleSize))
                    {
                        _selectedPolygonRoiIndex = i;
                        _isDraggingPolygonRoi = true;
                        _roiDragStart = imageCoord.Value;
                        _polygonRoiBeforeEdit = roi;
                        UpdateAllRoiDisplay();
                        return;
                    }
                }

                // 检查是否点击了多边形内部
                if (IsPointInPolygon(imageCoord.Value, roi.Points))
                {
                    _selectedPolygonRoiIndex = i;
                    _isDraggingPolygonRoi = true;
                    _roiDragStart = imageCoord.Value;
                    _polygonRoiBeforeEdit = roi;
                    UpdateAllRoiDisplay();
                    return;
                }
            }

            // 如果正在绘制新多边形
            if (_isDrawingPolygonRoi)
            {
                if (_polygonPoints.Count >= 3)
                {
                    var startScreen = GetScreenPositionFromImage(_polygonPoints[0]);
                    if (GetDistance(screenPos, startScreen) < ClosePolygonDistance)
                    {
                        FinishPolygonRoi();
                        return;
                    }
                }
                _polygonPoints.Add(imageCoord.Value);
                UpdateAllRoiDisplay();
                return;
            }

            // 取消选中并开始绘制新多边形
            _selectedPolygonRoiIndex = -1;
            _isDrawingPolygonRoi = true;
            _polygonPoints.Clear();
            _polygonPoints.Add(imageCoord.Value);
            UpdateAllRoiDisplay();
        }

        private static bool IsPointInPolygon(Point point, Point[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return false;
            bool inside = false;
            int n = polygon.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                    inside = !inside;
            }
            return inside;
        }

        private void HandlePolygonRoiMouseMove(Point screenPos)
        {
            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;

            if (_isDraggingPolygonVertex && _selectedPolygonRoiIndex >= 0 && _polygonRoiBeforeEdit.HasValue)
            {
                var original = _polygonRoiBeforeEdit.Value;
                if (original.Points != null && _draggingVertexIndex >= 0 && _draggingVertexIndex < original.Points.Length)
                {
                    var newPoints = (Point[])original.Points.Clone();
                    newPoints[_draggingVertexIndex] = imageCoord.Value;
                    _polygonRois[_selectedPolygonRoiIndex] = new PolygonRoi(newPoints);
                    UpdateAllRoiDisplay();
                    Cursor = Cursors.Hand;
                }
            }
            else if (_isDraggingPolygonRoi && _selectedPolygonRoiIndex >= 0 && _polygonRoiBeforeEdit.HasValue)
            {
                var original = _polygonRoiBeforeEdit.Value;
                if (original.Points != null)
                {
                    double dx = imageCoord.Value.X - _roiDragStart.X;
                    double dy = imageCoord.Value.Y - _roiDragStart.Y;
                    var newPoints = new Point[original.Points.Length];
                    for (int i = 0; i < original.Points.Length; i++)
                        newPoints[i] = new Point(original.Points[i].X + dx, original.Points[i].Y + dy);
                    _polygonRois[_selectedPolygonRoiIndex] = new PolygonRoi(newPoints);
                    UpdateAllRoiDisplay();
                    Cursor = Cursors.SizeAll;
                }
            }
            else if (_isDrawingPolygonRoi && _polygonPoints.Count > 0)
            {
                UpdateAllRoiDisplay();
            }
            else if (_selectedPolygonRoiIndex >= 0)
            {
                UpdatePolygonRoiCursor(imageCoord.Value);
            }
        }

        private void UpdatePolygonRoiCursor(Point imageCoord)
        {
            if (_selectedPolygonRoiIndex < 0 || _selectedPolygonRoiIndex >= _polygonRois.Count) { Cursor = Cursors.Cross; return; }
            var roi = _polygonRois[_selectedPolygonRoiIndex];
            if (roi.Points == null) { Cursor = Cursors.Cross; return; }

            double handleSize = GetScaledHandleSize();

            // 检查顶点
            foreach (var pt in roi.Points)
            {
                if (IsWithinHandleDistance(imageCoord, pt, handleSize)) { Cursor = Cursors.Hand; return; }
            }

            // 检查边线或内部
            if (IsPointInPolygon(imageCoord, roi.Points))
            { Cursor = Cursors.SizeAll; return; }

            for (int j = 0; j < roi.Points.Length; j++)
            {
                int next = (j + 1) % roi.Points.Length;
                if (IsPointNearLine(imageCoord, roi.Points[j], roi.Points[next], handleSize))
                { Cursor = Cursors.SizeAll; return; }
            }

            Cursor = Cursors.Cross;
        }

        private void HandlePolygonRoiMouseUp()
        {
            if ((_isDraggingPolygonRoi || _isDraggingPolygonVertex) && _selectedPolygonRoiIndex >= 0)
            {
                PolygonRoiChanged?.Invoke(this, _polygonRois.AsReadOnly());
            }
            _isDraggingPolygonRoi = _isDraggingPolygonVertex = false;
            _draggingVertexIndex = -1;
            _polygonRoiBeforeEdit = null;
            if (!_isDrawingPolygonRoi) Cursor = Cursors.Cross;
            UpdateAllRoiDisplay();
        }

        private void FinishPolygonRoi()
        {
            if (_polygonPoints.Count >= 3)
            {
                _polygonRois.Add(new PolygonRoi([.. _polygonPoints]));
                _selectedPolygonRoiIndex = _polygonRois.Count - 1;  // 选中刚创建的多边形
                PolygonRoiChanged?.Invoke(this, _polygonRois.AsReadOnly());
            }
            _polygonPoints.Clear();
            _isDrawingPolygonRoi = false;  // 只停止绘制状态，保持多边形模式
            Cursor = Cursors.Cross;
            UpdateAllRoiDisplay();
        }

        private void UpdateAllRoiDisplay()
        {
            RemoveOverlayByTag("rect_roi_"); RemoveOverlayByTag("ellipse_roi_"); RemoveOverlayByTag("polygon_roi_");
            double lineThickness = 2 / _currentScale, pointSize = 8 / _currentScale;

            for (int i = 0; i < _rectRois.Count; i++) DrawRectRoi(_rectRois[i], i, i == _selectedRectRoiIndex, lineThickness, pointSize);
            if (_isDrawingRoi && _currentRoi.HasValue) DrawRectRoi(_currentRoi.Value, -1, true, lineThickness, pointSize);

            for (int i = 0; i < _ellipseRois.Count; i++) DrawEllipseRoiShape(_ellipseRois[i], i, i == _selectedEllipseRoiIndex, lineThickness, pointSize);
            if (_isDrawingEllipseRoi && _currentEllipseRoi.HasValue) DrawEllipseRoiShape(_currentEllipseRoi.Value, -1, true, lineThickness, pointSize);

            for (int i = 0; i < _polygonRois.Count; i++) DrawPolygonRoiShape(_polygonRois[i], i, i == _selectedPolygonRoiIndex, lineThickness, pointSize);

            // 绘制正在绘制中的多边形
            if (_isDrawingPolygonRoi && _polygonPoints.Count > 0)
                DrawPolygonInProgress(lineThickness, pointSize);

            UpdateRoiInfoText();
        }

        private void DrawPolygonInProgress(double lineThickness, double pointSize)
        {
            var currentMousePos = GetImageCoordinate(Mouse.GetPosition(rootGrid));

            // 绘制已添加的顶点
            for (int i = 0; i < _polygonPoints.Count; i++)
            {
                var pt = _polygonPoints[i];
                var handle = new Ellipse
                {
                    Width = pointSize,
                    Height = pointSize,
                    Fill = i == 0 ? Brushes.Lime : Brushes.Magenta,
                    Stroke = Brushes.White,
                    StrokeThickness = lineThickness * 0.5,
                    Tag = $"polygon_roi_drawing_{i}",
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(handle, pt.X - pointSize / 2);
                Canvas.SetTop(handle, pt.Y - pointSize / 2);
                overlayCanvas.Children.Add(handle);
            }

            // 绘制已添加顶点之间的连线
            for (int i = 0; i < _polygonPoints.Count - 1; i++)
            {
                overlayCanvas.Children.Add(new Line
                {
                    X1 = _polygonPoints[i].X,
                    Y1 = _polygonPoints[i].Y,
                    X2 = _polygonPoints[i + 1].X,
                    Y2 = _polygonPoints[i + 1].Y,
                    Stroke = Brushes.Magenta,
                    StrokeThickness = lineThickness,
                    Tag = $"polygon_roi_drawing_line_{i}",
                    IsHitTestVisible = false
                });
            }

            // 绘制从最后一个顶点到当前鼠标位置的预览线
            if (currentMousePos.HasValue && _polygonPoints.Count > 0)
            {
                var lastPoint = _polygonPoints[^1];
                overlayCanvas.Children.Add(new Line
                {
                    X1 = lastPoint.X,
                    Y1 = lastPoint.Y,
                    X2 = currentMousePos.Value.X,
                    Y2 = currentMousePos.Value.Y,
                    Stroke = Brushes.Magenta,
                    StrokeThickness = lineThickness,
                    StrokeDashArray = s_dashArray,
                    Tag = "polygon_roi_drawing_preview",
                    IsHitTestVisible = false
                });

                // 如果有3个以上顶点，绘制闭合预览线
                if (_polygonPoints.Count >= 3)
                {
                    var firstPoint = _polygonPoints[0];
                    overlayCanvas.Children.Add(new Line
                    {
                        X1 = currentMousePos.Value.X,
                        Y1 = currentMousePos.Value.Y,
                        X2 = firstPoint.X,
                        Y2 = firstPoint.Y,
                        Stroke = Brushes.Lime,
                        StrokeThickness = lineThickness * 0.5,
                        StrokeDashArray = s_smallDashArray,
                        Tag = "polygon_roi_drawing_close_preview",
                        IsHitTestVisible = false
                    });
                }
            }
        }

        private void DrawRectRoi(RotatedRect roi, int index, bool isSelected, double lineThickness, double pointSize)
        {
            var corners = roi.GetCorners();
            overlayCanvas.Children.Add(new Polygon { Points = new PointCollection(corners), Fill = s_rectRoiFill, Stroke = isSelected ? Brushes.Lime : Brushes.Green, StrokeThickness = lineThickness, Tag = $"rect_roi_{index}", IsHitTestVisible = false });

            if (isSelected)
            {
                double halfPointSize = pointSize / 2;
                double largePointSize = pointSize * 1.2;
                double halfLargeSize = pointSize * 0.6;
                double smallPointSize = pointSize * 0.8;
                double halfSmallSize = pointSize * 0.4;
                double thinThickness = lineThickness * 0.5;

                // 中心点
                var center = new Ellipse { Width = largePointSize, Height = largePointSize, Fill = Brushes.Lime, Tag = $"rect_roi_{index}_center", IsHitTestVisible = false };
                Canvas.SetLeft(center, roi.CenterX - halfLargeSize); Canvas.SetTop(center, roi.CenterY - halfLargeSize);
                overlayCanvas.Children.Add(center);

                // 角点调整手柄
                for (int j = 0; j < 4; j++)
                {
                    var handle = new Rectangle { Width = pointSize, Height = pointSize, Fill = Brushes.White, Stroke = Brushes.Lime, StrokeThickness = thinThickness, Tag = $"rect_roi_{index}_corner_{j}", IsHitTestVisible = false };
                    Canvas.SetLeft(handle, corners[j].X - halfPointSize); Canvas.SetTop(handle, corners[j].Y - halfPointSize);
                    overlayCanvas.Children.Add(handle);
                }

                // 边缘中点调整手柄
                for (int j = 0; j < 4; j++)
                {
                    var edgeMid = GetMidPoint(corners[j], corners[(j + 1) % 4]);
                    var handle = new Ellipse { Width = smallPointSize, Height = smallPointSize, Fill = Brushes.White, Stroke = Brushes.Lime, StrokeThickness = thinThickness, Tag = $"rect_roi_{index}_edge_{j}", IsHitTestVisible = false };
                    Canvas.SetLeft(handle, edgeMid.X - halfSmallSize); Canvas.SetTop(handle, edgeMid.Y - halfSmallSize);
                    overlayCanvas.Children.Add(handle);
                }

                // 旋转手柄
                var rotatePos = GetRotateHandlePosition(roi);
                var topMid = GetMidPoint(corners[0], corners[1]);
                overlayCanvas.Children.Add(new Line { X1 = topMid.X, Y1 = topMid.Y, X2 = rotatePos.X, Y2 = rotatePos.Y, Stroke = Brushes.Lime, StrokeThickness = thinThickness, StrokeDashArray = s_smallDashArray, Tag = $"rect_roi_{index}_rotate_line", IsHitTestVisible = false });
                var rotateHandle = new Ellipse { Width = largePointSize, Height = largePointSize, Fill = Brushes.Orange, Stroke = Brushes.White, StrokeThickness = thinThickness, Tag = $"rect_roi_{index}_rotate", IsHitTestVisible = false };
                Canvas.SetLeft(rotateHandle, rotatePos.X - halfLargeSize); Canvas.SetTop(rotateHandle, rotatePos.Y - halfLargeSize);
                overlayCanvas.Children.Add(rotateHandle);
            }
        }

        private void DrawEllipseRoiShape(EllipseRoi e, int index, bool isSelected, double lineThickness, double pointSize)
        {
            var ellipse = new Ellipse { Width = e.RadiusX * 2, Height = e.RadiusY * 2, Fill = s_ellipseRoiFill, Stroke = isSelected ? Brushes.Blue : Brushes.DarkBlue, StrokeThickness = lineThickness, Tag = $"ellipse_roi_{index}", IsHitTestVisible = false };
            Canvas.SetLeft(ellipse, e.CenterX - e.RadiusX); Canvas.SetTop(ellipse, e.CenterY - e.RadiusY);
            if (Math.Abs(e.Angle) > 0.1) ellipse.RenderTransform = new RotateTransform(e.Angle, e.RadiusX, e.RadiusY);
            overlayCanvas.Children.Add(ellipse);

            if (isSelected)
            {
                double halfPointSize = pointSize / 2;
                double largePointSize = pointSize * 1.2;
                double halfLargeSize = pointSize * 0.6;
                double thinThickness = lineThickness * 0.5;

                // 中心点
                var center = new Ellipse { Width = largePointSize, Height = largePointSize, Fill = Brushes.Blue, Tag = $"ellipse_roi_{index}_center", IsHitTestVisible = false };
                Canvas.SetLeft(center, e.CenterX - halfLargeSize); Canvas.SetTop(center, e.CenterY - halfLargeSize);
                overlayCanvas.Children.Add(center);

                // 四个方向的调整手柄
                var handles = GetEllipseHandles(e);
                for (int j = 0; j < 4; j++)
                {
                    var handle = new Rectangle { Width = pointSize, Height = pointSize, Fill = Brushes.White, Stroke = Brushes.Blue, StrokeThickness = thinThickness, Tag = $"ellipse_roi_{index}_handle_{j}", IsHitTestVisible = false };
                    Canvas.SetLeft(handle, handles[j].X - halfPointSize); Canvas.SetTop(handle, handles[j].Y - halfPointSize);
                    overlayCanvas.Children.Add(handle);
                }

                // 旋转手柄
                var rotatePos = GetEllipseRotateHandlePosition(e);
                var topHandle = handles[0]; // 上方手柄
                overlayCanvas.Children.Add(new Line { X1 = topHandle.X, Y1 = topHandle.Y, X2 = rotatePos.X, Y2 = rotatePos.Y, Stroke = Brushes.Blue, StrokeThickness = thinThickness, StrokeDashArray = s_smallDashArray, Tag = $"ellipse_roi_{index}_rotate_line", IsHitTestVisible = false });
                var rotateHandle = new Ellipse { Width = largePointSize, Height = largePointSize, Fill = Brushes.Orange, Stroke = Brushes.White, StrokeThickness = thinThickness, Tag = $"ellipse_roi_{index}_rotate", IsHitTestVisible = false };
                Canvas.SetLeft(rotateHandle, rotatePos.X - halfLargeSize); Canvas.SetTop(rotateHandle, rotatePos.Y - halfLargeSize);
                overlayCanvas.Children.Add(rotateHandle);
            }
        }

        private void DrawPolygonRoiShape(PolygonRoi roi, int index, bool isSelected, double lineThickness, double pointSize)
        {
            if (roi.Points == null || roi.Points.Length < 3) return;

            overlayCanvas.Children.Add(new Polygon
            {
                Points = new PointCollection(roi.Points),
                Fill = isSelected ? s_polygonSelectedFill : s_polygonFill,
                Stroke = isSelected ? Brushes.Cyan : Brushes.Magenta,
                StrokeThickness = isSelected ? lineThickness * 1.5 : lineThickness,
                Tag = $"polygon_roi_{index}",
                IsHitTestVisible = false
            });

            if (isSelected)
            {
                double halfPointSize = pointSize / 2;
                double thinThickness = lineThickness * 0.5;

                // 绘制顶点手柄
                for (int j = 0; j < roi.Points.Length; j++)
                {
                    var handle = new Rectangle
                    {
                        Width = pointSize,
                        Height = pointSize,
                        Fill = Brushes.White,
                        Stroke = Brushes.Cyan,
                        StrokeThickness = thinThickness,
                        Tag = $"polygon_roi_{index}_vertex_{j}",
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(handle, roi.Points[j].X - halfPointSize);
                    Canvas.SetTop(handle, roi.Points[j].Y - halfPointSize);
                    overlayCanvas.Children.Add(handle);
                }

                double largePointSize = pointSize * 1.2;
                double halfLargeSize = pointSize * 0.6;

                // 绘制中心点
                var center = GetPolygonCenter(roi.Points);
                var centerPoint = new Ellipse
                {
                    Width = largePointSize,
                    Height = largePointSize,
                    Fill = Brushes.Cyan,
                    Tag = $"polygon_roi_{index}_center",
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(centerPoint, center.X - halfLargeSize);
                Canvas.SetTop(centerPoint, center.Y - halfLargeSize);
                overlayCanvas.Children.Add(centerPoint);
            }
        }

        private static Point GetPolygonCenter(Point[] points)
        {
            if (points == null || points.Length == 0) return new Point();
            double cx = 0, cy = 0;
            foreach (var p in points) { cx += p.X; cy += p.Y; }
            return new Point(cx / points.Length, cy / points.Length);
        }

        private void UpdateRoiInfoText()
        {
            var sb = new System.Text.StringBuilder();
            if (_rectRois.Count > 0) { sb.AppendLine($"矩形ROI: {_rectRois.Count}个"); for (int i = 0; i < Math.Min(_rectRois.Count, 3); i++) sb.AppendLine($"  #{i + 1}: {_rectRois[i].Width:F0}×{_rectRois[i].Height:F0}"); if (_rectRois.Count > 3) sb.AppendLine("  ..."); }
            if (_ellipseRois.Count > 0) sb.AppendLine($"椭圆ROI: {_ellipseRois.Count}个");
            if (_polygonRois.Count > 0) sb.AppendLine($"多边形ROI: {_polygonRois.Count}个");
            roiTextBlock.Visibility = sb.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            roiTextBlock.Text = sb.ToString().TrimEnd();
        }

        #endregion

        #region 角度测量

        public void ToggleAngleMeasureMode() { ExitAllModes(); _isAngleMeasuring = !_isAngleMeasuring; Cursor = _isAngleMeasuring ? Cursors.Cross : Cursors.Arrow; _anglePoints.Clear(); _selectedAngleMeasurementIndex = -1; }

        private void HandleAngleMeasureClick(Point screenPos)
        {
            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;

            double handleSize = GetScaledHandleSize();

            // 检查是否点击了已有角度测量的端点（用于拖动编辑）
            for (int i = 0; i < _angleMeasurements.Count; i++)
            {
                var m = _angleMeasurements[i];
                if (IsWithinHandleDistance(imageCoord.Value, m.P1, handleSize))
                {
                    _selectedAngleMeasurementIndex = i;
                    _isDraggingAnglePoint = true;
                    _draggingPointIndex = 0;
                    _roiDragStart = imageCoord.Value;
                    UpdateAllAngleMeasurementsDisplay();
                    return;
                }
                if (IsWithinHandleDistance(imageCoord.Value, m.Vertex, handleSize))
                {
                    _selectedAngleMeasurementIndex = i;
                    _isDraggingAnglePoint = true;
                    _draggingPointIndex = 1;
                    _roiDragStart = imageCoord.Value;
                    UpdateAllAngleMeasurementsDisplay();
                    return;
                }
                if (IsWithinHandleDistance(imageCoord.Value, m.P2, handleSize))
                {
                    _selectedAngleMeasurementIndex = i;
                    _isDraggingAnglePoint = true;
                    _draggingPointIndex = 2;
                    _roiDragStart = imageCoord.Value;
                    UpdateAllAngleMeasurementsDisplay();
                    return;
                }
            }

            // 检查是否点击了角度线（用于选中）
            for (int i = 0; i < _angleMeasurements.Count; i++)
            {
                var m = _angleMeasurements[i];
                if (IsPointNearLine(imageCoord.Value, m.P1, m.Vertex, handleSize) ||
                    IsPointNearLine(imageCoord.Value, m.Vertex, m.P2, handleSize))
                {
                    _selectedAngleMeasurementIndex = i;
                    _anglePoints.Clear();
                    UpdateAllAngleMeasurementsDisplay();
                    return;
                }
            }

            // 否则进行正常的角度绘制
            _selectedAngleMeasurementIndex = -1;
            _anglePoints.Add(imageCoord.Value);
            if (_anglePoints.Count == 3) { _angleMeasurements.Add((_anglePoints[0], _anglePoints[1], _anglePoints[2])); _anglePoints.Clear(); }
            UpdateAllAngleMeasurementsDisplay();
        }

        private void HandleAngleMeasureMouseUp()
        {
            _isDraggingAnglePoint = false;
            _draggingPointIndex = -1;
        }

        private void HandleAngleMeasureMouseMoveForDrag(Point screenPos)
        {
            if (!_isDraggingAnglePoint || _selectedAngleMeasurementIndex < 0) return;

            var imageCoord = GetImageCoordinate(screenPos);
            if (!imageCoord.HasValue) return;

            var m = _angleMeasurements[_selectedAngleMeasurementIndex];
            _angleMeasurements[_selectedAngleMeasurementIndex] = _draggingPointIndex switch
            {
                0 => (imageCoord.Value, m.Vertex, m.P2),
                1 => (m.P1, imageCoord.Value, m.P2),
                _ => (m.P1, m.Vertex, imageCoord.Value)
            };

            UpdateAllAngleMeasurementsDisplay();
            Cursor = Cursors.Hand;
        }

        private void HandleAngleMeasureMove(Point _) { if (_anglePoints.Count > 0) UpdateAllAngleMeasurementsDisplay(); }

        private void UpdateAllAngleMeasurementsDisplay()
        {
            RemoveOverlayByTag("angle_");
            double lineThickness = 2 / _currentScale, pointSize = 8 / _currentScale;

            for (int i = 0; i < _angleMeasurements.Count; i++)
                DrawAngleMeasurement(_angleMeasurements[i].P1, _angleMeasurements[i].Vertex, _angleMeasurements[i].P2, i, lineThickness, pointSize, i == _selectedAngleMeasurementIndex);

            if (_anglePoints.Count > 0)
            {
                var currentEnd = GetImageCoordinate(Mouse.GetPosition(rootGrid)) ?? (_anglePoints.Count > 0 ? _anglePoints[^1] : new Point());
                for (int i = 0; i < _anglePoints.Count; i++) overlayCanvas.Children.Add(CreatePoint(_anglePoints[i], pointSize, Brushes.Orange, $"angle_current_{i}"));
                if (_anglePoints.Count >= 1)
                {
                    var endPt = _anglePoints.Count >= 2 ? _anglePoints[1] : currentEnd;
                    overlayCanvas.Children.Add(new Line { X1 = _anglePoints[0].X, Y1 = _anglePoints[0].Y, X2 = endPt.X, Y2 = endPt.Y, Stroke = Brushes.Orange, StrokeThickness = lineThickness, Tag = "angle_current_line1", IsHitTestVisible = false });
                }
                if (_anglePoints.Count >= 2)
                    overlayCanvas.Children.Add(new Line { X1 = _anglePoints[1].X, Y1 = _anglePoints[1].Y, X2 = currentEnd.X, Y2 = currentEnd.Y, Stroke = Brushes.Orange, StrokeThickness = lineThickness, StrokeDashArray = s_dashArray, Tag = "angle_current_line2", IsHitTestVisible = false });
            }

            if (_angleMeasurements.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _angleMeasurements.Count; i++) sb.AppendLine($"#{i + 1}: {CalculateAngle(_angleMeasurements[i].P1, _angleMeasurements[i].Vertex, _angleMeasurements[i].P2):F2}°");
                angleTextBlock.Text = sb.ToString().TrimEnd(); angleTextBlock.Visibility = Visibility.Visible;
            }
            else angleTextBlock.Visibility = Visibility.Collapsed;
        }

        private void DrawAngleMeasurement(Point p1, Point vertex, Point p2, int index, double lineThickness, double pointSize, bool isSelected)
        {
            var lineColor = isSelected ? Brushes.Cyan : Brushes.Orange;
            var pointColor = isSelected ? Brushes.Lime : Brushes.Orange;
            var actualThickness = isSelected ? lineThickness * 1.5 : lineThickness;
            var actualPointSize = isSelected ? pointSize * 1.3 : pointSize;

            overlayCanvas.Children.Add(new Line { X1 = p1.X, Y1 = p1.Y, X2 = vertex.X, Y2 = vertex.Y, Stroke = lineColor, StrokeThickness = actualThickness, Tag = $"angle_{index}_line1", IsHitTestVisible = false });
            overlayCanvas.Children.Add(new Line { X1 = vertex.X, Y1 = vertex.Y, X2 = p2.X, Y2 = p2.Y, Stroke = lineColor, StrokeThickness = actualThickness, Tag = $"angle_{index}_line2", IsHitTestVisible = false });
            overlayCanvas.Children.Add(CreatePoint(p1, actualPointSize, pointColor, $"angle_{index}_p1"));
            overlayCanvas.Children.Add(CreatePoint(vertex, actualPointSize * 1.2, pointColor, $"angle_{index}_vertex"));
            overlayCanvas.Children.Add(CreatePoint(p2, actualPointSize, pointColor, $"angle_{index}_p2"));

            double angle = CalculateAngle(p1, vertex, p2);
            double startAngle = Math.Atan2(p1.Y - vertex.Y, p1.X - vertex.X);
            double endAngle = Math.Atan2(p2.Y - vertex.Y, p2.X - vertex.X);
            double arcRadius = 20 / _currentScale;
            var startPoint = new Point(vertex.X + arcRadius * Math.Cos(startAngle), vertex.Y + arcRadius * Math.Sin(startAngle));
            var endPoint = new Point(vertex.X + arcRadius * Math.Cos(endAngle), vertex.Y + arcRadius * Math.Sin(endAngle));

            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = vertex };
            figure.Segments.Add(new LineSegment(startPoint, true));
            figure.Segments.Add(new ArcSegment(endPoint, new Size(arcRadius, arcRadius), 0, angle > 180, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(vertex, true));
            geometry.Figures.Add(figure);
            overlayCanvas.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Stroke = Brushes.Orange, StrokeThickness = lineThickness, Fill = s_angleFill, Tag = $"angle_{index}_arc", IsHitTestVisible = false });
        }

        private static double CalculateAngle(Point p1, Point vertex, Point p2)
        {
            double v1x = p1.X - vertex.X, v1y = p1.Y - vertex.Y, v2x = p2.X - vertex.X, v2y = p2.Y - vertex.Y;
            return Math.Atan2(Math.Abs(v1x * v2y - v1y * v2x), v1x * v2x + v1y * v2y) * RadToDeg;
        }

        private void ClearAngleMeasurement() { _anglePoints.Clear(); _angleMeasurements.Clear(); _selectedAngleMeasurementIndex = -1; angleTextBlock.Visibility = Visibility.Collapsed; UpdateAllAngleMeasurementsDisplay(); }
        public IReadOnlyList<(Point P1, Point Vertex, Point P2)> GetAngleMeasurements() => _angleMeasurements.AsReadOnly();

        #endregion

        #region 物理单位

        public void SetPhysicalUnit(double pixelsPerUnit, string unitName)
        {
            _pixelsPerUnit = pixelsPerUnit > 0 ? pixelsPerUnit : 1.0;
            _physicalUnit = string.IsNullOrEmpty(unitName) ? "px" : unitName;
            UpdateInfoPanel();
        }

        public double GetPhysicalDistance(Point p1, Point p2)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            return Math.Sqrt(dx * dx + dy * dy) / _pixelsPerUnit;
        }
        public double GetPhysicalArea(double pixelArea) => pixelArea / (_pixelsPerUnit * _pixelsPerUnit);
        public string PhysicalUnit => _physicalUnit;
        public double PixelsPerUnit => _pixelsPerUnit;

        #endregion

        #region 辅助方法和清理

        private void ExitAllModes()
        {
            if (_isMeasuring) { _isMeasuring = false; _measurePoint1 = _measurePoint2 = null; _selectedDistanceMeasurementIndex = -1; _isDraggingMeasurePoint = false; }
            if (_isAngleMeasuring) { _isAngleMeasuring = false; _anglePoints.Clear(); _selectedAngleMeasurementIndex = -1; _isDraggingAnglePoint = false; }
            if (_isRoiMode) { _isRoiMode = _isDrawingRoi = _isDraggingRoi = _isResizingRoi = _isRotatingRoi = false; _currentRoi = null; _selectedRectRoiIndex = -1; }
            if (_isEllipseRoiMode) { _isEllipseRoiMode = _isDrawingEllipseRoi = _isDraggingEllipseRoi = _isResizingEllipseRoi = _isRotatingEllipseRoi = false; _currentEllipseRoi = null; _selectedEllipseRoiIndex = -1; }
            if (_isPolygonRoiMode) { _isPolygonRoiMode = _isDrawingPolygonRoi = _isDraggingPolygonRoi = _isDraggingPolygonVertex = false; _polygonPoints.Clear(); _selectedPolygonRoiIndex = -1; }
            Cursor = Cursors.Arrow; UpdateOverlayScale();
        }

        private void ClearAllRoi() { ClearRoi(); ClearEllipseRoi(); ClearPolygonRoi(); ClearMeasurement(); ClearAngleMeasurement(); UpdateAllRoiDisplay(); }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // H28: 取消所有事件订阅，避免内存泄漏
            rootGrid.MouseWheel -= OnMouseWheel;
            rootGrid.MouseDown -= OnMouseDown;
            rootGrid.MouseUp -= OnMouseUp;
            rootGrid.MouseMove -= OnMouseMove;
            rootGrid.MouseLeave -= OnMouseLeave;
            rootGrid.SizeChanged -= OnSizeChanged;
            KeyDown -= OnKeyDown;
            Drop -= OnDrop;
            DragOver -= OnDragOver;
            // M229: 清理 ContextMenu 及其闭包引用，避免内存泄漏
            rootGrid.ContextMenu = null;

            // M222: 取消未完成的像素缓存更新任务
            _pixelCacheCts?.Cancel();

            if (_currentWriteableBitmap != null)
            {
                // 如果 WriteableBitmap 未被冻结，则尝试移除事件处理程序
                // L29: -= 操作本身是安全的（移除未注册的处理程序不会抛异常），无需 try-catch
                if (!_currentWriteableBitmap.IsFrozen)
                {
                    _currentWriteableBitmap.Changed -= OnWriteableBitmapChanged;
                }
                _currentWriteableBitmap = null;
            }

            lock (_imageLock)
            {
                _cachedBitmapSource = null;
                _pixelCache = null;
            }

            // 清理集合
            _distanceMeasurements.Clear();
            _angleMeasurements.Clear();
            _anglePoints.Clear();
            _polygonPoints.Clear();
            _rectRois.Clear();
            _ellipseRois.Clear();
            _polygonRois.Clear();
        }

        #endregion
    }
}