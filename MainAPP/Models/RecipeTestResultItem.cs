using System;
using System.Windows.Media;

namespace MainAPP.Models
{
    /// <summary>
    /// 配方页「测试推理」单次结果记录（供配方窗口「推理结果」页展示）。
    /// 坐标口径与主流程 DetectionRecordService 一致：产品分割最小外接旋转矩形中心 → 三点标定转世界坐标(mm) → +配方平移补偿；
    /// 配方未完成标定时 WorldX/WorldY 为 null，列表退化为原图像素坐标并明确提示"未标定"。
    /// 角度已含 OffsetAngle；已标定且角度输出成功时为世界坐标系真实角度。
    /// </summary>
    /// <param name="Time">推理完成时间。</param>
    /// <param name="WorldX">世界坐标 X(mm)；未标定或该次无产品时为 null。</param>
    /// <param name="WorldY">世界坐标 Y(mm)；未标定或该次无产品时为 null。</param>
    /// <param name="PixelX">产品最小外接矩形中心的原图像素 X（与发送给机器人坐标同参考点）。</param>
    /// <param name="PixelY">产品最小外接矩形中心的原图像素 Y（与发送给机器人坐标同参考点）。</param>
    /// <param name="Angle">角度(°)（已含 OffsetAngle，且已换算到机器人发送域 (-180,180]，
    /// 与 ToVGTService 发送给机器人的 RZ 同口径）；未启用/角度模型未输出有效结果时为 null。</param>
    /// <param name="TargetCount">边缘检测目标数。</param>
    /// <param name="CostMs">推理耗时(ms)。</param>
    /// <param name="Remark">备注：坐标/角度有效性说明或失败原因。</param>
    public sealed record RecipeTestResultItem(
        DateTime Time,
        double? WorldX,
        double? WorldY,
        double PixelX,
        double PixelY,
        double? Angle,
        int TargetCount,
        double CostMs,
        string Remark)
    {
        /// <summary>时间列文本（时分秒毫秒）</summary>
        public string TimeText => Time.ToString("HH:mm:ss.fff");

        /// <summary>坐标列文本：已标定→世界 mm；否则原图像素并标注未标定</summary>
        public string CoordText => WorldX is { } x && WorldY is { } y
            ? $"X={x:F2}  Y={y:F2} mm"
            : (TargetCount > 0
                ? $"X={PixelX:F1}  Y={PixelY:F1} px（未标定）"
                : "无产品");

        /// <summary>角度列文本</summary>
        public string AngleText => Angle is { } a ? $"{a:F1}°" : "—";

        /// <summary>产品最小外接矩形中心原图像素文本（"X=…, Y=… px"）</summary>
        public string PixelText => $"X={PixelX:F0}, Y={PixelY:F0} px";

        /// <summary>耗时列文本</summary>
        public string CostText => $"{CostMs:F0} ms";

        /// <summary>
        /// 该次推理的可视化图（叠加分割/角度/抓取点标记的显示图，冻结后不可变，可安全跨线程引用）。
        /// 供「推理结果」页点击卡片放大查看；实例由 VM 在写入时冻结并随集合保留，列表清空后随 GC 释放。
        /// </summary>
        public ImageSource? Snapshot { get; init; }
    }
}
