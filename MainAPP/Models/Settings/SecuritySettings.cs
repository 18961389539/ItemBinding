namespace MainAPP.Models
{
    /// <summary>
    /// 安全配置（由 Settings 持有实例）。
    /// </summary>
    public class SecuritySettings
    {
        /// <summary>
        /// 管理员用户名
        /// </summary>
        public string UserName { get; set; } = "abb";

        /// <summary>
        /// 管理员密码
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// 登录超时时间（毫秒），超时后自动登出；300_000 = 5分钟
        /// </summary>
        public int ExistLoginTimeout { get; set; } = 300_000;

        /// <summary>
        /// 当前使用的配方名称
        /// M336a: 默认值改为 string.Empty，避免硬编码特定配方名（原"凉拌"为业务特定值）
        /// </summary>
        public string CurrentRecipeName { get; set; } = string.Empty;
    }
}
