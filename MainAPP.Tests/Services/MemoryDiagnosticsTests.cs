using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// MemoryDiagnostics 静态工具单元测试。
/// 覆盖 EstimateImageSize、GetCurrentMemoryUsage 格式、RunPeriodicSnapshotAsync 取消与参数校验。
/// 注意：不测试实际的 Serilog 输出（仅验证不抛异常）。
/// </summary>
public class MemoryDiagnosticsTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 3)]
    [InlineData(100, 200, 60000)]
    [InlineData(1920, 1080, 6220800)]
    public void EstimateImageSize_ReturnsWidthTimesHeightTimes3(int width, int height, long expectedBytes)
    {
        var size = MemoryDiagnostics.EstimateImageSize(width, height);

        Assert.Equal(expectedBytes, size);
    }

    [Fact]
    public void GetCurrentMemoryUsage_ReturnsNonEmptyString()
    {
        var usage = MemoryDiagnostics.GetCurrentMemoryUsage();

        Assert.False(string.IsNullOrEmpty(usage));
        Assert.Contains("WorkingSet=", usage);
        Assert.Contains("Managed=", usage);
        Assert.Contains("MB", usage);
    }

    [Fact]
    public void LogSnapshot_DoesNotThrow()
    {
        // 仅验证日志路径不抛异常
        var exception = Record.Exception(() => MemoryDiagnostics.LogSnapshot("test"));
        Assert.Null(exception);
    }

    [Fact]
    public void LogAllocation_WithExtra_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogAllocation("test", 1024 * 1024, "extra info"));
        Assert.Null(exception);
    }

    [Fact]
    public void LogAllocation_WithoutExtra_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogAllocation("test", 1024 * 1024));
        Assert.Null(exception);
    }

    [Fact]
    public void LogDeallocation_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogDeallocation("test", 1024 * 1024));
        Assert.Null(exception);
    }

    [Fact]
    public void LogQueueDepth_WithCapacity_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogQueueDepth("Q", 5, 10));
        Assert.Null(exception);
    }

    [Fact]
    public void LogQueueDepth_WithoutCapacity_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogQueueDepth("Q", 5));
        Assert.Null(exception);
    }

    [Fact]
    public void LogActiveTasks_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogActiveTasks("pool", 3));
        Assert.Null(exception);
    }

    [Fact]
    public void LogActiveTasks_WithForceLog_DoesNotThrow()
    {
        // P2-1: forceLog=true 跳过采样，确保关键节点（如退出摘要）必输出
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogActiveTasks("pool", 3, forceLog: true));
        Assert.Null(exception);
    }

    [Fact]
    public void LogCollectionSize_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogCollectionSize("col", 100));
        Assert.Null(exception);
    }

    [Fact]
    public void LogFrameSummary_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogFrameSummary(1, 1024, 2048, 4096, 5, 2, 50));
        Assert.Null(exception);
    }

    [Fact]
    public void LogFrameSummary_WithForceLog_DoesNotThrow()
    {
        // P2-1: forceLog=true 跳过采样，确保关键节点（如退出摘要）必输出
        var exception = Record.Exception(() =>
            MemoryDiagnostics.LogFrameSummary(1, 1024, 2048, 4096, 5, 2, 50, forceLog: true));
        Assert.Null(exception);
    }

    [Fact]
    public void LogForcedGC_DoesNotThrow()
    {
        var exception = Record.Exception(() => MemoryDiagnostics.LogForcedGC("test"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RunPeriodicSnapshotAsync_InvalidInterval_ThrowsArgumentOutOfRangeException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            MemoryDiagnostics.RunPeriodicSnapshotAsync(0, CancellationToken.None));
    }

    [Fact]
    public async Task RunPeriodicSnapshotAsync_NegativeInterval_ThrowsArgumentOutOfRangeException()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            MemoryDiagnostics.RunPeriodicSnapshotAsync(-1, CancellationToken.None));
    }

    [Fact]
    public async Task RunPeriodicSnapshotAsync_CancelledImmediately_StopsCleanly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = await Record.ExceptionAsync(() =>
            MemoryDiagnostics.RunPeriodicSnapshotAsync(60, cts.Token));

        Assert.Null(exception);
    }

    [Fact]
    public async Task RunPeriodicSnapshotAsync_CancelledAfterDelay_StopsCleanly()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var exception = await Record.ExceptionAsync(() =>
            MemoryDiagnostics.RunPeriodicSnapshotAsync(60, cts.Token));

        Assert.Null(exception);
    }
}
