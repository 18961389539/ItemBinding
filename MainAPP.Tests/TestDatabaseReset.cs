using MainAPP.Services;
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace MainAPP.Tests;

/// <summary>
/// 测试进程的数据根隔离 + 持久化测试库重置。
///
/// <para><b>为什么需要数据根隔离（2026-09-15 新增）</b>：数据根改造后，<see cref="DataPaths.Root"/>
/// 是按<b>机器</b>解析的（<c>D:\ProgramData\ItemBinding</c>），不再跟随可执行文件目录。
/// 若不覆盖，集成测试会直接读写——并删除——<b>真实的生产数据</b>。
/// 因此这里在模块初始化阶段就把数据根指向测试 bin 下的独立目录。</para>
///
/// <para><b>为什么需要重置库</b>：集成测试依赖单例 <c>BarcodeDataService</c>，库文件持久存在。
/// 测试只追加不清理，库文件逐轮膨胀——实测曾达 <b>14 GB / 6907 万行</b>，
/// 使全量测试从 9 秒劣化到 <b>40 分钟</b>（且逐轮递增：9min → 18min → 40min）。</para>
/// <para><b>为什么不在 DisposeAsync 里清理</b>：各测试类的 <c>ClearAllAsync</c> 在库变小前
/// 有效，但一旦膨胀，"删除 6900 万行"本身就是慢查询并会超时，而它被
/// <c>catch { }</c> 静默吞掉 —— 于是清理永远不生效，增长彻底失控。
/// 改为"每轮启动即删库重建"后，增长被从根上限定，且每轮都是全新 schema。</para>
///
/// <para>安全性：数据根被隔离到测试 bin 目录，绝不触碰真实业务库；
/// 删库后 schema 由 <c>EnsureCreatedAsync</c> 重建
/// （见 <see cref="MainAPP.Models.AppDbContext.ApplySchemaSyncAsync"/>：它在表缺失时会先 EnsureCreated，
/// 再按模型补齐列与索引）。</para>
///
/// <para>注意：本类型是测试程序集中唯一的 <see cref="ModuleInitializerAttribute"/>，
/// 数据根覆盖与删库必须在同一个初始化器里完成——多个 ModuleInitializer 之间执行顺序未定义，
/// 若拆开可能先读路径后设变量。</para>
/// </summary>
internal static class TestDatabaseReset
{
    [ModuleInitializer]
    internal static void Reset()
    {
        // 第一步：把数据根隔离到测试 bin 目录，必须先于任何 DataPaths 访问
        Environment.SetEnvironmentVariable(
            DataPaths.DataRootEnvironmentVariable,
            Path.Combine(AppContext.BaseDirectory, "TestDataRoot"));

        var dbDir = DataPaths.DatabaseDir;
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
