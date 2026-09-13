using System;
using System.Threading;
using System.Threading.Tasks;
using CoordinateSystemMapping;
using Extensions;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Extensions;
using MainAPP.Models;
using OpenCvSharp;

namespace MainAPP.Application
{
    /// <summary>
    /// 角度模型计算结果：绝对角度 + 用于绘制的两个中心点（原图像素坐标）+ 方向可信度。
    /// </summary>
    /// <param name="Angle">角度原始输出（世界 atan2 绝对角 [0,360) + OffsetAngle，可能越界/为负；
    /// 未归一化，由调用方 AngleTracker.Normalize 或 ToRobotAngle 落域 (-180,180]）。</param>
    /// <param name="FeatureCentroidImage">角度模型推理中心（特征掩码最小外接旋转矩形中心，原图像素坐标）。</param>
    /// <param name="ProductCentroidImage">产品中心点（产品掩码最小外接旋转矩形中心，原图像素坐标）。</param>
    /// <param name="DirectionOffsetRatio">方向可信度 = |特征中心点 − 产品中心点| / 产品长轴长度（原图像素口径）。
    /// 该向量正是角度的来源；比值趋近 0（特征居中/对称）时 atan2 分子分母同时趋零、角度由噪声决定，
    /// 调用方应比对本值是否低于 <see cref="YoloTool.MinCentroidOffsetRatio"/> 再决定是否采信模型角度。
    /// 2026-09-11 新增，用于修复"方向退化帧静默通过并输出无效角度"。</param>
    public sealed record AngleDetectionResult(
        double Angle,
        Point2f FeatureCentroidImage,
        Point2f ProductCentroidImage,
        double DirectionOffsetRatio);

