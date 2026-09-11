using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Integration;

/// <summary>
/// Recipe 文件 I/O 集成测试。
/// 验证 SaveToFile/LoadFromFile 的原子写入、JSON 往返序列化、CoordinateTool.Point2f 字段反序列化（IncludeFields=true）。
/// </summary>
public class RecipeFileIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public RecipeFileIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RecipeTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* 测试清理忽略异常 */ }
    }

    [Fact]
    public void SaveToFile_CreatesFile()
    {
        var recipe = Recipe.CreateDefault();
        recipe.Name = "测试配方1";
        var path = Path.Combine(_tempDir, "recipe1.recipe");

        recipe.SaveToFile(path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveToFile_UpdatesModifiedTime()
    {
        var recipe = new Recipe { Name = "T" };
        var originalModified = recipe.ModifiedTime;
        var path = Path.Combine(_tempDir, "recipe.recipe");
        Thread.Sleep(20);

        recipe.SaveToFile(path);

        Assert.True(recipe.ModifiedTime > originalModified);
    }

    [Fact]
    public void SaveToFile_OverwriteExistingFile_ReplacesAtomically()
    {
        var path = Path.Combine(_tempDir, "overwrite.recipe");
        var r1 = new Recipe { Name = "First" };
        r1.SaveToFile(path);
        var r2 = new Recipe { Name = "Second" };

        r2.SaveToFile(path);

        var loaded = Recipe.LoadFromFile(path);
        Assert.NotNull(loaded);
        Assert.Equal("Second", loaded!.Name);
    }

    [Fact]
    public void LoadFromFile_NonExistent_ReturnsNull()
    {
        var result = Recipe.LoadFromFile(Path.Combine(_tempDir, "missing.recipe"));

        Assert.Null(result);
    }

    [Fact]
    public void LoadFromFile_CorruptedJson_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "corrupt.recipe");
        File.WriteAllText(path, "{ this is not valid json }");

        var result = Recipe.LoadFromFile(path);

        Assert.Null(result);
    }

    [Fact]
    public void SaveToFile_LoadFromFile_RoundtripPreservesProperties()
    {
        var original = new Recipe
        {
            Name = "Roundtrip",
            Description = "测试描述",
            OffsetX = 12.5f,
            OffsetY = -7.5f,
            OffsetAngle = 90.0f,
            ImageTool = new ImageTool { ExposureTime = 500, Gain = 2.5f },
            YoloTool = new YoloTools
            {
                EdgeDetection = new YoloTool { Confidence = 0.7f },
                IsAngleDetectionEnabled = true
            }
        };
        var path = Path.Combine(_tempDir, "roundtrip.recipe");

        original.SaveToFile(path);
        var loaded = Recipe.LoadFromFile(path);

        Assert.NotNull(loaded);
        Assert.Equal("Roundtrip", loaded!.Name);
        Assert.Equal("测试描述", loaded.Description);
        Assert.Equal(12.5f, loaded.OffsetX);
        Assert.Equal(-7.5f, loaded.OffsetY);
        Assert.Equal(90.0f, loaded.OffsetAngle);
        Assert.NotNull(loaded.ImageTool);
        Assert.Equal(500, loaded.ImageTool!.ExposureTime);
        Assert.Equal(2.5f, loaded.ImageTool.Gain);
        Assert.NotNull(loaded.YoloTool);
        Assert.True(loaded.YoloTool!.IsAngleDetectionEnabled);
    }

    [Fact]
    public void SaveToFile_LoadFromFile_PreservesCoordinateToolPoint2fFields()
    {
        // L260: IncludeFields = true 必须保留，否则 Point2f 字段反序列化为 0
        var original = new Recipe
        {
            Name = "Coord",
            CoordinateTool = new CoordinateTool
            {
                Origin = new OpenCvSharp.Point2f(10.5f, 20.5f),
                XPoint = new OpenCvSharp.Point2f(100, 0),
                YPoint = new OpenCvSharp.Point2f(0, 100)
            }
        };
        var path = Path.Combine(_tempDir, "coord.recipe");

        original.SaveToFile(path);
        var loaded = Recipe.LoadFromFile(path);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.CoordinateTool);
        Assert.Equal(10.5f, loaded.CoordinateTool!.Origin.X);
        Assert.Equal(20.5f, loaded.CoordinateTool.Origin.Y);
        Assert.Equal(100f, loaded.CoordinateTool.XPoint.X);
        Assert.Equal(100f, loaded.CoordinateTool.YPoint.Y);
    }

    [Fact]
    public void SaveToFile_DoesNotLeaveTempFile()
    {
        var path = Path.Combine(_tempDir, "notemp.recipe");
        var recipe = new Recipe { Name = "T" };

        recipe.SaveToFile(path);

        Assert.False(File.Exists(path + ".tmp"), "临时文件应被清理或移动");
        Assert.True(File.Exists(path));
    }
}
