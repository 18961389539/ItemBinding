using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// DataRootMigrator 单元测试（2026-09-15 数据根改造 + 数据库布局统一）。
///
/// 回归背景：数据从"可执行文件目录"迁到统一数据根后，若不搬运历史数据，
/// 现场升级后打开程序会看到空库——历史记录凭空消失。
/// 同时业务库从数据根的 <c>DataBase\</c> 统一到 <c>Saves\DataBase\</c>（与日志库同目录）。
///
/// 测试全部使用临时目录驱动可注入路径的重载，<b>不触碰真实数据根</b>。
/// 重点验证"宁慢勿丢"：只复制不删除、校验后才改名、失败不影响原数据。
/// </summary>
public sealed class DataRootMigratorTests : IDisposable
{
    private readonly string _sandbox;

    public DataRootMigratorTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "itembinding-migrator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (System.IO.IOException)
        {
            // 临时目录清理失败不影响测试结论
        }
    }

    /// <summary>
    /// 构造旧布局的历史数据根（等价于改造前的 exe 目录）：
    /// 业务库在根下的 <c>DataBase\</c>，日志库在 <c>Saves\DataBase\</c>。
    /// </summary>
    private string CreateLegacyRoot(string name = "legacy")
    {
        var legacy = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(Path.Combine(legacy, "DataBase"));
        Directory.CreateDirectory(Path.Combine(legacy, "Saves", "DataBase"));
        Directory.CreateDirectory(Path.Combine(legacy, "Saves", "Security"));
        Directory.CreateDirectory(Path.Combine(legacy, "logs"));

        File.WriteAllText(Path.Combine(legacy, "DataBase", "barcode_data.db"), "legacy-barcode");
        File.WriteAllText(Path.Combine(legacy, "Saves", "DataBase", "Logs.db"), "legacy-logs");
        File.WriteAllText(Path.Combine(legacy, "Saves", "Security", "users.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "logs", "log-20260915.txt"), "legacy-log");

        return legacy;
    }

    /// <summary>构造已是统一布局（无根级 <c>DataBase\</c>）的数据根。</summary>
    private string CreateUnifiedLayoutRoot(string name = "unified")
    {
        var root = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(Path.Combine(root, "Saves", "DataBase"));
        Directory.CreateDirectory(Path.Combine(root, "Saves", "Security"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));

        File.WriteAllText(Path.Combine(root, "Saves", "DataBase", "barcode_data.db"), "unified-barcode");
        File.WriteAllText(Path.Combine(root, "Saves", "DataBase", "Logs.db"), "unified-logs");
        File.WriteAllText(Path.Combine(root, "Saves", "Security", "users.json"), "{}");
        File.WriteAllText(Path.Combine(root, "logs", "log-20260915.txt"), "unified-log");

        return root;
    }

    private string NewTarget(string name = "target") => Path.Combine(_sandbox, name);

    /// <summary>目标为空、旧数据齐全时，应搬运成功并把原目录改名保留。</summary>
    [Fact]
    public void MigrateIfNeeded_TargetEmpty_CopiesDataAndRetiresSource()
    {
        var legacy = CreateLegacyRoot();
        var target = NewTarget();

        var report = DataRootMigrator.MigrateIfNeeded(target, legacy);

        Assert.NotNull(report);

        // 业务库与日志库统一到 Saves\DataBase\
        Assert.True(File.Exists(Path.Combine(target, "Saves", "DataBase", "barcode_data.db")));
        Assert.True(File.Exists(Path.Combine(target, "Saves", "DataBase", "Logs.db")));
        Assert.True(File.Exists(Path.Combine(target, "Saves", "Security", "users.json")));
        Assert.True(File.Exists(Path.Combine(target, "logs", "log-20260915.txt")));

        // 搬运的是旧根下的各子目录（旧根本身即 exe 目录，当然必须继续存在），
        // 子目录被改名而不是删除
        Assert.False(Directory.Exists(Path.Combine(legacy, "DataBase")));
        Assert.False(Directory.Exists(Path.Combine(legacy, "Saves")));
        Assert.False(Directory.Exists(Path.Combine(legacy, "logs")));
        Assert.True(Directory.Exists(Path.Combine(legacy, "DataBase.migrated")));
        Assert.True(Directory.Exists(Path.Combine(legacy, "Saves.migrated")));
        Assert.True(Directory.Exists(Path.Combine(legacy, "logs.migrated")));
    }

    /// <summary>布局统一：业务库不得再落在数据根下的 DataBase\，必须进 Saves\DataBase\。</summary>
    [Fact]
    public void MigrateIfNeeded_LegacyDatabaseDir_LandsInUnifiedDatabaseDir()
    {
        var legacy = CreateLegacyRoot();
        var target = NewTarget();

        DataRootMigrator.MigrateIfNeeded(target, legacy);

        Assert.False(Directory.Exists(Path.Combine(target, "DataBase")));
        Assert.Equal("legacy-barcode", File.ReadAllText(Path.Combine(target, "Saves", "DataBase", "barcode_data.db")));
    }

    /// <summary>核心安全承诺：迁移绝不删除数据，原文件在 .migrated 里仍可回滚。</summary>
    [Fact]
    public void MigrateIfNeeded_RetainsOriginalDataForRollback()
    {
        var legacy = CreateLegacyRoot();
        var target = NewTarget();

        DataRootMigrator.MigrateIfNeeded(target, legacy);

        var retiredDb = Path.Combine(legacy, "DataBase.migrated");
        var retiredSaves = Path.Combine(legacy, "Saves.migrated");
        Assert.Equal("legacy-barcode", File.ReadAllText(Path.Combine(retiredDb, "barcode_data.db")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(retiredSaves, "Security", "users.json")));
    }

    /// <summary>目标已有同名文件时不得覆盖（目标可能是更新的数据），但其余条目仍要照常搬运。</summary>
    [Fact]
    public void MigrateIfNeeded_TargetHasSameFile_DoesNotOverwriteAndStillMigratesRest()
    {
        var legacy = CreateLegacyRoot();
        var target = NewTarget();

        Directory.CreateDirectory(Path.Combine(target, "Saves", "DataBase"));
        File.WriteAllText(Path.Combine(target, "Saves", "DataBase", "barcode_data.db"), "existing-target");

        var report = DataRootMigrator.MigrateIfNeeded(target, legacy);

        // 目标已有文件保持原样，不被旧数据覆盖
        Assert.Equal("existing-target", File.ReadAllText(Path.Combine(target, "Saves", "DataBase", "barcode_data.db")));

        // 其余条目（Saves/logs 中的其他文件）仍完成搬运
        Assert.NotNull(report);
        Assert.True(File.Exists(Path.Combine(target, "Saves", "Security", "users.json")));
        Assert.True(File.Exists(Path.Combine(target, "logs", "log-20260915.txt")));
    }

    /// <summary>
    /// 回归防护：目标只有 <c>Saves\DataBase\Logs.db</c>（例如上次迁移中断后程序跑过一次留下的残留）时，
    /// 不能因此跳过整个 Saves——否则 Recipes 与 Security 会被漏搬。
    /// </summary>
    [Fact]
    public void MigrateIfNeeded_PartiallyPopulatedTarget_MigratesRemainingEntries()
    {
        var legacy = CreateLegacyRoot();
        var target = NewTarget();

        Directory.CreateDirectory(Path.Combine(target, "Saves", "DataBase"));
        File.WriteAllText(Path.Combine(target, "Saves", "DataBase", "Logs.db"), "existing-logs");

        DataRootMigrator.MigrateIfNeeded(target, legacy);

        Assert.True(File.Exists(Path.Combine(target, "Saves", "Security", "users.json")));
        Assert.Equal("existing-logs", File.ReadAllText(Path.Combine(target, "Saves", "DataBase", "Logs.db")));
    }

    /// <summary>
    /// 同根内旧布局归一：数据根下已有旧布局的 <c>DataBase\barcode_data.db</c>（已跨根迁移过的机器），
    /// 应合并进 <c>Saves\DataBase\</c> 并退役旧目录。
    /// </summary>
    [Fact]
    public void MigrateIfNeeded_InRootOldLayout_NormalizesIntoUnifiedDatabaseDir()
    {
        var target = NewTarget();

        // 模拟"已按旧布局建好数据根"的机器
        Directory.CreateDirectory(Path.Combine(target, "DataBase"));
        Directory.CreateDirectory(Path.Combine(target, "Saves", "DataBase"));
        File.WriteAllText(Path.Combine(target, "DataBase", "barcode_data.db"), "old-layout-barcode");
        File.WriteAllText(Path.Combine(target, "Saves", "DataBase", "Logs.db"), "keep-logs");

        // 旧根无数据，只有目标根内的旧布局需要归一
        var emptyLegacy = Path.Combine(_sandbox, "empty-legacy");
        Directory.CreateDirectory(emptyLegacy);

        var report = DataRootMigrator.MigrateIfNeeded(target, emptyLegacy);

        Assert.NotNull(report);
        Assert.Equal("old-layout-barcode", File.ReadAllText(Path.Combine(target, "Saves", "DataBase", "barcode_data.db")));
        Assert.Equal("keep-logs", File.ReadAllText(Path.Combine(target, "Saves", "DataBase", "Logs.db")));
        Assert.False(Directory.Exists(Path.Combine(target, "DataBase")));
        Assert.True(Directory.Exists(Path.Combine(target, "DataBase.migrated")));
    }

    /// <summary>数据根与旧根相同、且已是统一布局时，不做任何事。</summary>
    [Fact]
    public void MigrateIfNeeded_SameRootUnifiedLayout_ReturnsNull()
    {
        var root = CreateUnifiedLayoutRoot();

        var report = DataRootMigrator.MigrateIfNeeded(root, root);

        Assert.Null(report);
        Assert.True(File.Exists(Path.Combine(root, "Saves", "DataBase", "barcode_data.db")));
    }

    /// <summary>旧根没有任何数据时返回 null（全新安装的正常路径）。</summary>
    [Fact]
    public void MigrateIfNeeded_NoLegacyData_ReturnsNull()
    {
        var legacy = Path.Combine(_sandbox, "empty-legacy");
        Directory.CreateDirectory(legacy);

        var report = DataRootMigrator.MigrateIfNeeded(NewTarget(), legacy);

        Assert.Null(report);
    }

    /// <summary>幂等：连续调用第二次应无事可做。</summary>
    [Fact]
    public void MigrateIfNeeded_SecondCall_IsNoOp()
    {
        var legacy = CreateLegacyRoot();
        var target = NewTarget();

        var first = DataRootMigrator.MigrateIfNeeded(target, legacy);
        var second = DataRootMigrator.MigrateIfNeeded(target, legacy);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    /// <summary>空目录不算"有数据"，不应触发迁移。</summary>
    [Fact]
    public void MigrateIfNeeded_LegacyHasOnlyEmptyFolders_ReturnsNull()
    {
        var legacy = Path.Combine(_sandbox, "empty-dirs-legacy");
        Directory.CreateDirectory(Path.Combine(legacy, "DataBase"));
        Directory.CreateDirectory(Path.Combine(legacy, "Saves"));

        var report = DataRootMigrator.MigrateIfNeeded(NewTarget(), legacy);

        Assert.Null(report);
    }
}
