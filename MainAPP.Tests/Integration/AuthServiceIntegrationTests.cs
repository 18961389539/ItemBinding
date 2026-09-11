using MainAPP.Models;
using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Integration;

/// <summary>
/// AuthService 用户认证服务集成测试。
/// 验证 Authenticate、Logout、EnsureAdminAccount、ResetDefaults、文件持久化等。
/// 注意：AuthService 为单例，依赖 Settings.Instance 的 UserName/Password 创建默认管理员。
/// 测试串行执行避免状态污染，并在结束后 ResetDefaults 恢复初始状态。
/// </summary>
public class AuthServiceIntegrationTests : IDisposable
{
    private readonly AuthService _auth = AuthService.Instance;

    public AuthServiceIntegrationTests()
    {
        _auth.ResetDefaults("admin", "admin123");
    }

    public void Dispose()
    {
        _auth.ResetDefaults("admin", "admin123");
    }

    [Fact]
    public void ResetDefaults_CreatesAdminAndOperator()
    {
        Assert.True(_auth.Users.Any(u => u.Role == UserRole.Admin));
        Assert.True(_auth.Users.Any(u => u.Role == UserRole.Operator));
    }

    [Fact]
    public void Authenticate_ValidCredentials_ReturnsTrue()
    {
        var result = _auth.Authenticate("admin", "admin123", out var user);

        Assert.True(result);
        Assert.NotNull(user);
        Assert.Equal("admin", user!.Username);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Equal(user, _auth.CurrentUser);
    }

    [Fact]
    public void Authenticate_WrongPassword_ReturnsFalse()
    {
        var result = _auth.Authenticate("admin", "wrong", out var user);

        Assert.False(result);
        Assert.Null(user);
        Assert.Null(_auth.CurrentUser);
    }

    [Fact]
    public void Authenticate_NonExistentUser_ReturnsFalse()
    {
        var result = _auth.Authenticate("ghost", "pwd", out var user);

        Assert.False(result);
        Assert.Null(user);
    }

    [Fact]
    public void Authenticate_CaseInsensitiveUsername()
    {
        var result = _auth.Authenticate("ADMIN", "admin123", out var user);

        Assert.True(result);
        Assert.NotNull(user);
    }

    [Fact]
    public void Authenticate_WithWhitespace_AutoTrimmed()
    {
        var result = _auth.Authenticate("  admin  ", "admin123", out var user);

        Assert.True(result);
        Assert.NotNull(user);
    }

    [Fact]
    public void Authenticate_DisabledUser_ReturnsFalse()
    {
        var admin = _auth.Users.First(u => u.Role == UserRole.Admin);
        admin.Enabled = false;

        var result = _auth.Authenticate(admin.Username, "admin123", out var user);

        Assert.False(result);
        Assert.Null(user);
        admin.Enabled = true; // 恢复
    }

    [Fact]
    public void Logout_ClearsCurrentUser()
    {
        _auth.Authenticate("admin", "admin123", out _);
        Assert.NotNull(_auth.CurrentUser);

        _auth.Logout();

        Assert.Null(_auth.CurrentUser);
    }

    [Fact]
    public void AutoLoginDefaultAdmin_EstablishesAdminSession()
    {
        _auth.Logout();
        Assert.Null(_auth.CurrentUser);

        var ok = _auth.AutoLoginDefaultAdmin();

        Assert.True(ok);
        Assert.NotNull(_auth.CurrentUser);
        Assert.Equal(UserRole.Admin, _auth.CurrentUser!.Role);
        Assert.True(_auth.CurrentUser!.Enabled);
    }

    [Fact]
    public void EnsureAdminAccount_UpdatesExistingAdmin()
    {
        _auth.EnsureAdminAccount("newadmin", "newpwd");

        var admin = _auth.Users.FirstOrDefault(u => u.Role == UserRole.Admin);
        Assert.NotNull(admin);
        Assert.Equal("newadmin", admin!.Username);
        Assert.True(admin.VerifyPassword("newpwd"));
    }

    [Fact]
    public void EnsureAdminAccount_NullUsername_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _auth.EnsureAdminAccount(null!, "pwd"));
    }

    [Fact]
    public void EnsureAdminAccount_NullPassword_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _auth.EnsureAdminAccount("u", null!));
    }

    [Fact]
    public void ResetDefaults_NullPassword_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _auth.ResetDefaults("admin", null!));
    }

    [Fact]
    public void Authenticate_TriggersSessionChanged()
    {
        var fired = false;
        EventHandler handler = (_, _) => fired = true;
        _auth.SessionChanged += handler;
        try
        {
            _auth.Authenticate("admin", "admin123", out _);
            Assert.True(fired);
        }
        finally
        {
            _auth.SessionChanged -= handler;
        }
    }

    [Fact]
    public void Logout_TriggersSessionChanged()
    {
        _auth.Authenticate("admin", "admin123", out _);
        var fired = false;
        EventHandler handler = (_, _) => fired = true;
        _auth.SessionChanged += handler;
        try
        {
            _auth.Logout();
            Assert.True(fired);
        }
        finally
        {
            _auth.SessionChanged -= handler;
        }
    }

    [Fact]
    public async Task SaveAsync_PersistsUsersToFile()
    {
        var barcode = $"U_{Guid.NewGuid():N}";
        _auth.Users.Add(AppUser.Create(barcode, "test", "p", UserRole.Operator));
        await _auth.SaveAsync();

        _auth.Reload();

        Assert.True(_auth.Users.Any(u => u.Username == barcode));
    }

    [Fact]
    public void Reload_PersistsAcrossInstances()
    {
        _auth.EnsureAdminAccount("persistedadmin", "pwd456");
        _auth.Reload();

        var admin = _auth.Users.FirstOrDefault(u => u.Role == UserRole.Admin);
        Assert.NotNull(admin);
        Assert.Equal("persistedadmin", admin!.Username);
    }
}
