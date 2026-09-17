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

        /// <summary>
        /// 是否启用离线授权门禁（默认启用）。
        /// <para>2026-09-16: 原先由 <c>App.xaml.cs</c> 的编译期常量 <c>ActivationRequired</c> 控制，
        /// 现场为规避"换 Windows 账户要求重新激活"把它置为 false —— 但常量形态意味着
        /// "临时停用"实为永久停用：恢复门禁必须改源码 + 重新编译 + 重新发布。
        /// 现改为运行期配置项：改本项（或环境变量 <c>ITEMBINDING_LICENSE_BYPASS</c>）即可切换，无需重编译。</para>
        /// <para>优先级见 <see cref="MainAPP.Services.LicenseService.IsGateEnabled"/>。</para>
        /// </summary>
        public bool LicenseRequired { get; set; } = true;
    }
}
