using MainAPP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// 库结构增量同步（<see cref="AppDbContext.ApplySchemaSyncAsync"/>）测试 —— 2026-09-16。
///
/// <para><b>回归背景</b>：原先新增一个列要三步人肉操作（改 DbModel、在手写的
/// <c>EnsureBrightnessColumnsAsync</c>/<c>EnsureTraceColumnsAsync</c> 里加一段
/// <c>if (!cols.Contains("X")) ALTER TABLE ...</c>、再去 <c>App.InitializeDatabaseAsync</c> 加调用），
/// 漏任何一步都在运行期表现为 "no such column"；索引同理，是另一份硬编码 DDL 清单。
/// 现在模型是唯一来源，本文件钉住三件事：
/// ① 老库（缺列缺索引）能被补齐，且**既有行不受影响**；
/// ② 同步幂等、且不留下无法处理的列；
/// ③ 模型里声明的关键索引确实建出来了（含为 AI 追溯问答加的三个复合索引）。</para>
///
/// <para>测试工程已 <c>DisableTestParallelization</c>，故本文件可以改真实表结构来模拟老库；
/// 每个用例结束时表结构都已回到最新态，不影响后续用例。</para>
/// </summary>
public class DatabaseSchemaSyncTests
{
    /// <summary>模拟"老库里的既有行"，用于验证补列不丢数据。</summary>
    private const string LegacyRowBarcode = "LEGACY-ROW-0001";

    /// <summary>
    /// 2026-09-16 之前靠手写 ALTER 逐次补上的可空列 —— 它们就是"人肉补列"的产物，
    /// 也是本用例要退回掉、再让同步补回来的那一批。
    /// </summary>
    private static readonly string[] HistoricallyAddedColumns =
    {
        "BrightMean", "DarkMean", "BrightnessDiff", "IsCalibrated",
        "RecipeName", "Result", "Station", "HeadFeatures", "HeadTruthPositive",
    };

    /// <summary>引用了上述列的复合索引（删列前必须先删索引，SQLite 不允许 DROP 带索引的列）。</summary>
    private static readonly string[] IndexesOnHistoricallyAddedColumns =
    {
        "IX_BarcodeData_RecipeName_DetectTime",
        "IX_BarcodeData_Station_DetectTime",
        "IX_BarcodeData_Result_DetectTime",
    };

    /// <summary>
    /// 老库场景：表已存在但缺列、缺索引 → 同步应补齐二者，并保留既有行。
    /// </summary>
    [Fact]
    public async Task SyncOnLegacyTable_AddsMissingColumnsAndIndexes_AndKeepsExistingRows()
    {
        using var ctx = new AppDbContext();

        // 先把库做到最新态，插入一行"历史数据"，再退回升级前的形状
        await ctx.ApplySchemaSyncAsync();
        // 用 EF 写入而不是手写 INSERT：表里有一批**非空无默认值**的列（WorldX / ImageFullName /
        // EncodeTime …），手写 INSERT 必须逐列给值，既啰嗦又容易漏；EF 会按模型补全。
        ctx.BarcodeData.Add(new DbModel { Barcode = LegacyRowBarcode });
        await ctx.SaveChangesAsync();
        await RevertToLegacyShapeAsync(ctx);

        // 前置断言：确实退回了老形状（否则后面的通过就没意义）
        var legacyColumns = await ReadNamesAsync(ctx, "PRAGMA table_info(BarcodeData)");
        Assert.DoesNotContain("RecipeName", legacyColumns);
        var legacyIndexes = await ReadNamesAsync(ctx, "PRAGMA index_list(BarcodeData)");
        Assert.DoesNotContain("IX_BarcodeData_RecipeName_DetectTime", legacyIndexes);

        var result = await ctx.ApplySchemaSyncAsync();

        try
        {
            // 1) 老库缺的可空列被全部补上，且没有"无法处理"的列
            Assert.Empty(result.SkippedColumns);
            foreach (var column in HistoricallyAddedColumns)
            {
                Assert.Contains(column, result.AddedColumns);
            }

            // 2) 补列后模型里的每一列都存在（缺任何一列，INSERT 都会报 no such column）
            var columns = await ReadNamesAsync(ctx, "PRAGMA table_info(BarcodeData)");
            foreach (var column in ModelColumnNames(ctx))
            {
                Assert.Contains(column, columns);
            }

            // 3) 依赖被删列的复合索引被重新创建
            foreach (var name in IndexesOnHistoricallyAddedColumns)
            {
                Assert.Contains(name, result.AddedIndexes);
            }

            // 4) 既有行必须原样保留（补列只是 ADD COLUMN，不动数据）
            Assert.Equal(1, await CountLegacyRowAsync(ctx));
        }
        finally
        {
            // 清掉本用例造的数据，避免影响其它断言计数的用例
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM BarcodeData WHERE Barcode = '" + LegacyRowBarcode + "'");
        }
    }

