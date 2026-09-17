using MainAPP.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// 接收循环监督器测试（2026-09-16）。
///
/// <para><b>回归背景</b>：<c>OpsWebHost.AcceptLoopAsync</c> 原为 <c>catch (Exception) { break; }</c>，
/// 且外面只有一个裸 <c>_ = AcceptLoopAsync()</c>——<b>任何</b>一次意外异常都会让运维门户永久不再响应，
/// 现场只表现为"门户打不开"。监督器的职责就是：只要服务仍应运行，循环退出/抛异常后都要拉起来。</para>
///
/// <para>用例用 <c>TimeSpan.Zero</c>/极小退避，避免测试等待真实时间。</para>
/// </summary>
public class AcceptLoopSupervisorTests
{
    /// <summary>构造一个可控的监督器：<paramref name="runLoop"/> 每次被调用递增计数。</summary>
    private static AcceptLoopSupervisor Create(
        Action onRun,
        Func<bool> shouldKeepRunning,
        out List<(int Attempt, TimeSpan Delay)> restarts,
        out List<Exception> failures,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null)
    {
        var restartLog = new List<(int, TimeSpan)>();
        var failureLog = new List<Exception>();
        restarts = restartLog;
        failures = failureLog;

        return new AcceptLoopSupervisor(
            runLoop: () => { onRun(); return Task.CompletedTask; },
            shouldKeepRunning: shouldKeepRunning,
            onRestart: (attempt, delay) => restartLog.Add((attempt, delay)),
            onLoopFailure: failureLog.Add,
            initialDelay: initialDelay ?? TimeSpan.Zero,
            maxDelay: maxDelay ?? TimeSpan.FromMilliseconds(8));
    }

    [Fact]
    public async Task LoopReturns_WhileServiceShouldRun_RestartsUntilToldToStop()
    {
        var runs = 0;
        // 跑 3 次后把服务标记为"应停止"，监督器随即结束
        var supervisor = Create(() => runs++, () => runs < 3, out var restarts, out _);

        await supervisor.RunAsync(CancellationToken.None);

        Assert.Equal(3, runs);
        Assert.Equal(2, restarts.Count);                 // 3 次运行之间有 2 次重启
        Assert.Equal(1, restarts[0].Attempt);
        Assert.Equal(2, restarts[1].Attempt);
        Assert.Equal(2, supervisor.RestartCount);
    }

    [Fact]
    public async Task ServiceShouldNotRun_DoesNotRestart()
    {
        var runs = 0;
        var supervisor = Create(() => runs++, () => false, out var restarts, out _);

        await supervisor.RunAsync(CancellationToken.None);

        Assert.Equal(0, runs);          // 一次都不跑
        Assert.Empty(restarts);
    }

    [Fact]
    public async Task LoopThrows_IsReported_AndStillRestarts()
    {
        var runs = 0;
        var boom = new InvalidOperationException("listener 炸了");

        var restartLog = new List<(int, TimeSpan)>();
        var failureLog = new List<Exception>();
        var supervisor = new AcceptLoopSupervisor(
            runLoop: () =>
            {
                runs++;
                if (runs <= 2)
                {
                    throw boom;      // 前两次抛异常
                }

                return Task.CompletedTask;
            },
            shouldKeepRunning: () => runs < 3,
            onRestart: (attempt, delay) => restartLog.Add((attempt, delay)),
            onLoopFailure: failureLog.Add,
            initialDelay: TimeSpan.Zero,
            maxDelay: TimeSpan.FromMilliseconds(8));

        // 关键：监督器本身不得把异常抛出去（否则 fire-and-forget 的宿主任务会变成未观察异常）
        var escaped = await Record.ExceptionAsync(() => supervisor.RunAsync(CancellationToken.None));

        Assert.Null(escaped);
        Assert.Equal(3, runs);
        Assert.Equal(2, failureLog.Count);
        Assert.All(failureLog, ex => Assert.Same(boom, ex));
        Assert.Equal(2, restartLog.Count);
    }

    [Fact]
    public async Task Backoff_GrowsExponentially_AndIsCapped()
    {
        var runs = 0;
        var supervisor = Create(
            () => runs++,
            () => runs < 5,
            out var restarts,
            out _,
            initialDelay: TimeSpan.FromMilliseconds(2),
            maxDelay: TimeSpan.FromMilliseconds(8));

        await supervisor.RunAsync(CancellationToken.None);

        Assert.Equal(4, restarts.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(2), restarts[0].Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(4), restarts[1].Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(8), restarts[2].Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(8), restarts[3].Delay);  // 到上限后不再增长
    }

    [Fact]
    public async Task Cancellation_StopsSupervisor_WithoutRestart()
    {
        var runs = 0;
        using var cts = new CancellationTokenSource();
        var supervisor = Create(() => runs++, () => true, out var restarts, out _);

        await cts.CancelAsync();
        await supervisor.RunAsync(cts.Token);

        Assert.Equal(0, runs);
        Assert.Empty(restarts);
    }

    [Fact]
    public void Constructor_RejectsNullDelegates()
    {
        Assert.Throws<ArgumentNullException>(() => new AcceptLoopSupervisor(
            null!, () => true, (_, _) => { }, _ => { }, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new AcceptLoopSupervisor(
            () => Task.CompletedTask, null!, (_, _) => { }, _ => { }, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new AcceptLoopSupervisor(
            () => Task.CompletedTask, () => true, null!, _ => { }, TimeSpan.Zero, TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => new AcceptLoopSupervisor(
            () => Task.CompletedTask, () => true, (_, _) => { }, null!, TimeSpan.Zero, TimeSpan.Zero));
    }
}
