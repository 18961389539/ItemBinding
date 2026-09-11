using BenchmarkDotNet.Attributes;
using MainAPP.Models;
using System.Text.Json;

namespace MainAPP.Benchmarks.Benchmarks;

/// <summary>
/// Recipe JSON 序列化/反序列化性能基准测试。
/// 配方保存加载使用 IncludeFields=true（Point2f 公共字段），是配方管理的性能关键路径。
/// 对比缓存的 JsonSerializerOptions 与每次新建 Options 的性能差异。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class RecipeJsonBenchmarks
{
    private Recipe _recipe;
    private string _recipeJson;
    private JsonSerializerOptions _cachedOpts;
    private JsonSerializerOptions _newOpts;

    [GlobalSetup]
    public void Setup()
    {
        _recipe = Recipe.CreateDefault();
        _recipe.OffsetX = 12.5f;
        _recipe.OffsetY = -3.2f;
        _recipe.OffsetAngle = 45.0f;
        _recipe.CoordinateTool = new CoordinateTool
        {
            PatternWidth = 15,
            PatternHeight = 11
        };

        _cachedOpts = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        _recipeJson = JsonSerializer.Serialize(_recipe, _cachedOpts);
    }

    [Benchmark(Description = "Serialize - 缓存 Options (IncludeFields)")]
    public string Serialize_CachedOpts() => JsonSerializer.Serialize(_recipe, _cachedOpts);

    [Benchmark(Description = "Serialize - 每次新建 Options")]
    public string Serialize_NewOpts()
    {
        // 反面教材：每次序列化都新建 Options，性能差
        _newOpts = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
        return JsonSerializer.Serialize(_recipe, _newOpts);
    }

    [Benchmark(Description = "Deserialize - 缓存 Options")]
    public Recipe? Deserialize_CachedOpts() => JsonSerializer.Deserialize<Recipe>(_recipeJson, _cachedOpts);

    [Benchmark(Description = "RoundTrip - 序列化+反序列化")]
    public Recipe? RoundTrip()
    {
        var json = JsonSerializer.Serialize(_recipe, _cachedOpts);
        return JsonSerializer.Deserialize<Recipe>(json, _cachedOpts);
    }
}
