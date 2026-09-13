namespace MainAPP.Models
{
    /// <summary>
    /// 算法参数配置（由 Settings 持有实例）。
    /// </summary>
    public class AlgorithmSettings
    {
        /// <summary>
        /// 是否启用识别数量去重（输送线场景下避免同一产品多帧重复计数）
        /// </summary>
        public bool DedupEnabled { get; set; } = true;

        /// <summary>
        /// 是否启用编码器差分去重（替代位置/角度匹配）。
        /// 开启后按编码器值的帧间差分预测匹配同一产品，不受视觉质心抖动/角度歧义影响。
        /// 需要编码器计数值有效（FrameResult.EncoderValue 非 0 且递增）。
        /// </summary>
        public bool DedupByEncoder { get; set; } = false;

        /// <summary>
        /// 无条码产品的匹配坐标轴："X" 或 "Y"（垂直于输送线运动方向的坐标更稳定）
        /// </summary>
        public string DedupTrackAxis { get; set; } = "Y";

        /// <summary>
        /// 无条码产品的位置匹配阈值（单位与世界坐标一致，通常为毫米）
        /// </summary>
        public double DedupPositionThreshold { get; set; } = 3.0;

        /// <summary>
        /// 无条码产品的角度匹配阈值（度）
        /// </summary>
        public double DedupAngleThreshold { get; set; } = 2.0;

        /// <summary>
        /// 跟踪项过期时间（秒，默认 60）：某产品最后一次成像后超过该时长未被再次拍到，
        /// 跟踪状态（条码/匹配坐标/编码器值/角度锁定）即从内存清除。
        ///
        /// ★ 取值判据：必须大于相邻两次触发的最大时间间隔 = 触发间隔(mm) ÷ 最慢线速(mm/s)，
        ///   建议 3 倍余量。触发间隔 200mm、最慢线速 20mm/s → 间隔 10s → 本值至少 30。
        ///   取小了慢速产线会重复计数（每帧被判新品，角度锁定也中断 → -9999 不发 VGT）；
        ///   取大了仅多占少量内存（每项约百字节级），无正确性风险——宁大勿小。
        /// </summary>
        public int TrackerExpireSeconds { get; set; } = 60;

        /// <summary>
        /// 检测框距图像左边缘的最小间距（像素）。
        /// <para>检测框左边距 ≥ 该值才判定为有效（否则过滤，不落库/不发 VGT/不抓取），
        /// 防止抓到画面边缘的半个产品。默认 10，可设为 0 允许贴边。</para>
        /// </summary>
        public double EdgeMarginLeftPixels { get; set; } = 10;

        /// <summary>
        /// 检测框距图像上边缘的最小间距（像素）。语义同 <see cref="EdgeMarginLeftPixels"/>。
        /// </summary>
        public double EdgeMarginTopPixels { get; set; } = 10;

        /// <summary>
        /// 检测框距图像右边缘的最小间距（像素）。语义同 <see cref="EdgeMarginLeftPixels"/>。
        /// </summary>
        public double EdgeMarginRightPixels { get; set; } = 10;

        /// <summary>
        /// 检测框距图像下边缘的最小间距（像素）。语义同 <see cref="EdgeMarginLeftPixels"/>。
        /// </summary>
        public double EdgeMarginBottomPixels { get; set; } = 10;

        /// <summary>
        /// 产品（分割掩码）面积下限（原图像素）。掩码面积低于此值的目标判定为无效并过滤
        /// （不落库、不发送 VGT、不参与去重/计数），用于剔除误检小目标/噪声。
        /// <para>0 = 不启用下限（默认）。推理图缩放时按 缩放X×缩放Y 换算回原图像素后比较。</para>
        /// <para>掩码面积为 0（掩码阈值下无有效像素、回退外接框）的目标：启用下限(>0)时一并过滤。</para>
        /// </summary>
        public double MinMaskAreaPixels { get; set; } = 0;

        /// <summary>
        /// 产品（分割掩码）面积上限（原图像素）。掩码面积高于此值的目标判定为无效并过滤，
        /// 用于剔除超大异常目标（如镜头异物/整面反光误检）。
        /// <para>0 = 不启用上限（默认）。口径同 <see cref="MinMaskAreaPixels"/>。</para>
        /// </summary>
        public double MaxMaskAreaPixels { get; set; } = 0;

        /// <summary>
        /// 是否启用"亮度"判向特征（无角度模型时消除掩码回退角度的 180° 方向歧义）。
        /// <para>2026-09-13 起本开关控制的是<b>特征池内的亮度特征</b>（<c>HeadTailFeaturePool</c> 的
        /// "BrightnessDiff"，排在几何特征之后、梯度/纹理之前），不再是独立判向路径。开启后：以掩码矩形
        /// 宽度轴为角度正向，统计掩码内两侧（拉伸后）平均灰度，较亮半区即产品头端——即
        /// "数值大的一侧是头部"，与特征池其余特征完全同一约定；头端落在角度负向半区时把回退角度 +180°，
        /// 使落库与发给机器人的角度扩展为唯一朝向 (-180,180]。判向结果由 AngleTracker 归一化。</para>
        /// <para>适用前提：产品长轴一端有系统性明暗特征（标签/色差等）且两端不对称；两端对称或光照
        /// 梯度与特征同量级时结果不可靠，可由配方 <c>IsBrightnessDirectionEnabled</c> 显式关闭。
        /// 默认 true：无角度模型时默认启用（本就是为消除 180° 歧义而设），保证落库/发送角度唯一；
        /// 公式页 nullable 覆盖用于现场关闭不适应样本。</para>
        /// </summary>
        public bool BrightnessDirectionEnabled { get; set; } = true;

        /// <summary>
        /// <b>[已废弃，2026-09-13]</b> 原"灰度判向头端明暗约定"：true = 头端偏亮，false = 头端偏暗。
        /// <para>废弃原因：亮度判向并入特征池后统一采用"数值大的一侧是头部"（零定标）约定，
        /// 与其余 6 个特征一致，不再需要外部明暗约定做符号修正。此字段仅为兼容既有 config
        /// 文件而保留（读写不影响任何判定结果），后续版本可移除。</para>
        /// </summary>
        [Obsolete("已废弃：亮度判向并入特征池后统一走『数值大的一侧是头部』，此开关不再影响判定结果。")]
        public bool BrightnessHeadEndIsBright { get; set; } = true;

        /// <summary>
        /// 灰度判向死区（平均灰度差，0~255）。
        /// <para><b>2026-09-11 起口径变更</b>：本值比较的是"掩码内对比度拉伸之后"的两侧平均灰度差
        /// （见 <see cref="BrightnessContrastStretchEnabled"/>）。拉伸把掩码内灰度的
        /// [pLow, pHigh] 线性映射到 [0,255]，因此本值相当于"满量程的百分比 × 2.55"：
        /// 默认 5.0 ≈ 满量程的 2%。拉伸关闭时退化为原始的绝对灰度差。</para>
        /// <para>两侧平均灰度差的绝对值低于该值判定"不可判"，维持掩码原角度不翻转
        /// （不引入抖动翻转），并输出节流告警日志。</para>
        /// </summary>
        public double BrightnessDirectionDeadband { get; set; } = 5.0;

        /// <summary>
        /// 2026-09-13: 头尾特征池级联开关（默认 true）。启用后，掩码回退路径的头尾判定
        /// 在灰度兜底之前增加"特征池级联"——按固定优先级依次计算几何/结构特征，
        /// 第一个出死区的特征以"数值大的一侧是头部"定头尾（零定标，见 HeadTailFeaturePool）。
        /// </summary>
        public bool HeadTailFeaturePoolEnabled { get; set; } = true;

        /// <summary>
        /// 特征池相对差死区阈值（默认 0.10）：特征的"正/负半区相对差"绝对值达到该值才判定头尾。
        /// 相对差 = (P−M)/(P+M) ∈ (-1,1)，天然抗整体光照缩放；轴向偏度特征使用 2× 本值。
        /// </summary>
        public double HeadTailFeatureDeadband { get; set; } = 0.10;

        /// <summary>
        /// 2026-09-13: 自适应死区总开关（<b>默认关</b>）。开启后，每个特征的实际死区改为
        /// <c>k × median(|v|)</c>（按配方维护 30 帧滑动窗口），自动适配不同产品的不对称量级——
        /// 这是"兼容更多产品"的主要手段（固定 0.10 对强不对称产品形同虚设、对弱不对称产品永不裁决）。
        /// <para><b>为什么默认关</b>：启用前应先经 <c>headtail_audit</c> 拿到固定死区下的基线
        /// （一致率/自洽性指标），否则改了行为却没有对照。开启后的副作用需要监控
        /// <c>takeOverRate</c>：死区变小会抬高所有 conf（conf 按固定 ConfReference 归一化，不随死区漂移），
        /// 若 takeOverRate 掉到 0 说明死区过小、让位机制失效，应调大 k。</para>
        /// <para>冷启动：窗口未满 30 帧时逐帧回退到 <see cref="HeadTailFeatureDeadband"/>（即现状行为），
        /// 故重启/换配方的过渡是安全降级而非不可预测行为。窗口不持久化——重启即重新冷启动。</para>
        /// </summary>
        public bool HeadTailAdaptiveDeadbandEnabled { get; set; } = false;

        /// <summary>
        /// 自适应死区系数 k（默认 0.5）：实际死区 = k × median(|v|)。
        /// <para><b>k 的真实语义是"设定出死区率"而非"灵敏度"</b>——k=0.5 时约 70-75% 的帧出死区
        /// （median 是 |v| 分布的中位）。k 越小越多弱信号发言（配合让位机制消化），越大越保守。
        /// 调整后观察 headtail_audit 的出死区率与 takeOverRate 交叉校验。</para>
        /// </summary>
        public double HeadTailAdaptiveDeadbandK { get; set; } = 0.5;

        /// <summary>
        /// 是否启用"掩码内对比度拉伸"（默认 true）：灰度判向前，把掩码内灰度的
        /// [<see cref="BrightnessStretchLowPercentile"/>, <see cref="BrightnessStretchHighPercentile"/>]
        /// 分位窗口线性映射到 [0,255]（超界截断），再统计两侧平均值与差值。
        /// <para>动机：本项目实测（2026-09-11）产品掩码内灰度只覆盖 47~107（占满量程 29%），
        /// 两端本来就小的反差被进一步压低，导致 96.1% 的帧 `|diff|` 落在死区内、判向形同虚设。
        /// 真实图回放（487 帧）显示拉伸可把 `|diff|` 中位数放大约 2.3 倍，超死区占比从 44.6% 提到 76%。</para>
        /// <para>副作用：落库的 <c>DbModel.BrightMean/DarkMean/BrightnessDiff</c> 变为"拉伸后"的值
        /// （换算后仍在 0~255，且 <c>BrightnessDiff = BrightMean − DarkMean</c> 不变式仍然成立）；
        /// 拉伸窗口过窄（近单色掩码）时自动跳过拉伸，行为与关闭时一致。</para>
        /// </summary>
        public bool BrightnessContrastStretchEnabled { get; set; } = true;

        /// <summary>
        /// 对比度拉伸窗口的低分位（0~100，默认 1）。取分位而非极值以避开孤立噪点与掩码边缘毛刺。
        /// </summary>
        public double BrightnessStretchLowPercentile { get; set; } = 1.0;

        /// <summary>
        /// 对比度拉伸窗口的高分位（0~100，默认 99）。语义见 <see cref="BrightnessStretchLowPercentile"/>。
        /// </summary>
        public double BrightnessStretchHighPercentile { get; set; } = 99.0;

        /// <summary>
        /// 检测结果 OK/NG 判定阈值（<b>百分比 0~100</b>，默认 75）。
        ///
        /// 判定规则（2026-09-12 由用户定义）：
        /// <c>条码读取成功 且 边缘检测置信度×100 ≥ 本阈值 → "OK"，否则 "NG"</c>。
        ///
        /// ★ 单位提醒：落库的 <c>DbModel.Score</c> 来自 <c>InferenceResultItem.Confidence</c>，
        /// 其量纲是 <b>0~1</b>（见该类注释与 <c>ConfidencePercent => Confidence * 100</c>）。
        /// 本配置沿用「百分比」是人类可读口径，比较时必须先乘 100，切勿直接与 Score 相比——
        /// 直接比会让 Score(≤1) 永远小于 75，全部误判为 NG。
        ///
        /// 该值参与落库 <c>DbModel.Result</c>，是 AI 对话回答「合格率/过站」类问题的数据基础。
        /// </summary>
        public double ResultOkScorePercent { get; set; } = 75.0;
    }
}
