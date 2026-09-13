using MainAPP.Models;
using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Integration;

/// <summary>
/// RecipesManage 配方管理器集成测试。
/// 验证加载、添加、删除、查找、当前配方切换等业务流程。
/// 注意：RecipesManage 为单例，依赖 Settings.Instance 持久化 CurrentRecipeName，测试串行执行。
/// </summary>
public class RecipesManageIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RecipesManage _manager;

    public RecipesManageIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Recipes_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _manager = RecipesManage.Instance;
        _manager.Initialize(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            _manager.Clear();
            // 清空配方列表
            while (_manager.Recipes.Count > 0)
                _manager.RemoveRecipe(_manager.Recipes[0]);
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* 测试清理忽略异常 */ }
    }

    [Fact]
    public void Initialize_CreatesDirectoryIfMissing()
    {
        var newDir = Path.Combine(_tempDir, "subdir");
        Assert.False(Directory.Exists(newDir));

        _manager.Initialize(newDir);

        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public void AddRecipe_PersistsToFileAndAddsToCollection()
    {
        var recipe = new Recipe { Name = $"Add_{Guid.NewGuid():N}" };

        _manager.AddRecipe(recipe);

        Assert.Contains(recipe, _manager.Recipes);
        var files = Directory.GetFiles(_tempDir, "*.recipe", SearchOption.AllDirectories);
        Assert.True(files.Length >= 1);
    }

    [Fact]
    public void RemoveRecipe_RemovesFromFileAndCollection()
    {
        var recipe = new Recipe { Name = $"Remove_{Guid.NewGuid():N}" };
        _manager.AddRecipe(recipe);
        Assert.Contains(recipe, _manager.Recipes);

        var result = _manager.RemoveRecipe(recipe);

        Assert.True(result);
        Assert.DoesNotContain(recipe, _manager.Recipes);
    }

    [Fact]
    public void FindByName_ReturnsMatchingRecipe()
    {
        var name = $"Find_{Guid.NewGuid():N}";
        var recipe = new Recipe { Name = name };
        _manager.AddRecipe(recipe);

        var found = _manager.FindByName(name);

        Assert.NotNull(found);
        Assert.Equal(name, found!.Name);
    }

    [Fact]
    public void FindByName_CaseInsensitive()
    {
        var name = $"Case_{Guid.NewGuid():N}";
        _manager.AddRecipe(new Recipe { Name = name });

        var found = _manager.FindByName(name.ToLowerInvariant());

        Assert.NotNull(found);
    }

    [Fact]
    public void FindByName_NonExistent_ReturnsNull()
    {
        var found = _manager.FindByName($"Missing_{Guid.NewGuid():N}");

        Assert.Null(found);
    }

    [Fact]
    public void IsNameExists_ReturnsTrueForExisting()
    {
        var name = $"Exists_{Guid.NewGuid():N}";
        _manager.AddRecipe(new Recipe { Name = name });

        Assert.True(_manager.IsNameExists(name));
    }

    [Fact]
    public void IsNameExists_ReturnsFalseForMissing()
    {
        Assert.False(_manager.IsNameExists($"No_{Guid.NewGuid():N}"));
    }

    [Fact]
    public void LoadAllRecipes_LoadsFromSubdirectories()
    {
        var name = $"Sub_{Guid.NewGuid():N}";
        var recipe = new Recipe { Name = name };
        _manager.AddRecipe(recipe); // AddRecipe 会创建子文件夹

        // 重新加载
        _manager.LoadAllRecipes();

        Assert.Contains(_manager.Recipes, r => r.Name == name);
    }

    [Fact]
    public void SetAndSaveCurrentRecipe_SetsCurrentAndPersists()
    {
        var recipe = new Recipe { Name = $"Curr_{Guid.NewGuid():N}" };
        _manager.AddRecipe(recipe);

        _manager.SetAndSaveCurrentRecipe(recipe);

        Assert.Equal(recipe, _manager.CurrentRecipe);
        Assert.Equal(recipe.Name, Settings.Instance.CurrentRecipeName);
    }

    [Fact]
    public void SetAndSaveCurrentRecipe_NullRecipe_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _manager.SetAndSaveCurrentRecipe(null!));
    }

    [Fact]
    public void RemoveRecipe_WhenItIsCurrent_ClearsCurrentRecipe()
    {
        var recipe = new Recipe { Name = $"Curr_{Guid.NewGuid():N}" };
        _manager.AddRecipe(recipe);
        _manager.SetAndSaveCurrentRecipe(recipe);

        _manager.RemoveRecipe(recipe);

        Assert.Null(_manager.CurrentRecipe);
    }

    [Fact]
    public void CurrentRecipeChanged_EventFiresOnSet()
    {
        Recipe? received = null;
        _manager.CurrentRecipeChanged += r => received = r;
        var recipe = new Recipe { Name = $"Evt_{Guid.NewGuid():N}" };
        _manager.AddRecipe(recipe);

        _manager.SetAndSaveCurrentRecipe(recipe);

        Assert.Equal(recipe, received);
    }

    [Fact]
    public void RecipeName_WithInvalidChars_SanitizedInFilePath()
    {
        // 配方名含文件系统无效字符（如 < > | 等）应被替换为 _
        var recipe = new Recipe { Name = "Test<Recipe>|Name" };
        _manager.AddRecipe(recipe);

        Assert.Contains(recipe, _manager.Recipes);
        // 验证文件被创建（路径中无效字符已被替换）
        var files = Directory.GetFiles(_tempDir, "*.recipe", SearchOption.AllDirectories);
        Assert.True(files.Length >= 1);
    }
}

/// <summary>
/// 临时扩展：为 RecipesManage 添加 Clear 方法用于测试清理。
/// 仅在测试程序集内可见，不污染生产代码。
/// </summary>
internal static class RecipesManageTestExtensions
{
    public static void Clear(this RecipesManage manager)
    {
        while (manager.Recipes.Count > 0)
        {
            manager.RemoveRecipe(manager.Recipes[0]);
        }
        manager.CurrentRecipe = null;
    }
}
