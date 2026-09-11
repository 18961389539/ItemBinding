using Extensions;
using HikScanner;
using HikScannerType = HikScanner.HikScanner;
using MainAPP.Models;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MainAPP.Services
{
    public sealed class RecipeScannerService
    {
        public bool HasScanner => Devices.Scanners.HasScanners;

        // L413a: 实时显示单帧获取图像的超时时间（秒），原为内联硬编码 30 秒
        private const int LiveDisplayFrameTimeoutSec = 30;

        // M27: 所有设备操作方法改为 async，避免在 UI 线程上 sync-over-async 阻塞

        // M163: 添加 CancellationToken 参数，传递给 AcquireAsync
        public async Task<bool> EnsureOpenAsync(CancellationToken cancellationToken = default)
        {
            var scanner = Devices.Scanners.HikScaner;
            if (scanner is null)
            {
                return false;
            }

            if (scanner.IsConnected)
            {
                return true;
            }

            try
            {
                scanner.Connect();
                scanner.SwitchToHardwareTrigger();
                return scanner.IsConnected;
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"打开扫码设备失败: {ex.Message}");
                return false;
            }
        }

        public Task ExecuteSoftwareTriggerAsync(CancellationToken cancellationToken = default)
        {
            RequireScanner().ExecuteSoftwareTrigger();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 切到软触发模式（配方页打开时，主循环已暂停）。
        /// </summary>
        public Task SwitchToSoftTriggerAsync(CancellationToken cancellationToken = default)
        {
            RequireScanner().SwitchToSoftwareTrigger();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 切回硬触发模式（配方页关闭时，主循环仍暂停）。
        /// </summary>
        public Task SwitchToHardTriggerAsync(CancellationToken cancellationToken = default)
        {
            RequireScanner().SwitchToHardwareTrigger();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 软触发取单帧。相机已在软触发模式，直接发信号取图。
        /// </summary>
        public async Task<FrameResult> GetImageAsync(uint timeoutMs = 5_000, CancellationToken cancellationToken = default)
        {
            var scanner = RequireScanner();

            scanner.ExecuteSoftwareTrigger();
            var grab = await scanner.GetImageAsync(timeoutMs).WaitAsync(cancellationToken).ConfigureAwait(false);
            return FrameResult.From(grab);
        }

        public async Task SetExposureTimeAsync(float exposureTime, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            RequireScanner().SetExposureTime(exposureTime);
        }

        public async Task SetGainAsync(float gain, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            RequireScanner().SetGain(gain);
        }

        public async Task<float> GetExposureTimeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            return RequireScanner().GetExposureTime();
        }

        public async Task<float> GetGainAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            return RequireScanner().GetGain();
        }

        public async Task<(float Min, float Max)> GetExposureRangeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            return RequireScanner().GetFloatRange("ExposureTime");
        }

        public async Task<(float Min, float Max)> GetGainRangeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            return RequireScanner().GetFloatRange("Gain");
        }

        public async Task StartLiveDisplayAsync(Action<BitmapSource> publishFrame, CancellationToken cancellationToken, int targetFps = 20)
        {
            if (publishFrame is null)
            {
                throw new ArgumentNullException(nameof(publishFrame));
            }

            var safeTargetFps = targetFps > 0 ? targetFps : 20;
            long targetFrameIntervalTicks = TimeSpan.TicksPerSecond / safeTargetFps;

            while (!cancellationToken.IsCancellationRequested)
            {
                BitmapSource? frameToPublish = null;
                long frameElapsedTicks = 0;
                var scanner = RequireScanner();
                long frameStart = Stopwatch.GetTimestamp();
                try
                {
                    scanner.ExecuteSoftwareTrigger();
                        // M104/H59/M215: 使用 WaitAsync(TimeSpan, CancellationToken) 重载替代每帧创建 linkedCts，
                        // 单帧超时由 TimeoutException 表达，外部取消仍走 OCE，避免 GC 压力并区分两种语义
                        // L413a: 单帧超时使用提取的常量 LiveDisplayFrameTimeoutSec
                        var grab = await scanner.GetImageAsync()
                            .WaitAsync(TimeSpan.FromSeconds(LiveDisplayFrameTimeoutSec), cancellationToken)
                            .ConfigureAwait(false);
                        // P0-FIX: 用 using 模式包装 imageResult，确保 ImDecode 抛异常或 continue 路径都能归还 ArrayPool 缓冲区
                        using var imageResult = FrameResult.From(grab);

                        // L362b: ImageData 可能为 null，提前跳过避免 ImDecode 抛异常
                        if (imageResult.ImageData is null)
                        {
                            LogService.Instance.Warning("实时显示: 图像数据为空，跳过该帧");
                            continue;
                        }
                        using var colorMat = Mat.ImDecode(imageResult.ImageData, ImreadModes.Color);
                        // M700: ImageData 已解码，归还 ArrayPool 缓冲区（Dispose 幂等，重复调用安全）
                        imageResult.ReleaseImageData();
                        // M329a: 解码失败时 colorMat 为空，访问 Width/Height 会得到 0 并产生无意义日志，跳过该帧
                        if (colorMat.Empty())
                        {
                            LogService.Instance.Warning("实时显示: 图像解码失败，跳过该帧");
                            continue;
                        }
                        if (imageResult.HasBarcodeResults)
                        {
                            Tools.DrawBarcodeResults(colorMat, imageResult.BarcodeResults);
                        }

                        frameToPublish = colorMat.ToBitmapSource();
                        frameToPublish.Freeze();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // 外部取消：终止整个实时显示循环
                        break;
                    }
                    catch (TimeoutException)
                    {
                        // H59: 单帧超时不终止循环，跳过该帧继续下一帧
                        LogService.Instance.Warning("实时显示单帧超时，跳过该帧");
                    }
                    catch (Exception ex)
                    {
                        // M13/L65: 单帧异常不应终止整个实时显示循环，记录完整异常
                        LogService.Instance.Warning($"实时显示单帧异常: {ex}");
                    }

                    frameElapsedTicks = Stopwatch.GetTimestamp() - frameStart;

                // M216: 每帧结束后按目标帧率延迟，避免 CPU 空转
                long remainingTicks = targetFrameIntervalTicks - frameElapsedTicks;
                if (remainingTicks > 0)
                {
                    await Task.Delay(TimeSpan.FromTicks(remainingTicks), cancellationToken).ConfigureAwait(false);
                }

                // M164: 出锁后再回调 publishFrame，避免持锁期间回调阻塞
                // M287: 包裹 try-catch 防止回调异常终止实时显示循环
                if (frameToPublish is not null)
                {
                    try
                    {
                        publishFrame(frameToPublish);
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning($"实时显示回调异常: {ex}");
                    }
                }
            }
        }

        // L30: DrawBarcodeResults 已提取到 Extensions.Tools.DrawBarcodeResults，此处删除重复实现

        private static HikScannerType RequireScanner()
        {
            var scanner = Devices.Scanners.HikScaner;
            if (scanner is null)
            {
                throw new InvalidOperationException("未连接到设备");
            }

            return scanner;
        }
    }
}
