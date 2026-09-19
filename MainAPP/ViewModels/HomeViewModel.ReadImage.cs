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
// Split from the original monolithic file: HomeViewModel frame grab + readiness
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class HomeViewModel
    {
        private uint? _lastFrameNumber;
        // M15: 跟踪 scanner 实例变化，实例变化时复位 _lastFrameNumber
        private HikScannerType? _lastScannerInstance;
        private bool _hasLoggedWaitForCapture;
        // 当日图片目录缓存：避免每帧都调用 Directory.CreateDirectory
        // null 表示目录创建失败（如 E 盘未就绪），调用方应跳过保存
        private string? _cachedFolder;
        private string? _cachedFolderDate;

        /// <summary>
        /// 获取当日图片保存目录，仅在日期/保存路径/配方名变化或首次调用时创建目录，避免每帧 IO。
        /// 返回 null 表示目录不可用（如可移动磁盘未就绪），调用方应跳过所有图片/日志保存。
        /// </summary>
        private string? GetOrCreateTodayFolder()
        {
            // L28: 目录名使用本地日期便于用户在文件系统中直接查找，不参与时间比对
            var today = DateTime.Now.ToString("yyyyMMdd");
            var saveFolder = Settings.Instance.PicturesSaveFolder;
            var recipeName = Settings.Instance.CurrentRecipeName;
            // M16: 缓存键包含保存路径和配方名，设置变更时自动重建目录
            var cacheKey = $"{saveFolder}|{recipeName}|{today}";
            if (_cachedFolderDate == cacheKey)
            {
                return _cachedFolder;
            }

            _cachedFolder = Path.Combine(saveFolder, recipeName, today);
            _cachedFolderDate = cacheKey;
            try
            {
                Directory.CreateDirectory(_cachedFolder);
            }
            catch (Exception ex)
            {
                // 设备未就绪/路径不可访问：降级为 Warning，避免每帧 ERROR 刷屏；
                // 清空缓存路径，调用方见 null 跳过保存
                _cachedFolder = null;
                LogService.Instance.Warning($"创建图片保存目录失败（路径: {saveFolder}），将跳过图片保存: {ex.Message}");
            }
            return _cachedFolder;
        }

        //增加丢包和丢帧
        #region ReadImageOneLoop
        private async Task ReadImageOneLoop(HikScannerType scanner)
        {
            // P0-2: Dispose 后 _cts 可能已释放，访问 Token 会抛 ObjectDisposedException，直接退出
            if (_disposed) return;
            CancellationToken token;
            try { token = _cts.Token; }
            catch (ObjectDisposedException) { return; }

            // 每次迭代重新获取最新的 scanner 实例，以感知 Scanners.Initialize 重建的新对象（如重连后的实例）
            var currentScanner = Devices.Scanners.HikScaner ?? scanner;
            if (!currentScanner.IsConnected)
            {
                // 设备未连接（可能在重连中），短暂等待后跳过本轮
                // M15: 设备重连后 FrameNumber 会从 0 重新开始，复位 _lastFrameNumber 避免误报大量丢包
                _lastFrameNumber = null;
                // L366a: 扫描枪断开后重置日志标志，重连后再次记录"开始等待拍照"
                _hasLoggedWaitForCapture = false;
                // P0-2: 设备断连，更新连接状态为"重连中"（橙色）
                UpdateScannerStatus(ScannerConnectionState.Connecting);
                try { await Task.Delay(100, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                return;
            }

            // P0-2: 设备已连接，更新连接状态（每轮循环都更新，确保重连后状态及时刷新为已连接）
            UpdateScannerStatus(ScannerConnectionState.Connected);

            // M15: 检测 scanner 实例变化（重连后可能创建新实例），复位帧号计数器
            if (_lastScannerInstance != currentScanner)
            {
                _lastFrameNumber = null;
                _lastScannerInstance = currentScanner;
            }

            FrameResult scanerResult;
            try
            {
                if (!_hasLoggedWaitForCapture)
                {
                    LogService.Instance.Info("开始等待拍照");
                    _hasLoggedWaitForCapture = true;
                }
                scanerResult = await GetImageWithRetryAsync(currentScanner, token).ConfigureAwait(false);
            }
            catch (HikScannerException ex)
            {
                HandleFrameGrabFailure(ex);
                return;
            }
            // M60: 删除冗余的 catch (OperationCanceledException) { throw; }，让取消异常自然传播

            if (scanerResult.HasBarcodeResults)
            {
                // 将每个识别到的条码通过消息发送到 MainWindow（MVVM-friendly）
                foreach (var br in scanerResult.BarcodeResults)
                {
                    // L423: 缓存 CodeString() 避免重复调用
                    var code = br.CodeString();
                    if (!string.IsNullOrEmpty(code))
                    {
                        WeakReferenceMessenger.Default.Send(new AddBarcodeMessage(code));
                    }
                }
            }
            DateTime grabTime = DateTime.Now;
            // 2026-09-17: 编码器绑定**延迟 2 帧**——本帧的编码器在后续第 2 帧收图时才解析，
            // 给编码器 UDP 包留出到达时间，覆盖「UDP 晚于图像到达」导致的绑定错位一拍（约 200mm）。
            // 解析动作见下方 flush 循环（ResolveEncoderBinding）。
            scanerResult.GrabTime = grabTime;
            scanerResult.GrabSequence = ++_grabSequence;
            // 2026-09-18: 设备墙钟（DeviceTime 参数，秒级，5s 缓存）随帧携带——timechain 日志 dev-clock 段的数据源
            scanerResult.DeviceClockText = currentScanner.GetCachedDeviceTime();

            if (!_lastFrameNumber.HasValue)
            {
                // 首次收到帧，只初始化，不判为丢包
                _lastFrameNumber = scanerResult.FrameNumber;
            }
            else
            {
                uint expected = _lastFrameNumber.Value == uint.MaxValue ? 0u : _lastFrameNumber.Value + 1u;
                if (scanerResult.FrameNumber != expected)
                {
                    // P1-2: 处理包号回绕与设备 reset 后 FrameNumber 从 0 重新开始的情况
                    // 若 当前包 < 预期，说明设备 reset 或包号回绕，不应误报为大量丢包
                    if (scanerResult.FrameNumber < expected)
                    {
                        // 设备 reset 或包号回绕：复位基线，不累计丢包
                        LogService.Instance.Info($"包号回绕或设备复位：当前包:{scanerResult.FrameNumber}, 预期:{expected}，已复位基线不计丢包");
                    }
                    else
                    {
                        // 正向丢包：当前包 > 预期
                        uint lost = scanerResult.FrameNumber - expected;
                        // P0-5: 设备 reset 后 FrameNumber 可能从一个新的大值开始（如内部计数器），
                        // 单次差值过大会被误判为瞬间丢失大量帧（如日志中的 1173369）。
                        // 设阈值 FrameNumberJumpThreshold：超过则视为设备 reset，复位基线不累计丢包。
                        // 阈值依据：10 FPS × 30s = 300 帧，断连 30s 已是严重故障，单次丢 1000 帧以上不合理。
                        const int FrameNumberJumpThreshold = 1000;
                        if (lost > FrameNumberJumpThreshold)
                        {
                            LogService.Instance.Warning($"检测到包号异常跳变（差值={lost} > {FrameNumberJumpThreshold}），疑似设备 reset，复位基线不计丢包：当前包:{scanerResult.FrameNumber}, 预期:{expected}");
                        }
                        else
                        {
                            LogService.Instance.Warning($"丢包/跳帧：当前包:{scanerResult.FrameNumber}, 预期:{expected}, 丢失:{lost}");
                            scanerResult.IsFrameLoss = true;
                            // L: 上游检测到丢包即累计丢帧统计，并通过节流策略提示用户（性能警告）
                            // P2: clamp 避免 uint→int 溢出为负数（lost 被阈值限制为 ≤1000，但防御性 clamp）
                            int lostInt = lost > (uint)int.MaxValue ? int.MaxValue : (int)lost;
                            IncrementDropFrameCount(lostInt);
                            NotifyFrameDrop($"检测到丢包/跳帧：当前包 {scanerResult.FrameNumber}，预期 {expected}，丢失 {lost}");
                        }
                    }
                }
                _lastFrameNumber = scanerResult.FrameNumber;
            }
            // 2026-09-17: 帧先进「待绑定」队列，凑满 2 帧延迟后按序解析编码器并入处理通道。
            // NeedDrop 判定随之移到入队时刻（对通道深度的判断更准确）。
            // 2026-09-18: 无编码器模式没有迟到的编码器包可等，跳过两帧等待直接绑定入通道（零额外延迟）。
            if (Settings.Instance.EncoderlessMode)
            {
                ResolveEncoderBinding(scanerResult);
                await EnqueueFrameForProcessingAsync(scanerResult, token).ConfigureAwait(false);
                return;
            }
            _pendingBind.Enqueue(scanerResult);
            while (_pendingBind.Count > EncoderBindDelayFrames)
            {
                var ready = _pendingBind.Dequeue();
                ResolveEncoderBinding(ready);
                await EnqueueFrameForProcessingAsync(ready, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 解析帧的编码器绑定（延迟 2 帧后调用，见 <see cref="_pendingBind"/>）：
        /// 取「Time ≤ 本帧拍照时刻」的最近一条编码器记录（不消费，见
        /// <see cref="ToVGTService.MostRecentDateEncode"/>），并做新鲜度判定——
        /// 超龄帧标记 <see cref="FrameResult.EncoderStale"/>，由 ProcessImageAsync 整条跳过
        /// （宁可不发，也不发「X/Y 与编码器错位一个触发间隔」的数据）。
        /// </summary>
        private void ResolveEncoderBinding(FrameResult frame)
        {
            // 2026-09-18: 无编码器模式——产线无编码器/静态线形态。跳过绑定与新鲜度判定：
            // Encode 恒为 0、T_enc=T_img（timechain 行带"(无编码器模式)"标记），帧照常处理。
            // ⚠️ 机械手侧不得使用编码器外推，否则 Encode=0 无锚点会按错误位置动作。
            if (Settings.Instance.EncoderlessMode)
            {
                if (!_encoderlessModeWarned)
                {
                    _encoderlessModeWarned = true;
                    LogService.Instance.Warning(
                        "无编码器模式已启用：跳过编码器绑定与新鲜度校验，Encode 恒为 0、X/Y 按拍照时刻直发——请确认机械手侧已关闭编码器外推！");
                }
                frame.EncoderReceivedTime = frame.GrabTime;
                frame.EncoderValue = 0;
                frame.EncoderStale = false;
                return;
            }

            var (encoder, time, stale) = _toVgtService.MostRecentDateEncode(frame.GrabTime);
            // P0-0: 无编码器数据/所有记录都晚于拍照时刻时回退 grabTime，保证 EncodeTime 有效（既有行为）
            frame.EncoderReceivedTime = time == DateTime.MinValue ? frame.GrabTime : time;
            frame.EncoderValue = encoder;
            frame.EncoderStale = stale;

            // 绑定延迟诊断（原在收图即算，随延迟绑定移到此处）：
            // 编码器记录的到达时刻 → 图像到达时刻的时间差。稳态 ≈ 编码器上报周期 + 图像传输延迟（几十 ms 级）；
            // 持续增大 = 上报断流恢复或处理积压——此时 Encode 与 XY 的对应关系
            // 已系统性滞后该差值 × 线速（详见 AlgorithmSettings.TrackerExpireSeconds 注释）。
            if (time != DateTime.MinValue)
            {
                var bindDelayMs = (frame.GrabTime - time).TotalMilliseconds;
                Volatile.Write(ref _lastBindDelayMs, (long)bindDelayMs);
                if (bindDelayMs > BindDelayWarnMs && (DateTime.Now - _lastBindDelayWarnAt).TotalSeconds >= HealthWarnThrottleSec)
                {
                    _lastBindDelayWarnAt = DateTime.Now;
                    LogService.Instance.Warning(
                        $"[绑定延迟] {bindDelayMs:F0}ms 超过阈值 {BindDelayWarnMs}ms —— 位置与编码器对应关系已滞后约 {bindDelayMs * 0.2:F0}mm(按200mm/s)，请检查编码器上报链路");
                }
            }
        }

        /// <summary>把帧写入处理通道（含 NeedDrop 判定，原在收图路径，移到入队时刻更准确）。</summary>
        private async Task EnqueueFrameForProcessingAsync(FrameResult frame, CancellationToken token)
        {
            // 当队列拥挤时，标记为需要丢弃以降低系统压力
            // L365a: 改用 Channel.Reader.Count 反映当前队列深度（替代原 Volatile.Read(_queuedImageCount)）
            var currentCount = _imageChannel.Reader.Count;
            if (currentCount >= MaxImagesinQueue)
            {
                frame.NeedDrop = true;
                MemoryDiagnostics.LogQueueDepth("ImageInfo(队列满-丢弃)", currentCount, MaxImagesinQueue);
                // L: 队列满时计入丢帧统计并通过节流策略提示用户（性能警告）
                IncrementDropFrameCount();
                NotifyFrameDrop($"图像队列已满({currentCount}/{MaxImagesinQueue})，已标记当前帧为丢弃");
            }

            // 入队到 Bounded Channel；NeedDrop 帧也需入队，由 ProcessImageAsync 发送空结果给 VGT。
            // H21/M221: Dispose 后 Writer 可能已 Complete，WriteAsync 抛 ChannelClosedException —— 直接退出循环。
            // P0-2: 使用本地 token 避免在 Dispose 后访问 _cts.Token 抛 ObjectDisposedException
            try
            {
                await _imageChannel.Writer.WriteAsync(frame, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (ChannelClosedException) { return; }
            catch (ObjectDisposedException) { return; }
        }

        // H15: 相机取帧超时上限。
        // 2026-09-05（用户拍板）: 10s → 3600s(1小时)。触发模式无触发信号(流水线未开)时 SDK 会阻塞
        // 至超时才返回空帧(Status=Timeout)——原 10s 导致每 10 秒产生一次"空帧→丢帧误计+误导日志"；
        // 延长到 1 小时使等待期基本静默（有帧时 SDK 立即返回，不受超时影响）。
        private const uint DefaultGrabTimeoutMs = 3_600_000;
        private async Task<FrameResult> GetImageWithRetryAsync(HikScannerType scanner, CancellationToken cancellationToken, uint timeoutMs = DefaultGrabTimeoutMs)
        {
            // L102: 重试次数使用提取的常量
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MaxGrabRetryAttempts; attempt++)
            {
                try
                {
                    var grab = await scanner.GetImageAsync(timeoutMs).ConfigureAwait(false);
                    return FrameResult.From(grab);
                }
                catch (HikScannerException ex) when (IsTransientFrameGrabFailure(ex))
                {
                    lastException = ex;
                    if (attempt < MaxGrabRetryAttempts)
                    {
                        LogService.Instance.Info($"获取图像失败，准备重试({attempt}/{MaxGrabRetryAttempts})，错误码: 0x{ex.ErrorCode:X8}");
                        // L103: 退避时间使用提取的常量
                        // P0-2: 使用传入的 cancellationToken 避免访问已释放的 _cts
                        await Task.Delay(GrabRetryDelayMs, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw;
                }
            }

            throw lastException ?? new InvalidOperationException("获取图像失败，且未返回有效异常信息。");
        }

        private static bool IsTransientFrameGrabFailure(HikScannerException ex)
        {
            // L101: 使用提取的错误码常量
            return ex.ErrorCode == HikErrorCodeTimeout;
        }

        private void HandleFrameGrabFailure(Exception exception)
        {
            if (exception is HikScannerException readerException && IsTransientFrameGrabFailure(readerException))
            {
                LogService.Instance.Warning($"扫描枪取图失败，已跳过本帧，错误码: 0x{readerException.ErrorCode:X8}");
                return;
            }

            LogService.Instance.Error($"扫描枪取图失败: {exception}");
        }

        private bool TryEnsureProcessingReady(out string errorMessage)
        {
            if (CurrentRecipe is null)
            {
                errorMessage = "没有任何配方被设置成当前配方，请检查！";
                return false;
            }

            if (CurrentRecipe.ImageTool is null)
            {
                errorMessage = $"配方:{CurrentRecipe.Name}没有进行图像配置,请检查！";
                return false;
            }

            if (CurrentRecipe.YoloTool is null || CurrentRecipe.YoloTool.EdgeDetection is null)
            {
                errorMessage = $"配方:{CurrentRecipe.Name}没有进行AI模型配置,请检查！";
                return false;
            }

            if (CoordinateTool is null)
            {
                errorMessage = $"配方:{CurrentRecipe.Name}没有标定,请检查！";
                return false;
            }

            // REVIEW-FIX: 取局部变量，避免每次访问属性触发可空流分析警告（属性每次求值可能变化）
            var coordinateTool = CoordinateTool;
            if (!_transformer.IsInitialized && IsCoordinateToolCalibrated(coordinateTool))
            {
                if (coordinateTool.SquareSize <= 0)
                {
                    errorMessage = $"配方:{CurrentRecipe.Name}的方格尺寸无效，当前值:{coordinateTool.SquareSize}";
                    return false;
                }

                _transformer.Initialize(
                    coordinateTool.Origin,
                    coordinateTool.XPoint,
                    coordinateTool.YPoint,
                    coordinateTool.SquareSize * 4d,
                    coordinateTool.SquareSize * 2d);
            }

            if (!_transformer.IsInitialized)
            {
                errorMessage = $"配方:{CurrentRecipe.Name}没有完成坐标系初始化,请检查！";
                return false;
            }

            if (string.IsNullOrWhiteSpace(EdgeDetection.ModelPath) || !File.Exists(EdgeDetection.ModelPath))
            {
                errorMessage = $"AI模型文件不存在:{EdgeDetection.ModelPath},请检查！";
                return false;
            }

            // H23: IsResize 时 ResizeScale/ResizeScaleY 不能为 0，否则除零崩溃
            if (EdgeDetection.IsResize && (EdgeDetection.ResizeScale <= 0 || EdgeDetection.ResizeScaleY <= 0))
            {
                errorMessage = $"配方:{CurrentRecipe.Name}的缩放比例无效（ResizeScale={EdgeDetection.ResizeScale}, ResizeScaleY={EdgeDetection.ResizeScaleY}），必须大于0";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        /// <summary>
        /// 判断坐标系标定是否完成：三个标定点不共点且 XPoint/YPoint 不与原点重合。
        /// 原判断 XPoint.X > 0.1f 不严谨，标定后 XPoint.X 可能为 0 或负值（取决于相机安装角度）。
        /// </summary>
        private static bool IsCoordinateToolCalibrated(CoordinateTool tool)
        {
            // 三个点中任意两个重合则未完成标定
            if (IsSamePoint(tool.Origin, tool.XPoint)) return false;
            if (IsSamePoint(tool.Origin, tool.YPoint)) return false;
            if (IsSamePoint(tool.XPoint, tool.YPoint)) return false;
            // 三个点共线则无法构成有效坐标系（叉积为 0）
            var cross = (tool.XPoint.X - tool.Origin.X) * (tool.YPoint.Y - tool.Origin.Y)
                      - (tool.XPoint.Y - tool.Origin.Y) * (tool.YPoint.X - tool.Origin.X);
            return Math.Abs(cross) > 0.5f;
        }

        private static bool IsSamePoint(Point2f a, Point2f b, float tolerance = 0.5f)
        {
            return Math.Abs(a.X - b.X) < tolerance && Math.Abs(a.Y - b.Y) < tolerance;
        }

        #endregion
    }
}