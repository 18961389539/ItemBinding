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
// Split from the original monolithic file: HomeViewModel image drawing
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class HomeViewModel
    {
        // L328: DrawPolygon 线宽
        private const float DrawPolygonLineWidth = 3f;
        // 2026-09-09: 头尾分割线（LimeGreen，短轴方向"一分为二"）与 发送角度向量箭头（橙，主轴方向）并存。
        // 分割线沿用旧"一分为二"参考线：沿掩码矩形短轴穿过中心，两端外扩，直观划分产品头/尾两半；
        // 向量箭头从掩码最小外接矩形中心（=发送 X/Y 图像点）沿矩形宽度轴（长轴）伸出，表达发送朝向。
        // 二者垂直交于矩形中心。颜色全限定（文件同时 using System.Windows.Media，裸 Color 会产生 CS0104 二义）
        private static readonly SixLabors.ImageSharp.Color MaskAxisLineColor = SixLabors.ImageSharp.Color.LimeGreen;
        private const float MaskAxisLineWidth = 1.5f;
        private const float MaskAxisPadRatio = 0.25f;
        private const float MaskAxisMinPadPixels = 6f;
        private static readonly SixLabors.ImageSharp.Color DirectionArrowColor = SixLabors.ImageSharp.Color.Orange;
        private const float DirectionArrowLineWidth = 2f;
        private const float DirectionArrowLengthPx = 56f;
        private const float ArrowHeadLengthPx = 14f;
        // 2026-09-09: 边界过滤"禁入线"辅助线（黄色）——按 Settings.Algorithm.EdgeMargin* 在原图上画出
        // 检测框不允许越过的四条参考线，供现场观察"半个产品/贴边产品"被拒的原因。
        // 仅可视化，不参与过滤（实际过滤见 DetectionRecordService.BuildAndSaveAsync 越界判定）。
        private static readonly SixLabors.ImageSharp.Color EdgeMarginLineColor = SixLabors.ImageSharp.Color.Yellow;
        private const float EdgeMarginLineWidth = 2f;

        // 2026-09-15: 抓取点标记颜色（洋红）。与已有标记区分：
        // 红=特征质心、蓝=产品质心、绿=头尾分割线、橙=发送方向箭头、黄=边界禁入线。
        private static readonly SixLabors.ImageSharp.Color GrabPointMarkColor = SixLabors.ImageSharp.Color.Magenta;
        private const float GrabPointMarkLineWidth = 2f;

        // 2026-09-15: 抓取点偏移因"头尾朝向不可信"被退化为中心时的标记颜色（灰）。
        // 此时生产实际发送的就是矩形中心 —— 用灰色与正常洋红区分，现场一眼能看出"配了偏移但没生效"。
        private static readonly SixLabors.ImageSharp.Color GrabPointSuppressedColor = SixLabors.ImageSharp.Color.Gray;
        /// <summary>
        /// 条码标签绘制字体。2026-09-09: 由 32 缩小为 18，避免大字号遮挡画面
        /// （当前全文件仅条码 DrawText 使用本字体）。
        /// </summary>
        private readonly Font _font = CreateDefaultFont();

        private static Font CreateDefaultFont()
        {
            try { return SixLabors.Fonts.SystemFonts.CreateFont("Arial", 18); }
            catch (Exception ex)
            {
                // M285a: 记录失败日志，fallback 前检查字体列表非空
                LogService.Instance.Warning($"创建默认字体失败: {ex}");
                if (!SixLabors.Fonts.SystemFonts.Families.Any())
                {
                    throw new InvalidOperationException("系统字体列表为空，无法创建默认字体", ex);
                }
                return SixLabors.Fonts.SystemFonts.CreateFont(SixLabors.Fonts.SystemFonts.Families.First().Name, 18);
            }
        }
        #region DrawImage
        /// <summary>
        /// 在给定图像上绘制检测框与条码文本，并根据设置保存绘制结果与原图。
        /// </summary>
        /// <example>
        /// DrawImage 会在 image 上按类别名分色绘制矩形（参考 InferenceColorPalette），并用红色绘制条码末 6 位作为标签。
        /// </example>
        private async Task DrawImage(Image<Rgb24> drawImg, FrameResult scanerResult, YoloResult<Segmentation> edgeResults,
            IReadOnlyList<AngleDrawInfo?>? angleDrawInfos, IReadOnlyList<bool?>? headFlips,
            IReadOnlyList<bool>? headTrusted,
            IReadOnlyList<DbModel?>? indexedRecords,
            string? folder, TimingLogger timings, YoloTool edge)
        {
            drawImg.Mutate(x =>
            {
                // 2026-09-09: 边界过滤"禁入线"辅助线——先于检测框绘制（目标框叠在线之上更清晰）。
                // EdgeMargin* 语义为原图像素（DetectionRecordService 中与还原后的 Bounds 比较），
                // drawImg 为推理图分辨率（IsResize 时原图已缩放），故按缩放比例折算到绘制坐标，
                // 与下方条码文本/角度绘制的缩放语义一致（IsResize=false 时坐标即原图，无需折算）。
                var marginAlg = MainAPP.Models.Settings.Instance.Algorithm;
                float mL = edge.IsResize ? (float)(marginAlg.EdgeMarginLeftPixels / edge.ResizeScale) : (float)marginAlg.EdgeMarginLeftPixels;
                float mT = edge.IsResize ? (float)(marginAlg.EdgeMarginTopPixels / edge.ResizeScaleY) : (float)marginAlg.EdgeMarginTopPixels;
                float mR = edge.IsResize ? (float)(marginAlg.EdgeMarginRightPixels / edge.ResizeScale) : (float)marginAlg.EdgeMarginRightPixels;
                float mB = edge.IsResize ? (float)(marginAlg.EdgeMarginBottomPixels / edge.ResizeScaleY) : (float)marginAlg.EdgeMarginBottomPixels;
                float drawW = drawImg.Width;
                float drawH = drawImg.Height;
                // 四条禁入线端点取内缩后的矩形四边（线与检测框同口径：框越线即被边界过滤拒收）
                x.DrawLine(EdgeMarginLineColor, EdgeMarginLineWidth,
                    new PointF(mL, mT), new PointF(drawW - mR, mT));
                x.DrawLine(EdgeMarginLineColor, EdgeMarginLineWidth,
                    new PointF(mL, drawH - mB), new PointF(drawW - mR, drawH - mB));
                x.DrawLine(EdgeMarginLineColor, EdgeMarginLineWidth,
                    new PointF(mL, mT), new PointF(mL, drawH - mB));
                x.DrawLine(EdgeMarginLineColor, EdgeMarginLineWidth,
                    new PointF(drawW - mR, mT), new PointF(drawW - mR, drawH - mB));

                int edgeIdx = 0;
                foreach (var edgeResult in edgeResults)
                {
                    // 按类别名分配稳定颜色，保证 DataGrid 中显示的颜色与图像上一致
                    var label = edgeResult.Name?.Name ?? string.Empty;
                    var colorHex = InferenceColorPalette.GetColorForLabel(label);
                    var drawColor = InferenceColorPalette.ToImageSharpColor(colorHex);
                    // 掩码最小外接旋转矩形：与 drawImg 同坐标系（drawImg 已按 IsResize 缩放为推理图，
                    // Segmentation 的 Bounds/掩码坐标与之天然一致，无需再缩放）。
                    // 提取一次供轮廓多边形与下方"掩码主轴参考线"复用，避免对掩码重复全扫描。
                    var maskRect = edgeResult.GetMaskMinAreaRect();
                    x.DrawPolygon(drawColor, DrawPolygonLineWidth, maskRect.Points);

                    // 目标是否有效（通过边界/面积过滤 → 入 UI 列表/落库/发送）：
                    // 越界/被过滤目标不画抓取点标记，避免"画了灰标却从未发出"的误导
                    bool isRecorded = indexedRecords is null
                        || (edgeIdx < indexedRecords.Count && indexedRecords[edgeIdx] is not null);

                    // ── 抓取点标记（2026-09-15）──
                    // 抓取点即实际发送给机器人的 X/Y。画出来现场才能核对与标定偏移量 ——
                    // 没有画面反馈时只能靠"发过去抓一下看结果"试错，效率极低。
                    // 几何与生产同源：统一走 GrabPointCalculator.ResolveOriginalImageOffset，
                    // 并同样按"朝向是否可信"决定是否施加偏移 —— 保证画面上的点就是实际发送的点。
                    var grabRecipe = MainAPP.Services.RecipesManage.Instance.CurrentRecipe;
                    var grabLongMm = grabRecipe?.GrabOffsetLongMm ?? 0f;
                    var grabShortMm = grabRecipe?.GrabOffsetShortMm ?? 0f;
                    if (isRecorded && (grabLongMm != 0 || grabShortMm != 0) && maskRect.MaskArea > 0)
                    {
                        bool? flipForGrab = headFlips is not null && edgeIdx < headFlips.Count
                            ? headFlips[edgeIdx]
                            : null;
                        // 朝向不可信 ⇒ 生产会把两个偏移一起退化为中心；画面必须同步，否则会画出"没真正发出的点"。
                        bool trustedForGrab = headTrusted is not null && edgeIdx < headTrusted.Count
                            && headTrusted[edgeIdx];
                        bool grabSuppressed = !trustedForGrab;
                        var effLong = grabSuppressed ? 0f : grabLongMm;
                        var effShort = grabSuppressed ? 0f : grabShortMm;

                        // 标定换算针对原图坐标系，而绘制发生在推理图坐标系：
                        // 统一入口返回原图系的偏移向量，再按缩放比折回绘制坐标系。
                        var cOrigX = edge.IsResize ? maskRect.Center.X * edge.ResizeScale : maskRect.Center.X;
                        var cOrigY = edge.IsResize ? maskRect.Center.Y * edge.ResizeScaleY : maskRect.Center.Y;
                        var (offOrigX, offOrigY) = GrabPointCalculator.ResolveOriginalImageOffset(
                            _transformer, cOrigX, cOrigY, maskRect.Angle,
                            edge.IsResize, edge.ResizeScale, edge.ResizeScaleY,
                            flipForGrab == true, effLong, effShort);
                        var offDrawX = edge.IsResize ? offOrigX / edge.ResizeScale : offOrigX;
                        var offDrawY = edge.IsResize ? offOrigY / edge.ResizeScaleY : offOrigY;

                        var grabPt = new PointF(
                            (float)(maskRect.Center.X + offDrawX),
                            (float)(maskRect.Center.Y + offDrawY));

                        // 十字 + 方框（沿用 DrawPolygon 方块，兼容 ImageSharp 3.x 无 DrawCircle）。
                        // 朝向不可信导致偏移被退化时改用灰色实心小方框：与"正常着色的抓取点"区分，
                        // 现场一眼能看出"配了偏移但没生效"，不必去翻日志。
                        const float GrabMarkHalf = 7f;
                        var markColor = grabSuppressed ? GrabPointSuppressedColor : GrabPointMarkColor;
                        x.DrawLine(markColor, GrabPointMarkLineWidth,
                            new PointF(grabPt.X - GrabMarkHalf, grabPt.Y), new PointF(grabPt.X + GrabMarkHalf, grabPt.Y));
                        x.DrawLine(markColor, GrabPointMarkLineWidth,
                            new PointF(grabPt.X, grabPt.Y - GrabMarkHalf), new PointF(grabPt.X, grabPt.Y + GrabMarkHalf));
                        x.DrawPolygon(markColor, GrabPointMarkLineWidth, new[]
                        {
                            new PointF(grabPt.X - 11f, grabPt.Y - 11f),
                            new PointF(grabPt.X + 11f, grabPt.Y - 11f),
                            new PointF(grabPt.X + 11f, grabPt.Y + 11f),
                            new PointF(grabPt.X - 11f, grabPt.Y + 11f),
                        });

                        // 偏移生效时用细线连回矩形中心，直观显示偏移方向与量级。
                        if (Math.Abs(offDrawX) > 0.5 || Math.Abs(offDrawY) > 0.5)
                        {
                            x.DrawLine(markColor, 1f,
                                new PointF(maskRect.Center.X, maskRect.Center.Y), grabPt);
                        }
                    }

                    // 仅有效目标画方向参考线，被过滤目标（半个产品/面积异常误检）不强调方向，避免误导现场人员。

                    // 角度模型结果绘制：产品质心 → 特征质心连线 + 两点 + 角度文本（与 edgeResults 同序）
                    AngleDrawInfo? angleInfo = angleDrawInfos is not null && edgeIdx < angleDrawInfos.Count
                        ? angleDrawInfos[edgeIdx]
                        : null;
                    // 2026-09-11: 服务端下发的"头尾是否翻转 180°"。2026-09-13 起全路径有值
                    // （含模型翻转——模型只产翻转信号、不再直接采信其角度），画面方向箭头统一据此复现实发朝向，
                    // 不再自行按灰度统计判定（见下方箭头段注释）。
                    bool? headFlip = headFlips is not null && edgeIdx < headFlips.Count
                        ? headFlips[edgeIdx]
                        : null;
                    if (angleInfo is not null)
                    {
                        // 原图像素 → 推理图坐标（与条码文本绘制逻辑一致）
                        PointF ScaleFromOriginal(PointF p) => edge.IsResize
                            ? new PointF(p.X / edge.ResizeScale, p.Y / edge.ResizeScaleY)
                            : p;
                        var featureCentroid = ScaleFromOriginal(new PointF(angleInfo.FeatureCentroidX, angleInfo.FeatureCentroidY));
                        var productCentroid = ScaleFromOriginal(new PointF(angleInfo.ProductCentroidX, angleInfo.ProductCentroidY));
                        x.DrawLine(SixLabors.ImageSharp.Color.Red, 1f, productCentroid, featureCentroid);
                        // 质心点：用 2x2 小方块（DrawPolygon）代替圆，兼容 ImageSharp 3.x API
                        PointF[] Square(PointF c) => new[]
                        {
                            new PointF(c.X - 3, c.Y - 3), new PointF(c.X + 3, c.Y - 3),
                            new PointF(c.X + 3, c.Y + 3), new PointF(c.X - 3, c.Y + 3),
                        };
                        x.DrawPolygon(SixLabors.ImageSharp.Color.Blue, 2f, Square(productCentroid));
                        x.DrawPolygon(SixLabors.ImageSharp.Color.Red, 2f, Square(featureCentroid));
                        // 2026-09-09: 不绘制角度数值文本（现场无需画面核对角度值，仅保留方向连线/质心标记）
                    }
                    // 2026-09-13: 方向箭头与上面的"质心→特征"连线并存（不再互斥）——
                    // 箭头指示实发朝向（=掩码主轴角 + 服务端翻转），连线示模型消歧信号来源，可对照核对。
                    if (isRecorded && maskRect.MaskArea > 0 && headFlip is not null)
                    {
                        // 2026-09-09: "发送位置 + 发送角度"向量箭头（掩码角度路径：未启用角度模型，或其方向退化回落）。
                        // 起点 = 掩码最小外接旋转矩形中心（正是落库/发送 X/Y 的图像对应点），
                        // 方向 = 矩形宽度轴（长轴，GetMaskMinAreaRect 已宽≥高归一化，与掩码回退发送角同源），
                        // 直接使用矩形自身中心/角度，无需世界坐标逆变换；带箭头头表达唯一朝向。
                        // 仅在有真实掩码像素（MaskArea>0）时绘制——掩码退化回退到外接框时方向无意义。
                        // 2026-09-11: 判据由 headFlip 是否为空决定（= 服务端确实走了掩码角度路径），
                        // 因此"角度模型启用但方向退化回落"的情形也会绘制箭头（此前恒不绘制，画面无任何朝向提示）。
                        // 2026-09-11: 头尾朝向**一律以服务端下发的 headFlip 为准**——见下方 arrowAngleDeg。
                        // 2026-09-09: 与箭头并存绘制"头尾分割线"（短轴方向、LimeGreen），二者垂直交于矩形中心：
                        // 分割线把产品划成头/尾两半（配合灰度判向核对头尾假设），箭头指示发送朝向。
                        var rectCenter = new PointF(maskRect.Center.X, maskRect.Center.Y);
                        // —— 头尾分割线（短轴）——
                        var (axisP1, axisP2) = ComputeMaskShortAxisSegment(rectCenter, maskRect.Width, maskRect.Height, maskRect.Angle);
                        x.DrawLine(MaskAxisLineColor, MaskAxisLineWidth, axisP1, axisP2);
                        // —— 发送角度向量箭头（长轴/主轴，橙色，区别于绿色分割线）——
                        // 长轴是无向轴，箭头方向不能只看几何角，否则指向会在 ±180° 间飘移；必须补上"头尾"。
                        // 2026-09-11 重构：头尾由**服务端下发的 headFlip** 决定，不再在 UI 侧二次判定。
                        //   背景：P5 起头尾判定优先级为"二维码位置 → 灰度统计"，而 UI 原先只看灰度统计
                        //   （且死区内不翻转），于是二维码把实发角翻 180° 时箭头仍指原方向 → 画面与实发角
                        //   相差 180°（所见非所发）。现直接复现服务端已经施加的那一次翻转，二者恒同源。
                        //   注：maskRect.Angle 与 fallbackAngle 仅差常量偏移（offsetAngle + 标定旋转），
                        //   两者同轴，"是否翻转 180°"这一关系在图像系与关系世界系中一致，故 bool 足够。
                        double arrowAngleDeg = maskRect.Angle + (headFlip == true ? 180.0 : 0.0);
                        float arrowRad = (float)(arrowAngleDeg * Math.PI / 180.0);
                        float arrowUx = (float)Math.Cos(arrowRad);
                        float arrowUy = (float)Math.Sin(arrowRad);
                        float arrowEndX = rectCenter.X + arrowUx * DirectionArrowLengthPx;
                        float arrowEndY = rectCenter.Y + arrowUy * DirectionArrowLengthPx;
                        // 主线段
                        x.DrawLine(DirectionArrowColor, DirectionArrowLineWidth, rectCenter, new PointF(arrowEndX, arrowEndY));
                        // 箭头头部：以端点为顶点、沿反向回折 ±24° 的两条短折线
                        float hx = -arrowUx * ArrowHeadLengthPx;
                        float hy = -arrowUy * ArrowHeadLengthPx;
                        float headCos = 0.9135f; // cos(24°)
                        float headSin = 0.4067f; // sin(24°)
                        var head1 = new PointF(
                            arrowEndX + hx * headCos - hy * headSin,
                            arrowEndY + hx * headSin + hy * headCos);
                        var head2 = new PointF(
                            arrowEndX + hx * headCos + hy * headSin,
                            arrowEndY - hx * headSin + hy * headCos);
                        x.DrawLine(DirectionArrowColor, DirectionArrowLineWidth, new PointF(arrowEndX, arrowEndY), head1);
                        x.DrawLine(DirectionArrowColor, DirectionArrowLineWidth, new PointF(arrowEndX, arrowEndY), head2);
                    }
                    edgeIdx++;
                }
                foreach (var barcode in scanerResult.BarcodeResults)
                {
                    var codeStr = barcode.CodeString();
                    if (codeStr.Contains("http", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    // H10: IsResize=false 时不做缩放转换，避免文本位置偏移到左上角
                    var center = barcode.Center();
                    var textPos = edge.IsResize
                        ? new PointF(center.X / edge.ResizeScale, center.Y / edge.ResizeScaleY)
                        : new PointF(center.X, center.Y);
                    // 2026-09-09: 画面二维码标签 = 条码末尾 2 个字符（如 0123456789 → 89），
                    // 缩短画面文字避免遮挡；条码不足 2 位时显示完整码（避免 Range 异常）。
                    var label = codeStr.Length >= 2 ? codeStr[^2..] : codeStr;
                    x.DrawText(label, _font, SixLabors.ImageSharp.Color.Red, textPos);
                }

            });
            timings.Split("MutateDraw");
            // 双缓冲交替赋值，确保 ImageViewer 的 DP 回调每帧触发（同一 WriteableBitmap 引用会短路，画面停帧）
            _showBitmapIndex ^= 1;
            _reusableShowBitmaps[_showBitmapIndex] = Tools.UpdateShow(drawImg, _reusableShowBitmaps[_showBitmapIndex]);
            ImageForShow = _reusableShowBitmaps[_showBitmapIndex];
            timings.Split("UpdateShow");
            if (Settings.Instance.IsSaveDraw && (scanerResult.HasBarcodeResults || edgeResults?.Count > 0))
            {
                // P0-FIX: 保存图片/日志失败不应中断推理主流程（如可移动磁盘未就绪、网络驱动器掉线）
                // 失败时降级为 Warning，避免每帧刷屏 ERROR；主流程（绘制+UI 显示）已完成，仅落盘失败
                // L501b: folder 为 null 表示目录创建失败（如 E 盘未就绪），跳过所有保存
                if (string.IsNullOrEmpty(folder))
                {
                    // 目录不可用，跳过保存（GetOrCreateTodayFolder 已记录 Warning）
                    timings.Split("SaveDraw");
                    return;
                }
                var saveFolder = folder!;
                try
                {
                    // M42: 传递 _cts.Token 以便应用退出时取消 I/O
                    await drawImg.SaveAsJpegAsync(Path.Combine(saveFolder, $"draw_{scanerResult.FrameNumber}.jpg"), _cts.Token).ConfigureAwait(false);
                    timings.Split("SaveDraw");
                    // 原图保存已移至 DrawImage 之前的 ProcessImageAsync 中执行，避免保存到已绘制的图像
                    await File.WriteAllTextAsync(Path.Combine(saveFolder, $"log_{scanerResult.FrameNumber}.txt"), timings.GetLog(), _cts.Token).ConfigureAwait(false);
                    timings.Split("SaveLog");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception saveEx) when (saveEx is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
                {
                    // 设备未就绪/路径不可访问：降级为 Warning，避免 ERROR 刷屏
                    LogService.Instance.Warning($"保存检测结果图片失败（路径: {folder}），已跳过本帧保存: {saveEx.Message}");
                }
            }
        }

        /// <summary>
        /// 计算掩码最小外接旋转矩形"短边方向一分为二"头尾分割线的两个端点。
        /// 端点 = 矩形中心 ± 短轴单位向量 × (半短边 + 外扩)，线段与矩形短边平行、穿过中心、两端略微穿出分割区域，
        /// 直观把产品划为头/尾两半（配合灰度判向的"头端明暗"假设核对）。
        /// <para>坐标系：与传入的 <paramref name="center"/> 一致（推理图坐标，DrawImage 的画布同坐标系）。</para>
        /// <para>方向说明：angle 描述掩码矩形宽度轴方向（度，逆时针、相对 X 轴，即 GetMaskMinAreaRect().Angle）；
        /// 短轴与长轴正交：宽度为长边时短轴 = 高度轴（angle + 90°），高度为长边时短轴 = 宽度轴（angle）。</para>
        /// </summary>
        /// <param name="center">掩码矩形中心（推理图坐标）。</param>
        /// <param name="width">掩码矩形宽度（宽度轴跨度）。</param>
        /// <param name="height">掩码矩形高度（高度轴跨度）。</param>
        /// <param name="angle">掩码矩形宽度轴角度（度）。</param>
        private static (PointF P1, PointF P2) ComputeMaskShortAxisSegment(PointF center, float width, float height, float angle)
        {
            // 短轴方向：宽度为长边时短轴 = 高度轴（angle+90°），否则短轴 = 宽度轴（angle）
            bool longIsWidth = width >= height;
            var shortSide = longIsWidth ? height : width;
            var angleDeg = longIsWidth ? angle + 90f : angle;

            // 外扩：max(最小像素, 短边 × 比例)。短边很小时线本身短，需保证外扩可见；外扩过大则失去"贯穿"感
            var pad = MathF.Max(MaskAxisMinPadPixels, shortSide * MaskAxisPadRatio);
            var halfLen = shortSide / 2f + pad;
            var rad = angleDeg * MathF.PI / 180f;
            var dx = MathF.Cos(rad);
            var dy = MathF.Sin(rad);
            return (new PointF(center.X - dx * halfLen, center.Y - dy * halfLen),
                    new PointF(center.X + dx * halfLen, center.Y + dy * halfLen));
        }

        /// <summary>
        /// 将推理结果转换为 UI 友好的列表项并更新 <see cref="InferenceResults"/>。
        /// 在后台线程完成数据拷贝，仅集合更新切换到 UI 线程。
        /// 必须在 edgeResults.Dispose() 之前调用。
        /// </summary>
        /// <param name="result">YOLO 推理结果（用于读取 Bounds 与 Confidence）</param>
        /// <param name="indexedRecords">
        /// 与 <paramref name="result"/> 严格同序的 DbModel 列表，由 <see cref="DetectionRecordService.BuildAndSaveAsync"/> 返回。
        /// 被边界过滤跳过的位置为 null，UI 也会跳过对应检测框显示。
        /// </param>
        private void UpdateInferenceResults(YoloResult<Segmentation>? result, IReadOnlyList<DbModel?> indexedRecords,
            IReadOnlyDictionary<int, double>? maskAreaByEdgeIndex = null)
        {
            // 在后台线程构建结果列表，避免在 UI 线程执行 CPU 密集计算
            var items = new List<InferenceResultItem>(result?.Count ?? 0);
            if (result is not null)
            {
                int idx = 0;
                foreach (var pred in result)
                {
                    // 按索引从 indexedRecords 取对应 DbModel，避免 Width×Height 匹配在多目标同尺寸时错配
                    var dbm = (idx < indexedRecords.Count) ? indexedRecords[idx] : null;
                    idx++;

                    // 被边界过滤跳过的检测框：UI 也不显示
                    if (dbm is null)
                    {
                        continue;
                    }

                    // Angle/Barcode 全部来自 DbModel，与数据库记录/发送机器人值完全一致：
                    // - Barcode=扫码枪位置匹配的真实字符串（无匹配时 "noread"）
                    // - Angle=DetectionRecordService 最终角度（角度模型或掩码回退，经跨帧锁定后由
                    //   AngleTracker 归一化到 (-180,180] 落库），与配方页/ToVGT 出口同域，直接显示即可；
                    //   未知哨兵(-9999)原样保留，AngleDisplay 据此显示"未知"
                    // Bounds 与 Confidence 取自 pred（原始推理数据），用于显示中心点与置信度
                    double maskArea = 0;
                    if (maskAreaByEdgeIndex is not null && maskAreaByEdgeIndex.TryGetValue(idx - 1, out var area))
                    {
                        maskArea = area;
                    }
                    items.Add(new InferenceResultItem
                    {
                        Confidence = pred.Confidence,
                        X = pred.Bounds.X,
                        Y = pred.Bounds.Y,
                        Width = pred.Bounds.Width,
                        Height = pred.Bounds.Height,
                        Angle = dbm.Angle,
                        Barcode = dbm.Barcode,
                        MaskAreaOriginalPixels = maskArea,
                        // 2026-09-08: 亮度判向统计随主页推理结果展示（null=未判向，与落库值同源，
                        // 2026-09-13 起由特征池产出；方便现场核对头端明暗/死区，无需切到数据库页）
                        BrightMean = dbm.BrightMean,
                        DarkMean = dbm.DarkMean,
                        BrightnessDiff = dbm.BrightnessDiff,
                    });
                }
            }

            // 2026-09-07: 本帧无有效目标（edgeResults 为空，或检测框全部被边界/面积过滤 → items 为空）时
            // 保留上一帧的产品信息，不做清空刷新，避免产线无产品经过时主页结果列表闪空。
            if (items.Count == 0)
            {
                return;
            }

            // 在 UI 线程更新 ObservableCollection（推理在后台线程，集合修改必须在 UI 线程）
            // 使用全限定名避免与 MainAPP.Application 命名空间冲突
            _ = System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                InferenceResults.Clear();
                foreach (var item in items)
                {
                    InferenceResults.Add(item);
                }
                OnPropertyChanged(nameof(ResultCount));
            }));
        }

        // M21: 接受 CancellationToken，在应用退出时取消未完成的保存任务
        private static void QueueSaveSourceImage(Image<Rgb24> sourceImg, string? folder, uint frameNumber, CancellationToken cancellationToken)
        {
            // L501b: folder 为 null 表示目录创建失败（如 E 盘未就绪），跳过保存
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }
            var saveFolder = folder!;
            var sourceCopy = sourceImg.CloneAs<Rgb24>();
            // H69: Task.Run 前检查取消，若已取消则立即释放克隆图像，避免 sourceCopy 泄漏
            if (cancellationToken.IsCancellationRequested)
            {
                sourceCopy.Dispose();
                return;
            }
            // L370a: 不向 Task.Run 传 token（让 lambda 内部的 SaveAsPngAsync 处理取消），确保 finally 总能执行
            _ = Task.Run(async () =>
            {
                try
                {
                    await sourceCopy.SaveAsPngAsync(Path.Combine(saveFolder, $"source_{frameNumber}.png"), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 应用关闭时取消，无需记录错误
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"保存原图失败(Frame={frameNumber}): {ex}");
                }
                finally
                {
                    sourceCopy.Dispose();
                }
            });
        }
        #endregion
    }
}