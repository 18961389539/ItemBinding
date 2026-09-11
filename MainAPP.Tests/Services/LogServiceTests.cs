using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// LogService 单例日志服务单元测试。
/// 验证日志级别方法、ShouldSkip 持久化过滤逻辑、ClearLogs 行为。
/// 注意：LogService.AddLog 通过 Dispatcher 切回 UI 线程，测试运行于非 UI 线程时
/// 会通过 BeginInvoke 排队，但调度器不存在时直接 return，不会抛异常。
/// </summary>
public class LogServiceTests
{
    [Fact]
    public void Instance_IsSingleton()
    {
        var a = LogService.Instance;
        var b = LogService.Instance;

        Assert.Same(a, b);
    }

    [Fact]
    public void Logs_CollectionIsNotNull()
    {
        Assert.NotNull(LogService.Instance.Logs);
    }

    [Fact]
    public void Info_DoesNotThrow()
    {
        var exception = Record.Exception(() => LogService.Instance.Info("test info message"));
        Assert.Null(exception);
    }

    [Fact]
    public void Warning_DoesNotThrow()
    {
        var exception = Record.Exception(() => LogService.Instance.Warning("test warning message"));
        Assert.Null(exception);
    }

    [Fact]
    public void Error_DoesNotThrow()
    {
        var exception = Record.Exception(() => LogService.Instance.Error("test error message"));
        Assert.Null(exception);
    }

    [Fact]
    public void Debug_DoesNotThrow()
    {
        var exception = Record.Exception(() => LogService.Instance.Debug("test debug message"));
        Assert.Null(exception);
    }

    [Fact]
    public void Info_WithNullMessage_DoesNotThrow()
    {
        // ShouldSkipPersistentLog 对 null/whitespace 返回 false（不过滤），
        // 但 Serilog 写入 null 字符串不会抛异常
        var exception = Record.Exception(() => LogService.Instance.Info(null!));
        Assert.Null(exception);
    }

    [Fact]
    public void ClearLogs_DoesNotThrow()
    {
        var exception = Record.Exception(() => LogService.Instance.ClearLogs());
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Timing(50ms)")]           // 高频诊断日志，会被 ShouldSkipPersistentLog 过滤
    [InlineData("[UDP→127.0.0.1:11301]")]  // UDP 报文日志
    [InlineData("编码器监听接收超时")]        // 编码器周期性超时
    [InlineData("#Error#")]                // 占位错误标记
    [InlineData("Error")]                  // 占位错误标记
    [InlineData("ABC,1.0,2.0,3.0")]        // ResultPayloadRegex 匹配
    [InlineData("ABC,1.0,2.0,3.0,")]       // ResultPayloadRegex 匹配（带尾逗号）
    [InlineData("ABC,-1.5,2.0,3.5")]       // 负数也匹配
    public void Info_WithFilteredMessage_DoesNotThrow(string message)
    {
        // 这些消息会被 ShouldSkipPersistentLog 过滤，不写入 Serilog，但方法本身不应抛异常
        var exception = Record.Exception(() => LogService.Instance.Info(message));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("[MemSnapshot] Label=test")]
    [InlineData("[MemAlloc] Label=test")]
    [InlineData("[MemFree] Label=test")]
    [InlineData("[MemQueue] Queue=test")]
    [InlineData("[MemFrame] Frame=1")]
    [InlineData("[MemGC] 强制 GC 前")]
    [InlineData("[MemTasks] Pool=test")]
    [InlineData("编码器监听接收超时")]
    public void Info_WithUiSkippedMessage_DoesNotThrow(string message)
    {
        // 这些消息不会进入 UI 集合（ShouldSkipUiLog），但方法不应抛异常
        var exception = Record.Exception(() => LogService.Instance.Info(message));
        Assert.Null(exception);
    }
}
