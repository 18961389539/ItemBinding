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
// Split from the original monolithic file: HomeViewModel per-frame processing pipeline
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class HomeViewModel
    {
        /// <summary>
        /// 处理单帧图像的异步流程：执行模型推理、匹配条码、生成记录并绘制结果。
        /// </summary>
        /// <param name="scanerResult">来自扫描设备的一帧图像及其附带的条码检测结果。</param>
        /// <remarks>
        /// 处理流程大致为：加载/缩放图像 -> 从池中借用预测器执行 SegmentAsync -> 将检测框与条码位置匹配 -> 保存与绘制。
        /// 注意：此方法会检查 _predictorPool 是否为 null，为 null 时记入丢帧并跳过当前帧。
        /// </remarks>
        private async Task ProcessImageAsync(FrameResult scanerResult)
        {
            // H78a: 先读取 EdgeDetection 属性，若抛 InvalidOperationException 则不进行自增，避免 _activeInferenceCount 泄漏
            // M76: 入口处对 CurrentRecipe 的 EdgeDetection 做局部快照，后续全部使用局部变量，避免处理过程中配方切换导致属性返回不同实例
            var edge = EdgeDetection;
            var previousCount = Interlocked.Increment(ref _activeInferenceCount) - 1;
            ResetInferenceDrainSignalIfNeeded(previousCount);
            var imageDataSize = scanerResult.ImageData?.Length ?? 0;
            try
            {
                // P0-FIX: 用 using 模式包装 scanerResult，确保所有异常路径自动归还 ArrayPool 租用缓冲区。
                // 内部仍保留 ReleaseImageData 的提前归还调用以减少内存占用；Dispose 幂等，重复调用安全。
                using (scanerResult)
                {
                    // L362b: ImageData 可能为 null（Image 为 null 时），提前跳过避免 ImDecode/Image.Load 抛异常
                    if (scanerResult.ImageData is null)
                    {
                        LogService.Instance.Warning($"图像数据为空，跳过处理: Frame={scanerResult.FrameNumber}");
                        // L: 计入丢帧统计并通过节流策略提示用户（性能警告）
                        IncrementDropFrameCount();
                        NotifyFrameDrop($"图像数据为空，已跳过处理: 帧 {scanerResult.FrameNumber}");
                        return;
                    }
                    using var timings = new TimingLogger($"{scanerResult.FrameNumber}", _inferenceDevice);
                    // REVIEW(2026-08-05): 图像解码改用 OpenCvSharp（Cv2.ImDecode，底层 libjpeg-turbo），
                    // 替代 ImageSharp 纯 CPU JPEG 解码（实测 LoadImage 50ms → 原生解码约 15-25ms）。
                    // 同时适配相机三种帧格式：JPEG 原生解码 / RGB8 零解码拷贝 / Mono8 灰度展开。
                    // 主管线仍保持 Image<Rgb24>，下游（推理/绘制/显示/保存）零改动。
                    using var sourceImg = DecodeImageData(
                        scanerResult,
                        edge.IsResize,
                        (int)scanerResult.Width / edge.ResizeScale,
                        (int)scanerResult.Height / edge.ResizeScaleY,
                        out var decodedFailed);
                    if (decodedFailed || sourceImg is null)
                    {
                        IncrementDropFrameCount();
                        NotifyFrameDrop($"图像解码失败，已跳过处理: 帧 {scanerResult.FrameNumber}");
                        return;
                    }

                    // M700: 解码后立即归还 ArrayPool 租用的 byte[] 缓冲区，避免 LOH 持续膨胀
                    scanerResult.ReleaseImageData();

                    var sourceImgSize = MemoryDiagnostics.EstimateImageSize(sourceImg.Width, sourceImg.Height);
                    if (scanerResult.IsFrameLoss || scanerResult.NeedDrop)
                    {
                        _toVgtService.SendTo([], scanerResult, Settings.Instance.MessageReceiver);
                        _reusableShowBitmap = Tools.UpdateShow(sourceImg, _reusableShowBitmap);
                        ImageForShow = _reusableShowBitmap;
                        LogService.Instance.Warning($"跳过处理: Num={scanerResult.FrameNumber},Loss={scanerResult.IsFrameLoss}, Drop={scanerResult.NeedDrop}");
                        return;
                    }

                    string? folder = GetOrCreateTodayFolder();
                    timings.Split("LoadImage");
                    timings.Split("PrepareFolder");
                    // 发起异步推理请求（SegmentAsync）。
                    // 建议：若需要更低延迟与更高吞吐量，考虑调整 YoloConfiguration 中的 MaximumCandidateBoxes 与 SuppressParallelInference。
                    YoloResult<Segmentation>? edgeResults;
                    var predictorPool = _predictorPool;
                    if (predictorPool is null)
                    {
                        // P1-1: 预测器池为 null 时仅记录一次日志，避免每帧刷屏
                        // 配置错误（模型文件缺失等）已在 CreatePredictorPoolAsync 中通过 NotificationService.Error 提示用户，
                        // 这里无需重复弹窗或累计丢帧计数（属于配置问题而非运行时丢帧）
                        if (!_hasLoggedPredictorNotReady)
                        {
                            _hasLoggedPredictorNotReady = true;
                            LogService.Instance.Warning("预测器池尚未初始化，将跳过后续帧处理直至模型加载完成");
                        }
                        return;
                    }

                    // M22: lease.Acquire 已传入 _cts.Token，租出阶段即可响应取消；
                    // SegmentAsync 本身不接收 token（底层 Task.Run 同步封装），取消在 lease 层处理
                    using var lease = predictorPool.Acquire(cancellationToken: _cts.Token);
                    // 诊断：打印实际配置
                    if (!_configLogged)
                    {
                        _configLogged = true;
                        var cfg = lease.Predictor.Configuration;
                        LogService.Instance.Info($"CUDA推理配置确认: Confidence={cfg.Confidence}, IoU={cfg.IoU}, MaxDetection={cfg.MaximumDetections}");
                    }
                    // REVIEW(2026-09-04): 分割推理加后端自愈触发点。
                    // ORT CUDA 的运行期错误（如 CUDNN_FE failure 7）意味着该会话已不可用，
                    // 之后每帧都会失败。这里：成功后清连续失败计数；失败时若达到阈值则触发
                    // 逐级降级重建（fire-and-forget），随后照常向上抛、由外层统一记日志/丢帧。
                    try
                    {
                        edgeResults = await lease.Predictor.SegmentAsync(sourceImg).ConfigureAwait(false);
                        if (Volatile.Read(ref _segConsecutiveFails) != 0)
                        {
                            Interlocked.Exchange(ref _segConsecutiveFails, 0);
                        }
                    }
                    catch (Exception ex) when (IsRuntimeBackendFailure(ex))
                    {
                        ScheduleSegBackendHeal(predictorPool);
                        throw;
                    }
                    try
                    {
                        timings.Split("Inference");
                        // 角度推理需要 Mat 源图（与分割结果同一推理图坐标系）。灰度判向（无角度模型时对掩码
                        // 回退角度做 180° 去歧义，见 DetectionRecordService）同样需要推理图 BGR Mat。
                        // 任一启用才 ToMat，避免每帧无谓转换开销。
                        var angleEnabled = IsAngleDetectionEnabled && _anglePredictorPool is not null;
                        // 2026-09-08: 灰度判向配方级覆盖（null=跟随设置页全局开关，与面积过滤的
                        // "配方覆盖→全局回退"一致）。仅未启用角度检测时参与（见 DetectionRecordService else 分支）。
                        var brightnessOverride = CurrentRecipe?.YoloTool?.IsBrightnessDirectionEnabled;
                        bool grayDirectionEnabled = !angleEnabled
                            && (brightnessOverride ?? Settings.Instance.Algorithm.BrightnessDirectionEnabled);
                        // 2026-09-13: 特征池同样吃这张 Mat（作为其掩码回退路径的图像输入），故它的
                        // 开关也必须参与 gate。此前只看 angleEnabled || grayDirectionEnabled，导致
                        // 「开特征池 + 关亮度判向」时 angleMat=null → Evaluate 因 bgrImage is null
                        // 静默返回 null → 7 个特征全不算、头尾退化为掩码主轴角且无日志。
                        // 注意语义分层：featurePoolEnabled 只决定"要不要给特征池喂图"；
                        // 其中的亮度特征 ④ 仍由 brightnessDirectionEnabled 单独把守
                        // （DetectionRecordService 传参 brightnessEnabled），关亮度判向只关那一个特征。
                        // 契约与回归测试见 AngleMatGate（勿在此内联展开条件）。
                        var featurePoolEnabled = Settings.Instance.Algorithm.HeadTailFeaturePoolEnabled;
                        using var angleMat = Application.AngleMatGate.ShouldCreate(
                            angleEnabled, grayDirectionEnabled, featurePoolEnabled)
                            ? sourceImg.ToMat()
                            : null;
                        var buildResult = await _detectionRecord.BuildAndSaveAsync(
                            scanerResult,
                            edgeResults!,
                            _transformer,
                            edge.IsResize,
                            edge.ResizeScale,
                            edge.ResizeScaleY,
                            Settings.Instance.IsSaveDraw,
                            folder,
                            _toVgtService.Speed,
                            timings.TotalTime,
                            angleMat,
                            _anglePredictorPool,
                            CurrentRecipe?.YoloTool?.AngleDetection,
                            CurrentRecipe?.OffsetAngle ?? 0f,
                            angleEnabled,
                            _cts.Token,
                            // 2026-09-07: 配方级面积过滤覆盖（null=回退全局设置）
                            edge.MinMaskAreaPixels,
                            edge.MaxMaskAreaPixels,
                            // 2026-09-08: 配方级灰度判向覆盖（null=回退全局 Algorithm.BrightnessDirectionEnabled）
                            brightnessOverride,
                            // 2026-09-15: 配方级抓取点偏移（产品局部坐标系，mm）。
                            // 原先的 OffsetX/OffsetY（世界系常量）已移除，旧配方值在加载时一次性迁移到这两项。
                            CurrentRecipe?.GrabOffsetLongMm ?? 0f,
                            CurrentRecipe?.GrabOffsetShortMm ?? 0f).ConfigureAwait(false);
                        timings.Split("BuildAndSave");

                        // H17: 发送 AI 识别计数到 MainWindow（经 ProductTracker 去重后仅计新增产品）
                        if (buildResult.ValidRecords.Count > 0)
                        {
                            int newCount = _productTracker.FilterNewAndCount(buildResult.ValidRecords);
                            if (newCount > 0)
                            {
                                WeakReferenceMessenger.Default.Send(new Messages.AddDetectionMessage(newCount));
                            }
                        }

                        _toVgtService.SendTo(buildResult.ValidRecords, scanerResult, Settings.Instance.MessageReceiver);
                        // 绘制前先保存未绘制的原图（IsSaveSource 与 IsSaveDraw 平级，可独立开启）。
                        // 必须在 DrawImage 就地绘制之前执行，否则保存的 source 图也会带上检测框。
                        if (Settings.Instance.IsSaveSource && (scanerResult.HasBarcodeResults || edgeResults?.Count > 0))
                        {
                            QueueSaveSourceImage(sourceImg, folder, scanerResult.FrameNumber, _cts.Token);
                            timings.Split("SaveSourceQueued");
                        }
                        // 直接在 sourceImg 上绘制，省掉 CloneAs 的 0.89MB LOH 分配
                        await DrawImage(sourceImg, scanerResult, edgeResults!, buildResult.AngleDrawInfos, buildResult.HeadFlips, buildResult.HeadTrusted, buildResult.IndexedRecords, folder, timings, edge).ConfigureAwait(false);
                        // 在推理完成后更新结果列表（在 finally 释放 edgeResults 之前完成数据拷贝）
                        // 传入 IndexedRecords：与 edgeResults 严格同序，UI 按索引直接取值避免 Width×Height 错配
                        // 2026-09-07: 一并传入 MaskAreaByEdgeIndex，让主页表格能显示过滤后目标的掩码面积
                        UpdateInferenceResults(edgeResults, buildResult.IndexedRecords, buildResult.MaskAreaByEdgeIndex);
                        timings.Split("DrawImage");
                        timings.Split("TotalCost");

                        // 帧处理完成后的内存摘要
                        MemoryDiagnostics.LogFrameSummary(
                            scanerResult.FrameNumber,
                            imageDataSize,
                            sourceImgSize,
                            sourceImgSize,
                            _imageChannel.Reader.Count,
                            Volatile.Read(ref _activeInferenceCount),
                            timings.TotalTime);
                    }
                    finally
                    {
                        // L501: 显式释放 edgeResults 中每个 Segmentation 的 Mask 缓冲区（归还 ArrayPool），
                        // 避免每帧每框的 float[] 沉淀到 LOH/Gen2 导致内存持续上涨。
                        edgeResults?.Dispose();
                    }
                }
            }
            finally
            {
                // 每进入 ProcessImageAsync 即计为一帧处理（含跳过/异常帧），用于 FPS 计算
                Interlocked.Increment(ref _processedFrameCount);
                SignalInferenceDrainedIfNeeded(Interlocked.Decrement(ref _activeInferenceCount));
                // 推送最新帧指标给 MainViewModel，替代原 Func 反向回调
                SendFrameMetrics();
            }
        }
    }
}