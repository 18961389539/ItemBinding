using System;

namespace MainAPP.Services
{
    /// <summary>
    /// 发送给机器人的角度域（2026-09-17 新增）。
    ///
    /// <para><b>为什么会有两种域</b>：部分机械手的腕关节只能旋转 ±90°。而产品朝向是一个整圈量，
    /// 若把 (<see cref="Signed180"/> 域) 的 150° 直接发过去，机械手转不过去。
    /// 对**180° 对称**的产品/夹爪，150° 与 −30° 物理上等价，因此可以发后者——
    /// 这就是 <see cref="Folded90"/> 域做的事。</para>
    ///
    /// <para><b>⚠️ 折叠的前提</b>：产品（或夹爪）必须 180° 对称。若产品有明确头尾、
    /// 机械手也没有"换向/翻转"动作，则**不要**用本域——折叠会把"产品装反 180°"伪装成正常角度，
    /// 掩盖上料问题且事后无法从数据里区分。那种场景应改为越界报警。</para>
    /// </summary>
    public enum AngleDomain
    {
        /// <summary>(-180, 180]，覆盖整圈。系统内部规范域与该域一致（默认）。</summary>
        Signed180,

        /// <summary>(-90, 90]，把超出部分折叠 180°。仅适用于 180° 对称的产品/夹爪。</summary>
        Folded90,
    }

    /// <summary>
    /// 角度域换算（纯函数，便于单测）。全链路只有这一个出站换算入口，
    /// 内置 VGT/LL 报文与自定义协议模板都走它，避免两条路径行为不一致。
    ///
    /// <para><b>内部规范域不变</b>：落库 <c>DbModel.Angle</c>、画面标记、抓取点计算、
    /// 头尾消歧（<c>IsHeadOppositeDegrees</c>）全部继续使用 (-180,180]。
    /// 本换算只作用于**发送前**——否则"产品装反"与"正常"会在数据里变得无法区分，
    /// 且画面上的角度会与实际发送值不一致。</para>
    /// </summary>
    public static class AngleDomainConverter
    {
        /// <summary>配置键：(-180, 180]。</summary>
        public const string Signed180Key = "Signed180";

        /// <summary>配置键：(-90, 90]。</summary>
        public const string Folded90Key = "Folded90";

        /// <summary>
        /// 解析配置值。<b>未知/空值一律回退 <see cref="AngleDomain.Signed180"/></b>——
        /// 发送域这类参数，配置损坏时退回"与历史行为一致"比退回"新行为"安全。
        /// </summary>
        public static AngleDomain Parse(string? value) =>
            string.Equals(value?.Trim(), Folded90Key, StringComparison.OrdinalIgnoreCase)
                ? AngleDomain.Folded90
                : AngleDomain.Signed180;

        /// <summary>域的可读描述（日志、UI 提示用）。</summary>
        public static string Describe(AngleDomain domain) =>
            domain == AngleDomain.Folded90 ? "-90 ~ 90（折叠 180°）" : "-180 ~ 180";

        /// <summary>
        /// 把内部规范域的角度换算到目标发送域。
        ///
        /// <para><b>哨兵保护</b>：<c>-9999</c>（<see cref="AngleTracker.UnknownAngle"/>）表示"角度未知"，
        /// 它**不是角度值**，必须原样返回——若按数值参与运算，`-9999 % 360 = -279 → +360 = 81°`，
        /// 会变成一个看起来完全正常的角度被发出去，比不换算危险得多。
        /// 调用方仍应在换算前按业务规则拒发（见 <c>ToVGTService</c>）。</para>
        /// </summary>
        /// <param name="angle">内部规范域角度（(-180,180]）或哨兵值。</param>
        /// <param name="domain">目标域。</param>
        /// <returns>目标域内的角度；输入为哨兵时原样返回。</returns>
        public static double ToDomain(double angle, AngleDomain domain)
        {
            if (angle == AngleTracker.UnknownAngle || double.IsNaN(angle))
            {
                return angle;
            }

            var signed = ToVGT.ToRobotAngle(angle);
            return domain == AngleDomain.Folded90 ? FoldTo90(signed) : signed;
        }

        /// <summary>
        /// 折叠到 (-90, 90]：超出 ±90 的部分减去/加上 180°。
        /// 边界：90→90、90.5→-89.5、-90→90、-90.5→89.5、180→0。
        /// </summary>
        private static double FoldTo90(double signed180)
        {
            if (signed180 > 90.0)
            {
                return signed180 - 180.0;
            }

            return signed180 <= -90.0 ? signed180 + 180.0 : signed180;
        }
    }
}
