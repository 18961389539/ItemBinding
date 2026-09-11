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

    #region ToVGT 静态门面（向后兼容桥接）

    /// <summary>
    /// 与 VGT（视觉引导系统）通信的静态门面类。
    /// 内部委托给 <see cref="ToVGTService"/> 实例。
    ///
    /// 此为过渡方案：所有新代码应通过 <see cref="IToVGTService"/> 接口使用。
    /// 该类将在 DI 改造完成后的 Stage 4 中移除。
    /// </summary>
    public static class ToVGT
    {
        private static volatile ToVGTService? _service;

        /// <summary>
        /// 设置底层服务实例（由 DI/App 启动时调用）。
        /// 仅在服务为 null 时设置，防止被覆盖。
        /// </summary>
        internal static void SetService(ToVGTService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            Interlocked.CompareExchange(ref _service, service, null);
        }

        private static ToVGTService Service =>
            _service ?? throw new InvalidOperationException("ToVGT 尚未初始化，请先调用 ToVGT.SetService() 。");

        /// <summary>表示机器人是否可达（Ping 成功）。</summary>
        public static bool IsRobotOnLive => Service.IsRobotOnLive;

        /// <summary>表示 VGT 是否可达（Ping 成功）。</summary>
        public static bool IsVGTOnLive => Service.IsVGTOnLive;

        /// <summary>相邻两次收到编码器报文的时间间隔（毫秒）。</summary>
        public static long Speed => Service.Speed;

        /// <summary>最近一次成功接收编码器报文的时间（本地时间）。</summary>
        public static DateTime? LastEncoderReceiveTime => Service.LastEncoderReceiveTime;

        /// <summary>启动 ToVGT 服务。</summary>
        public static void Start()
        {
            // 同步包装异步 StartAsync，使用 GetAwaiter().GetResult() 避免 sync-over-async 死锁
            // 该方法在 App.OnStartup 的同步阶段调用，UI 线程无其他 async 操作，安全
#pragma warning disable VSTHRD002
            Service.StartAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }

        /// <summary>异步停止 ToVGT 服务。</summary>
        public static Task StopAsync() => Service.DisposeAsync().AsTask();

        /// <summary>查找并移除最接近但不晚于指定时间点的编码器记录。</summary>
        public static (uint Encoder, DateTime Time) MostRecentDateEncode(DateTime searchTime)
            => Service.MostRecentDateEncode(searchTime);

        /// <summary>将检测结果发送给 VGT。</summary>
        public static void SendTo(IEnumerable<DbModel> models, FrameResult scannerResult, string receiver = "LL")
            => Service.SendTo(models, scannerResult, receiver);

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
