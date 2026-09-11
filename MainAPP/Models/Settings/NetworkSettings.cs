namespace MainAPP.Models
{
    /// <summary>
    /// 网络配置（由 Settings 持有实例）。
    /// </summary>
    public class NetworkSettings
    {
        /// <summary>
        /// 连通性检测 IP 地址（用于 Ping 检测）
        /// </summary>
        public string ConnectivityCheckIP { get; set; } = "127.0.0.1";

        /// <summary>
        /// 发送检测结果 IP 地址（用于发送检测结果到下游设备）
        /// </summary>
        public string DetectionResultSendIP { get; set; } = "127.0.0.1";

        /// <summary>
        /// 接收编码器报文端口（本地 UDP 监听端口，接收编码器报文）
        /// </summary>
        public int EncoderReceiverPort { get; set; } = 11301;

        /// <summary>
        /// 发送检测结果端口（向下游设备发送检测结果使用的 UDP 端口）
        /// </summary>
        public int DetectionResultSendPort { get; set; } = 2611;

        /// <summary>
        /// UDP 检测结果接收方协议标识："LL"（默认）或 "VGT"
        /// </summary>
        public string MessageReceiver { get; set; } = "LL";

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetries { get; set; } = 5;
    }
}
