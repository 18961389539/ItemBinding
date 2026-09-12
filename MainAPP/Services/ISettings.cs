namespace MainAPP.Services
{
    /// <summary>
    /// 算法参数设置接口。
    /// 从 <see cref="Models.Settings"/> 中提取，供 ProductTracker 和 AngleTracker 通过构造函数注入使用。
    /// </summary>
    public interface IAlgorithmSettings
    {
        bool DedupEnabled { get; }
        bool DedupByEncoder { get; }
        string DedupTrackAxis { get; }
        double DedupPositionThreshold { get; }
        double DedupAngleThreshold { get; }

        /// <summary>
        /// 跟踪项过期时间（秒）：某产品最后一次成像后超过该时长未被再次拍到，跟踪状态即清除。
        /// ★ 必须大于相邻两次触发的最大时间间隔 = 触发间隔(mm) ÷ 最慢线速(mm/s)，建议 3 倍余量。
        ///   例：触发间隔 200mm、最慢线速 20mm/s → 间隔 10s → 本值至少 30。
        ///   取小了慢速产线会重复计数（同一产品每帧被判新品）；取大了仅多占少量内存，无正确性风险。
        /// </summary>
        int TrackerExpireSeconds { get; }
    }

    /// <summary>
    /// 网络参数设置接口。
    /// 从 <see cref="Models.Settings"/> 中提取，供 ToVGTService 通过构造函数注入使用。
    /// </summary>
    public interface INetworkSettings
    {
        string ConnectivityCheckIP { get; }
        string DetectionResultSendIP { get; }
        int EncoderReceiverPort { get; }
        int DetectionResultSendPort { get; }
        string MessageReceiver { get; }
    }

    /// <summary>
    /// 存储参数设置接口。
    /// 从 <see cref="Models.Settings"/> 中提取，供 FrameRenderer / HomeViewModel 通过构造函数注入使用。
    /// </summary>
    public interface IStorageSettings
    {
        bool IsSaveDraw { get; }
        bool IsSaveSource { get; }
        int MinRecentDays { get; }
        string PicturesSaveFolder { get; }
    }
}
