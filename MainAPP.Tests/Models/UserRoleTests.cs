using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// UserRole 枚举测试，验证权限层级关系。
/// </summary>
public class UserRoleTests
{
    [Fact]
    public void Admin_HasHighestValue()
    {
        Assert.True((int)UserRole.Admin > (int)UserRole.Operator);
        Assert.True((int)UserRole.Operator > (int)UserRole.Guest);
    }

    [Theory]
    [InlineData(UserRole.Guest, 0)]
    [InlineData(UserRole.Operator, 1)]
    [InlineData(UserRole.Admin, 2)]
    public void EnumValues_AreAsExpected(UserRole role, int expected)
    {
        Assert.Equal(expected, (int)role);
    }
}
