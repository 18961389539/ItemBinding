using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace MainAPP.Tests;

/// <summary>
/// 测试启动前重置持久化测试库（2026-09-13）。
/// <para><b>为什么需要</b>：集成测试依赖单例 <c>BarcodeDataService</c>，其库文件落在
/// <c>AppContext.BaseDirectory/DataBase/barcode_data.db</c>（即 bin 下的<b>持久</b>路径）。
/// 测试只追加不清理，库文件逐轮膨胀——实测曾达 <b>14 GB / 6907 万行</b>，
/// 使全量测试从 9 秒劣化到 <b>40 分钟</b>（且逐轮递增：9min → 18min → 40min）。</para>
/// <para><b>为什么不在 DisposeAsync 里清理</b>：各测试类的 <c>ClearAllAsync</c> 在库变小前
/// 有效，但一旦膨胀，"删除 6900 万行"本身就是慢查询并会超时，而它被
/// <c>catch { }</c> 静默吞掉 —— 于是清理永远不生效，增长彻底失控。
/// 改为"每轮启动即删库重建"后，增长被从根上限定，且每轮都是全新 schema。</para>
/// <para>安全性：仅作用于测试进程的 bin 目录，不触碰 MainAPP 的真实库；
/// 删库后 schema 由 <c>EnsureCreatedAsync</c> 重建
/// （见 <c>AppDbContext.EnsureTableAndGetColumnsAsync</c> 的自愈兜底）。</para>
/// </summary>
internal static class TestDatabaseReset
{
    [ModuleInitializer]
    internal static void Reset()
    {
        var dbDir = Path.Combine(AppContext.BaseDirectory, "DataBase");
        // 三个文件都要删：-wal/-shm 是 SQLite WAL 模式的伴生文件，
        // 只删主库会留下不一致的预写日志，导致下次打开时试图回放而报错。
        foreach (var name in new[] { "barcode_data.db", "barcode_data.db-wal", "barcode_data.db-shm" })
        {
            try
            {
                var path = Path.Combine(dbDir, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 重置失败不应阻止测试运行（例如文件被其他句柄占用）
            }
        }
    }
}
