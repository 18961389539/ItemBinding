namespace MainAPP.Models
{
    /// <summary>
    /// 推理结果展示项（UI 友好格式）
    /// </summary>
    public class InferenceResultItem
    {
        /// <summary>置信度（0-1）</summary>
        public double Confidence { get; set; }

        /// <summary>置信度百分比（如 "95.3%"）</summary>
        public string ConfidencePercent => $"{Confidence * 100:F1}%";

        /// <summary>左上角 X 坐标</summary>
        public double X { get; set; }

        /// <summary>左上角 Y 坐标</summary>
        public double Y { get; set; }

        /// <summary>宽度</summary>
        public double Width { get; set; }

        /// <summary>高度</summary>
        public double Height { get; set; }

        /// <summary>中心 X 坐标</summary>
        public double CenterX => X + Width / 2;

        /// <summary>中心 Y 坐标</summary>
        public double CenterY => Y + Height / 2;

        /// <summary>
        /// 旋转角度（度），域 = 机器人发送域 (-180,180]（2026-09-05 起全系统规范域，
        /// <see cref="DetectionRecordService"/> 经 <c>AngleTracker</c> 归一化后落库即此域，
        /// 与配方详情页/发给机器人（RZ）数值完全一致，无需显示层换算）。
        /// 语义：
        /// - 角度模型成功：世界坐标系 atan2 绝对角换算后的机器人域值
        /// - 跨帧锁定：沿用历史锁定角换算后的值
        /// - 未识别到角度特征且无锁定：-9999（未知哨兵，负值原样保留，AngleDisplay 据此显示"未知"）
        /// </summary>
        public double Angle { get; set; }

        /// <summary>旋转角度显示（如 "-32.0°"、空，或角度为未知哨兵 -9999（< -180，域外）时显示"未知"）</summary>
        public string AngleDisplay => Angle < -180 ? "未知" : (Angle != 0 ? $"{Angle:F1}°" : string.Empty);

        /// <summary>
        /// 分割掩码面积（原图像素，与面积过滤阈值同口径）。
        /// <para>由 <see cref="DetectionRecordService"/> 在过滤通过后按原图像素计算并回传，
        /// 未经过滤的目标（null 记录）不会出现在列表中，故默认 0 仅在异常/旧数据时出现。</para>
        /// </summary>
        public double MaskAreaOriginalPixels { get; set; }

        /// <summary>掩码面积显示（千分位，如 "1,234,567"；0/未提供时为空）</summary>
        public string MaskAreaDisplay => MaskAreaOriginalPixels > 0 ? MaskAreaOriginalPixels.ToString("N0") : string.Empty;

        /// <summary>
        /// 绑定的条码（未匹配到条码时为 "noread"）
        /// 来自 <see cref="DetectionRecordService"/> 中通过 bounds.Contains(barcodeCenter) 位置匹配
        /// 的扫码枪真实识别结果
        /// </summary>
        public string Barcode { get; set; } = "noread";

        /// <summary>
        /// 亮度判向"正向半区"平均灰度（0~255）。null = 未判向/统计未完成。
        /// 来自 <see cref="HeadTailFeaturePool"/> 的亮度特征统计（与 DbModel.BrightMean 同值）。
        /// <para>2026-09-13 起该统计由特征池产出（原独立"灰度判向"已并入特征池，成为派生于级联
        /// 中的 "BrightnessDiff" 特征）；仅在启用亮度特征且统计完成（两侧均有掩码像素）时有值。</para>
        /// <para>2026-09-11 起为"掩码内对比度拉伸之后"的值，不再是原始绝对灰度（详见 DbModel.BrightMean）。</para>
        /// </summary>
        public double? BrightMean { get; set; }

        /// <summary>
        /// 亮度判向"负向半区"平均灰度（0~255）。null 语义与口径同 <see cref="BrightMean"/>。
        /// </summary>
        public double? DarkMean { get; set; }

        /// <summary>
        /// 两侧平均灰度差 = BrightMean − DarkMean（不变式在拉伸前后均成立）。
        /// null 语义与口径同 <see cref="BrightMean"/>。
        /// </summary>
        public double? BrightnessDiff { get; set; }
    }

    /// <summary>
    /// 推理结果颜色调色板：按类别名为目标分配稳定的颜色（同名同色）。
    /// 使用色盲友好调色板（参考 Wong 2011 Color Universal Design），避免红绿对比。
    /// </summary>
    public static class InferenceColorPalette
    {
        // 高对比度色盲友好调色板（参考 Wong 2011 Color Universal Design）
        private static readonly string[] Colors =
        {
            "#0072B2", // 蓝色
            "#E69F00", // 橙色
            "#009E73", // 绿色
            "#CC79A7", // 紫红色
            "#56B4E9", // 天蓝色
            "#D55E00", // 朱红色
            "#F0E442", // 黄色
            "#000000", // 黑色
            "#FF6F00", // 深橙
            "#00B8D4", // 青色
            "#76FF03", // 亮绿
            "#6200EA", // 深紫
        };

        /// <summary>
        /// 按类别名获取稳定的颜色（同名同色）。
        /// 用类别字符串哈希索引调色板，保证同名类别始终同色。
        /// </summary>
        public static string GetColorForLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return Colors[0];
            var hash = unchecked((uint)label.GetHashCode());
            // hash 为 uint，Colors.Length 为 int，二者运算会提升为 long，无法直接用作数组索引；
            // 这里显式将长度转为 uint 再将结果转回 int，保证编译通过且无符号取模正确。
            return Colors[(int)(hash % (uint)Colors.Length)];
        }

        /// <summary>
        /// 转换为 System.Windows.Media.Color
        /// </summary>
        public static System.Windows.Media.Color ToMediaColor(string hex)
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }

        /// <summary>
        /// 转换为 SixLabors.ImageSharp.Color
        /// </summary>
        public static SixLabors.ImageSharp.Color ToImageSharpColor(string hex)
        {
            return SixLabors.ImageSharp.Color.ParseHex(hex);
        }
    }
}
