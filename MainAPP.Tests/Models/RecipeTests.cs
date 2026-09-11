using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// Recipe 模型单元测试，验证默认值与 CreateDefault 工厂方法。
/// 文件 I/O 集成测试见 RecipeFileIntegrationTests。
/// </summary>
public class RecipeTests
{
    [Fact]
    public void Defaults_AreSetCorrectly()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var recipe = new Recipe();
        var after = DateTime.Now.AddSeconds(1);

        Assert.Equal(string.Empty, recipe.Name);
        Assert.Equal(string.Empty, recipe.Description);
        Assert.Equal(0, recipe.OffsetX);
        Assert.Equal(0, recipe.OffsetY);
        Assert.Equal(0, recipe.OffsetAngle);
        Assert.InRange(recipe.CreatedTime, before, after);
        Assert.InRange(recipe.ModifiedTime, before, after);
        Assert.NotNull(recipe.ImageTool);
        Assert.NotNull(recipe.YoloTool);
        Assert.NotNull(recipe.CoordinateTool);
    }

    [Fact]
    public void CreateDefault_ReturnsExpectedRecipe()
    {
        var recipe = Recipe.CreateDefault();

        Assert.Equal("默认配方", recipe.Name);
        Assert.Equal(string.Empty, recipe.Description);
        Assert.NotNull(recipe.ImageTool);
        Assert.NotNull(recipe.YoloTool);
        Assert.NotNull(recipe.CoordinateTool);
    }

    [Fact]
    public void CreateDefault_InstancesAreIndependent()
    {
        var r1 = Recipe.CreateDefault();
        var r2 = Recipe.CreateDefault();

        Assert.NotSame(r1, r2);
        Assert.NotSame(r1.ImageTool, r2.ImageTool);
        Assert.NotSame(r1.CoordinateTool, r2.CoordinateTool);
    }

    [Fact]
    public void Properties_CanBeModified()
    {
        var recipe = new Recipe
        {
            Name = "测试配方",
            Description = "描述",
            OffsetX = 1.5f,
            OffsetY = 2.5f,
            OffsetAngle = 90.0f
        };

        Assert.Equal("测试配方", recipe.Name);
        Assert.Equal("描述", recipe.Description);
        Assert.Equal(1.5f, recipe.OffsetX);
        Assert.Equal(2.5f, recipe.OffsetY);
        Assert.Equal(90.0f, recipe.OffsetAngle);
    }
}
