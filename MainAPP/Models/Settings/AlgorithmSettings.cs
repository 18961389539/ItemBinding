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
        /// 是否启用"分割线两侧平均灰度"判向（无角度模型时消除掩码回退角度的 180° 方向歧义）。
        /// <para>开启后：以掩码矩形宽度轴为角度正向，统计掩码内两侧平均灰度，较亮/较暗侧（见
        /// <see cref="BrightnessHeadEndIsBright"/>）作为产品头端；头端落在角度负向半区时把回退角度 +180°，
        /// 使落库与发给机器人的角度扩展为唯一朝向 (-180,180]。判向结果由 AngleTracker 归一化。</para>
        /// <para>适用前提：产品长轴一端有系统性明暗特征（标签/色差等）且两端不对称；两端对称或光照
        /// 梯度与特征同量级时结果不可靠，可由配方 <c>IsBrightnessDirectionEnabled</c> 显式关闭。
        /// 默认 true：无角度模型时默认启用（本就是为消除 180° 歧义而设），保证落库/发送角度唯一；
        /// 公式页 nullable 覆盖用于现场关闭不适应样本。</para>
        /// </summary>
        public bool BrightnessDirectionEnabled { get; set; } = true;

        /// <summary>
        /// 灰度判向的产品头端明暗约定：true = 头端（角度 0° 标定端）在图像上偏亮，较亮半区为头端；
        /// false = 头端偏暗，较暗半区为头端。现场标定一次后锁定。
        /// </summary>
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
    }
}