    /// <summary>同步必须幂等：第二次调用不再产生任何变更。</summary>
    [Fact]
    public async Task Sync_IsIdempotent()
    {
        using var ctx = new AppDbContext();
        await ctx.ApplySchemaSyncAsync();

        var second = await ctx.ApplySchemaSyncAsync();

        Assert.False(second.HasChanges);
        Assert.Empty(second.SkippedColumns);
    }

    /// <summary>
    /// 模型声明的**每一个**索引都必须真实存在于库中
    /// （通用防回归：日后新增索引若没被同步到就失败）。
    /// </summary>
    [Fact]
    public async Task Sync_CreatesEveryIndexDeclaredInModel()
    {
        using var ctx = new AppDbContext();
        await ctx.ApplySchemaSyncAsync();

        var actual = await ReadNamesAsync(ctx, "PRAGMA index_list(BarcodeData)");
        var expected = ModelIndexNames(ctx);

        Assert.NotEmpty(expected);
        foreach (var name in expected)
        {
            Assert.Contains(name, actual);
        }
    }

    /// <summary>
    /// 为追溯/AI 问答加的三个复合索引 + 条码索引必须在模型里声明
    /// （把「关键索引缺失」钉死：删掉声明本用例即失败）。
    /// </summary>
    [Fact]
    public void Model_DeclaresCompositeIndexesForTraceabilityQueries()
    {
        using var ctx = new AppDbContext();
        var expected = ModelIndexNames(ctx);

        // 查询形态：WHERE DetectTime >= @since AND <维度> = @v ORDER BY DetectTime DESC
        Assert.Contains("IX_BarcodeData_RecipeName_DetectTime", expected);
        Assert.Contains("IX_BarcodeData_Station_DetectTime", expected);
        Assert.Contains("IX_BarcodeData_Result_DetectTime", expected);
        // 条码：等值定位 + 宽表上的索引覆盖扫描
        Assert.Contains("IX_BarcodeData_Barcode", expected);
    }

    // ── helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// 把库退回"升级前"的形状：删掉历史上补过的那批可空列，以及依赖它们的复合索引。
    ///
    /// <para>为什么不是整表重建：模型里的**非空**列（WorldX / EncodeTime / Score …）是初版就有、
    /// 且按设计**不能**靠 ALTER 补的（SQLite 要求新列可空或有默认值）。若把整表退化成只有 4 列，
    /// 同步会（正确地）拒绝补那 20 个非空列，测试就失真了 —— 这一点正是第一版用例踩过的坑。</para>
    /// </summary>
    private static async Task RevertToLegacyShapeAsync(AppDbContext ctx)
    {
        foreach (var name in IndexesOnHistoricallyAddedColumns)
        {
            await ctx.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS " + name);
        }

        foreach (var column in HistoricallyAddedColumns)
        {
            await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData DROP COLUMN " + column);
        }
    }

    private static async Task<long> CountLegacyRowAsync(AppDbContext ctx)
    {
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM BarcodeData WHERE Barcode = $barcode";
        var param = cmd.CreateParameter();
        param.ParameterName = "$barcode";
        param.Value = LegacyRowBarcode;
        cmd.Parameters.Add(param);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>执行 PRAGMA 并收集第 2 列（名称列）。</summary>
    private static async Task<HashSet<string>> ReadNamesAsync(AppDbContext ctx, string pragma)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = ctx.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = pragma;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static IEnumerable<string> ModelColumnNames(AppDbContext ctx) =>
        ctx.Model.FindEntityType(typeof(DbModel))!.GetProperties().Select(p => p.GetColumnName());

    private static List<string> ModelIndexNames(AppDbContext ctx)
    {
        var entityType = ctx.Model.FindEntityType(typeof(DbModel))!;
        return entityType.GetIndexes()
            .Select(i => $"IX_{entityType.GetTableName()}_{string.Join("_", i.Properties.Select(p => p.GetColumnName()))}")
            .ToList();
    }
}
