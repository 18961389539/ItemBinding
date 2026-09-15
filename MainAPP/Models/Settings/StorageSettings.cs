using MainAPP.Services;
using System.IO;

namespace MainAPP.Models
{
    /// <summary>
    /// 文件保存策略配置（由 Settings 持有实例）。
    /// </summary>
    public class StorageSettings
    {
        /// <summary>
        /// 是否保存绘制了检测框和条码标注的结果图像
        /// </summary>
        public bool IsSaveDraw { get; set; } = true;

        /// <summary>
        /// 是否同时保存原始未标注的源图像
        /// </summary>
        public bool IsSaveSource { get; set; } = false;

        /// <summary>
        /// 最近N天内保存的图片保留天数，超过此天数的图片将被自动清理
        /// </summary>
        public int MinRecentDays { get; set; } = 3;

        /// <summary>
        /// 检测结果图像的保存目录。
        /// 2026-09-15: 默认值跟随统一数据根（DataPaths）；已保存的设置文件里若含旧的绝对路径，
        /// 由设置页迁移逻辑处理（见 SettingsViewModel）。
        /// </summary>
        public string PicturesSaveFolder { get; set; } = DataPaths.PicturesDir;
    }
}
