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
// Split from the original monolithic file: RecipeViewModel parameter optimization
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class RecipeViewModel
    {
        #region 参数优化

        /// <summary>
        /// 自动优化参数
        /// </summary>
        [RelayCommand]
        private void OptimizeParameter()
        {
            // 取消正在优化的任务
            if (IsOptimizing)
            {
                _optimizeCts?.Cancel();
                return;
            }

            _optimizeCts?.Cancel();
            _optimizeCts?.Dispose();
            _optimizeCts = new CancellationTokenSource();
            // H55: 在启动任务前设置 IsOptimizing=true，避免任务排队期间（首个 await 之前）IsOptimizing 仍为 false，
            // 导致用户再次点击时 Dispose 仍被排队任务引用的旧 CTS，或 finally 块误取消新 CTS
            IsOptimizing = true;
            // H37: 保存任务引用，便于 Dispose 时等待，避免 fire-and-forget 导致未观察异常或资源泄漏
            _optimizeTask = OptimizeParameterAsync(_optimizeCts.Token);
        }

        private async Task OptimizeParameterAsync(CancellationToken token)
        {
            // H70: 捕获 CTS 为局部变量，避免 finally 块读取 _optimizeCts 时存在竞态
            var cts = _optimizeCts;
            try
            {
                if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
                {
                    return;
                }

                // 使用 ScannerOptimizer 进行优化
                var device = _scannerService;

                (float minExposureTime, float maxExposureTime) = await device.GetExposureRangeAsync(token).ConfigureAwait(false);
                (float minGain, float maxGain) = await device.GetGainRangeAsync(token).ConfigureAwait(false);

                // 更新UI的频率，避免过于频繁，只在一定间隔后更新
                var lastUpdateTime = DateTime.MinValue;
                // L402a: 使用类常量 OptimizeUiUpdateIntervalMs
                var updateIntervalMs = OptimizeUiUpdateIntervalMs;
                Stopwatch stopwatch = Stopwatch.StartNew();
                // M20/M27: RunSimulatedAnnealing 为同步签名，回调内 async 方法用 GetAwaiter().GetResult()
                // 在 Task.Run 线程池线程上执行，无同步上下文死锁风险
                // M66: 回调内 sync-over-async 暂时保留 — 模拟退火回调签名为同步 (Func<float,float,(float,float)>)，
                // 无法直接 await；好在 Task.Run 已在线程池线程执行，无 UI 同步上下文死锁风险
                var result = await Task.Run(() => ScannerOptimizer.RunSimulatedAnnealing(
                    (exposure, gain) =>
                    {
                        // M320a: 优化回调 sync-over-async 传入 token，使设备方法可响应取消
                        device.SetExposureTimeAsync(exposure, token).GetAwaiter().GetResult();
                        device.SetGainAsync(gain, token).GetAwaiter().GetResult();
                        device.ExecuteSoftwareTriggerAsync(token).GetAwaiter().GetResult();

                        // REVIEW-FIX: 用 using 包裹 FrameResult。GetImageAsync 返回的 FrameResult
                        // 持有 ArrayPool<byte>.Shared.Rent 的整帧缓冲区（FrameResult.cs:87），
                        // 不释放则模拟退火每轮迭代泄漏一份帧大小缓冲，长时间优化内存持续上涨。
                        using var image = device.GetImageAsync(cancellationToken: token).GetAwaiter().GetResult();

                        // 计算得分
                        float score = image.BarcodeResults.Length > 0
                            ? image.BarcodeResults.Max(b => b.Confidence())
                            : 0f;

                        // 生成灰度图像，通过计算亮度来辅助得分
                        // L362b: ImageData 可能为 null，跳过亮度计算直接返回得分
                        if (image.ImageData is null)
                        {
                            return (score, 0f);
                        }
                        using Mat colorMat = Mat.ImDecode(image.ImageData, ImreadModes.Color);
                        using Mat grayMat = colorMat.CvtColor(ColorConversionCodes.BGR2GRAY);
                        float avgBrightness = 0;
                        if (image.HasBarcodeResults)
                        {
                            List<Point2f> points = new List<Point2f>();
                            foreach (var p in image.BarcodeResults.First().Location())
                            {
                                points.Add(new Point2f(p.X, p.Y));
                            }
                            var barcodeLocation = Cv2.BoundingRect(points);
                            var barcodeRect = grayMat[barcodeLocation];
                            avgBrightness = (float)barcodeRect.Mean().Val0;
                        }
                        else
                        {
                            avgBrightness = (float)grayMat.Mean().Val0;
                        }
                        var now = DateTime.Now;
                        if ((now - lastUpdateTime).TotalMilliseconds >= updateIntervalMs)
                        {
                            lastUpdateTime = now;

                            colorMat.PutText($"Score: {score:F2}, Brightness: {avgBrightness:F2},Gain:{gain:F2},Exposure:{exposure:F2}",
                                new OpenCvSharp.Point(100, colorMat.Height / 2),
                                HersheyFonts.HersheySimplex, 2.0, Scalar.Red, 3);

                            // 复制 Mat 以便异步更新 UI，避免阻塞主线程
                            UpdateImageForShow(colorMat.ToBitmapSource());
                        }

                        return (score, avgBrightness);
                    }, minExposureTime, maxExposureTime, minGain, maxGain,
                    cancellationToken: token), token).ConfigureAwait(false);
                stopwatch.Stop();
                // H14: 删除 _optimizeCts.Cancel() — 优化已完成，取消 token 会让后续 Task.Delay(500, token) 立即抛异常，
                // 导致 GetImageAsync() 验证图像永不采集。finally 块仍会 Cancel，此处冗余。
                if (ImageTool != null)
                {
                    // L75: 局部变量改为 camelCase 命名
                    var (exposure, gain, score) = (result.BestExposure, result.BestGain, result.BestScore);
                    ImageTool.ExposureTime = exposure;
                    ImageTool.Gain = gain;
                    OnPropertyChanged(nameof(ImageTool));
                    ShowInfo($"优化完成\n曝光时间: {exposure}\n增益: {gain}\n最佳得分: {score}\n耗时: {stopwatch.ElapsedMilliseconds}ms");
                    // H84/M320a: 传递 token
                    await device.SetExposureTimeAsync(exposure, token).ConfigureAwait(false);
                    await device.SetGainAsync(gain, token).ConfigureAwait(false);
                    // H14: 去掉 token，避免已取消的 token 导致 Task.Delay 抛异常
                    // M65: 补齐 ConfigureAwait(false)
                    // M314b: 使用提取的常量 OptimizePostApplyDelayMs
                    await Task.Delay(OptimizePostApplyDelayMs).ConfigureAwait(false);
                    await GetImageAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                // 确保异常路径下也取消优化任务，避免 cts 泄漏
                // H70: 使用局部变量 cts，避免读取 _optimizeCts 时的竞态
                // M341: Dispose 超时后可能已释放 cts，Cancel 会抛 ObjectDisposedException
                // VSTHRD103: CancellationTokenSource.Cancel() 本身是同步方法，分析器误报
#pragma warning disable VSTHRD103
                try { cts?.Cancel(); } catch (ObjectDisposedException) { }
#pragma warning restore VSTHRD103
                IsOptimizing = false;
            }
        }

        #endregion
    }
}