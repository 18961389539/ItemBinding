using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

// VSTHRD001: 使用 Dispatcher.BeginInvoke 切换到 UI 线程是 WPF 标准模式，无需 JoinableTaskFactory
// （与项目内 LogService/HomeViewModel 等文件级抑制一致）
#pragma warning disable VSTHRD001

namespace MainAPP.Services
{
    /// <summary>
    /// 单次实际发送给机器人的产品记录（主页"发送"面板一行）。
    /// 2026-09-07: 面板只展示"有产品（真实发出 UDP）"的信息，被角度未知过滤跳过的产品不入列。
    /// </summary>
    public sealed class RobotSendEntry
    {
        public RobotSendEntry(DateTime time, uint frame, uint encoder, string barcode, double x, double y, double angle)
        {
            Time = time;
            Frame = frame;
            Encoder = encoder;
            Barcode = barcode;
            X = x;
            Y = y;
            Angle = angle;
        }

        /// <summary>发送动作发生时间（本机时间，与日志一致）。</summary>
        public DateTime Time { get; }

        /// <summary>触发发送的帧号。</summary>
        public uint Frame { get; }

        /// <summary>触发发送时的编码器计数值。</summary>
        public uint Encoder { get; }

        /// <summary>条码字符串（noread=无条码）。</summary>
        public string Barcode { get; }

        /// <summary>产品世界坐标 X（mm）。</summary>
        public double X { get; }

        /// <summary>产品世界坐标 Y（mm）。</summary>
        public double Y { get; }

        /// <summary>产品角度（(-180,180]，已归一化，与发送值一致）。</summary>
        public double Angle { get; }

        /// <summary>角度显示：与发送值一致保留 2 位小数（Round(RZ,2) 落协议）；异常负值显示"未知"。</summary>
        public string AngleDisplay => Angle < -180 ? "未知" : $"{Angle:F2}°";
    }

    /// <summary>
    /// 2026-09-07: 发送给机器人（VGT/LL）的产品监视器（静态单例）。
    /// 由 ToVGTService.SendTo 在每次发送后调用 RecordBatch 记录实际发出的产品。
    /// 只记录"有产品"的发送：无产品（丢帧空包）或被角度未知过滤跳过的产品都不入列，
    /// 因此面板每一行都对应一条机器人真实收到的产品数据。
    /// 线程安全：SendTo 在推理后台线程调用，所有集合变更 marshal 到 UI Dispatcher。
    /// 容量：超 MaxEntries 从头裁剪（FIFO），保留最近记录。
    /// </summary>
    public sealed class RobotSendMonitor
    {
        /// <summary>单例（HomeView XAML 以 x:Static 绑定 Sends）。</summary>
        public static RobotSendMonitor Instance { get; } = new();

        private RobotSendMonitor() { }

        /// <summary>面板最多保留的记录条数（FIFO 裁剪）。</summary>
        public const int MaxEntries = 200;

        /// <summary>UI 集合：主页"发送"Tab 的 ItemsSource。仅 UI 线程修改。</summary>
        public ObservableCollection<RobotSendEntry> Sends { get; } = new();

        /// <summary>
        /// 记录一次实际发送（只记录真实发出的产品；列表为空则忽略，避免空帧刷屏）。
        /// 任一线程可调用。
        /// </summary>
        /// <param name="frame">帧号。</param>
        /// <param name="encoder">编码器计数值。</param>
        /// <param name="sent">本次已通过 UDP 发出的产品列表（条码/X/Y/角度）。</param>
        public void RecordBatch(uint frame, uint encoder, IReadOnlyList<MessageToVGT>? sent = null)
        {
            int sentCount = sent?.Count ?? 0;
            if (sentCount == 0)
            {
                return; // 无实际发出的产品（空帧/全被过滤）不记录
            }

            var now = DateTime.Now;
            var entries = new List<RobotSendEntry>(sentCount);
            foreach (var m in sent!)
            {
                entries.Add(new RobotSendEntry(now, frame, encoder,
                    m.Barcode ?? "noread", m.X, m.Y, m.RZ));
            }

            // marshal 到 UI 线程后再改 ObservableCollection；BeginInvoke 保序执行
            Post(() =>
            {
                foreach (var e in entries)
                {
                    Sends.Add(e);
                }

                TrimExcess();
            });
        }

        /// <summary>清空面板（菜单"清空记录"）。</summary>
        public void Clear()
        {
            Post(() =>
            {
                Sends.Clear();
            });
        }

        private void TrimExcess()
        {
            // 逐条 RemoveAt(0) 在超限量大时 O(n²)；超限量一般很小（单次多记录几条），可接受
            while (Sends.Count > MaxEntries)
            {
                Sends.RemoveAt(0);
            }
        }

        private static void Post(Action action)
        {
            // 全限定 System.Windows.Application：命名空间 MainAPP.Application 会遮蔽 using 引入的 Application
            var application = System.Windows.Application.Current;
            if (application?.Dispatcher is null || application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (application.Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            // 与 LogService.AddLog 同款：委托内包 try-catch + "_ ="观察 BeginInvoke 返回值，
            // 规避 vs-threading 分析器 VSTHRD001/VSTHRD110 且不因回调异常影响调度器
            _ = application.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"RobotSendMonitor UI 回调异常: {ex}");
                }
            });
        }
    }
}
