using MainAPP.Models;
using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Integration;

/// <summary>
/// BarcodeDataService 集成测试。
/// 验证添加、查询、分页、批量删除、清空、CSV 导入导出等核心数据流。
/// 注意：BarcodeDataService 是单例，依赖 Settings 与 LogService，测试串行执行避免状态污染。
/// </summary>
public class BarcodeDataServiceIntegrationTests : IAsyncDisposable
{
    public BarcodeDataServiceIntegrationTests()
    {
        // 与生产启动行为对齐：库结构由模型对齐（补列 + 补索引），见 AppDbContext.ApplySchemaSyncAsync
        EnsureDatabaseSchema();
    }

    private static void EnsureDatabaseSchema()
    {
        using var ctx = new MainAPP.Models.AppDbContext();
        ctx.ApplySchemaSyncAsync().GetAwaiter().GetResult();
    }

    private readonly BarcodeDataService _service = BarcodeDataService.Instance;

    [Fact]
    public async Task AddAsync_GetByIdAsync_Roundtrip()
    {
        var model = new DbModel
        {
            Barcode = $"TEST_{Guid.NewGuid():N}",
            Encode = 999,
            WorldX = 1.23,
            WorldY = 4.56,
            Angle = 78.9,
            CostTime = 42
        };

        var added = await _service.AddAsync(model);
        await _service.SavePendingAsync();
        var fetched = await _service.GetByIdAsync(added.Id);

        Assert.NotNull(fetched);
        Assert.Equal(model.Barcode, fetched!.Barcode);
        Assert.Equal(999u, fetched.Encode);
        Assert.Equal(1.23, fetched.WorldX);
    }

    [Fact]
    public async Task GetCountAsync_ReturnsIncreasingCount()
    {
        var beforeCount = await _service.GetCountAsync();

        await _service.AddAsync(new DbModel { Barcode = $"CNT_{Guid.NewGuid():N}" });
        await _service.SavePendingAsync();
        var afterCount = await _service.GetCountAsync();

        Assert.True(afterCount >= beforeCount + 1);
    }

