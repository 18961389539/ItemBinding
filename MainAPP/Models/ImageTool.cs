using System;

namespace MainAPP.Models
{
    /// <summary>
    /// 图像采集工具配置。
    /// 配置扫码枪的图像采集参数，包括曝光时间、增益等，
    /// 这些参数会在配方切换时自动应用到扫码枪设备。
    /// L409c: ExposureTime(200) 和 Gain(1) 为魔法数字默认值，基于设备调试经验设定。
    /// 暂不提取为常量（改动小但涉及配方序列化兼容性验证），后续统一规划。
    /// </summary>
    public class ImageTool
    {
        /// <summary>
        /// 图像文件目录路径（当不从扫码枪读取时使用）
        /// </summary>
        public string DirectoryPath { get; set; } = string.Empty;

        /// <summary>
        /// 是否从扫码枪实时读取图像；false 则从本地目录读取
        /// </summary>
        public bool ReadFromScanner { get; set; } = false;

        /// <summary>
        /// 从目录读取时跳过的帧数（用于去除开头不稳定的帧）
        /// </summary>
        public int RemoveCount { get; set; } = 0;

        /// <summary>
        /// 扫码枪曝光时间（微秒），默认200μs
        /// </summary>
        public float ExposureTime { get; set; } = 200;

        /// <summary>
        /// 扫码枪增益倍数，默认1.0
        /// </summary>
        public float Gain { get; set; } = 1;
    }
}
