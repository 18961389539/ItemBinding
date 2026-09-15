using MainAPP.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Models
{
    /// <summary>
    /// EF Core 数据库上下文，管理条码检测记录的持久化。
    /// 使用 SQLite 数据库，数据库文件位于应用目录下的 DataBase/barcode_data.db。
    /// 为常用查询字段（DetectTime、Encode、Angle、BarcodeScore、Score）建立了索引以优化查询性能。
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
        /// 配置模型：设置主键、默认值和索引
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

            // 为常用查询字段建立索引，优化按时间、编码器、角度、置信度的查询性能
            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.DetectTime);

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.Encode);

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.Angle);

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.BarcodeScore);

            modelBuilder.Entity<DbModel>()
                .HasIndex(m => m.Score);
        }

        /// <summary>
        /// 为已存在的数据库补充关键索引。
        /// 使用 CREATE INDEX IF NOT EXISTS 确保幂等性，适用于数据库迁移或版本升级场景。
        /// L40: 改为异步执行避免阻塞调用线程
        /// </summary>
        public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
        {
            await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_BarcodeData_DetectTime ON BarcodeData (DetectTime)", cancellationToken).ConfigureAwait(false);
            await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_BarcodeData_Encode ON BarcodeData (Encode)", cancellationToken).ConfigureAwait(false);
            await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_BarcodeData_Angle ON BarcodeData (Angle)", cancellationToken).ConfigureAwait(false);
            await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_BarcodeData_BarcodeScore ON BarcodeData (BarcodeScore)", cancellationToken).ConfigureAwait(false);
            await Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_BarcodeData_Score ON BarcodeData (Score)", cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取 BarcodeData 列，并保证"表一定存在"（自愈兜底，2026-09-13）。
        /// <para><b>为什么需要它</b>：<see cref="GetBarcodeDataColumnsAsync"/> 在表不存在时
        /// 返回<b>空集而非报错</b>（PRAGMA 对不存在的表不抛异常），于是所有
        /// <c>!cols.Contains(x)</c> 判断都为真，紧接着的 <c>ALTER TABLE BarcodeData</c>
        /// 会因 "no such table: BarcodeData" 失败。这会让"库文件被删除/首次运行"
        /// 的场景直接崩在启动路径上——测试环境删除膨胀库后即复现。</para>
        /// <para><b>兜底方式</b>：空列集 ⇒ 表不存在 ⇒ 调 <c>EnsureCreatedAsync</c> 按模型建全表。
        /// 该调用幂等（库中已有任何表时直接返回 false，不做任何改动），故对既有库零影响。</para>
        /// </summary>
        private async Task<HashSet<string>> EnsureTableAndGetColumnsAsync(CancellationToken cancellationToken)
        {
            var cols = await GetBarcodeDataColumnsAsync(cancellationToken).ConfigureAwait(false);
            if (cols.Count == 0)
            {
                await Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
                cols = await GetBarcodeDataColumnsAsync(cancellationToken).ConfigureAwait(false);
            }

            return cols;
        }

        /// <summary>
        /// 2026-09-08: 为已存在的数据库幂等补充灰度判向统计列（BrightMean/DarkMean/BrightnessDiff）。
        /// EnsureCreatedAsync 仅在库不存在时建表，升级已有库不会补新列——若缺列，新写入的 INSERT
        /// 会因 "no such column" 失败。SQLite 用 PRAGMA table_info 探测列，缺哪列补哪列
        /// （double? → REAL 可空列，无需默认值；ALTER TABLE ADD COLUMN 对既有行自动为 NULL）。
        /// </summary>
        public async Task EnsureBrightnessColumnsAsync(CancellationToken cancellationToken = default)
        {
            var cols = await EnsureTableAndGetColumnsAsync(cancellationToken).ConfigureAwait(false);

            // 缺哪列补哪列（double? → REAL 可空列，无需默认值；ALTER ADD COLUMN 对既有行自动为 NULL）
            if (!cols.Contains("BrightMean"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN BrightMean REAL NULL", cancellationToken).ConfigureAwait(false);
            }
            if (!cols.Contains("DarkMean"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN DarkMean REAL NULL", cancellationToken).ConfigureAwait(false);
            }
            if (!cols.Contains("BrightnessDiff"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN BrightnessDiff REAL NULL", cancellationToken).ConfigureAwait(false);
            }

            // 2026-09-13: 标定标志列（IsCalibrated：WorldX/Y 与 Angle 是否标定坐标系下的真值；
            // 未标定时 WorldX/Y 为像素、Angle 为图像角的兜底值，下游可据此区分真值与假数值）
            if (!cols.Contains("IsCalibrated"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN IsCalibrated INTEGER NULL", cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 读取 BarcodeData 表当前列名集合（SQLite 用 PRAGMA table_info 探测）。
        /// 抽出来供多个「幂等补列」方法复用。
        /// </summary>
        private async Task<HashSet<string>> GetBarcodeDataColumnsAsync(CancellationToken cancellationToken)
        {
            // 单行返回 "列名,类型,非空,默认值,主键"（PRAGMA 逗号分隔）
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var conn = Database.GetDbConnection();
            var wasClosed = conn.State != System.Data.ConnectionState.Open;
            if (wasClosed)
            {
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(BarcodeData)";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    cols.Add(reader.GetString(1)); // cid,name,type,notnull,dflt_value,pk → name 是第 2 列
                }
            }
            finally
            {
                if (wasClosed)
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }

            return cols;
        }

        /// <summary>
        /// 2026-09-12: 为已存在的数据库幂等补充追溯列（RecipeName / Result / Station）。
        /// 与 EnsureBrightnessColumnsAsync 同一套模式：EnsureCreated 不补列，既有库必须显式 ALTER。
        /// 三列均为可空 TEXT——历史行自动为 NULL，表示「当时没记录」，语义上区别于空字符串。
        /// 这是 AI 对话「自然语言查追溯数据」能力的数据基础：没有这三列，就回答不了
        /// 「某配方的合格率」「某工位的过站记录」这类问题。
        /// </summary>
        public async Task EnsureTraceColumnsAsync(CancellationToken cancellationToken = default)
        {
            var cols = await EnsureTableAndGetColumnsAsync(cancellationToken).ConfigureAwait(false);

            if (!cols.Contains("RecipeName"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN RecipeName TEXT NULL", cancellationToken).ConfigureAwait(false);
            }
            if (!cols.Contains("Result"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN Result TEXT NULL", cancellationToken).ConfigureAwait(false);
            }
            if (!cols.Contains("Station"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN Station TEXT NULL", cancellationToken).ConfigureAwait(false);
            }
            if (!cols.Contains("HeadFeatures"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN HeadFeatures TEXT NULL", cancellationToken).ConfigureAwait(false);
            }
            // 2026-09-13: 头尾真值符号（特征池自检）。可空 INTEGER——SQLite 布尔以 0/1 存储，
            // NULL 表示本帧无真值（无码/码过近/码垂直于长轴/特征池未运行），语义上区别于 false。
            if (!cols.Contains("HeadTruthPositive"))
            {
                await Database.ExecuteSqlRawAsync("ALTER TABLE BarcodeData ADD COLUMN HeadTruthPositive INTEGER NULL", cancellationToken).ConfigureAwait(false);
            }
        }
    }
}