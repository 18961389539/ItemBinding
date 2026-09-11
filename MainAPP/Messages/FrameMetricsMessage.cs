namespace MainAPP.Messages
{
    /// <summary>帧计数指标消息，由 HomeViewModel 发送给 MainViewModel。</summary>
    /// <param name="ProcessedFrameCount">已处理帧计数（用于 FPS 计算）。</param>
    /// <param name="DropFrameCount">累计丢帧计数。</param>
    public record FrameMetricsMessage(long ProcessedFrameCount, int DropFrameCount);
}
