using System;

namespace MainAPP.Application;

/// <summary>
/// 限频聚合计数门（2026-09-16）。
///
/// <para><b>解决的问题</b>：检测热路径上反复发生的同类事件（逐帧角度推理失败、质量门拒绝…）
/// 不应逐条写日志——产品连续经过时每秒可产生数条，既淹没真正有用的信息，也让日志 IO 成为负担
/// （项目内已有先例：<c>RecordAngleReject</c> 的 30 秒聚合、<c>LogService.ShouldSkipUiLog</c>）。
/// 本类型把"累计 → 到点才输出一次 → 带上窗口内次数"的逻辑收成一处**纯逻辑**，
/// 时钟可注入，因此可以直接单测窗口边界行为。</para>
///
/// <para><b>语义</b>：首次调用立即放行（与既有实现一致——第一次发生要马上可见）；
/// 之后的调用若距上次放行不足一个窗口，只计数不输出；到点后由 <see cref="TryFlush"/>
/// 返回 true 并给出窗口内累计次数，计数器归零。</para>
/// </summary>
internal sealed class AggregateLogThrottle
{
    private readonly object _lock = new();
    private readonly TimeSpan _interval;
    private readonly Func<DateTime> _clock;
    private DateTime _lastFlush = DateTime.MinValue;
    private int _pending;

    /// <param name="interval">输出窗口（多久最多输出一条）。</param>
    /// <param name="clock">时钟，默认 <see cref="DateTime.Now"/>；测试可注入可控时钟。</param>
    public AggregateLogThrottle(TimeSpan interval, Func<DateTime>? clock = null)
    {
        _interval = interval;
        _clock = clock ?? (static () => DateTime.Now);
    }

    /// <summary>当前窗口内尚未输出的累计次数（供诊断/测试观察）。</summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pending;
            }
        }
    }

    /// <summary>
    /// 记账一次。
    /// </summary>
    /// <param name="count">输出放行时，窗口内累计次数；未放行时为 0。</param>
    /// <returns>true = 本次到点，调用方应输出一条聚合日志。</returns>
    public bool TryFlush(out int count)
    {
        lock (_lock)
        {
            _pending++;

            var now = _clock();
            if (now - _lastFlush < _interval)
            {
                count = 0;
                return false;
            }

            _lastFlush = now;
            count = _pending;
            _pending = 0;
            return true;
        }
    }
}
