using MainAPP.Services;
using Serilog;
using System.IO;
using System.Text;
using Xunit;

namespace MainAPP.Tests.Integration;

/// <summary>
/// TemporaryLog 集成测试。
/// 验证临时日志的文件创建、写入和关闭行为。
/// TemporaryLog 为静态类，在静态构造函数中创建 Serilog 文件日志，
/// 日志文件位于 AppContext.BaseDirectory/Saves/temp-logs/temp-log-{date}.txt。
/// 注意：CloseAndFlush 后 Log 字段不可再使用，这些测试验证初始化和写入行为。
/// </summary>
public class TemporaryLogIntegrationTests : IDisposable
{
    private readonly string _tempLogDir;

    public TemporaryLogIntegrationTests()
    {
        _tempLogDir = Path.Combine(AppContext.BaseDirectory, "Saves", "temp-logs");
    }

    public void Dispose()
    {
        // 不调用 CloseAndFlush，因为它是静态的且不可逆，会影响其他测试
        // 仅清理测试产生的日志文件
        try
        {
            if (Directory.Exists(_tempLogDir))
            {
                foreach (var file in Directory.GetFiles(_tempLogDir, "temp-log-*.txt"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
            // 清理忽略异常
        }
    }

    /// <summary>
    /// 以共享读取模式读取日志文件内容，避免 Serilog File sink 持有写句柄时读取失败
    /// </summary>
    private static string ReadLogFileWithShare(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Log_AfterClassInit_IsNotNull()
    {
        // 静态构造函数已执行（首次访问 TemporaryLog 时），Log 字段应已初始化
        Assert.NotNull(TemporaryLog.Log);
    }

    [Fact]
    public void Log_Write_CreatesLogFile()
    {
        // TemporaryLog.Log 在静态构造时已创建文件 sink
        // Serilog 的文件 sink 会在首次写入时创建文件
        TemporaryLog.Log?.Information("Test log message from integration test {Timestamp}", DateTime.Now);

        // Serilog File sink 默认同步写入，无需显式 Flush

        // 验证日志目录存在
        Assert.True(Directory.Exists(_tempLogDir));

        // 验证至少有一个日志文件
        var logFiles = Directory.GetFiles(_tempLogDir, "temp-log-*.txt");
        Assert.True(logFiles.Length > 0, "应至少创建一个日志文件");
    }

    [Fact]
    public void Log_DebugLevel_WritesDebugMessages()
    {
        // TemporaryLog 配置为 MinimumLevel.Debug
        TemporaryLog.Log?.Debug("Debug level test message {Id}", 42);

        var logFiles = Directory.GetFiles(_tempLogDir, "temp-log-*.txt");
        Assert.True(logFiles.Length > 0);

        // 读取最新的日志文件内容
        var latestLog = logFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
        var content = ReadLogFileWithShare(latestLog);

        // Debug 消息应被写入（MinimumLevel.Debug）
        Assert.Contains("Debug level test message", content);
        Assert.Contains("42", content);
    }

    [Fact]
    public void Log_MultipleWrites_AllPersisted()
    {
        // 连续写入多条日志，验证都能持久化
        for (int i = 0; i < 10; i++)
        {
            TemporaryLog.Log?.Information("Batch message #{Index}", i);
        }

        var logFiles = Directory.GetFiles(_tempLogDir, "temp-log-*.txt");
        Assert.True(logFiles.Length > 0);

        var latestLog = logFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
        var content = ReadLogFileWithShare(latestLog);

        // 所有消息都应被写入
        for (int i = 0; i < 10; i++)
        {
            Assert.Contains($"Batch message #{i}", content);
        }
    }

    [Fact]
    public void Log_MicrosoftLevelOverride_FiltersBelowWarning()
    {
        // TemporaryLog 配置了 MinimumLevel.Override("Microsoft", Warning)
        // 验证 Microsoft 命名空间下低于 Warning 的日志不会被写入
        var logger = TemporaryLog.Log!.ForContext("SourceContext", "Microsoft.AspNetCore");
        logger.Debug("This debug should be filtered out");
        logger.Information("This info should be filtered out");
        logger.Warning("This warning should be written {Marker}", "MS_WARN_TEST");

        var logFiles = Directory.GetFiles(_tempLogDir, "temp-log-*.txt");
        if (logFiles.Length > 0)
        {
            var latestLog = logFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
            var content = ReadLogFileWithShare(latestLog);

            // Warning 应被写入
            Assert.Contains("MS_WARN_TEST", content);
            // Debug 和 Info 不应出现（被 Override 过滤）
            Assert.DoesNotContain("This debug should be filtered out", content);
            Assert.DoesNotContain("This info should be filtered out", content);
        }
    }
}
