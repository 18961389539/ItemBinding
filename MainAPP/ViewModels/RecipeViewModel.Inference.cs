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
// Split from the original monolithic file: RecipeViewModel test inference
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class RecipeViewModel
    {
        #region 推理结果
        /// <summary>
        /// 「测试推理」历史结果集合（供配方窗口「推理结果」页展示，最新一条插入在头部）
        /// </summary>
        public ObservableCollection<RecipeTestResultItem> TestResults { get; } = [];

        [ObservableProperty]
        private RecipeTestResultItem? _selectedResult;

        /// <summary>
        /// 清空测试推理历史（集合操作必须回到 UI 线程，列表绑定依赖 Dispatcher 序列化）
        /// </summary>
        [RelayCommand]
        private void ClearTestResults()
        {
            if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            {
                // VSTHRD110: fire-and-forget 封送回 UI 线程，显式丢弃 DispatcherOperation 以观察结果
                _ = d.InvokeAsync(ClearTestResults);
                return;
            }
            TestResults.Clear();
            SelectedResult = null;
        }

        /// <summary>
        /// 在头部插入一次测试推理结果并选中最新条目（推理结果页卡片列表最新在前，免倒序视图）。
        /// 推理任务在线程池线程完成（ConfigureAwait(false)），而 ObservableCollection 一旦被 UI
        /// CollectionView 订阅，跨线程变更会被拒绝或丢失——表现为“计数更新但卡片不显示”，
        /// 因此必须切换回 UI 线程再修改集合。
        /// </summary>
        private void AddTestResult(RecipeTestResultItem item)
        {
            if (System.Windows.Application.Current?.Dispatcher is { } d && !d.CheckAccess())
            {
                // VSTHRD110: fire-and-forget 封送回 UI 线程，显式丢弃 DispatcherOperation 以观察结果
                _ = d.InvokeAsync(() => AddTestResult(item));
                return;
            }
            TestResults.Insert(0, item);
            SelectedResult = item;
        }
        #endregion

        #region Inference
        /// <summary>
        /// AI模型推理
        /// </summary>
        [RelayCommand]
        private async Task Inference()
        {
            // 防御：VM 已被列表销毁（删除配方/卸载）时禁止继续操作，避免访问已释放 _cts
            if (_disposed)
            {
                LogService.Instance.Warning("测试推理：配方视图模型已释放，请关闭并重新打开配方后再试。");
                ShowWarning("配方视图已失效，请关闭后重新打开配方。");
                return;
            }
            // L372a: 防御性检查，避免覆盖正在执行的推理任务
            // L398a: 推理任务并发时给出提示，而非静默返回
            if (_inferenceTask is { IsCompleted: false })
            {
                LogService.Instance.Warning("测试推理：已有推理任务进行中，忽略本次点击。");
                ShowWarning("正在执行推理，请等待...");
                return;
            }
            LogService.Instance.Info("测试推理：开始执行");
            // M220: 跟踪推理任务，便于 Dispose 时等待
            _inferenceTask = InferenceCoreAsync();
            await _inferenceTask.ConfigureAwait(false);
        }

        private async Task InferenceCoreAsync()
        {
            try
            {
                if (this.YoloTool is null)
                {
                    // L357a: 错误消息统一使用中文
                    LogService.Instance.Warning("测试推理：YoloTool 配置为空。");
                    ShowError("AI 工具配置为空");
                    return;
                }
                if (this.YoloTool.EdgeDetection is null)
                {
                    LogService.Instance.Warning("测试推理：边缘检测配置为空。");
                    ShowError("边缘检测配置为空");
                    return;
                }
                if (_originalMat is null || _originalMat.Empty())
                {
                    LogService.Instance.Warning("测试推理：未加载图片(_originalMat 为空)。");
                    ShowError("请先加载图片");
                    return;
                }
                string fullname = this.YoloTool.EdgeDetection.ModelPath;
                if (!File.Exists(fullname))
                {
                    LogService.Instance.Warning($"测试推理：边缘检测模型文件不存在: {fullname}");
                    ShowError("边缘检测模型文件不存在，请检查路径。");
                    return;
                }
                if (_edgeModelPool == null || !string.Equals(_edgeModelPath, fullname, StringComparison.OrdinalIgnoreCase))
                {
                    LogService.Instance.Info($"测试推理：加载边缘模型 {fullname}");
                    _edgeModelPool?.Dispose();
                    // M97: 模型加载放在后台线程，避免阻塞 UI 线程
                    _edgeModelPool = await Task.Run(() => YoloPredictorPool.Create(fullname, new YoloPredictorPoolLayout
                    {
                        // L70: 使用提取的常量
                        CpuOnlyCount = RecipeInferenceCpuCount,
                    })).ConfigureAwait(false);
                    _edgeModelPath = fullname;
                }
                Stopwatch stopwatch = Stopwatch.StartNew();
                var edgeTool = this.YoloTool.EdgeDetection;
                YoloConfiguration configuration = new YoloConfiguration()
                {
                    IoU = edgeTool.IoU,
                    ApplyAutoOrient = edgeTool.ApplyAutoOrient,
                    Confidence = edgeTool.Confidence,
                    KeepAspectRatio = edgeTool.KeepAspectRatio,
                    SuppressParallelInference = edgeTool.SuppressParallelInference,
                };
                if (edgeTool.IsResize &&
                    (edgeTool.ResizeScale <= 0 || edgeTool.ResizeScaleY <= 0))
                {
                    LogService.Instance.Warning($"测试推理：缩放比例必须大于0 (X={edgeTool.ResizeScale}, Y={edgeTool.ResizeScaleY})");
                    ShowError("缩放比例必须大于0");
                    return;
                }

                using var inferenceMat = edgeTool.IsResize
                    ? _originalMat.Resize(new Size(0, 0), 1.0 / edgeTool.ResizeScale, 1.0 / edgeTool.ResizeScaleY)
                    : _originalMat.Clone();

                var matSize = (long)inferenceMat.Width * inferenceMat.Height * inferenceMat.Channels();
                MemoryDiagnostics.LogAllocation("RecopeVM(InferenceMat)", matSize,
                    $"WxH={inferenceMat.Width}x{inferenceMat.Height}, IsResize={edgeTool.IsResize}");

                // M284a: 传入 CancellationToken，使 Acquire 阶段可响应取消
                using var lease = _edgeModelPool!.Acquire(cancellationToken: _cts.Token);
                var segmentation = await lease.Predictor.SegmentAsync(inferenceMat.ToBytes(), configuration).ConfigureAwait(false);
                LogService.Instance.Info($"测试推理：边缘推理完成，目标数={segmentation?.Count ?? 0}，推理图={inferenceMat.Width}x{inferenceMat.Height}，IsResize={edgeTool.IsResize}，缩放比={edgeTool.ResizeScale}/{edgeTool.ResizeScaleY}");

                // 取分割结果中面积（OBB）最大的目标作为"产品"（单产品调试语义：角度推理与坐标显示共用同一目标）
                Segmentation? product = null;
                float bestArea = 0f;
                if (segmentation is not null)
                {
                    foreach (var seg in segmentation)
                    {
                        var rect = seg.GetMaskMinAreaRect();
                        if (rect.Area > bestArea)
                        {
                            bestArea = rect.Area;
                            product = seg;
                        }
                    }
                }
                LogService.Instance.Info($"测试推理：边缘目标数={segmentation?.Count ?? 0}，选定产品 OBB面积={bestArea:F0}");

                // ==== 角度模型推理（仅当配方勾选"角度检测"时执行）====
                // 2026-09-05 扩展：与主检测管线一致——先跑边缘分割，再对面积最大的目标
                // 走 摆正裁剪→角度模型分割→质心→(默认标定) 求出角度，用于配方页联调验证。
                var angleConfig = this.YoloTool?.AngleDetection;
                var angleEnabledFlag = this.YoloTool?.IsAngleDetectionEnabled ?? false;
                // 2026-09-08: 勾选角度检测但模型文件缺失/路径为空 → 视同"单个分割模型"状态。
                // 生产主链路在角度模型池加载失败（_anglePredictorPool=null → angleEnabled=false）时
                // 即走掩码回退+灰度判向；配方页同步回退，保证测试推理显示值 = 生产实发值。
                string? angleModelPath = angleConfig?.ModelPath;
                bool angleFileMissing = angleEnabledFlag && angleConfig is not null
                    && (string.IsNullOrWhiteSpace(angleModelPath) || !File.Exists(angleModelPath));
                var wantAngle = angleEnabledFlag && angleConfig is not null && !angleFileMissing;
                if (angleFileMissing)
                {
                    LogService.Instance.Warning($"测试推理：已勾选角度检测，但角度模型文件不存在: {angleModelPath}，本次按单个分割模型（掩码回退角度+灰度判向）执行。");
                    ShowWarning("已勾选角度检测，但角度模型文件不存在，本次按单个分割模型（掩码回退角度+灰度判向）执行。");
                }
                string angleEnterDesc = wantAngle ? "是" : angleEnabledFlag ? "否(模型文件缺失→分割回退)" : "否(跳过)";
                LogService.Instance.Info($"测试推理：角度检测开关={angleEnabledFlag}，AngleDetection配置={(angleConfig is null ? "null" : "存在")}，进入角度模型={angleEnterDesc}");
                AngleDetectionResult? angleResult = null;
                // 2026-09-11: 角度模型方向退化标记（特征质心偏移比低于配方阈值）。置位后不采信模型角度，
                // 改走与生产主链路相同的"掩码主轴角 + 灰度判向"兜底，保证配方页所见即所发。
                bool angleDirectionDegenerate = false;
                if (wantAngle)
                {
                    // 模型文件存在性已在上方判定（angleFileMissing=false 才进入），此处直接使用外层路径变量
                    var angleTool = angleConfig!;
                    LogService.Instance.Info($"测试推理：角度模型路径有效: {angleModelPath}");
                    try
                    {
                        // 懒加载角度模型池（CPU，与边缘检测同规格），路径变化时重建
                        if (_angleModelPool == null || !string.Equals(_angleModelPath, angleModelPath, StringComparison.OrdinalIgnoreCase))
                        {
                            LogService.Instance.Info($"测试推理：加载角度模型 {angleModelPath}");
                                _angleModelPool?.Dispose();
                                // wantAngle 成立 ⇒ angleFileMissing=false ⇒ 路径非空且文件存在，此处安全断言
                                _angleModelPool = await Task.Run(() => YoloPredictorPool.Create(angleModelPath!, new YoloPredictorPoolLayout
                            {
                                CpuOnlyCount = RecipeInferenceCpuCount,
                            })).ConfigureAwait(false);
                            _angleModelPath = angleModelPath;
                        }
                        // 产品选择已在上层（分割完成后）完成，此处直接使用面积最大的目标做角度推理
                        if (product is null)
                        {
                            LogService.Instance.Warning("测试推理：边缘检测未输出目标，无法执行角度推理。");
                            ShowWarning("边缘检测未输出目标，无法执行角度推理。");
                        }
                        else
                        {
                            using var angleLease = _angleModelPool!.Acquire(cancellationToken: _cts.Token);
                            angleResult = await AngleDetectionProcessor.ComputeAngleAsync(
                                inferenceMat,
                                product,
                                // 已标定→世界坐标变换器（角度为真实世界角度，与主流程一致）；
                                // 未标定→默认(未初始化)变换器，处理器自动退化为图像像素方向角并提示非真实值
                                BuildCalibratedTransformer() ?? new CoordinateTransformer(),
                                edgeTool.IsResize,
                                edgeTool.ResizeScale,
                                edgeTool.ResizeScaleY,
                                angleLease.Predictor,
                                angleTool,
                                (float)OffsetAngle,
                                _cts.Token,
                                // 诊断回调：仅配方页调试使用，把质量门拒绝原因落盘
                                reason => LogService.Instance.Warning($"测试推理：角度质量门拒绝，原因={reason}")).ConfigureAwait(false);
                            if (angleResult is null)
                            {
                                LogService.Instance.Warning("测试推理：角度模型已推理但未输出有效结果（具体原因见角度质量门日志）。");
                                ShowWarning("角度模型已推理但未输出有效结果（质量门不过/无特征），详见日志。");
                            }
                            else if (angleResult.DirectionOffsetRatio < angleTool.MinCentroidOffsetRatio)
                            {
                                // 2026-09-11: 模型方向退化——特征质心与产品质心几乎重合，方向向量趋近 0，
                                // 角度由噪声决定却能通过原有三道质量门。此处不采信模型角度，回落掩码兜底
                                // （与生产主链路 DetectionRecordService 的判定同口径、同阈值）。
                                double degenerateRatio = angleResult.DirectionOffsetRatio;
                                angleDirectionDegenerate = true;
                                angleResult = null;
                                string degenerateMsg =
                                    $"测试推理：角度模型方向退化（特征质心偏移比 {degenerateRatio:F4} < " +
                                    $"{angleTool.MinCentroidOffsetRatio:F4}），改用掩码主轴角 + 灰度判向兜底。";
                                LogService.Instance.Warning(degenerateMsg);
                                ShowWarning(degenerateMsg);
                            }
                            else
                            {
                                // 2026-09-05: AngleDetectionProcessor 返回"世界角+Offset"的原始值（可能越界，
                                // 如 328° 或负值），此处 ToRobotAngle 归一化到全系统规范域 (-180,180]，
                                // 与生产 DbModel.Angle 落库/机器人实收 RZ 同域，测试推理显示所见即所发。
                                angleResult = angleResult with { Angle = ToVGT.ToRobotAngle(angleResult.Angle) };
                                LogService.Instance.Info($"测试推理角度: {angleResult.Angle:F1}°");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 取消向上传播
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error($"角度模型推理失败: {ex}");
                        ShowWarning($"角度模型推理失败: {ex.Message}");
                    }
                }

                // H54: ToImageSharp() 返回的 Image 与 PlotImageAsync 返回的 colorImage 均为 IDisposable，需用 using 释放
                // 2026-09-05: SegmentAsync 可能返回 null（无目标/失败）。CreateDisplayImageAsync 统一所有权：
                // 有分割结果 → 画分割可视化（输入 sharpImg 用后即弃）；无结果 → 直接借用 sharpImg 作为显示图。
                // 二者都只由外层 colorImage 这一个 using 负责释放，避免双重释放；非泛型 Image 亦无 Clone()。
                var sharpImg = inferenceMat.ToImageSharp();
                using var colorImage = await CreateDisplayImageAsync(segmentation, sharpImg).ConfigureAwait(false);
                stopwatch.Stop();

                // 无分割可视化时的显示图：直接使用原推理图；分割成功则释放输入图（PlotImageAsync 已生成新图）
                async Task<Image> CreateDisplayImageAsync(YoloResult<Segmentation>? seg, Image source)
                {
                    if (seg is null)
                    {
                        return source; // 所有权移交给调用方 using（colorImage）
                    }

                    using (source)
                    {
                        return await seg.PlotImageAsync(source).ConfigureAwait(false);
                    }
                }

                // H54: colorImage.ToMat() 返回的中间 Mat 也需释放，避免 OpenCV 非托管内存泄漏
                using var rawPlottedMat = colorImage.ToMat();
                if (angleResult is not null)
                {
                    // 角度质心坐标为"原图像素"；rawPlottedMat 为推理图坐标，需除以缩放系数还原后再绘制
                    double sx = edgeTool.IsResize ? edgeTool.ResizeScale : 1.0;
                    double sy = edgeTool.IsResize ? edgeTool.ResizeScaleY : 1.0;
                    var productPoint = new Point(angleResult.ProductCentroidImage.X / sx, angleResult.ProductCentroidImage.Y / sy);
                    var featurePoint = new Point(angleResult.FeatureCentroidImage.X / sx, angleResult.FeatureCentroidImage.Y / sy);
                    // 黄:产品质心 → 特征质心方向线(即角度方向)；品红:特征质心
                    Cv2.Circle(rawPlottedMat, productPoint, 6, new Scalar(0, 255, 255), 2, LineTypes.AntiAlias);
                    Cv2.Circle(rawPlottedMat, featurePoint, 6, new Scalar(255, 0, 255), 2, LineTypes.AntiAlias);
                    Cv2.Line(rawPlottedMat, productPoint, featurePoint, new Scalar(0, 255, 255), 2, LineTypes.AntiAlias);
                }
                using var plottedMat = rawPlottedMat.Resize(new Size(), edgeTool.ResizeScale, edgeTool.ResizeScaleY);
                UpdateImageForShow(plottedMat.ToBitmapSource());

                string angleOutDesc = angleResult is not null ? $"{angleResult.Angle:F1}°" : "无";

                // ==== 测试结果汇总：产品坐标 X/Y + 角度 ====
                // 产品参考点（原图像素）：2026-09-05 起与生产发送口径一致——固定取面积最大目标的
                // 最小外接旋转矩形中心（minAreaRect.Center），不再使用角度处理器产品质心或掩码加权质心；
                // 保证配方页"测试推理显示值 = 实际发送给机器人的坐标"。缩放语义与原掩码质心路径相同。
                Point2f productPixel;
                // 掩码主轴角（度）：角度未启用时生产会以该角(+OffsetAngle)兜底发机器人，配方页同步展示
                double maskFallbackAngleDeg = 0;
                // 2026-09-08: 灰度判向所需掩码矩形字段（推理图坐标系，与 inferenceMat/分割掩码一致），
                // 供回退分支复用生产主链路同款判向实现；仅存在有效掩码像素（MaskArea>0）时赋值。
                float maskRectCenterX = 0, maskRectCenterY = 0, maskRectAngleDeg = 0, maskArea = 0;
                // 2026-09-08: 掩码矩形宽度（推理图像素=长轴长），掩码回退角度走三点标定换算世界角时
                // 需沿长轴取端点（ComputeMaskAngleCalibrated），与生产 BuildAndSaveAsync 口径一致。
                float maskRectWidth = 0;
                if (product is not null)
                {
                    var (_, rect) = product.GetMaskStats();
                    productPixel = new Point2f(
                        rect.Center.X * (edgeTool.IsResize ? edgeTool.ResizeScale : 1f),
                        rect.Center.Y * (edgeTool.IsResize ? edgeTool.ResizeScaleY : 1f));
                    if (rect.MaskArea > 0)
                    {
                        maskFallbackAngleDeg = rect.Angle;
                        maskRectCenterX = rect.Center.X;
                        maskRectCenterY = rect.Center.Y;
                        maskRectAngleDeg = rect.Angle;
                        maskRectWidth = rect.Width;
                        maskArea = rect.MaskArea;
                    }
                }
                else
                {
                    productPixel = default;
                }

                // 世界坐标换算（矩形中心版，作为"无有效掩码"时的兜底）：
                // 三点标定完成→按主流程口径转 mm；未标定→图像像素坐标并明确提示（非真实坐标，仅供联调比对）。
                // 2026-09-15: 有有效掩码时，下方判向完成后会按"抓取点"重算并覆盖这里的结果
                // （抓取点需要在头尾朝向确定之后才能算）。
                string coordText = "无产品";
                string remark;
                double? worldX = null, worldY = null;
                if (segmentation is { Count: > 0 })
                {
                    var protoCalibTf = BuildCalibratedTransformer();
                    if (protoCalibTf is not null)
                    {
                        var phys = protoCalibTf.ImageToPhysical(productPixel);
                        worldX = phys.X;
                        worldY = phys.Y;
                        coordText = $"X={phys.X:F2} mm, Y={phys.Y:F2} mm";
                    }
                    else
                    {
                        coordText = $"X={productPixel.X:F1} px, Y={productPixel.Y:F1} px（未标定，非真实坐标）";
                    }
                }

                // 2026-09-13: 角度口径与生产 BuildAndSaveAsync 统一——基础角恒为掩码主轴世界角 + OffsetAngle，
                // 角度模型只产出翻转信号（不再直接采信其角度数值）；sendAngle = 基础角 ± 180°（哪端是头）。
                double? sendAngle = null;
                bool angleFromMaskFallback = false;
                bool brightnessDirectionTried = false;
                BrightnessDirectionStats? brightnessStats = null;
                HeadTailPoolDecision? testPoolDecision = null;

                // ---- 唯一基础角：掩码主轴世界角 + OffsetAngle（与生产 ComputeMaskAngleCalibrated 同口径）----
                // 主轴两端点 ImageToPhysical 后 atan2，未标定时退回图像角（同生产行为，仅供联调）。
                var calibForAngle = BuildCalibratedTransformer();
                double maskWorldAngle = calibForAngle is not null && maskArea > 0
                    ? DetectionRecordService.ComputeMaskAngleCalibrated(
                        maskRectCenterX, maskRectCenterY, maskRectAngleDeg, maskRectWidth, maskArea,
                        calibForAngle, edgeTool.IsResize, edgeTool.ResizeScale, edgeTool.ResizeScaleY)
                    : maskFallbackAngleDeg;
                var fallbackAngle = maskWorldAngle + (double)OffsetAngle;

                // ---- 模型翻转信号（2026-09-13：只判头尾，不提供角度数值）----
                // 特征端向量（特征中心 − 产品中心）投影到主轴 u；与 fallbackAngle 同坐标系。
                double? modelFlipAngle = null;
                if (angleResult is not null && !angleDirectionDegenerate)
                {
                    double dxModel, dyModel;
                    if (calibForAngle is not null)
                    {
                        var (cfx, cfy) = calibForAngle.ImageToPhysical(
                            angleResult.FeatureCentroidImage.X, angleResult.FeatureCentroidImage.Y);
                        var (cpx, cpy) = calibForAngle.ImageToPhysical(
                            angleResult.ProductCentroidImage.X, angleResult.ProductCentroidImage.Y);
                        dxModel = cfx - cpx;
                        dyModel = cfy - cpy;
                    }
                    else
                    {
                        double sx = edgeTool.IsResize ? edgeTool.ResizeScale : 1.0;
                        double sy = edgeTool.IsResize ? edgeTool.ResizeScaleY : 1.0;
                        dxModel = (angleResult.FeatureCentroidImage.X - angleResult.ProductCentroidImage.X) / sx;
                        dyModel = (angleResult.FeatureCentroidImage.Y - angleResult.ProductCentroidImage.Y) / sy;
                    }

                    double modelLen = Math.Sqrt(dxModel * dxModel + dyModel * dyModel);
                    if (modelLen > 1e-9)
                    {
                        double radM = fallbackAngle * Math.PI / 180.0;
                        double cosM = (dxModel * Math.Cos(radM) + dyModel * Math.Sin(radM)) / modelLen;
                        modelFlipAngle = cosM >= 0 ? fallbackAngle : fallbackAngle + 180.0;
                    }
                }

                if (modelFlipAngle is not null)
                {
                    sendAngle = ToVGT.ToRobotAngle(modelFlipAngle.Value);
                }
                else
                {
                    // 模型信号不可用（未启用/方向退化/无输出）→ 特征池/亮度兜底（与生产消歧链同构）。
                    // 灰度判向已并入特征池成为 BrightnessDiff 特征（2026-09-13），不再独立兜底；
                    // 测试推理显示值 = 生产实发值（所见即所发）这一不变式继续成立。
                    bool brightnessEnabled = this.YoloTool?.IsBrightnessDirectionEnabled
                        ?? Models.Settings.Instance.Algorithm.BrightnessDirectionEnabled;
                    if (product is not null && maskArea > 0)
                    {
                        // 复用生产同一实现：inferenceMat 与 product 掩码/Bounds 同处推理图坐标系，
                        // 与特征池内部灰度统计的坐标系约定一致；死区/拉伸参数由全局设置提供。
                        // 配方页不传条码（测试推理无真值），codePresent=false → 不做码区剔除。
                        var alg = Models.Settings.Instance.Algorithm;
                        testPoolDecision = HeadTailFeaturePool.Evaluate(
                            fallbackAngle, inferenceMat, product,
                            maskRectCenterX, maskRectCenterY, maskRectAngleDeg, maskArea,
                            maskRectWidth, codePresent: false, codeCenterX: 0, codeCenterY: 0,
                            alg.HeadTailFeatureDeadband,
                            brightnessEnabled,
                            alg.BrightnessContrastStretchEnabled,
                            alg.BrightnessStretchLowPercentile,
                            alg.BrightnessStretchHighPercentile,
                            out brightnessStats);
                        sendAngle = ToVGT.ToRobotAngle(
                            testPoolDecision is { Decisive: true }
                                ? testPoolDecision.Angle
                                : fallbackAngle);
                        brightnessDirectionTried = brightnessEnabled;
                    }
                    else
                    {
                        sendAngle = ToVGT.ToRobotAngle(fallbackAngle);
                    }
                    angleFromMaskFallback = true;
                }

                // ── 抓取点（2026-09-15）：与生产 BuildAndSaveAsync 同口径 ──
                // 配方页必须与实际发送一致（"所见即所发"不变式），故产品坐标从矩形中心改算为抓取点。
                // 朝向判定与生产同源：模型翻转 → 特征池 → 无向回退角；全部回退时（朝向不可信）
                // 长轴偏移不生效、自动退化为中心，避免抓反。
                if (maskArea > 0)
                {
                    var rawSendAngle = modelFlipAngle
                        ?? (testPoolDecision is { Decisive: true } pdx ? pdx.Angle : (double?)null)
                        ?? fallbackAngle;
                    var headTrustedForGrab = modelFlipAngle.HasValue || testPoolDecision is { Decisive: true };
                    var headFlippedForGrab = DetectionRecordService.IsHeadOppositeDegrees(rawSendAngle, fallbackAngle);
                    var effectiveGrabLong = headTrustedForGrab ? GrabOffsetLongMm : 0d;

                    // 矩形在推理图坐标系上算出，productPixel 已按缩放比还原到原图坐标系；
                    // 非等比缩放（ResizeScale != ResizeScaleY）会改变方向，故长轴方向按同一比例换算。
                    var grabDirX = Math.Cos(maskRectAngleDeg * Math.PI / 180.0);
                    var grabDirY = Math.Sin(maskRectAngleDeg * Math.PI / 180.0);
                    if (edgeTool.IsResize)
                    {
                        grabDirX *= edgeTool.ResizeScale;
                        grabDirY *= edgeTool.ResizeScaleY;
                    }

                    var grabDirLen = Math.Sqrt((grabDirX * grabDirX) + (grabDirY * grabDirY));
                    double kGrabLong = 1.0, kGrabShort = 1.0;
                    if (calibForAngle is not null && grabDirLen > 1e-12)
                    {
                        kGrabLong = GrabPointCalculator.PixelsPerMmAlong(
                            calibForAngle, productPixel.X, productPixel.Y, grabDirX / grabDirLen, grabDirY / grabDirLen);
                        kGrabShort = GrabPointCalculator.PixelsPerMmAlong(
                            calibForAngle, productPixel.X, productPixel.Y, -grabDirY / grabDirLen, grabDirX / grabDirLen);
                    }

                    var (grabPxX, grabPxY) = GrabPointCalculator.ComputeImagePoint(
                        productPixel.X, productPixel.Y, grabDirX, grabDirY, headFlippedForGrab,
                        effectiveGrabLong, GrabOffsetShortMm, kGrabLong, kGrabShort);

                    if (calibForAngle is not null)
                    {
                        var (gx, gy) = calibForAngle.ImageToPhysical(grabPxX, grabPxY);
                        worldX = gx;
                        worldY = gy;
                        coordText = $"X={gx:F2} mm, Y={gy:F2} mm";
                    }
                    else
                    {
                        worldX = null;
                        worldY = null;
                        coordText = $"X={grabPxX:F1} px, Y={grabPxY:F1} px（未标定，非真实坐标）";
                    }
                }

                // 判向完成（两侧均有像素）时附上亮度统计，页面上直接核对头端明暗与死区是否合适。
                // 2026-09-11: 半区命名改为"正向/负向"（原名"亮/暗半区"与事实相反，两值大小关系不固定）；
                // 并显示掩码内对比度拉伸窗口，便于现场判断绝对灰度水平（窗口整体偏低说明成像偏暗）。
                string brightnessDetail = brightnessStats is { } bStats
                    ? $" 正向半区={bStats.MeanPlus:F0} 负向半区={bStats.MeanMinus:F0} 差值={bStats.Diff:F1}"
                      + (double.IsNaN(bStats.Low)
                          ? "（未拉伸）"
                          : $"（拉伸窗口 {bStats.Low:F0}~{bStats.High:F0}）")
                    : string.Empty;
                string fallbackAngleDesc = angleFromMaskFallback
                    ? (angleDirectionDegenerate ? "模型方向退化→" : string.Empty)
                      + (testPoolDecision is { Decisive: true } pd
                          ? $"掩码主轴角+特征池[{pd.SourceFeature}]{brightnessDetail}"
                          : brightnessDirectionTried
                              ? $"掩码主轴角+brightness特征{brightnessDetail}"
                              : "掩码主轴角")
                    : string.Empty;
                // 2026-09-13: 模型翻转路径（角度=掩码主轴角 ± 180°）单独标注，与回退链路区分
                string modelFlipDesc = modelFlipAngle is not null ? "掩码主轴角+模型翻转" : string.Empty;
                string angleText = sendAngle is not null
                    ? $"\n角度: {sendAngle:F1}°"
                      + (modelFlipAngle is not null
                          ? $"（{modelFlipDesc}）"
                          : angleFromMaskFallback
                              ? $"（{fallbackAngleDesc}）"
                              : string.Empty)
                    : wantAngle
                        ? "\n角度: 无（角度模型未输出有效结果，详见日志质量门原因）"
                        : string.Empty;
                if (!wantAngle)
                {
                    // 2026-09-08: 勾选但模型文件缺失已回退单分割模型，备注区分原因（与生产行为一致）
                    string cause = angleFileMissing ? "角度模型文件缺失" : "角度未启用";
                    remark = (worldX is null ? "未标定 · " : string.Empty)
                             + (angleFromMaskFallback ? $"{cause}({fallbackAngleDesc})" : cause);
                }
                else if (angleResult is null)
                {
                    remark = worldX is null ? "未标定 · 角度无输出" : "角度无输出";
                }
                else
                {
                    remark = worldX is null ? "未标定 · 推理成功" : "OK（标定真实值）";
                }
                var summary = $"推理完成，耗时: {stopwatch.Elapsed.TotalMilliseconds:F0} ms\n"
                            + $"目标数: {segmentation?.Count ?? 0}\n"
                            + $"产品坐标: {coordText}"
                            + angleText;
                string angleOutLog = sendAngle is not null ? $"{sendAngle:F1}°" : angleOutDesc;
                LogService.Instance.Info($"测试推理完成：耗时={stopwatch.Elapsed.TotalMilliseconds:F0}ms，目标数={segmentation?.Count ?? 0}，产品坐标={coordText}，角度输出={angleOutLog}");
                ShowInfo(summary);

                // 记录到「推理结果」页并选中最新一条（角度=最终发送域值，与机器人实收 RZ 一致）
                AddTestResult(new RecipeTestResultItem(
                    DateTime.Now,
                    worldX,
                    worldY,
                    productPixel.X,
                    productPixel.Y,
                    sendAngle,
                    segmentation?.Count ?? 0,
                    stopwatch.Elapsed.TotalMilliseconds,
                    remark));
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"推理失败: {ex}");
                AddTestResult(new RecipeTestResultItem(
                    DateTime.Now, null, null, 0, 0, null, 0, 0, $"推理失败: {ex.Message}"));
                ShowError($"推理失败: {ex.Message}");
            }
        }
        #endregion
    }
}