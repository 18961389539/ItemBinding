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
// Split from the original monolithic file: HomeViewModel main loop + inference scheduling
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class HomeViewModel
    {
        #region Loop
        /// <summary>
        /// 主循环：完成一系列校验后启动图像读取与处理的后台任务。
        /// </summary>
        /// <remarks>
        /// 该方法不在 UI 线程阻塞，它会启动两个后台任务：一个用于从扫描设备读取图像，另一个用于处理队列中的图像。
        /// 若需要提高吞吐量，可调整队列长度（MaxImagesinQueue）与处理并发策略；若遇到并发问题，可通过配置 SuppressParallelInference。
        /// </remarks>
        private async Task Loop()
        {
            // L362c: 配方可能在 HomeViewModel 构造后才初始化（App.OnStartup 中 base.OnStartup 先于 InitializeRecipes 执行），
            // 因此 TryEnsureProcessingReady 失败时不能直接 return，需等待配方就绪后继续
            while (!_cts.Token.IsCancellationRequested)
            {
                if (TryEnsureProcessingReady(out var errorMessage))
                {
                    _lastProcessingReadyError = null;
                    break;
                }
                // 只在错误消息变化时记录日志，避免重复刷屏
                if (_lastProcessingReadyError != errorMessage)
                {
                    LogService.Instance.Warning(errorMessage);
                    _lastProcessingReadyError = errorMessage;
                }
                try
                {
                    await Task.Delay(ProcessingReadyRetryMs, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            if (_cts.Token.IsCancellationRequested)
            {
                return;
            }
            // H40: 扫描枪就绪检查 —— 启动阶段静默等待，初始化完成后才报 Warning
            HikScannerType? scanner = null;
            while (!_cts.Token.IsCancellationRequested)
            {
                scanner = Devices.Scanners.HikScaner;
                if (scanner is not null && scanner.IsConnected)
                {
                    break;
                }

                // 仅在已检测到扫码枪后仍断连时才报 Warning（启动阶段静默等待）
                if (Devices.Scanners.HasScanners)
                {
                    LogService.Instance.Warning("扫描枪未连接，10 秒后重试...");
                    // P0-2: 已检测到扫码枪但未连接，标记为"重连中"（橙色）
                    UpdateScannerStatus(ScannerConnectionState.Connecting);
                }
                else
                {
                    // P0-2: 扫码枪尚未检测到，标记为"未连接"（红色）
                    UpdateScannerStatus(ScannerConnectionState.Disconnected);
                }

                await Task.Delay(10_000, _cts.Token).ConfigureAwait(false);
            }

            scanner ??= Devices.Scanners.HikScaner;
            if (scanner is null)
            {
                LogService.Instance.Error("扫描枪未初始化，无法启动图像读取循环。");
                // P0-2: 扫码枪未初始化
                UpdateScannerStatus(ScannerConnectionState.Disconnected);
                return;
            }

            // P0-2: 扫描枪已就绪，更新连接状态为已连接（绿色）
            UpdateScannerStatus(ScannerConnectionState.Connected);

            // 记录处理循环启动前的内存基线
            MemoryDiagnostics.LogSnapshot("LoopStart");

            // 启动后台图像读取循环，读取出的 FrameResult 被入队供处理任务消费。
            _readImageLoopTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    // M700: 配方页打开时跳过整个取帧循环，杜绝 ReadImageOneLoop 进入 AcquireAsync 的竞态
                    if (s_isPaused)
                    {
                        await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                        continue;
                    }

                    try
                    {
                        await ReadImageOneLoop(scanner).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error($"Loop 主循环捕获未处理异常: {ex}");
                        TemporaryLog.Log?.Error(ex.ToString());
                        // L22: 异常后短暂退避，传入 token 以便取消时立即退出
                        // M153: 补齐 ConfigureAwait(false)
                        try { await Task.Delay(100, _cts.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { break; }
                    }
                }
                LogService.Instance.Info("Loop 任务结束");
                MemoryDiagnostics.LogSnapshot("ReadLoopEnd");
            }, _cts.Token);

            // 启动后台图像处理循环
            _processImageLoopTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var scanerResult in _imageChannel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
                    {
                        try
                        {
                            StartProcessImageAsync(scanerResult);
                        }
                        catch (Exception ex)
                        {
                            LogService.Instance.Error($"ProcessImageAsync 捕获未处理异常: {ex}");
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"图像通道读取异常: {ex}");
                }
                LogService.Instance.Info("ProcessImageAsync 任务结束");
                MemoryDiagnostics.LogSnapshot("ProcessLoopEnd");
            }, _cts.Token);
        }
        #endregion

        /// <summary>
        /// REVIEW(2026-08-05): 解码压缩帧为 Image&lt;Rgb24&gt;，按相机帧格式三路适配：
        /// - JPEG：Cv2.ImDecode（libjpeg-turbo）+ BGR→RGB + 零拷贝包装（替代 ImageSharp 纯 CPU 解码）；
        /// - RGB8 Packed：原始像素，无解码，拷贝一次后包装（ArrayPool 租约不能直接被 Image 引用）；
        /// - Mono8：灰度，展开为 RGB24。
        /// 主管线类型不变，下游零改动。
        /// </summary>
        /// <param name="result">当前帧（含图像数据与格式标志）。</param>
        /// <param name="isResize">是否缩放解码（推理图缩小）。</param>
        /// <param name="targetWidth">缩放目标宽（原图宽 / ResizeScale）。</param>
        /// <param name="targetHeight">缩放目标高（原图高 / ResizeScaleY）。</param>
        /// <param name="failed">解码是否失败。</param>
        private static Image<Rgb24>? DecodeImageData(FrameResult result, bool isResize, int targetWidth, int targetHeight, out bool failed)
        {
            failed = false;
            var data = result.ImageData;
            if (data is null || data.Length == 0)
            {
                failed = true;
                return null;
            }

            // REVIEW(2026-08-05): 首帧记录相机实际输出的像素格式/分辨率（诊断 LoadImage 耗时构成用）
            if (!_frameFormatLogged)
            {
                _frameFormatLogged = true;
                LogService.Instance.Info(
                    $"相机帧格式: {result.PixelFormatName}, 分辨率: {result.Width}x{result.Height}, " +
                    $"数据大小: {data.Length / 1024.0:F1} KB");
            }

            try
            {
                Image<Rgb24> image;
                if (result.IsRgb8Packed)
                {
                    // 原始 RGB8：无需解码；ArrayPool 租约在调用后立即归还（内存会被复用），必须拷贝一份再包装
                    var copy = new byte[data.Length];
                    Array.Copy(data, copy, data.Length);
                    image = Image.WrapMemory<Rgb24>(copy, (int)result.Width, (int)result.Height);
                }
                else if (result.IsMono8)
                {
                    // 灰度：展开为 RGB24（每像素 1 → 3 字节）
                    image = new Image<Rgb24>((int)result.Width, (int)result.Height);
                    image.ProcessPixelRows(accessor =>
                    {
                        var w = accessor.Width;
                        for (int y = 0; y < accessor.Height; y++)
                        {
                            var row = accessor.GetRowSpan(y);
                            int srcIdx = y * w;
                            for (int x = 0; x < row.Length; x++)
                            {
                                byte v = data[srcIdx + x];
                                row[x] = new Rgb24(v, v, v);
                            }
                        }
                    });
                }
                else
                {
                    // JPEG：libjpeg-turbo 原生解码
                    using var decodedMat = Cv2.ImDecode(data, ImreadModes.Color);
                    if (decodedMat.Empty() || decodedMat.Width <= 0 || decodedMat.Height <= 0)
                    {
                        failed = true;
                        return null;
                    }

                    Mat working = decodedMat;
                    using var resizedMat = isResize
                        ? decodedMat.Resize(new OpenCvSharp.Size(Math.Max(1, targetWidth), Math.Max(1, targetHeight)))
                        : null;
                    if (resizedMat is not null)
                    {
                        working = resizedMat;
                    }

                    // BGR → RGB 通道交换后零拷贝包装（字节数组由本方法新建，生命周期由调用方 using 保证）
                    using var rgbMat = new Mat();
                    Cv2.CvtColor(working, rgbMat, ColorConversionCodes.BGR2RGB);
                    var bytes = new byte[rgbMat.Total() * rgbMat.ElemSize()];
                    System.Runtime.InteropServices.Marshal.Copy(rgbMat.Data, bytes, 0, bytes.Length);
                    image = Image.WrapMemory<Rgb24>(bytes, rgbMat.Width, rgbMat.Height);
                }

                // 统一缩放（仅 RGB8/Mono8 路径需要；JPEG 路径已在解码时缩）
                if (isResize && (image.Width != targetWidth || image.Height != targetHeight))
                {
                    image.Mutate(x => x.Resize(Math.Max(1, targetWidth), Math.Max(1, targetHeight)));
                }
                return image;
            }
            catch (Exception ex)
            {
                // 解码失败（坏帧/格式不支持）：记录并跳过该帧，不中断主循环
                LogService.Instance.Error($"图像解码失败: {ex.Message}");
                failed = true;
                return null;
            }
        }

        private void StartProcessImageAsync(FrameResult scanerResult)
        {
            var currentActive = Volatile.Read(ref _activeInferenceCount);
            // 仅在并发数发生显著变化时记录日志，避免每帧刷屏
            if (currentActive % InferenceLogSamplingInterval == 0 || currentActive >= InferenceLogActiveThreshold)
            {
                MemoryDiagnostics.LogActiveTasks("Inference", currentActive);
            }

            _ = Task.Run(async () =>
            {
                // 内存优化：用 SemaphoreSlim 限制并发推理，避免大量帧同时持有大对象触发 OOM
                // M338: 将 WaitAsync 纳入 try-catch，避免 Dispose 取消 CTS 时 OCE 逃逸成为未观察异常
                bool acquired;
                try
                {
                    acquired = await _inferenceSemaphore.WaitAsync(TimeSpan.FromMilliseconds(200), _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // P0-FIX: 信号量等待被取消时 scanerResult 尚未被 ProcessImageAsync 接管，显式释放避免泄漏
                    scanerResult.Dispose();
                    return;
                }
                catch (ObjectDisposedException)
                {
                    // P0-FIX: 信号量已释放，显式释放 scanerResult 避免泄漏
                    scanerResult.Dispose();
                    return;
                }

                if (!acquired)
                {
                    // L360a: 缓存 Volatile.Read 结果，复用 currentActive，避免连续两次调用
                    LogService.Instance.Warning(
                        $"并发推理槽位已满(Active={currentActive})，跳过帧 {scanerResult.FrameNumber} 以释放内存压力");
                    // L: 计入丢帧统计并通过节流策略提示用户（性能警告）
                    IncrementDropFrameCount();
                    NotifyFrameDrop($"并发推理槽位已满，已跳过帧 {scanerResult.FrameNumber}");
                    // P0-FIX: 未能进入处理流程时显式释放 scanerResult，避免 ArrayPool 缓冲区泄漏
                    scanerResult.Dispose();
                    return;
                }

                try
                {
                    await ProcessImageAsync(scanerResult).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // M354: 应用关闭时 _cts 取消导致 OCE，属正常退出，不记录为错误
                    // P0-FIX: 取消时 ProcessImageAsync 可能未执行到 using 块，显式释放避免泄漏
                    scanerResult.Dispose();
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"ProcessImageAsync 异步任务异常: {ex}");
                    // P0-FIX: 异常逃逸时同样确保释放，Dispose 幂等所以重复调用安全
                    scanerResult.Dispose();
                }
                finally
                {
                    // H21: Dispose 后信号量可能已释放
                    try { _inferenceSemaphore.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }, _cts.Token);
        }

        private async Task WaitForInferenceDrainAsync()
        {
            TaskCompletionSource<bool> waitTcs;

            // H78b: 用 lock 保护 count 与 TCS 读取的原子性，避免 TOCTOU 竞态
            // （count>0 但读到已 completed 的旧 TCS 导致 drain 提前返回）
            lock (_inferenceLock)
            {
                if (Volatile.Read(ref _activeInferenceCount) == 0)
                    return;
                waitTcs = _inferenceDrainedTcs;
            }

            await waitTcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// 供非 UI 调用方等待主循环在途推理租约全部归还（按真实状态等待，替代固定时长盲等）。
        /// 必须与 <see cref="PauseLoop"/> 配对：先 <see cref="PauseLoop"/>，再 await 本方法，最后切换相机触发模式。
        /// 主视图模型实例不可用或无在途推理时立即返回 true；超时返回 false，调用方应记录告警。
        /// </summary>
        /// <param name="timeout">最长等待时间（应覆盖主循环单帧取图超时 + 余量）</param>
        /// <param name="cancellationToken">取消令牌</param>
        public static async Task<bool> WaitForMainLoopDrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var viewModel = TryGetCurrentViewModel();
            if (viewModel is null)
            {
                // 设计时或 DI 容器未就绪：无可等待对象，按已排空处理
                return true;
            }

            try
            {
                await viewModel.WaitForInferenceDrainAsync().WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// 从 DI 容器获取当前主视图模型实例（<see cref="App"/> 中注册为单例）。
        /// 设计时或容器尚未构建时返回 null。
        /// </summary>
        private static HomeViewModel? TryGetCurrentViewModel()
        {
            try
            {
                return App.Services?.GetService<HomeViewModel>();
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"获取主视图模型实例失败: {ex.Message}");
                return null;
            }
        }

        private void ResetInferenceDrainSignalIfNeeded(int previousCount)
        {
            if (previousCount != 0)
            {
                return;
            }

            // H78b: 在 lock 内替换 TCS，确保 WaitForInferenceDrainAsync 不会读到旧值
            lock (_inferenceLock)
            {
                _inferenceDrainedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private void SignalInferenceDrainedIfNeeded(int currentCount)
        {
            if (currentCount != 0)
            {
                return;
            }

            // H78b: 在 lock 内读取 TCS 再 TrySetResult，确保与 WaitForInferenceDrainAsync 一致
            TaskCompletionSource<bool>? toSignal;
            lock (_inferenceLock)
            {
                toSignal = _inferenceDrainedTcs;
            }

            toSignal.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> CreateCompletedTcs()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.TrySetResult(true);
            return tcs;
        }

        // L365a: 图像帧通道（生产者=ReadImageOneLoop，消费者=_processImageLoopTask）。
        // Bounded(MaxImagesinQueue) + Wait：队列满时 WriteAsync 阻塞；这里仅用作容量上限，
        // 实际丢帧逻辑由 ReadImageOneLoop 中通过 Reader.Count 判断后置 NeedDrop 实现。
        private readonly Channel<FrameResult> _imageChannel = Channel.CreateBounded<FrameResult>(new BoundedChannelOptions(MaxImagesinQueue)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }
}