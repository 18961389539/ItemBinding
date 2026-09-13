using MainAPP.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    #region MessageToVGT (数据模型)

    /// <summary>
    /// 表示要发送给 VGT 的单条消息。
    /// 包含位置 (X,Y)、绕 Z 轴的角度 (RZ) 以及条码信息。
    /// <para>该类型负责将自身序列化为 VGT 协议要求的字符串格式（通过 <see cref="ToString"/>）。</para>
    /// </summary>
    public class MessageToVGT
    {
        /// <summary>
        /// 创建一个新的 <see cref="MessageToVGT"/> 实例。
        /// </summary>
        public MessageToVGT() { }

        /// <summary>
        /// 绕 Z 轴的角度（度）。发送时会保留两位小数。
        /// </summary>
        public double RZ { get; set; }

        /// <summary>
        /// X 坐标（单位按 VGT 协议或上下文约定）。发送时会保留两位小数。
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y 坐标（单位按 VGT 协议或上下文约定）。发送时会保留两位小数。
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// 条码内容，如果没有读到条码则为空字符串或自定义占位文本（例如 "noread"）。
        /// </summary>
        public string Barcode { get; set; } = string.Empty;

        // L258: 条码截断长度阈值，超过此长度才执行 Remove 前缀处理
        private const int BarcodeTruncationThreshold = 30;

        /// <summary>
        /// 将当前消息按 VGT 要求的文本协议序列化。
        /// </summary>
        /// <returns>协议格式的字符串，例如："[X]12.34[Y]56.78[RZ]90.12[T]1[ID1]barcode"</returns>
        public override string ToString()
        {
            // L257: 使用 InvariantCulture 格式化数值，避免当前文化的小数分隔符破坏协议
            return string.Create(CultureInfo.InvariantCulture, $"[X]{Math.Round(X, 2)}[Y]{Math.Round(Y, 2)}[RZ]{Math.Round(RZ, 2)}[T]1[ID1]{Barcode}");
        }

        /// <summary>
        /// 序列化为雷雷协议格式：条码,X,Y,角度,
        /// </summary>
        public string ToStringLeiLei()
        {
            // M37: 不修改自身 Barcode 属性（ToString 类方法不应有副作用），使用局部变量
            // M161: 用 Math.Min 防止 BarcodeRemoveCount 超过条码长度导致 Remove 抛出异常
            var barcode = Barcode.Length > BarcodeTruncationThreshold
                ? Barcode.Remove(0, Math.Min(Settings.Instance.BarcodeRemoveCount, Barcode.Length))
                : Barcode;
            // L257: 使用 InvariantCulture 格式化数值，避免当前文化的小数分隔符破坏协议
            return string.Create(CultureInfo.InvariantCulture, $"{barcode},{Math.Round(X, 2)},{Math.Round(Y, 2)},{Math.Round(RZ, 2)},");
        }
    }

    #endregion

    #region 角度域工具（原 ToVGT 静态门面的纯函数部分，2026-09-13 精简）

    /// <summary>
    /// 角度域纯工具类。
    /// <para>2026-09-13：移除 ToVGT 静态门面的"实例桥接"双轨（Start/Stop/SendTo/状态委托
    /// 全部删除，无实际调用方）——通信一律走 DI 注入的 <see cref="IToVGTService"/>；
    /// 此处仅保留无副作用的 <see cref="ToRobotAngle"/> 纯函数，供全链路角度归一化共用。</para>
    /// </summary>
    public static class ToVGT
    {
        /// <summary>
        /// 把任意来源的角度归一化到系统规范域/机器人协议域 (-180,180]。
        /// 2026-09-05 起 DbModel.Angle 落库已直接为此域，本方法为幂等换算，
        /// 保留作防御：历史脏数据/CSV 导入越界值（如旧 [0,360) 的 355、405、负值）仍能安全落域。
        /// 规则：大于 180° 的角减 360°（355→-5、270→-90、181→-179），其余保持不变；
        /// 先对输入取模 360 并负值归一，保证任意来源值（含越界/负值）都映射到 (-180,180]。
        /// </summary>
        /// <param name="angle">任意角度值（度），通常来自 AngleTracker 的 (-180,180] 输出或旧域数据。</param>
        /// <returns>(-180,180] 区间内的角度（度）。</returns>
        public static double ToRobotAngle(double angle)
        {
            var a = angle % 360.0;
            if (a < 0) a += 360.0;   // 输入为负（如 -5）→ 355 → 再按规则转回 -5
            return a > 180.0 ? a - 360.0 : a;
        }
    }

    #endregion
}
