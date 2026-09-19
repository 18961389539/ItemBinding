using MainAPP.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// VGT（视觉引导系统）通信服务接口。
    /// 负责 UDP 消息发送、编码器监听、连通性监控。
    /// 实现 <see cref="IAsyncDisposable"/> 以支持异步资源释放。
    /// </summary>
    public interface IToVGTService : IAsyncDisposable
    {
        /// <summary>
        /// 启动服务：创建 UDP 客户端、启动连通性监控和编码器监听后台任务。
        /// </summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// 相邻两次收到编码器报文的时间间隔（毫秒）。
        /// </summary>
        long Speed { get; }

        /// <summary>
        /// 机器人是否可达（Ping 成功）。
        /// </summary>
        bool IsRobotOnLive { get; }

        /// <summary>
        /// VGT 是否可达（Ping 成功）。
        /// </summary>
        bool IsVGTOnLive { get; }

        /// <summary>
        /// 最近一次成功接收编码器报文的时间（本地时间），为 null 表示尚未收到任何报文。
        /// </summary>
        DateTime? LastEncoderReceiveTime { get; }

        /// <summary>
        /// 最近一次接收到的编码器原始值（counts），为 null 表示尚未收到任何报文。
        /// REVIEW(2026-08-05): 供 UI 状态栏显示当前编码器值。
        /// </summary>
        uint? LastEncoderValue { get; }

        /// <summary>
        /// 查找并移除最接近但不晚于指定时间点的编码器记录。
        /// </summary>
        /// <returns>Encoder/Time 为命中的记录（无命中时 0/MinValue）；Stale = 记录已超龄（XY 与编码器错位约一个触发间隔），发送路径据此可整条拒发。</returns>
        (uint Encoder, DateTime Time, bool Stale) MostRecentDateEncode(DateTime searchTime);

        /// <summary>
        /// 将检测结果转换为消息并通过 UDP 发送给 VGT。
        /// </summary>
        /// <param name="models">要发送的数据库模型集合。</param>
        /// <param name="scannerResult">当前帧结果（提供编码器值等上下文）。</param>
        /// <param name="receiver">接收方标识（"VGT" 或 "LL"）。</param>
        void SendTo(IEnumerable<DbModel> models, FrameResult scannerResult, string receiver = "LL");
    }
}
