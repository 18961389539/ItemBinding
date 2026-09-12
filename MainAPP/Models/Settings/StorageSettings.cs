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
        /// 检测结果图像的保存目录
        /// </summary>
        public string PicturesSaveFolder { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves", "Pictures");
    }
}
