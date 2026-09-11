using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace MainAPP.Models
{
    /// <summary>
    /// 应用用户模型，支持 MVVM 属性通知。
    /// 包含用户名、显示名、角色、启用状态和密码信息。
    /// 密码以明文存储（生产环境应使用哈希），验证时进行精确字符串比较。
    /// </summary>
    public partial class AppUser : ObservableObject
    {
        /// <summary>
        /// 登录用户名（唯一标识）
        /// </summary>
        [ObservableProperty]
        private string _username = string.Empty;

        /// <summary>
        /// 用户显示名称（用于UI展示）
        /// </summary>
        [ObservableProperty]
        private string _displayName = string.Empty;

        /// <summary>
        /// 用户角色：Guest(访客)、Operator(操作员)、Admin(管理员)
        /// </summary>
        [ObservableProperty]
        private UserRole _role = UserRole.Operator;

        /// <summary>
        /// 用户是否启用，禁用后无法登录
        /// </summary>
        [ObservableProperty]
        private bool _enabled = true;

        /// <summary>
        /// 用户密码（明文存储）
        /// </summary>
        // L103: setter 改为 private，强制通过 SetPassword 修改
        // JsonInclude: 允许 JSON 反序列化写入 private setter（LoadUsers 反序列化用户列表）
        [JsonInclude]
        public string Password { get; private set; } = string.Empty;

        /// <summary>
        /// 工厂方法：创建新用户实例
        /// </summary>
        /// <param name="username">登录用户名</param>
        /// <param name="displayName">显示名称，为空时使用用户名</param>
        /// <param name="password">密码</param>
        /// <param name="role">用户角色</param>
        /// <param name="enabled">是否启用</param>
        /// <returns>创建的用户实例</returns>
        public static AppUser Create(string username, string displayName, string password, UserRole role, bool enabled = true)
        {
            var user = new AppUser
            {
                Username = username.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim(),
                Role = role,
                Enabled = enabled
            };

            user.SetPassword(password);
            return user;
        }

        /// <summary>
        /// 设置密码
        /// </summary>
        /// <param name="password">新密码</param>
        public void SetPassword(string password)
        {
            Password = password ?? string.Empty;
        }

        /// <summary>
        /// 验证密码是否匹配（恒定时间比较，防范时序攻击）
        /// </summary>
        /// <param name="password">待验证的密码</param>
        /// <returns>密码匹配返回 true</returns>
        // H88a: 使用恒定时间比较，先比较长度（长度不同直接返回 false 是安全的，
        // 因为攻击者无法通过响应时间区分"长度不符"与"长度相符但内容不符"以外的信息），
        // 再用 XOR 累积差异，确保内容比较耗时与具体匹配位置无关。
        public bool VerifyPassword(string password)
        {
            var stored = Password;
            var input = password ?? string.Empty;

            if (stored.Length != input.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < stored.Length; i++)
            {
                diff |= stored[i] ^ input[i];
            }
            return diff == 0;
        }
    }
}