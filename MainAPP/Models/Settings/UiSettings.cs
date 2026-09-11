namespace MainAPP.Models
{
    /// <summary>
    /// 界面配置（由 Settings 持有实例）。
    /// </summary>
    public class UiSettings
    {
        /// <summary>
        /// 主窗口宽度
        /// </summary>
        public int WindowWidth { get; set; } = 1200;

        /// <summary>
        /// 主窗口高度
        /// </summary>
        public int WindowHeight { get; set; } = 800;

        /// <summary>
        /// 界面语言（如 "zh-CN"、"en-US"）
        /// </summary>
        public string Language { get; set; } = "zh-CN";

        /// <summary>
        /// 界面主题（"Light" 或 "Dark"）
        /// </summary>
        public string Theme { get; set; } = "Light";

        /// <summary>
        /// 主窗口标题
        /// </summary>
        public string WindowTitle { get; set; } = "ABB机器人";
    }
}
