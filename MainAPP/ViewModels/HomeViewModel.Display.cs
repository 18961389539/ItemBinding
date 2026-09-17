using CommunityToolkit.Mvvm.ComponentModel;
using Extensions;
using HikScanner;
using HikScannerType = HikScanner.HikScanner;
using MainAPP.Application;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using System.Windows.Threading;
using MainAPP.Models;
using MainAPP.Services;
using OpenCvSharp;
using CommunityToolkit.Mvvm.Messaging;
using MainAPP.Messages;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;

using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using System.IO;
using JinlongYolo.YoloSharp.Extensions;
using System.Runtime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using Serilog;
using MainAPP;
using Microsoft.Extensions.DependencyInjection;

// ============================================================
// Split from the original monolithic file: HomeViewModel display + frame metrics
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class HomeViewModel
    {
        private readonly object _imageLock = new();
        private ImageSource? _imageForShow;
        /// <summary>
        /// 复用的 WriteableBitmap，避免每帧 new 导致 LOH 碎片。
        /// </summary>
        private WriteableBitmap? _reusableShowBitmap;
        public ImageSource? ImageForShow
        {
            get { return _imageForShow; }
            set
            {
                // REVIEW-FIX (跨线程回归修复): 未冻结的 WriteableBitmap 由 UI 线程创建，
                // 赋给绑定（DependencyProperty）也必须在 UI 线程，否则 WPF 抛
                // "必须与 DependencyObject 相同的 Thread 上创建 DependencySource"。
                // 推理线程调用 setter 时转发到 UI 线程执行（含 OnPropertyChanged 与绑定更新）。
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    // VSTHRD110: discard 观察 InvokeAsync 结果（异步转发，无需等待）
                    _ = dispatcher.InvokeAsync(() => ImageForShow = value);
                    return;
                }

                // H87a: 加锁保护 setter，避免并发推理线程导致 double Dispose 或 GDI+ 句柄泄漏
                lock (_imageLock)
                {
                    // WriteableBitmap 原生支持跨线程，无需 Freeze（复用场景下 Freeze 会导致下次 WritePixels 失败）
                    if (value is Freezable freezable && !freezable.IsFrozen && value is not WriteableBitmap)
                    {
                        freezable.Freeze();
                    }

                    // 释放旧图像源（复用的 WriteableBitmap 不从 setter 释放）
                    if (_imageForShow is IDisposable disposable && !ReferenceEquals(_imageForShow, value))
                    {
                        disposable.Dispose();
                    }

                    _imageForShow = value;
                }
                OnPropertyChanged();
            }
        }

    // 推理结果详细信息列表
    private ObservableCollection<InferenceResultItem> _inferenceResults = new();
    /// <summary>
    /// 推理结果详细信息列表，供 UI DataGrid 绑定展示每个目标的类别、置信度、坐标与角度。
    /// </summary>
    public ObservableCollection<InferenceResultItem> InferenceResults
    {
        get => _inferenceResults;
        private set => SetProperty(ref _inferenceResults, value);
    }

    /// <summary>当前结果总数，供 UI 标题栏显示。</summary>
    public int ResultCount => InferenceResults.Count;

    // P0-2: 扫描枪连接状态指示
    private ScannerConnectionState _scannerStatus = ScannerConnectionState.Disconnected;

    /// <summary>
    /// 扫描枪连接状态（Disconnected/Connecting/Connected）。
    /// </summary>
    public ScannerConnectionState ScannerStatus
    {
        get => _scannerStatus;
        set
        {
            if (SetProperty(ref _scannerStatus, value))
            {
                OnPropertyChanged(nameof(ScannerStatusText));
                OnPropertyChanged(nameof(ScannerStatusColor));
            }
        }
    }

    /// <summary>
    /// 扫描枪连接状态文本（"已连接"/"未连接"/"重连中"），供 UI 绑定。
    /// </summary>
    public string ScannerStatusText => ScannerStatus switch
    {
        ScannerConnectionState.Connected => "已连接",
        ScannerConnectionState.Connecting => "重连中",
        _ => "未连接"
    };

    /// <summary>
    /// 扫描枪连接状态颜色（绿色/红色/橙色），使用十六进制字符串便于 XAML 绑定。
    /// </summary>
    public string ScannerStatusColor => ScannerStatus switch
    {
        ScannerConnectionState.Connected => "#4CAF50",   // 绿色
        ScannerConnectionState.Connecting => "#FF9800", // 橙色
        _ => "#F44336"                                   // 红色
    };

    /// <summary>
    /// 累计丢帧/跳帧数，供状态栏显示。
    /// </summary>
    public int DropFrameCount
    {
        get => _dropFrameCount;
        private set
        {
            if (SetProperty(ref _dropFrameCount, value))
            {
                OnPropertyChanged(nameof(DropFrameDisplay));
            }
        }
    }

    /// <summary>状态栏显示用的丢帧文本。</summary>
    public string DropFrameDisplay => $"丢帧: {_dropFrameCount}";

    /// <summary>状态栏显示：丢帧率（HealthSampleWindowSec 滑动窗口）。</summary>
    public string DropRateDisplay
    {
        get => _dropRateDisplay;
        private set => SetProperty(ref _dropRateDisplay, value);
    }
    private string _dropRateDisplay = "丢帧率: --";

    /// <summary>状态栏显示：编码器绑定延迟（最近一帧）。</summary>
    public string BindDelayDisplay
    {
        get => _bindDelayDisplay;
        private set => SetProperty(ref _bindDelayDisplay, value);
    }
    private string _bindDelayDisplay = "绑定延迟: --";

    /// <summary>状态栏显示：编码器链路状态（数据新鲜度）。</summary>
    public string EncoderLinkDisplay
    {
        get => _encoderLinkDisplay;
        private set => SetProperty(ref _encoderLinkDisplay, value);
    }
    private string _encoderLinkDisplay = "编码器链路: --";

    /// <summary>
    /// 状态栏显示：当前推理后端（2026-09-16 新增）。
    ///
    /// <para>背景：推理后端会随自愈降级（CUDA → OpenVINO GPU → OpenVINO CPU → CPU），
    /// 但此前只写进 Timing 日志字段与降级通知，<b>界面上没有任何指示</b>——
    /// 现场遇到"变慢了 / 判定变得不一样"时，无法一眼判断此刻跑在 GPU 还是 CPU，
    /// 而这恰恰是排查该类问题的第一条线索。</para>
    /// </summary>
    public string InferenceBackendDisplay
    {
        get => _inferenceBackendDisplay;
        private set => SetProperty(ref _inferenceBackendDisplay, value);
    }
    private string _inferenceBackendDisplay = "推理后端: 初始化中…";

    /// <summary>当前推理后端是否已降级（非 CUDA）。HUD 据此变色提示。</summary>
    public bool IsInferenceDegraded
    {
        get => _isInferenceDegraded;
        private set => SetProperty(ref _isInferenceDegraded, value);
    }
    private bool _isInferenceDegraded;

    /// <summary>
    /// 更新"当前推理后端"的唯一入口（可从任意线程调用；非 UI 线程会转发到 UI 线程再通知属性）。
    /// 同时更新 <c>_inferenceDevice</c>（Timing 日志用），保证画面与日志同源。
    /// </summary>
    /// <param name="deviceName">后端显示名，如 CUDA / OpenVINO GPU / CPU。</param>
    /// <param name="backend">后端枚举；非 CUDA 视为已降级。为 null 时按 CUDA 处理（模型加载器未给出后端时）。</param>
    internal void SetInferenceBackend(string deviceName, InferenceBackend? backend = null)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return;
        }

        var degraded = backend is not null && backend != InferenceBackend.Cuda;
        _inferenceDevice = deviceName;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            // VSTHRD110: discard 观察 InvokeAsync 结果（异步转发，无需等待）
            _ = dispatcher.InvokeAsync(() => ApplyInferenceBackendDisplay(deviceName, degraded));
            return;
        }

        ApplyInferenceBackendDisplay(deviceName, degraded);
    }

    private void ApplyInferenceBackendDisplay(string deviceName, bool degraded)
    {
        InferenceBackendDisplay = degraded
            ? $"推理后端: {deviceName}（已降级）"
            : $"推理后端: {deviceName}";
        IsInferenceDegraded = degraded;
    }

    /// <summary>
    /// 每秒健康巡检：丢帧率（处理/丢帧增量）、绑定延迟展示、编码器链路新鲜度。
    /// 全部在 UI 线程执行（DispatcherTimer.Tick），只做轻量聚合与属性通知。
    /// </summary>
    private void OnHealthTick(object? sender, EventArgs e)
    {
        // 1) 编码器链路新鲜度
        try
        {
            var last = _toVgtService.LastEncoderReceiveTime;
            if (last is null)
            {
                EncoderLinkDisplay = "编码器链路: 无数据";
            }
            else
            {
                var ageSec = (DateTime.Now - last.Value).TotalSeconds;
                EncoderLinkDisplay = ageSec > 5
                    ? $"编码器链路: 超时 {ageSec:F0}s"
                    : $"编码器链路: 正常({ageSec:F1}s 前)";
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"编码器链路状态刷新失败: {ex.Message}");
        }

        // 2) 丢帧率（窗口 = HealthSampleWindowSec）
        var processed = Interlocked.Read(ref _processedFrameCount);
        var dropped = _dropFrameCount;
        var dProcessed = processed - _lastHealthProcessed;
        var dDropped = dropped - _lastHealthDropped;
        _lastHealthProcessed = processed;
        _lastHealthDropped = dropped;

        if (dProcessed >= MinSamplesForRate)
        {
            var ratePercent = dDropped * 100.0 / dProcessed;
            DropRateDisplay = $"丢帧率: {ratePercent:F1}% ({dDropped}/{dProcessed})";

            if (ratePercent >= DropRateWarnPercent
                && (DateTime.Now - _lastDropRateWarnAt).TotalSeconds >= HealthWarnThrottleSec)
            {
                _lastDropRateWarnAt = DateTime.Now;
                LogService.Instance.Warning(
                    $"[采集过载] 丢帧率 {ratePercent:F1}%（{dDropped}/{dProcessed}，窗口 {HealthSampleWindowSec}s）" +
                    "——处理能力不足，存在绑定系统性错位风险，请扩容推理或降低触发频率");
            }
        }
        else if (dProcessed == 0)
        {
            DropRateDisplay = "丢帧率: -- (无帧)";
        }

        // 3) 绑定延迟展示
        var bindDelayMs = Volatile.Read(ref _lastBindDelayMs);
        BindDelayDisplay = _lastBindDelayMs == 0
            ? "绑定延迟: --"
            : $"绑定延迟: {bindDelayMs:F0} ms";
    }

    /// <summary>
    /// 已处理帧计数（用于 FPS 计算），每处理完一帧递增。
    /// 与检测结果无关，即使没有检测到目标也会计数。
    /// </summary>
    public long ProcessedFrameCount => Interlocked.Read(ref _processedFrameCount);

    /// <summary>
    /// 线程安全地累计丢帧数并通知 UI（WPF 绑定引擎支持跨线程 PropertyChanged）。
    /// </summary>
    private void IncrementDropFrameCount(int count = 1)
    {
        if (count <= 0) return;
        Interlocked.Add(ref _dropFrameCount, count);
        OnPropertyChanged(nameof(DropFrameCount));
        OnPropertyChanged(nameof(DropFrameDisplay));
        // 通过 messenger 推送最新帧指标给 MainViewModel，替代原 Func 反向回调
        SendFrameMetrics();
    }

    /// <summary>
    /// 通过 WeakReferenceMessenger 广播当前帧计数指标（ProcessedFrameCount / DropFrameCount），
    /// 供 MainViewModel 订阅以更新 FPS / 丢帧显示。
    /// 替代原 MainViewModel.GetProcessedFrameCount / GetDropFrameCount 的反向回调。
    /// </summary>
    private void SendFrameMetrics()
    {
        try
        {
            WeakReferenceMessenger.Default.Send(new FrameMetricsMessage(
                Interlocked.Read(ref _processedFrameCount),
                _dropFrameCount));
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"SendFrameMetrics 失败: {ex}");
        }
    }

    /// <summary>
    /// 丢帧通知节流：首次立即提示，之后每 DropFrameNotifyInterval 次提示一次，避免每帧弹窗刷屏。
    /// </summary>
    private void NotifyFrameDrop(string message, bool isError = false)
    {
        int n = Interlocked.Increment(ref _dropNotifyCounter);
        if (n == 1 || n % DropFrameNotifyInterval == 0)
        {
            if (isError) NotificationService.Error(message);
            else NotificationService.Warning(message);
        }
    }

    /// <summary>
    /// 线程安全地更新扫描枪连接状态。
    /// 后台线程通过 Dispatcher 切换到 UI 线程，确保 PropertyChanged 事件在 UI 线程触发。
    /// </summary>
    private void UpdateScannerStatus(ScannerConnectionState newState)
    {
        // 快速路径：状态未变化时直接返回，避免不必要的 Dispatcher 调度
        if (_scannerStatus == newState)
        {
            return;
        }

        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null)
            {
                _ = dispatcher.BeginInvoke(new Action(() => ScannerStatus = newState));
            }
            else
            {
                ScannerStatus = newState;
            }
        }
        catch
        {
            // Dispatcher 不可用时直接设置（INPC 在 WPF 中支持跨线程绑定更新）
            ScannerStatus = newState;
        }
    }

    }
}