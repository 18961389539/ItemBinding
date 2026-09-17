using MainAPP.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Models
{
    /// <summary>
    /// EF Core 数据库上下文，管理条码检测记录的持久化。
    /// 使用 SQLite，数据库文件位于统一数据根（<see cref="DataPaths.Root"/>）的 Saves\DataBase\barcode_data.db。
    ///
    /// <para><b>结构演进机制（2026-09-16 重构）</b>：本类在 <see cref="OnModelCreating"/> 中声明的
    /// **列与索引即为唯一来源**；<see cref="ApplySchemaSyncAsync"/> 负责把已存在的库对齐到这个模型
    /// （补缺失的可空列、补缺失的索引），因此"给老库加列/加索引"不再需要手写 ALTER 或新增 EnsureXxx 方法。
    /// 重构前是两套手写方法（<c>EnsureBrightnessColumnsAsync</c> / <c>EnsureTraceColumnsAsync</c> /
    /// <c>EnsureIndexesAsync</c>），新增字段必须记得去改它们并加调用，漏掉就是运行期 "no such column"。</para>
    /// </summary>
    public class AppDbContext : DbContext
    {
        // L39: OnConfiguring 会被 EF Core 内部多次调用，Directory.CreateDirectory 只需执行一次
        private static int _directoryEnsured;

        /// <summary>
        /// 条码检测记录数据集
        /// </summary>
        public DbSet<DbModel> BarcodeData { get; private set; } = null!;

        /// <summary>
        /// 配置数据库连接，使用 SQLite 并将数据库文件存放在统一数据根（<see cref="DataPaths.Root"/>）的
        /// DataBase 子目录下。2026-09-15 起不再固定写死在 exe 目录。
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbDir = DataPaths.DatabaseDir;
            // L39: 使用 Interlocked.CompareExchange 确保目录创建只执行一次，避免重复的系统调用
            if (Interlocked.CompareExchange(ref _directoryEnsured, 1, 0) == 0)
            {
                Directory.CreateDirectory(dbDir);
            }
            var dbPath = DataPaths.BarcodeDatabase;
            // REVIEW-FIX: 连接串显式设置 Default Timeout=30（秒），Microsoft.Data.Sqlite 打开连接时
            // 据此执行 PRAGMA busy_timeout=30000，避免多写并发（检测入队 flush / 清库 / 批量删除 /
            // CSV 导入）时立即抛 SQLITE_BUSY 导致已出队记录静默丢失。
            optionsBuilder.UseSqlite($"Data Source={dbPath};Default Timeout=30");
        }

        /// <summary>
        /// 配置模型：设置主键、默认值和索引。★ 本方法是**列与索引的唯一来源**，
        /// <see cref="ApplySchemaSyncAsync"/> 会据此把已存在的库补齐（详见该方法的注释）。
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 主键配置
            modelBuilder.Entity<DbModel>()
                .HasKey(m => m.Id);

            // DetectTime 数据库列默认值（仅直接 SQL 插入且未指定该列时生效）。
            // EF 落库时 DbModel.DetectTime 带 C# 初始化器 DateTime.Now，始终发送实体值，
            // 数据库默认值实际不参与 EF 写入路径，见 DbModel.DetectTime 注释。
            modelBuilder.Entity<DbModel>()
                .Property(m => m.DetectTime)
                .HasDefaultValueSql("datetime('now', 'localtime')");

            // ── 索引按「真实查询形态」选，不是按字段好看 ─────────────────────────
            // 三组查询形态（依据 Services/AI/AiChatService.cs 与 Services/BarcodeDataService.cs）：
            //   ① 时间范围 + ORDER BY DetectTime DESC（列表/图表/日报/清理）
            //   ② 时间范围 + 维度等值过滤（AI 问答：按配方 / 工位 / 结果统计）
            //   ③ 按条码查过站记录

            // ① 时间范围 + 倒序分页：几乎所有列表查询都吃它
            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.DetectTime);

            // 编码器序号范围（GetByEncodeRangeAsync）
            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.Encode);

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.Angle);

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.BarcodeScore);

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.Score);

            // ② 复合索引 (维度, DetectTime)。
            // 查询形态统一是 WHERE DetectTime >= @since AND <维度> = @v ORDER BY DetectTime DESC，
            // 复合索引可同时覆盖过滤与排序，避免 SQLite 先按维度取全量再排序。
            // 列顺序必须"等值列在前、范围列在后"，否则索引退化为只用第一列。
            modelBuilder.Entity<DbModel>()
                .HasIndex(m => new { m.RecipeName, m.DetectTime });

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => new { m.Station, m.DetectTime });

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => new { m.Result, m.DetectTime });

            // ③ 条码。两种查询收益不同，别以为它能救下所有条码检索：
            //   - 等值查询（x.Barcode == code，AI 追溯问答）→ 用索引直接定位，收益最大；
            //   - 模糊查询（d.Barcode.Contains(x) → LIKE '%x%'，数据库页检索）→ 前导通配符**无法**用索引定位，
            //     但本表宽（约 25 列，含 HeadFeatures 等长文本），有索引后 SQLite 可走"索引覆盖扫描"，
            //     只读条码列而不逐行读整行，仍显著优于全表扫描。
            //     若模糊检索日后成为瓶颈，正解是引入 FTS5 虚表，或把检索改成前缀匹配（LIKE 'x%' 可用索引）。
            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.Barcode);
        }

        /// <summary>
        /// 幂等地把「EF 模型」与「实际库结构」对齐（2026-09-16）。
        ///
        /// <para><b>解决什么问题</b>：原先新增一个列要三步人肉操作——改 <see cref="DbModel"/>、
        /// 在 <c>EnsureBrightnessColumnsAsync</c>/<c>EnsureTraceColumnsAsync</c> 里手写一段
        /// <c>if (!cols.Contains("X")) ALTER TABLE ...</c>、再去 <c>App.InitializeDatabaseAsync</c>
        /// 里加一次调用。漏任何一步，症状都是**运行期 INSERT 报 "no such column"**。
        /// 现在模型是唯一来源：模型里有的列与索引，本方法负责让库里也有。</para>
        ///
        /// <para><b>为什么不用 EF Migrations</b>：本项目要离线部署，且现场已有大批由
        /// <c>EnsureCreated</c> 建出来的库（没有 <c>__EFMigrationsHistory</c> 表）。
        /// 引入 Migrations 首先要给每个已部署站点的库打 baseline（把首个迁移标记为"已应用"），
        /// 那一步比现在的补列更依赖人肉，还要引入 Design 期依赖。</para>
        ///
        /// <para><b>能力边界（务必知晓）</b>：只处理<b>增量</b>——补缺失的列、补缺失的索引。
        /// <b>删列 / 改名 / 改类型 / 改约束一律不处理</b>，这类变更需要显式迁移步骤，
        /// 项目内既有先例是 <c>App.MigrateLegacyAngleDomainAsync</c>（[0,360) → (-180,180] 的历史数据改写）。
        /// 另外，非空且无默认值的列无法自动补（SQLite 的 ALTER ADD COLUMN 对已有行的表要求新列可空或有默认值），
        /// 这类列会记入 <see cref="SchemaSyncResult.SkippedColumns"/> 并打 Error 日志，**绝不静默跳过**。</para>
        /// </summary>
        /// <returns>本次实际发生的变更，供调用方写日志、供测试断言。</returns>
        public async Task<SchemaSyncResult> ApplySchemaSyncAsync(CancellationToken cancellationToken = default)
        {
            var entityType = Model.FindEntityType(typeof(DbModel))
                ?? throw new InvalidOperationException("DbModel 未注册到 EF 模型，无法同步库结构。");
            var table = entityType.GetTableName() ?? nameof(BarcodeData);

            // ── 1) 保证表存在 ─────────────────────────────────────────────
            var existingColumns = await GetColumnNamesAsync(table, cancellationToken).ConfigureAwait(false);
            if (existingColumns.Count == 0)
            {
                // 新库：EnsureCreated 按模型建全表（幂等：库中已有任何表时直接返回 false，不做改动）
                await Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
                existingColumns = await GetColumnNamesAsync(table, cancellationToken).ConfigureAwait(false);
            }

            if (existingColumns.Count == 0)
            {
                // 库文件里有别的表时 EnsureCreated 不会补建本表。此处宁可抛出可读异常，
                // 也不让后续 ALTER 抛 "no such table" 那种无从定位的错误。
                throw new InvalidOperationException(
                    $"表 {table} 不存在且无法自动创建（库中可能已有其它表导致 EnsureCreated 跳过）。请人工检查数据库文件。");
            }

            // ── 2) 补缺失的列（模型 → 库）────────────────────────────────
            var addedColumns = new List<string>();
            var skippedColumns = new List<string>();

            foreach (var property in entityType.GetProperties())
            {
                var column = property.GetColumnName();
                if (existingColumns.Contains(column))
                {
                    continue;
                }

                // SQLite 的 ALTER TABLE ADD COLUMN 对已有行的表要求新列"可空或有默认值"
                var hasDefault = property.GetDefaultValue() is not null
                    || !string.IsNullOrEmpty(property.GetDefaultValueSql());
                if (!property.IsNullable && !hasDefault)
                {
                    skippedColumns.Add(column);
                    continue;
                }

                var sqliteType = MapToSqliteType(property.ClrType);
                if (sqliteType is null)
                {
                    skippedColumns.Add(column);
                    continue;
                }

                await ExecuteDdlAsync(
                    $"ALTER TABLE {RequireSafeIdentifier(table)} ADD COLUMN {RequireSafeIdentifier(column)} {sqliteType} NULL",
                    cancellationToken).ConfigureAwait(false);
                addedColumns.Add(column);
            }

            // ── 3) 补缺失的索引（模型 → 库）──────────────────────────────
            var existingIndexes = await GetIndexNamesAsync(table, cancellationToken).ConfigureAwait(false);
            var addedIndexes = new List<string>();

            foreach (var index in entityType.GetIndexes())
            {
                var columns = index.Properties.Select(p => p.GetColumnName()).ToList();
                // 与 EF 默认命名约定一致（IX_{表}_{列}），保证新库(EnsureCreated)与老库(本方法补齐)索引同名
                var name = $"IX_{table}_{string.Join("_", columns)}";
                if (existingIndexes.Contains(name))
                {
                    continue;
                }

                await ExecuteDdlAsync(
                    $"CREATE INDEX IF NOT EXISTS {RequireSafeIdentifier(name)} ON {RequireSafeIdentifier(table)} " +
                    $"({string.Join(", ", columns.Select(RequireSafeIdentifier))})",
                    cancellationToken).ConfigureAwait(false);
                addedIndexes.Add(name);
            }

            return new SchemaSyncResult(addedColumns, addedIndexes, skippedColumns);
        }

        /// <summary>
        /// CLR 类型 → SQLite 列类型。仅覆盖本项目实际使用的类型；
        /// 返回 null 表示无法映射（调用方会记入 SkippedColumns 并打 Error，不静默跳过）。
        /// </summary>
        private static string? MapToSqliteType(Type clrType)
        {
            var type = Nullable.GetUnderlyingType(clrType) ?? clrType;

            if (type == typeof(byte[]))
            {
                return "BLOB";
            }
            if (type == typeof(string) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
                || type == typeof(decimal) || type == typeof(Guid))
            {
                return "TEXT";
            }
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(bool))
            {
                return "INTEGER";
            }
            if (type == typeof(double) || type == typeof(float))
            {
                return "REAL";
            }

            return null;
        }

        /// <summary>
        /// 执行含<b>动态标识符</b>（表名/列名/索引名）的 DDL。
        ///
        /// <para><b>为什么要单独一个入口</b>：SQL 参数只能承载"值"，表名/列名/索引名这类**标识符无法参数化**，
        /// 只能拼进语句文本——这正是分析器 EF1002（"插值字符串直接进 SQL"）的告警点。
        /// 本方法的标识符全部来自 EF 模型（代码定义、非用户输入），并强制经
        /// <see cref="RequireSafeIdentifier"/> 白名单校验，因此集中在此执行一次，
        /// 既不必在每个调用点重复抑制告警，也不会让拼接散落在多处。</para>
        /// </summary>
        private Task ExecuteDdlAsync(string sql, CancellationToken cancellationToken) =>
            Database.ExecuteSqlRawAsync(sql, cancellationToken);

        /// <summary>标识符白名单校验：仅允许字母/数字/下划线且不以数字开头，否则直接抛错。</summary>
        private static string RequireSafeIdentifier(string identifier)
        {
            var ok = !string.IsNullOrEmpty(identifier)
                && (char.IsLetter(identifier[0]) || identifier[0] == '_')
                && identifier.All(c => char.IsLetterOrDigit(c) || c == '_');
            if (!ok)
            {
                throw new InvalidOperationException(
                    $"拒绝执行含非法 SQL 标识符的 DDL：\"{identifier}\"（标识符应来自 EF 模型）。");
            }

            return identifier;
        }

        /// <summary>读取某表当前列名集合（SQLite 用 PRAGMA table_info 探测；表不存在时返回空集而非报错）。</summary>
        private Task<HashSet<string>> GetColumnNamesAsync(string table, CancellationToken cancellationToken) =>
            // 单行返回 "cid,name,type,notnull,dflt_value,pk"，name 是第 2 列
            ReadPragmaNameSetAsync($"PRAGMA table_info({table})", cancellationToken);

        /// <summary>读取某表当前索引名集合（PRAGMA index_list；表不存在时返回空集）。</summary>
        private Task<HashSet<string>> GetIndexNamesAsync(string table, CancellationToken cancellationToken) =>
            // 单行返回 "seq,name,unique,origin,partial"，name 是第 2 列
            ReadPragmaNameSetAsync($"PRAGMA index_list({table})", cancellationToken);

        /// <summary>
        /// 执行 PRAGMA 并把第 2 列（名称列）收成集合。连接按需打开/关闭，生命周期收敛在本方法内。
        /// </summary>
        private async Task<HashSet<string>> ReadPragmaNameSetAsync(string pragma, CancellationToken cancellationToken)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var conn = Database.GetDbConnection();
            var wasClosed = conn.State != System.Data.ConnectionState.Open;
            if (wasClosed)
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = pragma;
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (reader.FieldCount > 1)
                    {
                        names.Add(reader.GetString(1));
                    }
                }
            }
            finally
            {
                if (wasClosed)
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }

            return names;
        }
    }
}
