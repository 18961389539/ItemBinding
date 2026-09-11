namespace MainAPP.Models
{
    /// <summary>
    /// 扫描枪连接状态枚举（P0-2: 主页相机连接状态指示）。
    /// </summary>
    public enum ScannerConnectionState
    {
        /// <summary>未连接</summary>
        Disconnected,
        /// <summary>重连中</summary>
        Connecting,
        /// <summary>已连接</summary>
        Connected
    }
}
