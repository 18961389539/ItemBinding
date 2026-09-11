using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// AppUser 模型单元测试，覆盖工厂方法、密码设置与恒定时间密码验证。
/// </summary>
public class AppUserTests
{
    [Fact]
    public void Create_SetsUsernameTrimmed()
    {
        var user = AppUser.Create("  alice  ", "Alice", "pwd", UserRole.Operator);

        Assert.Equal("alice", user.Username);
    }

    [Fact]
    public void Create_WithBlankDisplayName_FallsBackToUsername()
    {
        var user = AppUser.Create("bob", "   ", "pwd", UserRole.Operator);

        Assert.Equal("bob", user.DisplayName);
    }

    [Fact]
    public void Create_NullDisplayName_FallsBackToUsername()
    {
        var user = AppUser.Create("bob", null!, "pwd", UserRole.Operator);

        Assert.Equal("bob", user.DisplayName);
    }

    [Fact]
    public void Create_DefaultsEnabledTrue()
    {
        var user = AppUser.Create("u", "d", "p", UserRole.Guest);

        Assert.True(user.Enabled);
    }

    [Theory]
    [InlineData(UserRole.Guest)]
    [InlineData(UserRole.Operator)]
    [InlineData(UserRole.Admin)]
    public void Create_PreservesRole(UserRole role)
    {
        var user = AppUser.Create("u", "d", "p", role);

        Assert.Equal(role, user.Role);
    }

    [Fact]
    public void SetPassword_Null_BecomesEmpty()
    {
        var user = AppUser.Create("u", "d", "initial", UserRole.Operator);

        user.SetPassword(null!);

        Assert.Equal(string.Empty, user.Password);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var user = AppUser.Create("u", "d", "s3cret", UserRole.Operator);

        Assert.True(user.VerifyPassword("s3cret"));
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var user = AppUser.Create("u", "d", "s3cret", UserRole.Operator);

        Assert.False(user.VerifyPassword("wrong"));
    }

    [Fact]
    public void VerifyPassword_DifferentLength_ReturnsFalse()
    {
        var user = AppUser.Create("u", "d", "short", UserRole.Operator);

        Assert.False(user.VerifyPassword("much-longer-password"));
    }

    [Fact]
    public void VerifyPassword_NullInput_ReturnsFalse()
    {
        var user = AppUser.Create("u", "d", "pwd", UserRole.Operator);

        Assert.False(user.VerifyPassword(null!));
    }

    [Fact]
    public void VerifyPassword_EmptyPasswordMatchesEmpty()
    {
        var user = AppUser.Create("u", "d", string.Empty, UserRole.Operator);

        Assert.True(user.VerifyPassword(string.Empty));
    }
}
