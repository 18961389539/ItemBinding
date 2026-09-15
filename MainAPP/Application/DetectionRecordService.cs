using CoordinateSystemMapping;
using Extensions;
using HikScanner;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Extensions;
using MainAPP.Models;
using MainAPP.Services;
using OpenCvSharp;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Application;

/// <summary>
/// 推理结果记录的组装与落库服务（单例）。
/// 负责将 YOLO 分割检测结果与条码识别结果进行匹配绑定，
/// 计算世界坐标，生成 DbModel 记录并批量写入数据库。
///
/// 匹配逻辑：
/// 1. 遍历每个 YOLO 检测框（边缘分割结果）
/// 2. 过滤检测框距图像边缘不足对应边最小间距的产品（四边独立 EdgeMargin{Left,Top,Right,Bottom}Pixels，
///    设置页可配置，默认各 10px，当前见 Models.Settings）——过滤后不落库/不去重/不发 VGT（不给机器人发消息）
/// 3. 对每个检测框，查找其内部包含的条码中心点
/// 4. 将条码与检测框绑定，计算世界坐标和角度
/// 5. 批量保存到数据库
/// </summary>
public sealed class DetectionRecordService
{
    private readonly IBarcodeDataService _barcodeData;
    private readonly AngleTracker _angleTracker;

    // 2026-09-13: 标定构图（镜像与否）仅首次记录日志，让"镜像假设"可见而非改了才知道
    private static bool s_mirrorStateLogged;

    // 2026-09-07: 角度质量门拒绝诊断日志的节流状态。主流程不传 onRejected 时拒绝原因被静默丢弃，
    // 大量 -9999 无从定位。这里统一记录并限频（30s 一条聚合 Warning），避免高拒绝率刷爆日志。
    private static readonly object AngleRejectLogLock = new();
    private static DateTime _lastAngleRejectLogTime = DateTime.MinValue;
    private static int _angleRejectCountSinceLog;

    /// <summary>角度质量门拒绝聚合日志的限频窗口（秒）。</summary>
    private static readonly TimeSpan AngleRejectLogInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 配方未配置角度检测工具（<c>YoloTool.AngleDetection</c> 为 null）时使用的默认参数实例。
    /// 仅用于读取质量门阈值（只读用途），避免每帧 new 一个 YoloTool 造成 Gen0 垃圾。
    /// </summary>
    private static readonly YoloTool DefaultAngleTool = new();

    /// <summary>
    /// 工位标识（2026-09-12 新增，落库 <c>DbModel.Station</c>）＝ 本机机器码。
    ///
    /// <para>取值复用项目既有的机器身份：<see cref="LicenseService.MachineCodeText"/> —— 激活窗口里
    /// 展示/可复制的那个「本机机器码」，由 CPU 标识 + 系统盘卷序列号 + 首个物理网卡 MAC + 机器名 + 用户名
    /// 哈希而成（见 <c>LicenseCore.HardwareFingerprint</c>，零 WMI：注册表 + P/Invoke）。</para>
    ///
    /// <para>★ 必须缓存：采集含注册表读取、<c>GetVolumeInformation</c> P/Invoke 与网卡枚举，
    /// 若写在每条检测记录的热路径上（每秒数十条）会拖慢产线。项目内既有先例——
    /// <c>LicenseService.IsActivated</c> 的注释即为「缓存启动时结果，避免每帧采集硬件指纹」。</para>
    ///
    /// <para>⚠️ 注意：机器码参与了 <c>Environment.UserName</c>，因此<b>换 Windows 账户运行会使本值改变</b>，
    /// 同一台机器在不同账户下会被记成不同工位。若需与账户无关的工位标识，应改用机器名等，
    /// 但那会与授权指纹脱钩，需另行决策。</para>
    /// </summary>
    /// <summary>当前工位标识（本机机器码），供 AI 查询等跨类场景复用（采集逻辑见 StationCode）。</summary>
    public static string? CurrentStation => StationCode.Value;

