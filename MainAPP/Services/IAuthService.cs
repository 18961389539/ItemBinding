using MainAPP.Models;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// 用户认证服务抽象。当前实现为单例 <see cref="AuthService"/>，
    /// 为后续 ViewModel 改造为构造函数注入做准备。
    /// 保留 <see cref="AuthService.Instance"/> 静态属性以向后兼容现有调用。
    /// </summary>
    public interface IAuthService
    {
        /// <summary>所有用户列表（支持 WPF 绑定）</summary>
        ObservableCollection<AppUser> Users { get; }

        /// <summary>当前已登录的用户，未登录时为 null</summary>
        AppUser? CurrentUser { get; }

        /// <summary>会话变更事件，登录/登出时触发</summary>
        event EventHandler? SessionChanged;

        /// <summary>
        /// 从文件重新加载用户列表，确保默认用户存在，并重置当前会话
        /// </summary>
        void Reload();

        /// <summary>
        /// 认证用户登录
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        /// <param name="user">认证成功时输出用户对象，失败时为 null</param>
        /// <returns>认证成功返回 true</returns>
        bool Authenticate(string username, string password, out AppUser? user);

        /// <summary>登出当前用户，清除会话状态</summary>
        void Logout();

        /// <summary>
        /// 确保系统中存在指定用户名和密码的管理员账户。
        /// 如果不存在则创建，如果已存在则更新其凭据。
        /// </summary>
        void EnsureAdminAccount(string username, string password);

        /// <summary>
        /// 重置用户列表为默认账户（一个管理员 + 一个操作员），并保存
        /// </summary>
        void ResetDefaults(string adminUsername, string adminPassword);

        /// <summary>将用户列表序列化为 JSON 并写入文件</summary>
        void Save();

        /// <summary>异步将用户列表序列化为 JSON 并写入文件</summary>
        Task SaveAsync();
    }
}
