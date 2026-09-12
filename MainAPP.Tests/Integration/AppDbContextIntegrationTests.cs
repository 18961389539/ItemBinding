using MainAPP.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MainAPP.Tests.Integration;

/// <summary>
/// AppDbContext EF Core 数据库上下文集成测试。
/// 使用 SQLite InMemory 或临时文件数据库验证 CRUD、索引、分页等。
/// 注意：AppDbContext.OnConfiguring 强制使用文件路径 SQLite，故在临时目录中创建。
/// </summary>
public class AppDbContextIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public AppDbContextIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"AppDbContext_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        // 通过 SetCurrentDirectory 让 AppDbContext 在临时目录创建 .db 文件
        Environment.SetEnvironmentVariable("APPCONTEXT_BASE_DIR", _tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* 测试清理忽略异常 */ }
    }

    [Fact]
    public async Task CanCreateDatabase()
    {
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();

        Assert.True(await ctx.Database.CanConnectAsync());
    }

    [Fact]
    public async Task AddAsync_InsertsRecord()
    {
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();

        var model = new DbModel
        {
            Barcode = "ABC123",
            Encode = 100,
            WorldX = 1.5,
            WorldY = 2.5,
            Angle = 30.0,
            Width = 40.0,
            Height = 30.0,
            CostTime = 50
        };
        ctx.BarcodeData.Add(model);
        await ctx.SaveChangesAsync();

        Assert.True(model.Id > 0);
    }

    [Fact]
    public async Task QueryById_ReturnsInsertedRecord()
    {
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();
        var model = new DbModel { Barcode = "FINDME", WorldX = 99 };
        ctx.BarcodeData.Add(model);
        await ctx.SaveChangesAsync();
        var id = model.Id;

        var fetched = await ctx.BarcodeData.AsNoTracking().FirstAsync(m => m.Id == id);

        Assert.Equal("FINDME", fetched.Barcode);
        Assert.Equal(99, fetched.WorldX);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesExistingRecord()
    {
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();
        var model = new DbModel { Barcode = "BEFORE", WorldX = 0 };
        ctx.BarcodeData.Add(model);
        await ctx.SaveChangesAsync();

        model.Barcode = "AFTER";
        model.WorldX = 100;
        await ctx.SaveChangesAsync();

        var fetched = await ctx.BarcodeData.AsNoTracking().FirstAsync(m => m.Id == model.Id);
        Assert.Equal("AFTER", fetched.Barcode);
        Assert.Equal(100, fetched.WorldX);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();
        var model = new DbModel { Barcode = "DELETE" };
        ctx.BarcodeData.Add(model);
        await ctx.SaveChangesAsync();
        var id = model.Id;

        ctx.BarcodeData.Remove(model);
        await ctx.SaveChangesAsync();

        var exists = await ctx.BarcodeData.AnyAsync(m => m.Id == id);
        Assert.False(exists);
    }

    [Fact]
    public async Task Pagination_SkipsAndTakesCorrectly()
    {
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();
        for (int i = 0; i < 15; i++)
        {
            ctx.BarcodeData.Add(new DbModel
            {
                Barcode = $"P{i:D2}",
                DetectTime = DateTime.Now.AddSeconds(i)
            });
        }
        await ctx.SaveChangesAsync();

        var page2 = await ctx.BarcodeData
            .OrderByDescending(m => m.DetectTime)
            .Skip(5)
            .Take(5)
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(5, page2.Count);
    }

    [Fact]
    public async Task EnsureIndexesAsync_DoesNotThrow()
    {
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();

        var exception = await Record.ExceptionAsync(() => ctx.EnsureIndexesAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task EnsureIndexesAsync_IsIdempotent()
    {
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();

        await ctx.EnsureIndexesAsync();
        var exception = await Record.ExceptionAsync(() => ctx.EnsureIndexesAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task BarcodeEmptyString_PersistsSuccessfully()
    {
        // DbModel.Barcode 默认值为 string.Empty，数据库列为 NOT NULL；
        // EF Core 不会自动将 null 转空字符串，此处验证空字符串能正常持久化
        using var ctx = new AppDbContext();
        await ctx.Database.EnsureCreatedAsync();
        // 与生产启动行为对齐：EnsureCreated 不补列，既有库必须显式 ALTER（模型加列后旧库文件同步）
        await ctx.EnsureTraceColumnsAsync();
        await ctx.EnsureBrightnessColumnsAsync();
        var model = new DbModel { Barcode = string.Empty };
        ctx.BarcodeData.Add(model);
        await ctx.SaveChangesAsync();

        var fetched = await ctx.BarcodeData.AsNoTracking().FirstAsync(m => m.Id == model.Id);
        Assert.Equal(string.Empty, fetched.Barcode);
    }
}
