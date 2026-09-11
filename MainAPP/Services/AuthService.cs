using MainAPP.Models;
using System.Collections.ObjectModel;
using System.IO;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// 用户认证服务（单例）。
    /// 管理用户列表的加载、保存、认证和会话状态。
    /// 用户数据以 JSON 文件持久化存储在 Saves/Security/users.json。
    /// 确保系统中始终存在 Admin 和 Operator 两个默认角色账户。
    /// </summary>
    public sealed class AuthService : IAuthService
    {
        private static readonly Lazy<AuthService> InstanceLazy = new(() => new AuthService());

        // L106: 复用 JsonSerializerOptions 实例，避免每次 Save 都重新分配
        private static readonly JsonSerializerOptions s_jsonOpts = new() { WriteIndented = true };

        /// <summary>用户数据文件的存储路径</summary>
        private readonly string _storePath;

        /// <summary>单例实例</summary>
        public static AuthService Instance => InstanceLazy.Value;

        /// <summary>所有用户列表（支持 WPF 绑定）</summary>
        public ObservableCollection<AppUser> Users { get; } = [];

        /// <summary>当前已登录的用户，未登录时为 null</summary>
        public AppUser? CurrentUser { get; private set; }

        /// <summary>会话变更事件，登录/登出时触发</summary>
        public event EventHandler? SessionChanged;

        private AuthService()
        {
            _storePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves", "Security", "users.json");
            Reload();
        }

        /// <summary>
        /// 从文件重新加载用户列表，确保默认用户存在，并重置当前会话
        /// </summary>
        public void Reload()
        {
            Users.Clear();
            foreach (var user in LoadUsers())
            {
                Users.Add(user);
            }

            EnsureDefaultUsers();
            CurrentUser = null;
            Save();
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 认证用户登录
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        /// <param name="user">认证成功时输出用户对象，失败时为 null</param>
        /// <returns>认证成功返回 true</returns>
        public bool Authenticate(string username, string password, out AppUser? user)
        {
            // M172: 提取 username?.Trim() 为局部变量，避免在 LINQ 谓词中重复调用
            var trimmedUsername = username?.Trim();
            user = Users.FirstOrDefault(x => string.Equals(x.Username, trimmedUsername, StringComparison.OrdinalIgnoreCase));
            if (user is null || !user.Enabled || !user.VerifyPassword(password))
            {
                user = null;
                return false;
            }

            CurrentUser = user;
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// 登出当前用户，清除会话状态
        /// </summary>
        public void Logout()
        {
            CurrentUser = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 启动自动登录：直接以系统内的管理员身份建立会话（免登录窗口）。
        /// 优先选择与 Settings.UserName 同名的已启用管理员，否则取第一个已启用管理员。
        /// 用于"默认管理员登录、全部权限"的产线场景。
        /// </summary>
        /// <returns>是否成功建立管理员会话；无管理员时返回 false</returns>
        public bool AutoLoginDefaultAdmin()
        {
            var adminName = Settings.Instance.UserName;
            var admin =
                // 优先：与 Settings 管理员同名的已启用管理员
                Users.FirstOrDefault(x => x.Role == UserRole.Admin && x.Enabled
                    && string.Equals(x.Username, adminName, StringComparison.OrdinalIgnoreCase))
                // 其次：任意已启用管理员
                ?? Users.FirstOrDefault(x => x.Role == UserRole.Admin && x.Enabled)
                // 兜底：任意管理员（即使被禁用也尝试，确保有会话可建立）
                ?? Users.FirstOrDefault(x => x.Role == UserRole.Admin);
            if (admin is null)
            {
                return false;
            }

            CurrentUser = admin;
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// 确保系统中存在指定用户名和密码的管理员账户。
        /// 如果不存在则创建，如果已存在则更新其凭据。
        /// </summary>
        public void EnsureAdminAccount(string username, string password)
        {
            // M125: 校验 username 非空，避免后续 username.Trim() 抛 NRE 难以定位
            ArgumentNullException.ThrowIfNull(username);
            // M173: 校验 password 非空
            ArgumentNullException.ThrowIfNull(password);

            var adminUser = Users.FirstOrDefault(x => x.Role == UserRole.Admin);
            if (adminUser is null)
            {
                adminUser = AppUser.Create(username, "管理员", password, UserRole.Admin, true);
                Users.Insert(0, adminUser);
                // M124: 创建分支也需持久化，与更新分支保持一致
                Save();
            }
            else
            {
                adminUser.Username = username.Trim();
                adminUser.DisplayName = string.IsNullOrWhiteSpace(adminUser.DisplayName) ? "管理员" : adminUser.DisplayName;
                adminUser.Role = UserRole.Admin;
                adminUser.Enabled = true;
                adminUser.SetPassword(password);
                // M124: 更新分支也需持久化，与创建分支保持一致，否则 Reload 后凭据丢失
                Save();
            }
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 重置用户列表为默认账户（一个管理员 + 一个操作员），并保存
        /// </summary>
        public void ResetDefaults(string adminUsername, string adminPassword)
        {
            // M173: 校验 password 非空
            ArgumentNullException.ThrowIfNull(adminPassword);
            Users.Clear();
            Users.Add(AppUser.Create(adminUsername, "管理员", adminPassword, UserRole.Admin, true));
            Users.Add(AppUser.Create("operator", "操作员", string.Empty, UserRole.Operator, true));
            Save();
            CurrentUser = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 将用户列表序列化为 JSON 并写入文件
        /// </summary>
        // L410a: Save() 与 SaveAsync() 逻辑高度重复（仅同步/异步 IO 差异）。
        // 暂不抽取共享核心方法，因为抽取需引入共享缓冲/流抽象，改动较大且当前调用点有限，留待后续统一重构。
        public void Save()
        {
            // L387: 将 tempFile 声明移至 try 外，便于 catch 中清理
            var tempFile = _storePath + ".tmp";
            try
            {
                var directory = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var snapshot = Users.ToList();
                string json = JsonSerializer.Serialize(snapshot, s_jsonOpts);
                // L15: 原子写入，先写临时文件再替换，避免崩溃导致用户数据损坏
                File.WriteAllText(tempFile, json);
                if (File.Exists(_storePath))
                {
                    File.Replace(tempFile, _storePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempFile, _storePath);
                }
            }
            catch (Exception ex)
            {
                // L387: 清理残留的临时文件，避免占用
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch (Exception cleanupEx) { System.Diagnostics.Trace.WriteLine($"清理临时文件失败: {tempFile}, {cleanupEx}"); }
                LogService.Instance.Error($"保存用户数据失败: {ex}");
            }
        }

        /// <summary>
        /// 异步将用户列表序列化为 JSON 并写入文件
        /// </summary>
        public async Task SaveAsync()
        {
            var tempFile = _storePath + ".tmp";
            try
            {
                var directory = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var snapshot = Users.ToList();
                string json = JsonSerializer.Serialize(snapshot, s_jsonOpts);
                // L15: 原子写入，先写临时文件再替换，避免崩溃导致用户数据损坏
                // M291: 使用 File.WriteAllTextAsync 异步写入
                await File.WriteAllTextAsync(tempFile, json).ConfigureAwait(false);
                if (File.Exists(_storePath))
                {
                    File.Replace(tempFile, _storePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempFile, _storePath);
                }
            }
            catch (Exception ex)
            {
                // L387: 清理残留的临时文件，避免占用
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch (Exception cleanupEx) { System.Diagnostics.Trace.WriteLine($"清理临时文件失败: {tempFile}, {cleanupEx}"); }
                LogService.Instance.Error($"保存用户数据失败: {ex}");
            }
        }

        /// <summary>
        /// 从 JSON 文件加载用户列表
        /// </summary>
        private IEnumerable<AppUser> LoadUsers()
        {
            if (!File.Exists(_storePath))
            {
                return [];
            }

            try
            {
                var json = File.ReadAllText(_storePath);
                return JsonSerializer.Deserialize<List<AppUser>>(json) ?? [];
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"加载用户数据失败: {ex}");
                return [];
            }
        }

        // L409a: LoadUsersAsync() 为死代码（无任何调用方，Reload 仅使用同步 LoadUsers），已删除。

        /// <summary>
        /// 确保系统中至少存在一个 Admin 和一个 Operator 账户，
        /// 不存在时自动创建默认账户
        /// </summary>
        private void EnsureDefaultUsers()
        {
            var adminUsername = Settings.Instance.UserName;
            var adminPassword = Settings.Instance.Password;

            if (!Users.Any(x => x.Role == UserRole.Admin))
            {
                Users.Insert(0, AppUser.Create(adminUsername, "管理员", adminPassword, UserRole.Admin, true));
            }

            if (!Users.Any(x => x.Role == UserRole.Operator))
            {
                Users.Add(AppUser.Create("operator", "操作员", string.Empty, UserRole.Operator, true));
            }
        }
    }
}
