using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoordinateSystemMapping;
using Extensions;
using HikScanner;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Extensions;
using JinlongYolo.YoloSharp.Plotting;
using MainAPP.Application;
using MainAPP.Models;
using MainAPP.Services;
using MainAPP.Views;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Image = SixLabors.ImageSharp.Image;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;
// ============================================================
// Split from the original monolithic file: RecipeViewModel live display + calibration debug
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class RecipeViewModel
    {
        #region 实时显示

        private async Task StartLiveDisplayAsync()
        {
            // H51b/L395a: 只 Cancel 不 Dispose，避免后台任务访问已 Dispose 的 token 抛 ObjectDisposedException；
            // 旧 _liveDisplayCts 有意不在此处 Dispose（由 GC 回收），Dispose 中显式释放
            // VSTHRD103: CancellationTokenSource.Cancel() 本身是同步方法，分析器误报
#pragma warning disable VSTHRD103
            _liveDisplayCts?.Cancel();
#pragma warning restore VSTHRD103

            // 与测试推理互斥：推理正占用软触发取帧通道，此时开启实时显示会互相抢帧
            if (_inferenceTask is { IsCompleted: false })
            {
                SetProperty(ref _isLiveDisplayEnabled, false);
                ShowWarning("推理进行中，请等待推理结束再开启实时显示。");
                return;
            }

            _liveDisplayCts = new CancellationTokenSource();
            var token = _liveDisplayCts.Token;
            // M67: 捕获 token 后使用前检查是否已取消（StopLiveDisplay 可能在 await 期间被调用）
            if (token.IsCancellationRequested)
            {
                return;
            }
            if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
            {
                // M14: 启动失败时回滚 IsLiveDisplayEnabled 状态
                SetProperty(ref _isLiveDisplayEnabled, false);
                return;
            }
            try
            {
                await _scannerService.StartLiveDisplayAsync(UpdateImageForShow, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"实时显示异常: {ex}");
                // M274a: catch 块中先调用 StopLiveDisplay 取消 CTS，再回滚状态
                StopLiveDisplay();
                SetProperty(ref _isLiveDisplayEnabled, false);
            }
        }

        private void StopLiveDisplay()
        {
            // M67: 只 Cancel，不立即 Dispose CTS，避免后台任务访问已 Dispose 的 token 抛 ObjectDisposedException；
            // CTS 将在 StartLiveDisplayAsync 重新赋值前或 Dispose 中显式释放
            _liveDisplayCts?.Cancel();
        }

        #endregion

        #region 调试信息
        // L331: 改为带 INotifyPropertyChanged 的属性
        private bool _isShowDebug;
        public bool IsShowDebug { get => _isShowDebug; set => SetProperty(ref _isShowDebug, value); }

        private void DebugCallback(string message, Mat mat)
        {
            if (!IsShowDebug)
            {
                return;
            }
            // REVIEW(2026-08-05): 此回调在后台线程（GetThreePoints 的 Task.Run）执行，
            // OpenCV HighGUI（NamedWindow/ImShow）在 WPF 非 UI 线程调用易触发 SEH/AccessViolation，
            // 且 App 对这类异常不 Handle 直接崩退。这里 try-catch 兜底，防止标定流程崩溃。
            try
            {
                Cv2.NamedWindow(message, WindowFlags.Normal);
                Cv2.ImShow(message, mat);
                if (message.Contains("调试图像", StringComparison.Ordinal))
                {
                    Cv2.WaitKey(1);
                    Cv2.DestroyWindow(message);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"调试图像显示失败(已跳过): {ex.Message}");
            }
        }

        #region 查找参考点
        /// <summary>
        /// 查找参考点（三个圆点和原点）
        /// </summary>
        [RelayCommand]
        private async Task FindReferencePointsAsync()
        {
            if (_originalMat == null || _originalMat.Empty())
            {
                ShowWarning("尚未获取图像");
                return;
            }

            if (CoordinateTool == null)
            {
                ShowWarning("坐标工具未初始化");
                return;
            }

            // L: 标定进行中标志置位，UI 切换为"标定中..."并禁用按钮
            IsCalibrating = true;
            try
            {
                // 使用原始图像进行检测
                using var grayMat = _originalMat.Channels() == 1 ? _originalMat.Clone() : _originalMat.CvtColor(ColorConversionCodes.BGR2GRAY);
                // M298a: Chessboard.GetThreePoints 签名不接受 enableDebug 参数，只能通过 static 属性控制调试输出
                Chessboard.EnableDebugOutput = IsShowDebug;
                // L: 将 CPU 密集型圆点检测放到后台线程执行，避免阻塞 UI；支持取消
                var token = _cts.Token;
                var detectedRects = await Task.Run(() => Chessboard.GetThreePoints(grayMat,
                    patternSize: CoordinateTool.PatternSize,
                    roiPadding: CoordinateTool.RoiPadding,
                    minArea: CoordinateTool.MinArea,
                    maxArea: CoordinateTool.MaxArea,
                    minCircularity: CoordinateTool.MinCircularity,
                    bilateralD: CoordinateTool.BilateralD,
                    bilateralSigmaColor: CoordinateTool.BilateralSigmaColor,
                    bilateralSigmaSpace: CoordinateTool.BilateralSigmaSpace,
                    cannyThreshold1: CoordinateTool.CannyThreshold1,
                    cannyThreshold2: CoordinateTool.CannyThreshold2,
                    kernelSize: CoordinateTool.KernelSize,
                    enableCLAHE: CoordinateTool.EnableCLAHE,
                    enableMultiStrategy: CoordinateTool.EnableMultiStrategy,
                    debugCallback: DebugCallback), token).ConfigureAwait(true);

                // 将 RotatedRect 转换为 Point2f 用于坐标系
                var circles = detectedRects.Select(r => r.Center).ToList();

                if (circles.Count < 3)
                {
                    ShowWarning($"仅检测到 {circles.Count} 个圆点，需要3个圆点来确定坐标系");
                    return;
                }
                if (CoordinateTool.SquareSize <= 0)
                {
                    ShowWarning("方格尺寸必须大于0");
                    return;
                }

                // L358a: _coordinateSystem 改为局部变量
                var coordinateSystem = Chessboard.GetCoordinateSystem(circles);
                var origin = coordinateSystem.Origin;
                var xPoint = coordinateSystem.XPoint;
                var yPoint = coordinateSystem.YPoint;

                var pixelDistX = Math.Sqrt(Math.Pow(xPoint.X - origin.X, 2) + Math.Pow(xPoint.Y - origin.Y, 2));
                var pixelDistY = Math.Sqrt(Math.Pow(yPoint.X - origin.X, 2) + Math.Pow(yPoint.Y - origin.Y, 2));
                var pixelPerSquareX = pixelDistX / 2d;
                var pixelPerSquareY = pixelDistY;

                // 更新坐标工具的参考点
                CoordinateTool.Origin = origin;
                CoordinateTool.XPoint = xPoint;
                CoordinateTool.YPoint = yPoint;
                OnPropertyChanged(nameof(CoordinateTool));

                // 在原始图像上绘制参考点
                using var colorMat = _originalMat.Channels() == 1 ? _originalMat.CvtColor(ColorConversionCodes.GRAY2BGR) : _originalMat.Clone();
                DrawReferencePoints(colorMat, coordinateSystem, circles);
                UpdateImageForShow(colorMat.ToBitmapSource());

                ShowInfo($"成功找到参考点\n原点: ({origin.X:F2}, {origin.Y:F2})\nX轴: ({xPoint.X:F2}, {xPoint.Y:F2})\nY轴: ({yPoint.X:F2}, {yPoint.Y:F2})\n方格尺寸: {CoordinateTool.SquareSize}\nX方向每格像素: {pixelPerSquareX:F2}\nY方向每格像素: {pixelPerSquareY:F2}");
                // P2-19: 标定完成后通知 IsCalibrated 属性，刷新向导状态
                OnPropertyChanged(nameof(IsCalibrated));
            }
            catch (OperationCanceledException)
            {
                // L: 标定被取消（通常由 Dispose 触发），静默退出
            }
            catch (Exception ex)
            {
                // REVIEW(2026-08-05): 兜底捕获所有异常——此方法由 RelayCommand 直接调用，
                // 未捕获异常会经 Dispatcher/AppDomain 通道处理，重则直接崩退（SEH 等不 Handle）。
                LogService.Instance.Error($"查找基准点失败: {ex}");
                ShowWarning($"查找基准点失败: {ex.Message}");
            }
            finally
            {
                IsCalibrating = false;
            }
        }
        #endregion
        #region 在图像上绘制参考点
        /// <summary>
        /// 在图像上绘制参考点
        /// </summary>
        private void DrawReferencePoints(Mat mat, ThreePointCoordinateSystem coordinateSystem, List<Point2f> allCircles)
        {
            // 绘制检测到的圆点（灰色）
            foreach (var circle in allCircles)
            {
                Cv2.Circle(mat, new OpenCvSharp.Point((int)circle.X, (int)circle.Y), 10, Scalar.Gray, 2);
            }
            coordinateSystem.DrawOnMat(mat);
        }
        #endregion
        #region 标定量化验证
        /// <summary>
        /// 标定结果数值汇总文本（三点像素坐标 + X/Y 方向每格像素当量），供「坐标系创建」页展示。
        /// 未标定时显示引导文案；由状态周期刷新与标定完成路径共同更新。
        /// </summary>
        public string CalibrationSummaryText
        {
            get
            {
                var calib = CoordinateTool;
                if (calib is null || !IsCalibrated)
                {
                    return "尚未完成标定 — 先采图并执行「查找基准点」。";
                }

                double pixelDistX = Math.Sqrt(Math.Pow(calib.XPoint.X - calib.Origin.X, 2) + Math.Pow(calib.XPoint.Y - calib.Origin.Y, 2));
                double pixelDistY = Math.Sqrt(Math.Pow(calib.YPoint.X - calib.Origin.X, 2) + Math.Pow(calib.YPoint.Y - calib.Origin.Y, 2));

                return $"原点: ({calib.Origin.X:F1}, {calib.Origin.Y:F1}) px\n"
                     + $"X轴点: ({calib.XPoint.X:F1}, {calib.XPoint.Y:F1}) px\n"
                     + $"Y轴点: ({calib.YPoint.X:F1}, {calib.YPoint.Y:F1}) px\n"
                     + $"方格尺寸: {calib.SquareSize:F2} mm/格\n"
                     + $"X方向每格: {pixelDistX / 2d:F2} px（= {calib.SquareSize} mm）\n"
                     + $"Y方向每格: {pixelDistY:F2} px（= {calib.SquareSize} mm）";
            }
        }

        /// <summary>
        /// 在当前图像上叠加标定三点标记（洋红=原点/青=X轴/橙=Y轴）并刷新显示。
        /// 纯显示辅助，不修改配方数据；无图或无标定时提示。
        /// </summary>
        [RelayCommand]
        private void OverlayCalibrationPoints()
        {
            if (CoordinateTool is null)
            {
                ShowWarning("坐标工具未初始化");
                return;
            }
            if (_originalMat is null || _originalMat.Empty())
            {
                ShowWarning("尚未获取图像");
                return;
            }

            using var colorMat = _originalMat.Channels() == 1
                ? _originalMat.CvtColor(ColorConversionCodes.GRAY2BGR)
                : _originalMat.Clone();

            Cv2.Circle(colorMat, new OpenCvSharp.Point((int)CoordinateTool.Origin.X, (int)CoordinateTool.Origin.Y), 12, new Scalar(255, 0, 255), 2, LineTypes.AntiAlias);
            Cv2.Circle(colorMat, new OpenCvSharp.Point((int)CoordinateTool.XPoint.X, (int)CoordinateTool.XPoint.Y), 12, new Scalar(255, 255, 0), 2, LineTypes.AntiAlias);
            Cv2.Circle(colorMat, new OpenCvSharp.Point((int)CoordinateTool.YPoint.X, (int)CoordinateTool.YPoint.Y), 12, new Scalar(0, 165, 255), 2, LineTypes.AntiAlias);

            UpdateImageForShow(colorMat.ToBitmapSource());
            OnPropertyChanged(nameof(CalibrationSummaryText));
        }
        #endregion
        #endregion
    }
}