    [Fact]
    public async Task GetByBarcodeAsync_ReturnsMatchingRecords()
    {
        var barcode = $"LOOKUP_{Guid.NewGuid():N}";
        await _service.AddAsync(new DbModel { Barcode = barcode, WorldX = 100 });
        await _service.AddAsync(new DbModel { Barcode = barcode, WorldX = 200 });
        await _service.SavePendingAsync();

        var results = await _service.GetByBarcodeAsync(barcode);

        Assert.True(results.Count >= 2);
        Assert.All(results, r => Assert.Equal(barcode, r.Barcode));
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsLatestRecords()
    {
        await _service.AddAsync(new DbModel
        {
            Barcode = $"RECENT_{Guid.NewGuid():N}",
            DetectTime = DateTime.Now
        });
        await _service.SavePendingAsync();

        var recent = await _service.GetRecentAsync(5);

        Assert.True(recent.Count > 0);
        // 按 DetectTime 降序，第一个应为最近
        Assert.True(recent[0].DetectTime >= recent[^1].DetectTime);
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsCorrectPage()
    {
        // 添加足够数据
        for (int i = 0; i < 3; i++)
        {
            await _service.AddAsync(new DbModel
            {
                Barcode = $"PAGE_{i}_{Guid.NewGuid():N}",
                DetectTime = DateTime.Now.AddSeconds(i)
            });
        }
        await _service.SavePendingAsync();

        var (items, total) = await _service.GetPagedAsync(pageNumber: 1, pageSize: 2);

        Assert.True(total >= 3);
        Assert.True(items.Count <= 2);
    }

    [Fact]
    public async Task SearchAsync_ByBarcode_ReturnsMatchingRecords()
    {
        var barcode = $"SEARCH_{Guid.NewGuid():N}";
        await _service.AddAsync(new DbModel { Barcode = barcode, Encode = 50 });
        await _service.SavePendingAsync();

        var results = await _service.SearchAsync(barcode: barcode);

        Assert.True(results.Count >= 1);
        Assert.All(results, r => Assert.Equal(barcode, r.Barcode));
    }

    [Fact]
    public async Task SearchPagedAsync_ReturnsItemsAndTotal()
    {
        var barcode = $"SP_{Guid.NewGuid():N}";
        await _service.AddAsync(new DbModel { Barcode = barcode });
        await _service.AddAsync(new DbModel { Barcode = barcode });
        await _service.SavePendingAsync();

        var (items, total) = await _service.SearchPagedAsync(pageNumber: 1, pageSize: 10, barcode: barcode);

        Assert.True(total >= 2);
        Assert.True(items.Count <= 10);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesRecord()
    {
        var model = new DbModel { Barcode = $"UPD_{Guid.NewGuid():N}", WorldX = 0 };
        await _service.AddAsync(model);
        await _service.SavePendingAsync();

        model.WorldX = 999;
        await _service.UpdateAsync(model);

        var fetched = await _service.GetByIdAsync(model.Id);
        Assert.Equal(999, fetched!.WorldX);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecord()
    {
        var model = new DbModel { Barcode = $"DEL_{Guid.NewGuid():N}" };
        await _service.AddAsync(model);
        await _service.SavePendingAsync();
        var id = model.Id;

        await _service.DeleteAsync(id);

        var fetched = await _service.GetByIdAsync(id);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task BulkDeleteAsync_RemovesMultipleRecords()
    {
        var ids = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            var m = new DbModel { Barcode = $"BULK_{i}_{Guid.NewGuid():N}" };
            await _service.AddAsync(m);
            ids.Add(m.Id);
        }
        await _service.SavePendingAsync();

        await _service.BulkDeleteAsync(ids);

        foreach (var id in ids)
        {
            var fetched = await _service.GetByIdAsync(id);
            Assert.Null(fetched);
        }
    }

    [Fact]
    public async Task ExportToCsvAsync_WritesFileWithHeader()
    {
        await _service.AddAsync(new DbModel
        {
            Barcode = $"EXP_{Guid.NewGuid():N}",
            Encode = 1,
            WorldX = 2.5,
            WorldY = 3.5,
            Angle = 4.5,
            Width = 5,
            Height = 6,
            CostTime = 7,
            DetectTime = new DateTime(2026, 7, 15, 10, 30, 0)
        });
        await _service.SavePendingAsync();

        var tempFile = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.csv");
        try
        {
            await _service.ExportToCsvAsync(tempFile, default);

            Assert.True(File.Exists(tempFile));
            var lines = await File.ReadAllLinesAsync(tempFile);
            Assert.True(lines.Length >= 2);
            // L383: CSV 表头已扩展为 21 列（2026-09-08 尾追灰度判向统计列），与 GetCsvHeader() 完全一致
            Assert.Equal("Id,Barcode,Encode,WorldX,WorldY,Angle,Width,Height,ImageX,ImageY,Area,ImageBarcodeX,ImageBarcodeY,BarcodeScore,Score,Speed,CostTime,DetectTime,BrightMean,DarkMean,BrightnessDiff", lines[0]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ImportFromCsvAsync_ImportsRecords()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"import_{Guid.NewGuid():N}.csv");
        var barcode = $"IMP_{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllLinesAsync(tempFile, new[]
            {
                "Id,Barcode,Encode,X,Y,Angle,Width,Height,CostTime,DetectTime",
                $"0,{barcode},100,1.5,2.5,30.0,40,30,50,2026-07-15 10:30:00"
            });

            await _service.ImportFromCsvAsync(tempFile);

            var results = await _service.GetByBarcodeAsync(barcode);
            Assert.True(results.Count >= 1);
            Assert.Equal(100u, results[0].Encode);
            Assert.Equal(1.5, results[0].WorldX);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExportImport_Roundtrip_PreservesData()
    {
        var barcode = $"RT_{Guid.NewGuid():N}";
        await _service.AddAsync(new DbModel
        {
            Barcode = barcode,
            Encode = 12345,
            WorldX = 100.5,
            WorldY = 200.25,
            Angle = 45.0,
            Width = 30,
            Height = 40,
            CostTime = 25,
            DetectTime = new DateTime(2026, 7, 15, 10, 30, 0)
        });
        await _service.SavePendingAsync();

        var exportFile = Path.Combine(Path.GetTempPath(), $"rt_export_{Guid.NewGuid():N}.csv");
        try
        {
            await _service.ExportToCsvAsync(exportFile, default);
            await _service.ImportFromCsvAsync(exportFile);

            var results = await _service.GetByBarcodeAsync(barcode);
            // 至少应包含原数据 + 导入数据 = 2 条
            Assert.True(results.Count >= 2);
        }
        finally
        {
            if (File.Exists(exportFile)) File.Delete(exportFile);
        }
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsValidAggregates()
    {
        await _service.AddAsync(new DbModel
        {
            Barcode = $"STAT_{Guid.NewGuid():N}",
            CostTime = 100,
            WorldX = 10,
            WorldY = 20,
            Angle = 30,
            Width = 40,
            Height = 50
        });
        await _service.SavePendingAsync();

        var stats = await _service.GetStatisticsAsync();

        Assert.True(stats.TotalRecords >= 1);
        Assert.True(stats.AvgCostTime >= 0);
    }

    [Fact]
    public async Task AddRangeAsync_BatchInsertsRecords()
    {
        var beforeCount = await _service.GetCountAsync();
        var models = new List<DbModel>();
        for (int i = 0; i < 5; i++)
        {
            models.Add(new DbModel { Barcode = $"RNG_{i}_{Guid.NewGuid():N}" });
        }

        await _service.AddRangeAsync(models);
        await _service.SavePendingAsync();
        var afterCount = await _service.GetCountAsync();

        Assert.True(afterCount >= beforeCount + 5);
    }

    [Fact]
    public async Task GetByTimeRangeAsync_ReturnsRecordsInRange()
    {
        var start = DateTime.Now.AddSeconds(-1);
        var barcode = $"TR_{Guid.NewGuid():N}";
        await _service.AddAsync(new DbModel
        {
            Barcode = barcode,
            DetectTime = DateTime.Now
        });
        await _service.SavePendingAsync();
        var end = DateTime.Now.AddSeconds(1);

        var results = await _service.GetByTimeRangeAsync(start, end);

        Assert.Contains(results, r => r.Barcode == barcode);
    }

    public async ValueTask DisposeAsync()
    {
        // P2-17/测试清理: 清空数据库避免数据累积膨胀
        // 历史问题：测试依赖单例 BarcodeDataService，使用持久化数据库文件
        // 每次测试只追加不清理，日积月累膨胀到 4.76 GB 导致 ExportToCsvAsync 卡死
        // 在测试类销毁时调用 ClearAllAsync 清空表数据，防止再次膨胀
        try
        {
            await _service.ClearAllAsync();
        }
        catch
        {
            // 清理失败不阻止测试结果报告
        }
    }
}