    /// <summary>
    /// 角度检测处理器：分割 - 分割两级模型，为角度提供**头尾翻转信号**。
    /// 流程：产品分割结果 → OBB 摆正裁剪 → 角度模型（分割）输出方向特征掩码
    /// → 特征中心（特征掩码 OBB 中心）→ 逆变换回推理图坐标 → ×缩放还原原图像素
    /// → 三点标定转世界坐标 → atan2(特征 OBB 中心 − 产品 OBB 中心) → [0,360) + OffsetAngle（未归一化，
    /// 由调用方 AngleTracker.Normalize / ToRobotAngle 统一落域 (-180,180]）。
    /// <para>2026-09-13：生产主链路（DetectionRecordService）已改为"基础角 = 掩码主轴世界角"，
    /// 本处理器输出的 <see cref="AngleDetectionResult.Angle"/> 不再作为最终角度被直接采信——
    /// 仅提供特征中心/产品中心（OBB 中心）作翻转信号来源 + <see cref="AngleDetectionResult.DirectionOffsetRatio"/>
    /// 判方向可信度；Angle 字段保留供绘制/诊断/配方页展示。</para>
    /// 任一环节失败/质量门不过返回 null，由调用方回退（跨帧锁定/未知哨兵 -9999）。
    /// 质量门参数来自配方 AngleDetection 配置（YoloTool），见
    /// <see cref="YoloTool.CropPaddingRatio"/> / <see cref="YoloTool.MinMaskFillRatio"/> / <see cref="YoloTool.MinFeatureAreaRatio"/>。
    /// <para>2026-09-11 起另输出 <see cref="AngleDetectionResult.DirectionOffsetRatio"/>（方向可信度）：
    /// 上面三道门只管"能不能算"，不看"算出来的方向可不可信"。特征居中/对称时两个中心点几乎重合，
    /// 方向向量趋近 0，角度由噪声决定却能通过全部门限。调用方应比对该比值与
    /// <see cref="YoloTool.MinCentroidOffsetRatio"/>，低于阈值时改用"掩码主轴角 + 灰度判向"兜底。</para>
    /// </summary>
    public static class AngleDetectionProcessor
    {
        /// <summary>
        /// 计算单个产品的角度（返回原始值，域未归一）：标定完成（transformer 已初始化）时为世界坐标系角度；
        /// 未标定时自动退化为图像像素方向角（供配方页等无标定场景联调，方向语义与标定一致）。
        /// 注意：返回角 = atan2 结果 + OffsetAngle，可能越界（如 355 或 -5），请调用方
        /// 使用 ToVGT.ToRobotAngle 归一化到全系统规范域 (-180,180]。
        /// </summary>
        /// <param name="sourceImage">推理图（与产品分割结果同一坐标系）。</param>
        /// <param name="product">产品分割结果（掩码/OBB 均在推理图坐标系）。</param>
        /// <param name="transformer">三点标定变换器（图像像素 → 世界 mm）。</param>
        /// <param name="isResize">是否缩放推理（坐标还原系数）。</param>
        /// <param name="resizeWidth">X 方向缩放系数。</param>
        /// <param name="resizeHeight">Y 方向缩放系数。</param>
        /// <param name="anglePredictor">角度模型预测器（分割任务）。</param>
        /// <param name="angleTool">角度检测配置（裁剪 padding / 质量门阈值）。</param>
        /// <param name="offsetAngle">手动标定的角度偏移（配方 OffsetAngle，度）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="onRejected">可选诊断回调：任一环节被质量门拒绝/失败时以原因字符串调用
        /// （仅配方页等调试场景传入；主流程逐帧调用不传，避免每帧写日志刷屏）。</param>
        /// <returns>角度计算结果（含用于绘制的质心，原图像素坐标）；质量门不过/失败返回 null。</returns>
        public static async Task<AngleDetectionResult?> ComputeAngleAsync(
            Mat sourceImage,
            Segmentation product,
            CoordinateTransformer transformer,
            bool isResize,
            int resizeWidth,
            int resizeHeight,
            YoloPredictor anglePredictor,
            YoloTool angleTool,
            float offsetAngle,
            CancellationToken cancellationToken = default,
            Action<string>? onRejected = null)
        {
            if (sourceImage is null || product is null || transformer is null || anglePredictor is null || angleTool is null)
            {
                onRejected?.Invoke("参数为空(sourceImage/product/transformer/anglePredictor/angleTool)");
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 1. 产品 OBB + 掩码质量门
            var rect = product.GetMaskMinAreaRect();
            if (rect.Width <= 1 || rect.Height <= 1 || rect.Area <= 0)
            {
                onRejected?.Invoke($"产品 OBB 无效(W={rect.Width},H={rect.Height},Area={rect.Area})");
                return null;
            }

            var fillRatio = rect.MaskArea / rect.Area;
            if (fillRatio < angleTool.MinMaskFillRatio)
            {
                onRejected?.Invoke($"产品掩码填充率 {fillRatio:F3} < MinMaskFillRatio={angleTool.MinMaskFillRatio}");
                return null;
            }

            // 2. 摆正裁剪（推理图坐标系）
            var center = new Point2f(rect.Center.X, rect.Center.Y);
            using var crop = sourceImage.CropAligned(
                center, rect.Width, rect.Height, rect.Angle, angleTool.CropPaddingRatio, out var toSource);

            // 3. 角度模型推理（分割任务），输入裁剪图
            using var cropImage = crop.ToImageSharp();
            using var features = await anglePredictor.SegmentAsync(cropImage).ConfigureAwait(false);
            if (features is null || features.Count == 0)
            {
                onRejected?.Invoke($"角度模型在裁剪图 {crop.Width}x{crop.Height} 上无任何输出");
                return null;
            }

            // 4. 取掩码面积最大的特征区域
            Segmentation? best = null;
            float bestArea = 0f;
            foreach (var feature in features)
            {
                var featureRect = feature.GetMaskMinAreaRect();
                if (featureRect.MaskArea > bestArea)
                {
                    bestArea = featureRect.MaskArea;
                    best = feature;
                }
            }

            if (best is null || bestArea <= 0)
            {
                onRejected?.Invoke($"角度模型输出 {features.Count} 个目标但无可选特征掩码");
                return null;
            }

            // 5. 特征面积质量门（相对裁剪图）
            var featureRatio = bestArea / (crop.Width * crop.Height);
            if (featureRatio < angleTool.MinFeatureAreaRatio)
            {
                onRejected?.Invoke($"特征掩码面积占比 {featureRatio:F5} < MinFeatureAreaRatio={angleTool.MinFeatureAreaRatio}");
                return null;
            }

            // 6. 特征中心（裁剪图坐标）= 特征掩码最小外接旋转矩形（OBB）中心，逆变换 → 推理图坐标。
            //    2026-09-13: 由灰度加权质心 GetMaskCentroid 改为 OBB 中心——与系统主口径（落库/发送坐标
            //    maskMinAreaRect.Center、特征池扫描中心、UI 箭头起点）统一，消除"角度路径用质心、
            //    其余全用 OBB 中心"的口径分叉。OBB 中心 = 凸包包围框几何中心，不含置信度软信息，
            //    理想矩形成品上与质心近似重合；异形/毛边产品的抗噪性略降，换取全链路口径一致可核对。
            var cfFeatureRect = best.GetMaskMinAreaRect();
            var cfCrop = cfFeatureRect.Center;
            var cfInfer = toSource.Apply(cfCrop.X, cfCrop.Y);

            // 7. 产品中心（推理图坐标）= 产品掩码最小外接旋转矩形中心（rect 已在质量门处取得，同上统一 OBB 口径）
            var cpInfer = rect.Center;

            // 8. 还原到原图像素坐标（推理图坐标 × 缩放系数）
            var cfImg = new Point2f(
                isResize ? cfInfer.X * resizeWidth : cfInfer.X,
                isResize ? cfInfer.Y * resizeHeight : cfInfer.Y);
            var cpImg = new Point2f(
                isResize ? cpInfer.X * resizeWidth : cpInfer.X,
                isResize ? cpInfer.Y * resizeHeight : cpInfer.Y);

            // 9. 方向可信度：|特征质心 − 产品质心| / 产品长轴长度（原图像素口径）。
            //    这条向量正是角度的来源；比值趋近 0 时 atan2 的分子分母同时趋零，角度由噪声决定。
            //    此处不做拒绝（硬质量门仍只管"能不能算"），把比值交给调用方，由它决定是否改用灰度判向兜底。
            double longAxisPx = isResize ? rect.Width * (double)resizeWidth : rect.Width;
            double offsetRatio = ComputeDirectionOffsetRatio(cfImg, cpImg, longAxisPx);

            // 10. 方向角：atan2(特征 − 产品)。
            //    标定完成时用世界坐标(mm)之差计算——世界系下方向不随相机歪斜/旋转变化；
            //    未标定（如配方页联调）时退化为原图像素方向角。CoordinateTransformer 未初始化时
            //    ImageToPhysical 内部比例因子为 0 会除零返回 NaN，绝不能拿未初始化变换器参与求角。
            double dx, dy;
            if (transformer.IsInitialized)
            {
                var cfWorld = transformer.ImageToPhysical(cfImg);
                var cpWorld = transformer.ImageToPhysical(cpImg);
                dx = cfWorld.X - cpWorld.X;
                dy = cfWorld.Y - cpWorld.Y;
            }
            else
            {
                // 2026-09-13: 未标定统一回推理图坐标系取像素角——此前直接用原图像素差 atan2，
                // 而掩码路径未标定返回的是推理图系主轴角（rectAngleDeg）；X/Y 不等比缩放时两个
                // "图像角"不相等，配方页无标定联调会在两路径间看到角度差。此处分轴还原到推理图系，
                // 与 TryApplyBarcodeHeadDirection 的未标定分支预处理一致，两路径联调口径统一。
                dx = (cfImg.X - cpImg.X) / (isResize ? resizeWidth : 1);
                dy = (cfImg.Y - cpImg.Y) / (isResize ? resizeHeight : 1);
            }
            var rad = Math.Atan2(dy, dx);
            var angle = rad * 180.0 / Math.PI + offsetAngle;
            angle %= 360.0;
            if (angle < 0)
            {
                angle += 360.0;
            }

            return new AngleDetectionResult(angle, cfImg, cpImg, offsetRatio);
        }

        /// <summary>
        /// 方向可信度 = |特征中心点 − 产品中心点| / 产品长轴长度（两个输入必须同处一个坐标系，通常为原图像素）。
        /// 这条向量就是角度的来源：比值越大方向越稳，趋近 0（特征居中/对称）时 atan2 的分子分母同时趋零，
        /// 角度将由噪声决定。调用方按 <see cref="YoloTool.MinCentroidOffsetRatio"/> 判定是否采信模型角度。
        /// </summary>
        /// <param name="featureCentroid">特征中心点（特征掩码 OBB 中心）。</param>
        /// <param name="productCentroid">产品中心点（产品掩码 OBB 中心）。</param>
        /// <param name="longAxisLength">产品长轴长度（与中心点同坐标系）。</param>
        /// <returns>偏移比（无量纲）；长轴长度无效（≤1）时返回 0，由调用方按"方向退化"处理。</returns>
        internal static double ComputeDirectionOffsetRatio(
            Point2f featureCentroid, Point2f productCentroid, double longAxisLength)
        {
            if (longAxisLength <= 1.0)
            {
                return 0.0;
            }

            double dx = featureCentroid.X - productCentroid.X;
            double dy = featureCentroid.Y - productCentroid.Y;
            return Math.Sqrt(dx * dx + dy * dy) / longAxisLength;
        }
    }
}