    private static readonly Lazy<string?> StationCode = new(
        static () =>
        {
            try
            {
                var code = LicenseService.MachineCodeText;
                // 空串归一为 null：列可空，NULL 表示"未记录"，比空串更利于 SQL 过滤与导出可读
                return string.IsNullOrWhiteSpace(code) ? null : code;
            }
            catch (Exception ex)
            {
                // 采集失败不应阻断检测与落库：Station 退化为 NULL（语义同"未记录"）
                LogService.Instance.Warning($"采集本机机器码失败，Station 将写空: {ex.Message}");
                return null;
            }
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    // 2026-09-05: 边缘最小间距已拆为四边独立（Settings.Algorithm.EdgeMargin{Left,Top,Right,Bottom}Pixels，设置页可编辑，默认各 10px）

    // REVIEW-FIX: long 乘积钳制到 int 范围，供 isResize 缩放后的 Bounds 使用，
    // 避免 int×int 溢出产生负/错误坐标（调用方 DetectionRecordService 内部使用）。
    private static int ClampToInt(long value) => value switch
    {
        > int.MaxValue => int.MaxValue,
        < int.MinValue => int.MinValue,
        _ => (int)value,
    };

    /// <summary>
    /// 未启用角度检测时，将分割掩码最小外接旋转矩形的主轴方向换算为"世界坐标系角度"后兜底发机器人。
    /// <para>2026-09-08 起与角度模型路径同口径：角度模型对"产品质心→特征质心"两个世界点差取 atan2
    /// （AngleDetectionProcessor.ComputeAngleAsync），此处对"主轴两端点"两个世界点差取 atan2——
    /// 主轴端点在推理图坐标系沿矩形宽度轴（长轴）取半宽长度，先按缩放还原到原图像素，再分别
    /// <see cref="CoordinateTransformer.ImageToPhysical"/>。这样掩码回退角度天然吸收标定的旋转/镜像/
    /// 各向异性缩放，不再把"图像坐标系角度"直接当"机器人坐标系角度"用（图像 Y 与机器人 Y 反向时
    /// 会产生整体负号偏差，旧实现仅靠 OffsetAngle 加法无法修正）。</para>
    /// <para>兼容行为：掩码退化（<paramref name="maskArea"/>≤0，回退 Bounds 无旋转信息）返回 0；
    /// 未标定（<see cref="CoordinateTransformer.IsInitialized"/>=false）返回原始图像主轴角
    /// <paramref name="rectAngleDeg"/>（同旧实现，图像角仅供联调，非真实世界值）。</para>
    /// <remarks>返回值域未归一化（atan2 结果 [-180,180) 量级），OffsetAngle 叠加与 AngleTracker
    /// 归一化由调用方完成——与角度模型路径"atan2+Offset→归一化"顺序完全一致。</remarks>
    /// </summary>
    /// <param name="rectCenterX">掩码矩形中心 X（推理图坐标系）。</param>
    /// <param name="rectCenterY">掩码矩形中心 Y（推理图坐标系）。</param>
    /// <param name="rectAngleDeg">掩码矩形主轴角（度，图像/推理图坐标系，BuildMinAreaRect 宽≥高归一化后的长轴方向）。</param>
    /// <param name="rectWidth">掩码矩形宽度（推理图像素，= 长轴长度，用于取主轴端点）。</param>
    /// <param name="maskArea">掩码面积（>0 表示有有效掩码像素，主轴角可信）。</param>
    /// <param name="transformer">三点标定变换器（图像像素 → 世界 mm）。</param>
    /// <param name="isResize">是否缩放推理（坐标还原系数）。</param>
    /// <param name="resizeWidth">X 方向还原系数（推理图 → 原图）。</param>
    /// <param name="resizeHeight">Y 方向还原系数（推理图 → 原图）。</param>
    /// <returns>世界坐标系主轴角度（度）；退化/未标定回退见说明。</returns>
    internal static double ComputeMaskAngleCalibrated(
        float rectCenterX, float rectCenterY, float rectAngleDeg, float rectWidth, float maskArea,
        CoordinateTransformer transformer, bool isResize, int resizeWidth, int resizeHeight)
    {
        // 回退矩形（无有效掩码像素）角度无意义，返回 0（与旧 ComputeMaskAngle 语义一致）
        if (maskArea <= 0)
        {
            return 0;
        }

        // 矩形极窄（<2px，理论仅在退化掩码出现）：主轴端点过近，ImageToPhysical 差分 atan2 数值不稳，
        // 退回图像角（同旧实现）。真实产品经面积过滤不可能落入此分支。
        if (rectWidth <= 1)
        {
            return rectAngleDeg;
        }

        // 未标定：ImageToPhysical 无有效映射，退回旧实现的图像主轴角（非真实世界值，仅供联调）
        if (!transformer.IsInitialized)
        {
            return rectAngleDeg;
        }

        // 主轴方向单位向量（图像/推理图坐标系，角度语义与 rect.Angle 一致：宽度轴=长轴方向）
        double rad = rectAngleDeg * Math.PI / 180.0;
        double ux = Math.Cos(rad);
        double uy = Math.Sin(rad);
        // 主轴两端点（推理图坐标系，半宽长）
        float halfW = rectWidth / 2f;
        double p1xInf = rectCenterX + ux * halfW;
        double p1yInf = rectCenterY + uy * halfW;
        double p2xInf = rectCenterX - ux * halfW;
        double p2yInf = rectCenterY - uy * halfW;
        // 推理图坐标 → 原图像素（两轴独立还原；必须对端点分别还原再变换，而非先转角度——
        // X/Y 缩放不等比时角度本身会改变，直接缩放角度会引入误差）
        double p1x = isResize ? p1xInf * resizeWidth : p1xInf;
        double p1y = isResize ? p1yInf * resizeHeight : p1yInf;
        double p2x = isResize ? p2xInf * resizeWidth : p2xInf;
        double p2y = isResize ? p2yInf * resizeHeight : p2yInf;
        // 两端点转世界坐标后取 atan2——与角度模型路径（质心两点世界差 atan2）同口径
        // 注：ImageToPhysical(double,double) 返回命名元组 (WorldX, WorldY)，取成员用 WorldX/WorldY
        var (w1x, w1y) = transformer.ImageToPhysical(p1x, p1y);
        var (w2x, w2y) = transformer.ImageToPhysical(p2x, p2y);
        return Math.Atan2(w1y - w2y, w1x - w2x) * 180.0 / Math.PI;
    }

    public DetectionRecordService(IBarcodeDataService barcodeData, AngleTracker angleTracker)
    {
        _barcodeData = barcodeData;
        _angleTracker = angleTracker;
    }

    /// <summary>
    /// 生成检测记录并写入数据库。
    /// </summary>
    /// <returns>
    /// 元组返回两个视图：
    /// <list type="bullet">
    /// <item><see cref="DetectionBuildResult.ValidRecords"/>：仅包含通过边界过滤的有效记录，用于落库/去重/发送 VGT。</item>
    /// <item><see cref="DetectionBuildResult.IndexedRecords"/>：与 <paramref name="edgeResults"/> 严格同序，被跳过的位置为 null，用于 UI 按索引取值避免错配。</item>
    /// </list>
    /// </returns>
    public async Task<DetectionBuildResult> BuildAndSaveAsync(FrameResult scanerResult,
                                                                YoloResult<Segmentation> edgeResults,
                                                                CoordinateTransformer transformer,
                                                                bool isResize,
                                                                int resizeWidth,
                                                                int resizeHeight,
                                                                bool saveDraw,
                                                                string? folder,
                                                                long speed,
                                                                int costTime,
                                                                Mat? angleSourceImage,
                                                                YoloPredictorPool? anglePredictorPool,
                                                                YoloTool? angleTool,
                                                                float offsetAngle,
                                                                bool angleDetectionEnabled,
                                                                CancellationToken cancellationToken = default,
                                                                // 2026-09-15: 原先的 offsetX/offsetY（世界坐标系常量平移补偿）已移除 ——
                                                                // 世界系常量方向固定，只在单一产品角度下成立，现改由产品局部坐标系的抓取点偏移承担。
                                                                // 2026-09-07: 配方级面积过滤覆盖（YoloTool.EdgeDetection，null=回退全局 Settings.Algorithm 值）
                                                                double? recipeMinMaskAreaPixels = null,
                                                                double? recipeMaxMaskAreaPixels = null,
                                                                // 2026-09-08: 配方级灰度判向覆盖（YoloTool.IsBrightnessDirectionEnabled，null=回退全局
                                                                // Settings.Algorithm.BrightnessDirectionEnabled；仅在未启用角度检测时参与，见 else 分支）
                                                                bool? recipeBrightnessDirectionEnabled = null,
                                                                // 2026-09-15: 配方级抓取点偏移（产品局部坐标系，mm）。
                                                                // 长轴正值 = 朝产品头部；短轴正值 = 面朝头部时的右手侧；均为 0 时抓取点即矩形中心。
                                                                float grabOffsetLongMm = 0,
                                                                float grabOffsetShortMm = 0)
    {
        ArgumentNullException.ThrowIfNull(scanerResult);
        ArgumentNullException.ThrowIfNull(edgeResults);
        ArgumentNullException.ThrowIfNull(transformer);

        // 2026-09-13: 标定构图可见化——镜像/同构影响消歧链的 cos 翻转，首次进帧即记录（只记一次）
        if (!s_mirrorStateLogged)
        {
            s_mirrorStateLogged = true;
            if (transformer.IsInitialized)
            {
                LogService.Instance.Info(
                    transformer.IsMirrored
                        ? "[标定] 三点标定构图为镜像（X/Y 轴反向）——二维码/模型消歧已按镜像翻转 cos 对齐特征池头向约定；如同构不要镜像，请重标定。"
                        : "[标定] 三点标定同构无镜像（X/Y 轴同向），消歧无需翻转。");
            }
            else
            {
                LogService.Instance.Info("[标定] 未初始化——坐标/角度按图像系兜底（WorldX/Y 像素、Angle 图像角），落库标记 IsCalibrated=false。");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 2026-09-08: 灰度判向有效开关 = 配方级覆盖 ?? 全局设置（两处解析口径一致，见 HomeViewModel
        // grayDirectionEnabled 与 angleMat 生成条件，保证 ToMat 与判向执行不脱节）
        bool brightnessDirectionEnabled = recipeBrightnessDirectionEnabled
            ?? Models.Settings.Instance.Algorithm.BrightnessDirectionEnabled;

        var detectTime = DateTime.Now;
        // M40: 使用 FrameResult 中传递的真实时间戳
        var encodeTime = scanerResult.EncoderReceivedTime;
        var records = new List<DbModel>();
        // 与 edgeResults 严格同序：被 continue 跳过的位置用 null 占位，便于 UI 按索引取值
        var indexedRecords = new List<DbModel?>();
        // 2026-09-07: 通过过滤的目标 → 掩码面积（原图像素），key=目标在 edgeResults 中的索引。
        // 供主页表格显示，避免 UI 侧为显示面积重复扫描掩码。
        var maskAreaByEdgeIndex = new Dictionary<int, double>();
        // 与 edgeResults 严格同序的角度模型绘制信息（角度模型成功时非 null，原图像素坐标）
        var angleDrawInfos = new List<AngleDrawInfo?>();
        // 2026-09-11: 与 edgeResults 严格同序的"头尾是否翻转 180°"标记（掩码角度路径专用，其余位置 null）。
        // 语义 = 本方法对实发角**实际施加**的翻转（二维码优先 → 灰度兜底）。UI 方向箭头据此复现同一次翻转，
        // 与实发角严格同源；此前 UI 自行按灰度统计判定，在二维码链路启用后会与实发角相差 180°（所见非所发）。
        var headFlips = new List<bool?>();
        // P0-1/2: 条码候选列表——每个条码只能被一个检测框消费，防止同一条码被多个产品重复绑定
        // 原始 BarcodeResults 可能包含同一帧中多个产品的条码，逐个匹配后从候选列表移除
        var availableBarcodes = new List<HikBarcodeResult>(scanerResult.BarcodeResults ?? Array.Empty<HikBarcodeResult>());

        // 2026-09-08: 识别可见性日志——本帧检出>0 时在方法末打一条 Info 汇总（画面画框 ↔ 日志一一对应，
        // 解决"画面识别到但日志无记录"的排查盲区）；每个被过滤目标再记 Debug 明细（含原因/位置/阈值）。
        int passedFilterCount = 0;        // 通过 边界+面积 过滤的有效目标数
        int filteredByBoundaryCount = 0;  // 因越界（距边缘不足边距）被拒数
        int filteredByAreaCount = 0;      // 因掩码面积超范围被拒数
        // 2026-09-15: 因朝向不可信而把"抓取点长轴偏移"退化为 0 的目标数（策略：宁可抓在中心，也不抓反）
        int grabLongSuppressedByUntrustedHead = 0;
        var detectLogFrame = scanerResult.FrameNumber;

        int idx = 0;
        foreach (var edgeResult in edgeResults)
        {
            // M123: 记录当前 edgeResult 在序列中的索引，用于文件名区分（在 continue 前递增）
            var edgeIdx = idx;
            idx++;
            var bounds = isResize
                // REVIEW-FIX: 先转 long 再乘并钳制到 int 范围。原实现 int×int 相乘，
                // 大分辨率+大缩放因子时溢出为负值/错误值，导致 Bounds 异常。
                ? new Rectangle(
                    ClampToInt((long)edgeResult.Bounds.X * resizeWidth),
                    ClampToInt((long)edgeResult.Bounds.Y * resizeHeight),
                    ClampToInt((long)edgeResult.Bounds.Width * resizeWidth),
                    ClampToInt((long)edgeResult.Bounds.Height * resizeHeight))
                : new Rectangle(edgeResult.Bounds.X, edgeResult.Bounds.Y, edgeResult.Bounds.Width, edgeResult.Bounds.Height);

            // 2026-09-05: 四边独立最小间距可配置（Settings.Algorithm.EdgeMargin*Pixels，默认各 10px），
            // 检测框距对应图像边缘不足该值（含部分/完全出界）判定无效，防止抓到半个产品。
            var edgeAlg = Models.Settings.Instance.Algorithm;
            double marginLeft = edgeAlg.EdgeMarginLeftPixels;
            double marginTop = edgeAlg.EdgeMarginTopPixels;
            double marginRight = edgeAlg.EdgeMarginRightPixels;
            double marginBottom = edgeAlg.EdgeMarginBottomPixels;
            if (bounds.Left < marginLeft
                || bounds.Top < marginTop
                || bounds.Right > (scanerResult.Width - marginRight)
                || bounds.Bottom > (scanerResult.Height - marginBottom))
            {
                // 超出图像边界：在 indexedRecords 中占位 null，保持与 edgeResults 同序
                filteredByBoundaryCount++;
                LogService.Instance.Debug(
                    $"[识别][过滤] 帧={detectLogFrame} #{edgeIdx} {edgeResult.Name?.Name ?? "?"} 越界被拒: " +
                    $"框=({bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}) " +
                    $"图={scanerResult.Width}×{scanerResult.Height} 边距(L/T/R/B)={marginLeft:F0}/{marginTop:F0}/{marginRight:F0}/{marginBottom:F0}");
                indexedRecords.Add(null);
                angleDrawInfos.Add(null);
                headFlips.Add(null);
                continue;
            }

            // 2026-09-05: 发送/落库坐标源改为最小外接旋转矩形中心（maskMinAreaRect.Center，用户要求），
            // 不再使用掩码灰度加权质心。GetMaskStats 单次遍历同时产出两者，此处仅消费矩形中心。
            // 矩形中心与质心同处"掩码局部+检测框偏移"坐标系（isResize 时统一按缩放比还原原图坐标）。
            var (_, maskMinAreaRect) = edgeResult.GetMaskStats();

            // 2026-09-07: 产品掩码面积上下限过滤（原图像素，0=禁用）。
            // 优先级：配方级覆盖(recipeMin/MaxMaskAreaPixels) → 全局(Settings.Algorithm)；
            // null = 未在配方设置 → 回退全局。isResize 时掩码面积处于推理图分辨率，
            // 需 ×(ResizeScale×ResizeScaleY) 换算回原图像素再比较，保证阈值口径与缩放设置无关。
            // 掩码面积为 0（阈值下无有效像素、回退外接框）的目标在启用下限(>0)时一并过滤。
            double minMaskAreaPx = recipeMinMaskAreaPixels ?? edgeAlg.MinMaskAreaPixels;
            double maxMaskAreaPx = recipeMaxMaskAreaPixels ?? edgeAlg.MaxMaskAreaPixels;
            double maskAreaOriginalPixels = isResize
                ? (double)maskMinAreaRect.MaskArea * resizeWidth * resizeHeight
                : maskMinAreaRect.MaskArea;
            if ((minMaskAreaPx > 0 && maskAreaOriginalPixels < minMaskAreaPx)
                || (maxMaskAreaPx > 0 && maskAreaOriginalPixels > maxMaskAreaPx))
            {
                // 面积超范围（误检小目标/超大异常目标）：不落库、不发送 VGT、不参与去重计数。
                // 与边界过滤一致，indexed/angle 占位 null 保持与 edgeResults 同序。
                filteredByAreaCount++;
                var areaRangeDesc = maxMaskAreaPx > 0
                    ? $"[{minMaskAreaPx:F0}, {maxMaskAreaPx:F0}]"
                    : $"[{minMaskAreaPx:F0}, ∞)";
                LogService.Instance.Debug(
                    $"[识别][过滤] 帧={detectLogFrame} #{edgeIdx} {edgeResult.Name?.Name ?? "?"} 面积被拒: " +
                    $"掩码={maskAreaOriginalPixels:F0}px 阈值={areaRangeDesc}");
                indexedRecords.Add(null);
                angleDrawInfos.Add(null);
                headFlips.Add(null);
                continue;
            }

            // 通过面积过滤：记录原图像素掩码面积供主页显示（edgeIdx 为同序索引 key）
            passedFilterCount++;
            maskAreaByEdgeIndex[edgeIdx] = maskAreaOriginalPixels;

            double imageX, imageY;
            if (isResize)
            {
                // isResize=true：推理图坐标 × 缩放比例 = 原图坐标
                imageX = maskMinAreaRect.Center.X * resizeWidth;
                imageY = maskMinAreaRect.Center.Y * resizeHeight;
            }
            else
            {
                // isResize=false：坐标已经是原图坐标，无需缩放
                imageX = maskMinAreaRect.Center.X;
                imageY = maskMinAreaRect.Center.Y;
            }

            double contourWorldX, contourWorldY;
            if (!transformer.IsInitialized)
            {
                // 2026-09-13: 未标定守卫——CoordinateTransformer 未初始化时 _scaleX=0，
                // ImageToPhysical 除零返回 NaN/Infinity，会静默污染落库/发送/跟踪匹配。
                // 显式回退原图像素坐标并告警（与 ComputeMaskAngleCalibrated 未标定回退
                // 图像主轴角的处置对称），联调场景可用、且能第一时间发现"没标定就上线"。
                contourWorldX = imageX;
                contourWorldY = imageY;
                LogService.Instance.Warning(
                    $"[识别][未标定] 帧={detectLogFrame} #{edgeIdx} 三点标定未初始化，" +
                    "WorldX/WorldY 回退原图像素坐标（仅供联调，非真实 mm 值），请完成标定后上线。");
            }
            else
            {
                (contourWorldX, contourWorldY) = transformer.ImageToPhysical(imageX, imageY);
            }

            // 2026-09-15: 原先在此处叠加的世界坐标系常量平移补偿（OffsetX/OffsetY）已移除。
            // 抓取点偏移改由产品局部坐标系表达，且必须在头尾朝向确定之后才能计算
            // （长轴是无向轴，不带头尾符号会有 50% 概率偏到反方向），见下方消歧链收口之后的
            // GrabPointCalculator 调用。此处 contourWorld 保持为"矩形中心"，供角度去重与 UI 使用。
            var barcode = "noread";
            var barcodeScore = 0d;
            var imageBarcodeX = 0d;
            var imageBarcodeY = 0d;
            double angle = 0d;

            // UI-FIX(2026-08-13): 码绑定改用最小外接旋转矩形包含判定，替代原轴对齐 bounds.Contains。
            // 轴对齐外接矩形在斜放产品时四角"悬空"，条码贴边时中心点易落入悬空区/相邻产品框导致错绑；
            // 旋转矩形贴合产品轮廓，消除悬空区误绑。
            var rotatedCorners = maskMinAreaRect.GetCorners();
            if (isResize)
            {
                // 推理图坐标 → 原图坐标（与 bounds 缩放语义一致，见上 L124-132）
                var scaled = new SixLabors.ImageSharp.PointF[rotatedCorners.Length];
                for (int c = 0; c < rotatedCorners.Length; c++)
                {
                    scaled[c] = new SixLabors.ImageSharp.PointF(
                        rotatedCorners[c].X * resizeWidth,
                        rotatedCorners[c].Y * resizeHeight);
                }

                rotatedCorners = scaled;
            }

            // P0-1: 在候选条码列表中查找中心落在当前检测框内的条码，找到后立即从列表移除，
            // 防止同一条码被后续检测框重复绑定（导致多个产品显示同一条码）
            for (int bi = 0; bi < availableBarcodes.Count; bi++)
            {
                var barcodeResult = availableBarcodes[bi];
                var barcodeCenter = barcodeResult.Center();
                if (IsPointInRotatedRect(barcodeCenter.X, barcodeCenter.Y, rotatedCorners))
                {
                    barcode = barcodeResult.CodeString();
                    barcodeScore = barcodeResult.Confidence();
                    imageBarcodeX = barcodeCenter.X;
                    imageBarcodeY = barcodeCenter.Y;
                    availableBarcodes.RemoveAt(bi);
                    break;
                }
            }

            // 角度：角度检测启用时由角度模型计算；未启用时用分割掩码最小外接矩形主轴角度。
            // 跨帧锁定：某一帧拍到角度即锁定并沿用；未拍到特征且无锁定时输出 -9999（未知哨兵）。
            // 输出域：(-180,180]（AngleTracker 归一化，与 DbModel.Angle 落库/机器人发送域一致）。
            AngleDrawInfo? angleDrawInfo = null;
            double? modelAngle = null;
            // 2026-09-08: 判向统计快照（2026-09-13 起由特征池产出）——声明在分支外，供下方 DbModel 落库引用；
            // 未启用亮度特征/无灰度图/掩码退化/单侧无像素时为 null。
            BrightnessDirectionStats? brightnessStats = null;
            // 2026-09-11: 掩码角度路径实际施加的头尾翻转（true=相对 fallbackAngle 翻转了 180°）。
            // 供 UI 方向箭头复现同一朝向（null = 未走掩码角度路径，无可绘制朝向）。
            bool? headFlipped = null;
            // 2026-09-11: 角度模型"方向退化"时回落掩码兜底。模型的方向来自"产品质心 → 特征质心"的有向向量，
            // 特征居中/对称时两质心几乎重合，atan2 分子分母同时趋零 → 角度由噪声决定，却能通过原有三道
            // 质量门。此时改走"未启用角度模型"的路径重算：掩码主轴角（三点标定换算世界角）+ 灰度判向定头尾。
            // 只换"方向的判据"，几何、坐标系与输出域完全不变。
            // 2026-09-13: 角度口径统一（不再分"角度模型路径 / 掩码回退路径"）——
            // 基础角恒为掩码主轴世界角（fallbackAngle），角度模型与二维码/特征池一样，
            // 只产出"哪端是头"的翻转信号；统一消歧链：二维码 → 模型翻转 → 特征池 → 无向回退角。

            // 2026-09-12: 「条码读取成功」的判据提到这里，成为**单一来源**。
            // 原先它声明在下方的回退块内，只有走到掩码角度路径时才存在；
            // 但 OK/NG 落库（Result 列）在块外执行，非掩码路径下也需要该判据，
            // 且绝不能在两处各写一遍判据——否则日后改 "noread" 哨兵规则会漏改一处。
            bool hasBarcode = !string.IsNullOrEmpty(barcode)
                              && !string.Equals(barcode, "noread", StringComparison.OrdinalIgnoreCase);

            // 2026-09-13: 特征池判定结果（声明在方法级——DbModel 落库在 if 块外引用）
            HeadTailPoolDecision? poolDecision = null;

            // 2026-09-13: 头尾真值符号（QR 一致率自检用），由 TryApplyBarcodeHeadDirection 顺带输出。
            // 同样声明在方法级——DbModel 落库在 if 块外引用。null = 本帧无真值（无码/码过近/码垂直于长轴）。
            bool? headTruthPositive = null;

            // ==== 唯一基础角：掩码主轴世界角 + OffsetAngle（2026-09-13 两路径合一） ====
            // 主轴角经三点标定换算为世界坐标系角度（ComputeMaskAngleCalibrated，主轴两端点
            // ImageToPhysical 后 atan2），未标定时回退推理图主轴角；OffsetAngle 对所有路径有效。
            // 头尾歧义（180° 方向）由下方消歧链裁决：二维码位置 → 模型翻转 → 特征池 → 无向回退角。
            var fallbackAngle = ComputeMaskAngleCalibrated(
                maskMinAreaRect.Center.X, maskMinAreaRect.Center.Y, maskMinAreaRect.Angle,
                maskMinAreaRect.Width, maskMinAreaRect.MaskArea,
                transformer, isResize, resizeWidth, resizeHeight) + offsetAngle;

            double longAxisPx = isResize ? maskMinAreaRect.Width * (double)resizeWidth : maskMinAreaRect.Width;

            // 头端向量：产品中心（OBB 中心，原图像素）→ 二维码中心（原图像素）。
            // 必须换算到与 fallbackAngle 同一坐标系：已标定 → 世界 mm（角度即世界角）；
            // 未标定 → 推理图坐标（此时角度退化为推理图主轴角，见 ComputeMaskAngleCalibrated）。
            // 配方平移补偿 offsetX/offsetY 是常量偏移，取两点差时自动抵消，无需叠加。
            double headOffsetPx = Math.Sqrt(
                (imageBarcodeX - imageX) * (imageBarcodeX - imageX)
                + (imageBarcodeY - imageY) * (imageBarcodeY - imageY));
            double dxHead, dyHead;
            if (transformer.IsInitialized)
            {
                var (pwX, pwY) = transformer.ImageToPhysical(imageX, imageY);
                var (bwX, bwY) = transformer.ImageToPhysical(imageBarcodeX, imageBarcodeY);
                dxHead = bwX - pwX;
                dyHead = bwY - pwY;
            }
            else
            {
                dxHead = (imageBarcodeX - imageX) / (isResize ? resizeWidth : 1);
                dyHead = (imageBarcodeY - imageY) / (isResize ? resizeHeight : 1);
            }

            // —— 角度模型：只产出翻转信号（2026-09-13，不再提供完整角度）——
            // 特征端向量（特征中心 − 产品中心）投影到主轴 u：≥ 0 = 特征端在 +u 半球 → 头在 +u
            // → 不翻转；< 0 → 头在 −u → fallbackAngle + 180°。与二维码头向量同坐标系处理。
            double? modelFlipAngle = null;
            if (angleDetectionEnabled && anglePredictorPool != null && angleSourceImage != null)
            {
                var modelSourceImage = angleSourceImage!;
                var modelPool = anglePredictorPool!;
                var angleResult = await TryComputeAngleWithModelAsync(
                                      modelSourceImage, edgeResult, transformer,
                                      isResize, resizeWidth, resizeHeight,
                                      modelPool, angleTool, offsetAngle, cancellationToken)
                                  ?? null;
                float minOffsetRatio = (angleTool ?? DefaultAngleTool).MinCentroidOffsetRatio;
                if (angleResult is not null && angleResult.DirectionOffsetRatio >= minOffsetRatio)
                {
                    double dxModel, dyModel;
                    if (transformer.IsInitialized)
                    {
                        var (fx, fy) = transformer.ImageToPhysical(
                            angleResult.FeatureCentroidImage.X, angleResult.FeatureCentroidImage.Y);
                        var (px, py) = transformer.ImageToPhysical(
                            angleResult.ProductCentroidImage.X, angleResult.ProductCentroidImage.Y);
                        dxModel = fx - px;
                        dyModel = fy - py;
                    }
                    else
                    {
                        dxModel = (angleResult.FeatureCentroidImage.X - angleResult.ProductCentroidImage.X)
                                  / (isResize ? resizeWidth : 1);
                        dyModel = (angleResult.FeatureCentroidImage.Y - angleResult.ProductCentroidImage.Y)
                                  / (isResize ? resizeHeight : 1);
                    }

                    double modelLen = Math.Sqrt(dxModel * dxModel + dyModel * dyModel);
                    if (modelLen > 1e-9)
                    {
                        double radM = fallbackAngle * Math.PI / 180.0;
                        double cosM = (dxModel * Math.Cos(radM) + dyModel * Math.Sin(radM)) / modelLen;
                        // 镜像标定下世界系 cos 与图像系相反，反转判定与特征池"大的一侧是头"对齐
                        var effCosM = transformer.IsInitialized && transformer.IsMirrored ? -cosM : cosM;
                        modelFlipAngle = effCosM >= 0 ? fallbackAngle : fallbackAngle + 180.0;
                    }

                    // 绘制信息：产品中心 → 特征中心连线（原图像素），供 UI 显示消歧信号来源
                    angleDrawInfo = new AngleDrawInfo(
                        ToVGT.ToRobotAngle(modelFlipAngle ?? fallbackAngle),
                        angleResult.FeatureCentroidImage.X, angleResult.FeatureCentroidImage.Y,
                        angleResult.ProductCentroidImage.X, angleResult.ProductCentroidImage.Y);
                }
                else if (angleResult is not null)
                {
                    // 方向退化（特征居中）：仅丢失模型的消歧信号，基础角照常，顺延特征池定头尾
                    RecordAngleReject(
                        $"模型方向退化（偏移比 {angleResult.DirectionOffsetRatio:F4} < {minOffsetRatio:F4}），丢消歧信号，顺延特征池定头尾");
                }
            }

            // —— 特征池"数值大的一侧是头部"级联（几何 → 亮度 → 结构 → 纹理）——
            // 仅在模型翻转信号不可用时才需要它定头尾（模型可用帧跳过，算力与旧模型路径持平）；
            // 灰度判向已并入特征池成为 BrightnessDiff 特征（排在几何之后、梯度/纹理之前），
            // 不再作为独立兜底路径；特征池此处照常产出 BrightMean/DarkMean/BrightnessDiff 统计。
            if (Models.Settings.Instance.Algorithm.HeadTailFeaturePoolEnabled && modelFlipAngle is null)
            {
                // 2026-09-13: 画像缺失必须显式告警。曾因 HomeViewModel 的 angleMat gate 漏了
                // 特征池开关，导致「开特征池 + 关亮度判向」时 angleMat 恒为 null，特征池每帧
                // 静默跳过（7 个特征全不算且无任何日志）。这条 Warning 是防回归的哨兵——
                // 若上线后出现，说明上游又有人在 gate 里漏了 featurePoolEnabled。
                if (angleSourceImage is null)
                {
                    LogService.Instance.Warning(
                        "[特征池] 已启用但本帧无 Mat 源图（上游 angleMat 为 null）——本帧不做头尾判向。" +
                        "请检查 HomeViewModel 的 angleMat 创建条件是否仍包含 HeadTailFeaturePoolEnabled");
                }
                else
                {
                    var alg = Models.Settings.Instance.Algorithm;

                    // 2026-09-13: 自适应死区——读窗口取本帧基准（按配方分桶，窗口未满回退固定值），
                    // 判定后把本帧各特征的 |v| 回写窗口（★全帧记录，无论是否出死区，防自锁闭环）。
                    // 读取发生在记录之前，故本帧死区由"此前 30 帧"决定，不存在自我影响。
                    var adaptiveEnabled = alg.HeadTailAdaptiveDeadbandEnabled;
                    var recipeKey = RecipesManage.Instance.CurrentRecipe?.Name ?? "(无配方)";
                    Func<string, double>? adaptiveBase = null;
                    if (adaptiveEnabled)
                    {
                        adaptiveBase = name => HeadTailAdaptiveDeadband.Instance.GetBaseDeadband(
                            recipeKey, name, alg.HeadTailFeatureDeadband, alg.HeadTailAdaptiveDeadbandK);
                    }

                    var codePresent = imageBarcodeX != 0 || imageBarcodeY != 0;
                    poolDecision = HeadTailFeaturePool.Evaluate(
                        fallbackAngle, angleSourceImage, edgeResult,
                        maskMinAreaRect.Center.X, maskMinAreaRect.Center.Y, maskMinAreaRect.Angle, maskMinAreaRect.MaskArea,
                        longAxisPx, codePresent, imageBarcodeX, imageBarcodeY,
                        alg.HeadTailFeatureDeadband,
                        brightnessDirectionEnabled,
                        alg.BrightnessContrastStretchEnabled,
                        alg.BrightnessStretchLowPercentile,
                        alg.BrightnessStretchHighPercentile,
                        out brightnessStats,
                        adaptiveBaseDeadband: adaptiveBase);

                    if (adaptiveEnabled && poolDecision is not null)
                    {
                        foreach (var f in poolDecision.Features)
                        {
                            HeadTailAdaptiveDeadband.Instance.Record(
                                recipeKey, f.Name, Math.Abs(f.SignedValue));
                        }
                    }

                    if (poolDecision is { Decisive: true })
                    {
                        LogService.Instance.Debug(
                            $"[特征池判向] {poolDecision.SourceFeature} 定头尾: 角度 {fallbackAngle:F1}°→{poolDecision.Angle:F1}°（翻转={poolDecision.Flipped}）");
                    }
                }
            }

            // —— 消歧链收口：二维码位置 → 模型翻转 → 特征池 → 无向回退角 ——
            // 二维码是产品上的物理地标、位置固定，可用时以它为准；模型是强方向信号次之；
            // 特征池取首个出死区特征兜底；全部不可用则维持不带朝向的回退角。
            var barcodeAngle = TryApplyBarcodeHeadDirection(
                              fallbackAngle, hasBarcode, dxHead, dyHead, headOffsetPx, longAxisPx,
                              invertForMirror: transformer.IsInitialized && transformer.IsMirrored,
                              out var barcodeTruthPositive);
            // 真值符号仅在有码且 QR 成功定头尾时有效；否则保持 null（语义区别于 false）
            headTruthPositive = barcodeAngle.HasValue ? barcodeTruthPositive : (bool?)null;
            modelAngle = barcodeAngle
                          ?? modelFlipAngle
                          ?? (poolDecision is { Decisive: true } ? (double?)poolDecision.Angle : null)
                          ?? fallbackAngle;
            // 头尾翻转标记：与实发角完全同源（二维码 → 模型 → 特征池），供 UI 箭头复现朝向
            headFlipped = IsHeadOppositeDegrees(modelAngle.Value, fallbackAngle);

            // ── 抓取点（2026-09-15）：产品局部坐标系偏移，必须在头尾朝向确定之后计算 ──
            // 长轴是无向轴（MinAreaRect.Angle 已做 ±180° 二义消除），不带头尾符号会有 50% 概率偏到反方向。
            // 矩形在推理图坐标系上算出，而 imageX/imageY 已按缩放比还原到原图坐标系；
            // 非等比缩放（ResizeScale != ResizeScaleY）会改变方向，故先把长轴方向按同一比例换算过去。
            var rectAngleRad = maskMinAreaRect.Angle * Math.PI / 180.0;
            var longAxisDirX = Math.Cos(rectAngleRad);
            var longAxisDirY = Math.Sin(rectAngleRad);
            if (isResize)
            {
                longAxisDirX *= resizeWidth;
                longAxisDirY *= resizeHeight;
            }

            // 朝向是否可信：至少一条消歧路径给出明确信号；全部回退到无向角时不可信。
            var headDirectionTrusted = barcodeAngle.HasValue
                                    || modelFlipAngle.HasValue
                                    || (poolDecision is { Decisive: true });
            var effectiveGrabLongMm = grabOffsetLongMm;
            if (!headDirectionTrusted && grabOffsetLongMm != 0)
            {
                // 策略 A：宁可抓在中心，也不要抓反 —— 朝向不可信时长轴偏移退化，短轴偏移仍生效
                effectiveGrabLongMm = 0;
                grabLongSuppressedByUntrustedHead++;
            }

            var (grabImageX, grabImageY) = GrabPointCalculator.ComputeImagePoint(
                transformer, imageX, imageY, longAxisDirX, longAxisDirY, headFlipped == true,
                effectiveGrabLongMm, grabOffsetShortMm);

            double grabWorldX, grabWorldY;
            if (transformer.IsInitialized)
            {
                (grabWorldX, grabWorldY) = transformer.ImageToPhysical(grabImageX, grabImageY);
            }
            else
            {
                // 未标定：世界坐标即原图像素坐标（与上方 contourWorld 同口径）
                grabWorldX = grabImageX;
                grabWorldY = grabImageY;
            }

            // 注意：角度去重仍用矩形中心（contourWorld），与本改动无关，避免影响既有锁角行为。
            angle = _angleTracker.Resolve(
                scanerResult.EncoderValue, contourWorldX, contourWorldY, modelAngle);
            angleDrawInfos.Add(angleDrawInfo);
            headFlips.Add(headFlipped);

            var dbModel = new DbModel
            {
                // ImageX/ImageY 仍为「产品中心」：二维码到产品的距离统计等分析依赖它，不随抓取点变化。
                ImageX = imageX,
                ImageY = imageY,
                // 2026-09-15: WorldX/WorldY 改为「抓取点」—— 它就是要发送给机器人的坐标，
                // 机器人协议 $X/$Y、VGT 发送、位置去重与范围查询都消费这两个字段。
                WorldX = grabWorldX,
                WorldY = grabWorldY,
                Angle = angle,
                ImageBarcodeX = imageBarcodeX,
                ImageBarcodeY = imageBarcodeY,
                BarcodeScore = barcodeScore,
                Barcode = barcode,
                Encode = scanerResult.EncoderValue,
                CostTime = costTime,
                Width = bounds.Width,
                Height = bounds.Height,
                // M155: bounds.Width/Height 为 int，相乘在大图时会 int 溢出，先转 long 再相乘
                Area = (long)bounds.Width * bounds.Height,
                DetectTime = detectTime,
                EncodeTime = encodeTime,
                // L108: imageReceivedTime 与 detectTime 完全相同，直接使用 detectTime
                ImageReceivedTime = detectTime,
                Speed = speed,
                // 2026-09-13: 标定标志（区分真 mm/世界角 与 未标定的像素/图像角兜底值）
                IsCalibrated = transformer.IsInitialized,
                Score = edgeResult.Confidence,
                // 2026-09-08: 灰度判向统计随记录落库（仅无角度模型回退 + 判向实际统计完成时有值），
                // 供现场标定/验证：头端明暗假设是否成立、死区阈值是否合适、判向方向一致性分析。
                BrightMean = brightnessStats?.MeanPlus,
                DarkMean = brightnessStats?.MeanMinus,
                BrightnessDiff = brightnessStats?.Diff,
                // 2026-09-13: 头尾特征池判定轨迹（JSON），诊断特征池在现网产品的区分度与"大=头"约定一致性。
                HeadFeatures = HeadTailFeaturePool.SerializeTrace(poolDecision),
                // 2026-09-13: 头尾真值符号（QR 一致率自检）。与 HeadFeatures 同约定，
                // 使「各特征判得对不对」可在离线侧直接算（无需重算投影）。
                HeadTruthPositive = headTruthPositive,
                // 2026-09-12: 追溯列——记录本帧检测时生效的配方名，使「某配方的合格率/耗时」
                // 这类问题可被回答。取当前配方名，与界面/TCP 切配方同源（RecipesManage 单例）。
                RecipeName = RecipesManage.Instance.CurrentRecipe?.Name,
                // 2026-09-12: OK/NG 判定（用户定义的规则：条码读取成功 且 置信度 ≥ 阈值 → OK）。
                // 判定与量纲换算统一由 DetectionResultEvaluator 承担（Score 是 0~1，阈值是百分比）。
                // 「条码读取成功」复用上方方法级 hasBarcode（含 "noread" 哨兵排除），单一来源。
                Result = DetectionResultEvaluator.Evaluate(
                    hasBarcode,
                    edgeResult.Confidence,
                    Models.Settings.Instance.Algorithm.ResultOkScorePercent),
                // 2026-09-12: 工位 = 本机机器码（见 StationCode 字段注释：进程内只采集一次并缓存）。
                // 单机场景下"工位"即这台检测设备，用于把同一件产品的记录归到一条过站链上。
                Station = StationCode.Value,
            };

            if (saveDraw && !string.IsNullOrEmpty(folder))
            {
                // M123: 文件名加入 edgeResult 索引，避免同一帧多个检测结果文件名冲突
                dbModel.ImageFullName = Path.Combine(folder, $"draw_{scanerResult.FrameNumber}_{edgeIdx}.jpg");
            }

            records.Add(dbModel);
            indexedRecords.Add(dbModel);
        }

        // 2026-09-08: 识别可见性日志——本帧检出>0 时必记 Info，保证"画面检出目标"与日志一一对应。
        // 空帧不打（避免与 [UDP→ 空包摘要重复刷屏）；被过滤目标的逐条原因见上方 [识别][过滤] Debug 明细。
        if (idx > 0)
        {
            var filteredTotal = idx - passedFilterCount;
            // 2026-09-15: 朝向不可信时抓取点长轴偏移会退化为中心（策略 A），此处显式提示，避免现场"配了没效果"困惑
            var grabNote = grabLongSuppressedByUntrustedHead > 0
                ? $" 抓取点长轴偏移退化={grabLongSuppressedByUntrustedHead}(朝向不可信)"
                : string.Empty;
            LogService.Instance.Info(
                $"[识别] 帧={detectLogFrame} 检出={idx} 有效={passedFilterCount} " +
                $"被过滤={filteredTotal}(边界={filteredByBoundaryCount},面积={filteredByAreaCount}){grabNote}");
        }

        // M175: 将 cancellationToken 传递给 AddRangeAsync，支持取消批量写入
        await _barcodeData.AddRangeAsync(records, cancellationToken).ConfigureAwait(false);
        return new DetectionBuildResult(records, indexedRecords, angleDrawInfos, maskAreaByEdgeIndex, headFlips);
    }

    /// <summary>二维码中心离产品质心的最小像素距离比（相对产品长轴长度），低于此值无法判定头尾。</summary>
    private const double MinBarcodeHeadOffsetRatio = 0.05;

    /// <summary>二维码方向与长轴方向的 |cos| 下限：低于此值说明二维码几乎垂直于长轴，投影没有区分度。</summary>
    private const double MinBarcodeHeadCos = 0.30;

    /// <summary>
    /// 2026-09-11: 用二维码位置校正产品头尾（180° 方向）；返回 null 表示"二维码无法定头尾"，由调用方回退特征池。
    /// <para>原理：角度正向 u = (cos(baseAngle), sin(baseAngle)) 已由角度值本身给出（已标定 = 世界角；
    /// 未标定 = 图像主轴角，与 <c>ComputeMaskAngleCalibrated</c> 同口径）。计算"产品质心 → 二维码中心"
    /// 在 u 上的投影：投影 ≥ 0 表示二维码（头端）落在角度正向半区，与约定一致、不翻转；投影 &lt; 0 则 +180°，
    /// 使头端与角度正向对齐——与 <see cref="HeadTailFeaturePool"/> 的"数值大的一侧是头部"同一套
    /// "头端与角度正向一致"的约定，两者可互换。</para>
    /// <para><b>只改方向、不改角度</b>：翻转量恒为 180°，长轴几何、分割轴、输出域均不变。</para>
    /// <para>保护条件（任一不满足即返回 null 回退特征池）：无二维码；二维码中心离产品质心过近
    /// （相对长轴不足 <see cref="MinBarcodeHeadOffsetRatio"/>，此时 cos 由噪声决定）；
    /// 二维码方向几乎垂直于长轴（|cos| &lt; <see cref="MinBarcodeHeadCos"/>，投影无区分度）。</para>
    /// </summary>
    /// <param name="baseAngle">掩码主轴角（未归一化；与头端向量必须同坐标系）。</param>
    /// <param name="hasBarcode">本帧是否把二维码绑定到了该产品（条码非空且非 noread）。</param>
    /// <param name="dxHead">产品质心 → 二维码中心的向量 X（与 baseAngle 同坐标系）。</param>
    /// <param name="dyHead">产品质心 → 二维码中心的向量 Y（与 baseAngle 同坐标系）。</param>
    /// <param name="headOffsetPx">二维码中心到产品质心的距离（原图像素，仅用于几何保护）。</param>
    /// <param name="longAxisPx">产品长轴长度（原图像素，仅用于几何保护）。</param>
    /// <param name="truthPositive">输出：条码（头端）是否落在 +u 正向半区（proj ≥ 0）。
    /// <b>仅在返回非 null 时有意义</b>（返回 null 时置 false）——它是特征池自检所需的"头尾真值符号"，
    /// 与 <see cref="HeadTailFeaturePool"/> 的"数值大的一侧是头部"同约定，故可直接与各特征符号比一致率。</param>
    /// <returns>校正后的角度；无法判定时返回 null。</returns>
    internal static double? TryApplyBarcodeHeadDirection(
        double baseAngle,
        bool hasBarcode,
        double dxHead,
        double dyHead,
        double headOffsetPx,
        double longAxisPx,
        bool invertForMirror,
        out bool truthPositive)
    {
        truthPositive = false;
        if (!hasBarcode || !IsBarcodeFarEnoughForHeadDecision(headOffsetPx, longAxisPx))
        {
            return null;
        }

        double headLength = Math.Sqrt(dxHead * dxHead + dyHead * dyHead);
        if (headLength <= 1e-9)
        {
            return null;
        }

        double rad = baseAngle * Math.PI / 180.0;
        double cos = (dxHead * Math.Cos(rad) + dyHead * Math.Sin(rad)) / headLength;
        if (Math.Abs(cos) < MinBarcodeHeadCos)
        {
            return null; // 二维码几乎垂直于长轴，前后投影没有区分度
        }

        // 真值符号：投影 ≥ 0 表示头端在角度正向半区（与后续"不翻转"分支同一判据）
        // 2026-09-13: 镜像标定（transformer.IsMirrored）下反射使世界系 cos 符号与图像系相反，
        // 反转判定以对齐特征池"数值大的一侧是头部"约定，避免 QR 一致率系统性反向。
        var effCos = invertForMirror ? -cos : cos;
        truthPositive = effCos >= 0;
        // 头端（二维码所在端）落在角度负向半区时翻转 180°，使其与角度正向对齐
        return effCos >= 0 ? baseAngle : baseAngle + 180.0;
    }

    /// <summary>
    /// 二维码中心离产品质心的距离是否足以判定头尾（相对产品长轴长度）。
    /// 过近时"二维码在哪一端"由噪声决定，必须回退灰度判向而不是硬猜。
    /// </summary>
    /// <param name="headOffsetPx">二维码中心到产品质心的距离（原图像素）。</param>
    /// <param name="longAxisPx">产品长轴长度（原图像素）。</param>
    internal static bool IsBarcodeFarEnoughForHeadDecision(double headOffsetPx, double longAxisPx)
        => longAxisPx > 1.0 && headOffsetPx >= MinBarcodeHeadOffsetRatio * longAxisPx;

    /// <summary>头尾翻转判定的角度容差（度）。翻转量恒为精确的 180°，留容差仅为吸收浮点误差。</summary>
    private const double HeadOppositeToleranceDegrees = 1.0;

    /// <summary>
    /// 2026-09-11: 判定"实际发出的角度"相对基准轴是否被头尾翻转了 180°。
    /// <para>用途：把服务端**已经施加**的头尾翻转作为唯一事实下发给 UI，让画面方向箭头与实发角同源；
    /// 此前 UI 在绘制时自行按灰度统计二次判定，二维码链路（P5）启用后会与实发角相差 180°（所见非所发）。</para>
    /// <para>翻转量恒为 0 或 180°（见 <see cref="TryApplyBarcodeHeadDirection"/> 与
    /// <see cref="HeadTailFeaturePool"/>），故用"接近 180°"判定而非符号比较，
    /// 对 offsetAngle / 标定旋转等常量偏移天然免疫（两点同偏移相减抵消）。</para>
    /// </summary>
    /// <param name="angle">最终角度（已含头尾翻转）。</param>
    /// <param name="baseAngle">基准轴角度（未含头尾翻转）。</param>
    /// <returns>true = 相对基准轴反向（翻转 180°）；false = 同向，或角度非法（NaN）。</returns>
    internal static bool IsHeadOppositeDegrees(double angle, double baseAngle)
    {
        if (double.IsNaN(angle) || double.IsNaN(baseAngle))
        {
            return false;
        }

        double diff = (angle - baseAngle) % 360.0;
        if (diff < 0)
        {
            diff += 360.0;
        }

        return Math.Abs(diff - 180.0) <= HeadOppositeToleranceDegrees;
    }

    /// <summary>
    /// 调用角度模型计算产品角度（世界坐标系原始角 + Offset，可能越界）。
    /// 返回 null 表示质量门不过或业务失败（调用方回退）；
    /// 推理异常/环境问题（模型加载、运行时故障）记录 Error 日志并返回 null。
    /// 注意：返回值域为 [0,360)+Offset 的"未归一化原始值"；归一化到 (-180,180]
    /// 由调用方 AngleTracker.Resolve / 显示端 ToRobotAngle 完成。
    /// </summary>
    private static async Task<AngleDetectionResult?> TryComputeAngleWithModelAsync(
        Mat angleSourceImage,
        Segmentation edgeResult,
        CoordinateTransformer transformer,
        bool isResize,
        int resizeWidth,
        int resizeHeight,
        YoloPredictorPool anglePredictorPool,
        YoloTool? angleTool,
        float offsetAngle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var lease = anglePredictorPool.Acquire(cancellationToken: cancellationToken);
            return await AngleDetectionProcessor.ComputeAngleAsync(
                       angleSourceImage, edgeResult, transformer,
                       isResize, resizeWidth, resizeHeight,
                       lease.Predictor, angleTool ?? new YoloTool(), offsetAngle, cancellationToken,
                       onRejected: RecordAngleReject)
                   .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 环境/模型问题（非质量门）：记 Error，避免线上问题被静默吞掉
            LogService.Instance.Error($"角度模型推理异常，输出未知角度(-9999): {ex}");
            return null;
        }
    }

    /// <summary>
    /// 2026-09-07: 记录角度质量门拒绝原因（节流 30 秒聚合一条 Warning）。
    /// 主流程逐帧调用不逐条写日志——产品连续经过时拒绝可能每秒数十次，
    /// 限频聚合后既能暴露"是哪道门在拒"又不会刷屏。失败即重置计数窗口。
    /// </summary>
    private static void RecordAngleReject(string reason)
    {
        lock (AngleRejectLogLock)
        {
            _angleRejectCountSinceLog++;
            var now = DateTime.Now;
            if ((now - _lastAngleRejectLogTime) < AngleRejectLogInterval)
            {
                return;
            }

            _lastAngleRejectLogTime = now;
            var count = _angleRejectCountSinceLog;
            _angleRejectCountSinceLog = 0;
            LogService.Instance.Warning(
                $"[角度诊断] 最近 {AngleRejectLogInterval.TotalSeconds:0}s 内" +
                $"角度未被采信 {count} 次（质量门拒绝 / 方向退化），最近原因: {reason}");
        }
    }

    /// <summary>
    /// UI-FIX(2026-08-13): 判断点是否在凸多边形（最小外接旋转矩形角点）内。
    /// 叉积同号法：依次计算点到各边的叉积，全部同号（含 0，点在边上）即为内部。
    /// </summary>
    private static bool IsPointInRotatedRect(float px, float py, SixLabors.ImageSharp.PointF[] polygon)
    {
        if (polygon is null || polygon.Length < 3)
        {
            return false;
        }

        bool? sign = null;
        for (int i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            var cross = (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);
            if (Math.Abs(cross) < 1e-6f)
            {
                continue; // 点在边上：视为内部
            }

            var isPositive = cross > 0;
            if (sign is null)
            {
                sign = isPositive;
            }
            else if (isPositive != sign)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 角度模型绘制信息（用于在 draw 图上绘制"产品质心 → 特征质心"连线与角度标注）。坐标为原图像素。
/// </summary>
/// <param name="Angle">角度标注值，(-180,180]（角度模型成功时已由构造处 ToRobotAngle 换算，
/// 与 DbModel.Angle 落库/发送域一致）。</param>
/// <param name="FeatureCentroidX">特征质心（推理中心）X，原图像素。</param>
/// <param name="FeatureCentroidY">特征质心（推理中心）Y，原图像素。</param>
/// <param name="ProductCentroidX">产品质心 X，原图像素。</param>
/// <param name="ProductCentroidY">产品质心 Y，原图像素。</param>
public sealed record AngleDrawInfo(
    double Angle,
    float FeatureCentroidX,
    float FeatureCentroidY,
    float ProductCentroidX,
    float ProductCentroidY);

/// <summary>
/// DetectionRecordService.BuildAndSaveAsync 的返回结果。
/// </summary>
/// <param name="ValidRecords">通过边界过滤的有效记录（用于落库/去重/发送 VGT）。</param>
/// <param name="IndexedRecords">与 edgeResults 严格同序的记录，被跳过的位置为 null（用于 UI 按索引取值避免错配）。</param>
/// <param name="AngleDrawInfos">与 edgeResults 严格同序的角度绘制信息，未计算角度模型的位置为 null。</param>
/// <param name="MaskAreaByEdgeIndex">
/// 2026-09-07：通过全部过滤的目标 → 掩码面积（原图像素），key 为该目标在 edgeResults 中的索引。
/// 仅含有效目标，供主页表格显示面积（与面积过滤阈值同口径），无需 UI 侧重复扫描掩码。
/// </param>
/// <param name="HeadFlips">
/// 2026-09-11：与 edgeResults 严格同序的头尾翻转标记（null = 该目标未走掩码角度路径，画面无朝向可绘）。
/// true 表示实发角相对掩码主轴被翻转了 180°（头端与角度正向对齐的约定）。UI 方向箭头据此复现朝向，
/// 与实发角严格同源，避免"画面箭头与发送角度相差 180°"。
/// </param>
public sealed record DetectionBuildResult(
    IReadOnlyList<DbModel> ValidRecords,
    IReadOnlyList<DbModel?> IndexedRecords,
    IReadOnlyList<AngleDrawInfo?> AngleDrawInfos,
    IReadOnlyDictionary<int, double> MaskAreaByEdgeIndex,
    IReadOnlyList<bool?> HeadFlips);

