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
        // 2026-09-15: 抓取点偏移（产品局部坐标系）默认 0 = 抓取点即掩码最小外接旋转矩形中心
        Assert.Equal(0, recipe.GrabOffsetLongMm);
        Assert.Equal(0, recipe.GrabOffsetShortMm);
        Assert.False(recipe.GrabOffsetMigrated);
        // 旧的世界系平移补偿字段保留，但仅用于旧配方一次性迁移，默认 0 且不再参与坐标计算
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
            GrabOffsetLongMm = 1.5f,
            GrabOffsetShortMm = 2.5f,
            OffsetAngle = 90.0f
        };

        Assert.Equal("测试配方", recipe.Name);
        Assert.Equal("描述", recipe.Description);
        Assert.Equal(1.5f, recipe.GrabOffsetLongMm);
        Assert.Equal(2.5f, recipe.GrabOffsetShortMm);
        Assert.Equal(90.0f, recipe.OffsetAngle);
    }

    /// <summary>旧的世界系平移补偿应被一次性迁移到抓取点偏移，且幂等、不覆盖现场手填值。</summary>
    [Fact]
    public void MigrateLegacyOffsets_MovesWorldOffsetsIntoGrabOffsetsOnce()
    {
        var recipe = new Recipe { Name = "迁移", OffsetX = 12.5f, OffsetY = -7.5f };

        Assert.True(recipe.MigrateLegacyOffsets());
        Assert.Equal(12.5f, recipe.GrabOffsetLongMm);
        Assert.Equal(-7.5f, recipe.GrabOffsetShortMm);
        Assert.Equal(0f, recipe.OffsetX);
        Assert.Equal(0f, recipe.OffsetY);
        Assert.True(recipe.GrabOffsetMigrated);

        // 幂等：已迁移过就不再动，避免覆盖现场后来手填的抓取点偏移
        recipe.GrabOffsetLongMm = 3f;
        Assert.False(recipe.MigrateLegacyOffsets());
        Assert.Equal(3f, recipe.GrabOffsetLongMm);
    }

    /// <summary>旧值为 0 时不应标记迁移（否则会把"从未配过补偿"误判为已迁移）。</summary>
    [Fact]
    public void MigrateLegacyOffsets_NoLegacyValues_DoesNothing()
    {
        var recipe = new Recipe { Name = "无旧值" };

        Assert.False(recipe.MigrateLegacyOffsets());
        Assert.False(recipe.GrabOffsetMigrated);
        Assert.Equal(0f, recipe.GrabOffsetLongMm);
    }
}
