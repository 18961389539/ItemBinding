using MainAPP.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.IO;

namespace MainAPP.Models
{
    /// <summary>
    /// EF Core 数据库上下文，用于访问 Serilog SQLite Sink 写入的日志数据库。
    /// 数据库文件位于应用目录下的 Saves/DataBase/Logs.db。
    /// 表结构由 Serilog SQLite Sink 创建，列：id, Timestamp, Level, Exception, RenderedMessage, Properties。
    /// 注意：本上下文不调用 EnsureCreatedAsync，表结构由 Serilog Sink 维护；
    /// 仅用于查询/删除已存在的日志记录，不写入新日志（写入仍由 Serilog 负责）。
    /// </summary>
    public class LogDbContext : DbContext
    {
        /// <summary>
        /// 日志数据集，映射到 Serilog SQLite Sink 创建的 Logs 表
        /// </summary>
        public DbSet<LogEntry> Logs => Set<LogEntry>();

        /// <summary>
        /// 配置数据库连接，使用 SQLite 并指向统一数据根（<see cref="DataPaths.Root"/>）的 Saves/DataBase/Logs.db。
        /// 不在此处创建目录：Serilog 首次写入时自行创建，未写入前查询应返回空。
        /// 2026-09-15 起不再固定写死在 exe 目录。
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = DataPaths.LogDatabase;
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        /// <summary>
        /// 配置模型：映射到 Serilog SQLite Sink 创建的 Logs 表。
        /// Timestamp 列为 TEXT，存储格式为 "yyyy-MM-dd HH:mm:ss.fff"（本地时间），
        /// 通过值转换器在 DateTime 与 TEXT 之间双向转换，保证 LINQ 查询正确翻译为字符串比较。
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LogEntry>(entity =>
            {
                entity.ToTable("Logs");
                entity.HasKey(e => e.Id);

                // Serilog SQLite Sink 将 DateTime 存储为 "yyyy-MM-dd HH:mm:ss.fff" 格式的 TEXT。
                // 使用值转换器在 DateTime 与 TEXT 之间双向转换：
                // - 写入时 DateTime → 字符串
                // - 读取时字符串 → DateTime（SpecifyKind 为 Local，避免时区转换）
                // - LINQ 查询中带 Timestamp 的比较会被翻译为字符串比较，与原 raw SQL 语义一致
                entity.Property(e => e.Timestamp)
                    .HasConversion(
                        v => v.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                        v => ParseTimestamp(v))
                    .HasColumnType("TEXT");

                entity.Property(e => e.Level).HasColumnType("TEXT");
                // M288b: Serilog SQLite Sink 中 Exception/Properties 允许 NULL，标记 IsRequired(false)
                // 与 CLR 属性 string? 类型匹配，避免 EF Core 读取 NULL 时抛 InvalidOperationException
                entity.Property(e => e.Exception).HasColumnType("TEXT").IsRequired(false);
                entity.Property(e => e.RenderedMessage).HasColumnType("TEXT");
                entity.Property(e => e.Properties).HasColumnType("TEXT").IsRequired(false);

                // 忽略 LogEntry 上的计算属性与 UI 属性（不在 Logs 表中）
                entity.Ignore(e => e.Message);
                entity.Ignore(e => e.TimestampText);
                entity.Ignore(e => e.FormattedException);
                entity.Ignore(e => e.Color);
                entity.Ignore(e => e.LevelDisplay);
                entity.Ignore(e => e.LevelFullName);

                // 2026-09-12: Timestamp 索引声明（实际创建见 EnsureTimestampIndexAsync）——
                // Serilog Sink 建表只带 id 主键，无 Timestamp 索引；
                // 保留期清理（DeleteOlderThanAsync）与时间范围查询在无索引时是全表扫描，
                // 日志行数随保留期调大后（600 万行级）扫描会显著变慢。
                entity.HasIndex(e => e.Timestamp)
                    .HasDatabaseName("IX_Logs_Timestamp");
            });
        }

        /// <summary>
        /// 确保 Logs 表存在 Timestamp 索引（幂等，IF NOT EXISTS）。
        ///
        /// <para>为什么需要：表结构由 Serilog Sink 维护（本上下文不调用 EnsureCreatedAsync），
        /// Sink 建表只带 id 主键。保留期调大（7 天 @ 3 帧/s ≈ 日均 26 万行）后，
        /// DataRetentionService 的按时间清理与本上下文的时间范围查询都是 Timestamp 上的
        /// 全表扫描——索引将其降为索引范围扫描。EF 的 HasIndex 声明不会应用到已存在的库，
        /// 故此方法在启动/清理前显式执行一次 CREATE INDEX IF NOT EXISTS。</para>
        /// </summary>
        public async System.Threading.Tasks.Task EnsureTimestampIndexAsync(
            System.Threading.CancellationToken cancellationToken = default)
        {
            await Database.ExecuteSqlRawAsync(
                "CREATE INDEX IF NOT EXISTS IX_Logs_Timestamp ON Logs (Timestamp);",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 解析 Serilog SQLite Sink 存储的时间戳字符串。
        /// H90a: storeTimestampInUtc:false，存储的是本地时间。
        /// <summary>
        /// 解析时间戳字符串。
        /// 使用 DateTimeStyles.None 原样解析，不调用 ToLocalTime，并用 SpecifyKind 指定为 Local，
        /// 避免将本地时间误当 UTC 解析后再 ToLocalTime 导致显示晚 8 小时。
        /// 解析失败时返回 DateTime.Now 作为兜底，避免 MinValue 污染下游排序与图表显示 0001 年。
        /// </summary>
        private static DateTime ParseTimestamp(string? timestamp)
        {
            if (DateTime.TryParse(timestamp, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedTimestamp))
            {
                return DateTime.SpecifyKind(parsedTimestamp, DateTimeKind.Local);
            }

            // L104: 解析失败返回当前时间，避免 MinValue 污染下游排序与图表显示
            return DateTime.Now;
        }
    }
}
