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
