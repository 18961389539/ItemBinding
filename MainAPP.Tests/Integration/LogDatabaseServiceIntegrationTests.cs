using MainAPP.Services;
using Microsoft.Data.Sqlite;
using System.IO;
using Xunit;

namespace MainAPP.Tests.Integration;

/// <summary>
/// LogDatabaseService 集成测试。
/// 验证日志数据库的读取和清空操作。
/// 由于 Serilog SQLite sink 为异步批量写入，测试通过 ADO.NET 直接插入日志记录，
/// 绕过 Serilog 的缓冲机制，以验证 LoadLogsAsync/ClearLogsAsync 的 SQL 和解析逻辑。
/// 注意：LogDatabaseService 为单例，数据库路径固定在 Saves/DataBase/Logs.db。
/// </summary>
public class LogDatabaseServiceIntegrationTests : IAsyncDisposable
{
    private readonly LogDatabaseService _service = LogDatabaseService.Instance;
    private readonly string _dbPath;
    private readonly string _dbDir;

    public LogDatabaseServiceIntegrationTests()
    {
        // LogDatabaseService 使用的数据库路径：Saves/DataBase/Logs.db（AppContext.BaseDirectory 下）
        _dbDir = Path.Combine(AppContext.BaseDirectory, "Saves", "DataBase");
        _dbPath = Path.Combine(_dbDir, "Logs.db");
        Directory.CreateDirectory(_dbDir);
        // 每次测试前重建数据库，确保干净状态
        ResetDatabase();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        ResetDatabase();
    }

    private void ResetDatabase()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                SqliteConnection.ClearAllPools();
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // 测试清理忽略异常
        }
    }

    /// <summary>
    /// 创建 Logs 表并插入测试日志记录
    /// </summary>
    private void InsertTestLogs(int count)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        // 创建 Logs 表，结构与 Serilog SQLite sink 一致
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Logs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Level TEXT NOT NULL,
                    Exception TEXT,
                    RenderedMessage TEXT NOT NULL,
                    Properties TEXT
                )
                """;
            cmd.ExecuteNonQuery();
        }

        // 批量插入测试记录
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO Logs (Timestamp, Level, Exception, RenderedMessage, Properties)
                VALUES (@ts, @level, @ex, @msg, @props)
                """;
            for (int i = 0; i < count; i++)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("@ts", $"2026-07-15 10:00:{i:00}");
                cmd.Parameters.AddWithValue("@level", i % 2 == 0 ? "INFO" : "ERROR");
                cmd.Parameters.AddWithValue("@ex", i % 2 == 0 ? DBNull.Value : "TestException");
                cmd.Parameters.AddWithValue("@msg", $"Test message #{i}");
                cmd.Parameters.AddWithValue("@props", $"{{\"Index\":{i}}}");
                cmd.ExecuteNonQuery();
            }
        }
    }

    [Fact]
    public async Task LoadLogsAsync_EmptyDatabase_ReturnsEmptyList()
    {
        // 数据库文件不存在时返回空列表
        var logs = await _service.LoadLogsAsync();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task LoadLogsAsync_WithRecords_ReturnsOrderedByIdDesc()
    {
        InsertTestLogs(5);

        var logs = await _service.LoadLogsAsync();

        // Serilog SQLite sink 按 id DESC 排序，最新记录在前
        Assert.Equal(5, logs.Count);
        // id=5 的记录 Timestamp 为 10:00:04，应排在首位
        Assert.Contains("Test message #4", logs[0].RenderedMessage);
    }

    [Fact]
    public async Task LoadLogsAsync_RespectsLimitParameter()
    {
        InsertTestLogs(20);

        var logs = await _service.LoadLogsAsync(limit: 5);

        Assert.Equal(5, logs.Count);
    }

    [Fact]
    public async Task LoadLogsAsync_ParsesTimestampAsLocalTime()
    {
        InsertTestLogs(1);

        var logs = await _service.LoadLogsAsync();

        Assert.Single(logs);
        // H90a: ParseTimestamp 使用 SpecifyKind 指定为 Local，不应进行时区转换
        Assert.Equal(DateTimeKind.Local, logs[0].Timestamp.Kind);
    }

    [Fact]
    public async Task LoadLogsAsync_NormalizesLevelNames()
    {
        // 插入不同级别的日志，验证 NormalizeLevel 的映射
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS Logs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Level TEXT NOT NULL,
                    Exception TEXT,
                    RenderedMessage TEXT NOT NULL,
                    Properties TEXT
                )
                """;
            cmd.ExecuteNonQuery();
        }

        var testCases = new[]
        {
            ("INFORMATION", "INFO"),
            ("WARN", "WARNING"),
            ("WARNING", "WARNING"),
            ("VERBOSE", "DEBUG"),
            ("ERROR", "ERROR"),
            ("DEBUG", "DEBUG"),
            ("INFO", "INFO"),
        };

        foreach (var (input, _) in testCases)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Logs (Timestamp, Level, Exception, RenderedMessage, Properties)
                VALUES (@ts, @level, '', @msg, '')
                """;
            cmd.Parameters.AddWithValue("@ts", "2026-07-15 10:00:00");
            cmd.Parameters.AddWithValue("@level", input);
            cmd.Parameters.AddWithValue("@msg", $"Level test: {input}");
            cmd.ExecuteNonQuery();
        }

        var logs = await _service.LoadLogsAsync();

        Assert.Equal(testCases.Length, logs.Count);
        // 验证每条记录的 Level 已被规范化（按 id DESC，倒序匹配）
        for (int i = 0; i < testCases.Length; i++)
        {
            var expected = testCases[testCases.Length - 1 - i].Item2;
            Assert.Equal(expected, logs[i].Level);
        }
    }

    [Fact]
    public async Task ClearLogsAsync_RemovesAllRecords()
    {
        InsertTestLogs(10);

        var beforeCount = (await _service.LoadLogsAsync()).Count;
        Assert.Equal(10, beforeCount);

        await _service.ClearLogsAsync();

        var afterCount = (await _service.LoadLogsAsync()).Count;
        Assert.Equal(0, afterCount);
    }

    [Fact]
    public async Task ClearLogsAsync_EmptyDatabase_DoesNotThrow()
    {
        // 数据库不存在时 ClearLogsAsync 不抛异常
        var exception = await Record.ExceptionAsync(() => _service.ClearLogsAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task LoadLogsAsync_HandlesCorruptDatabaseGracefully()
    {
        // 写入无效数据（非数据库文件），验证 SqliteException 被捕获
        File.WriteAllBytes(_dbPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });

        var exception = await Record.ExceptionAsync(() => _service.LoadLogsAsync());
        // LoadLogsAsync 内部 catch SqliteException 返回空列表，不向上抛
        Assert.Null(exception);
    }
}
