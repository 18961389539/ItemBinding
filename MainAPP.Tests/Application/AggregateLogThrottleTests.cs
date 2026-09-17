using MainAPP.Application;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// 限频聚合计数门测试（2026-09-16）。
///
/// <para><b>回归背景</b>：检测热路径上的同类事件（逐帧角度推理失败、质量门拒绝…）原本逐条写日志，
/// 产品连续经过时每秒数条，会淹没真正有用的信息。抽成 <see cref="AggregateLogThrottle"/> 后，
/// 这里把三条语义钉住：① 首次发生必须立即可见；② 窗口内只计数不输出；
/// ③ 到点输出时次数准确且计数器归零。</para>
/// </summary>
public class AggregateLogThrottleTests
{
    [Fact]
    public void FirstCall_FlushesImmediately()
    {
        // 首次发生要马上可见——否则现场会以为"根本没触发"
        var gate = new AggregateLogThrottle(TimeSpan.FromSeconds(30));

        Assert.True(gate.TryFlush(out var count));

        Assert.Equal(1, count);
    }

    [Fact]
    public void CallsWithinWindow_AccumulateWithoutFlushing()
    {
        var now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Local);
        var gate = new AggregateLogThrottle(TimeSpan.FromSeconds(30), () => now);

        Assert.True(gate.TryFlush(out _));            // 第 1 次：放行

        now = now.AddSeconds(5);
        Assert.False(gate.TryFlush(out var blocked)); // 窗口内：只计数
        Assert.Equal(0, blocked);
        Assert.False(gate.TryFlush(out _));
        Assert.Equal(2, gate.PendingCount);           // 累计 2 次未输出

        now = now.AddSeconds(30);                     // 距上次放行 35s ≥ 30s
        Assert.True(gate.TryFlush(out var count));

        Assert.Equal(3, count);                       // 2 次累计 + 本次 1 次
        Assert.Equal(0, gate.PendingCount);           // 计数器已归零
    }

    [Fact]
    public void IntervalBoundary_FlushesExactlyAtWindow()
    {
        var now = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Local);
        var interval = TimeSpan.FromSeconds(30);
        var gate = new AggregateLogThrottle(interval, () => now);
        gate.TryFlush(out _);

        now = now.Add(interval).AddTicks(-1);
        Assert.False(gate.TryFlush(out _));  // 差 1 tick：仍在窗口内

        now = now.AddTicks(1);
        Assert.True(gate.TryFlush(out _));   // 恰好到点：放行
    }

    [Fact]
    public async Task ConcurrentCalls_NeitherLoseNorDoubleCount()
    {
        // 该门被多个推理线程共享（每帧每目标调用），必须证明计数不丢不重。
        // 间隔取 Zero ⇒ 每次调用都会放行，因此"放行次数之和 + 末次未放行残留 == 调用总数"。
        var gate = new AggregateLogThrottle(TimeSpan.Zero);
        const int Calls = 2000;
        var flushed = 0;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < Calls / 8; i++)
            {
                if (gate.TryFlush(out var count))
                {
                    Interlocked.Add(ref flushed, count);
                }
            }
        })));

        Assert.Equal(Calls, flushed + gate.PendingCount);
    }
}
