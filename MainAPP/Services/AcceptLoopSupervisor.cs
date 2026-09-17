using System;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// HTTP 接收循环的监督器（2026-09-16）。
    ///
    /// <para><b>背景</b>：三个自研 Web 宿主（相机调试 5188 / AI 对话 5190 / 运维门户 5191）都是
    /// "while 循环 + <c>HttpListener.GetContextAsync</c>"收请求。**循环一旦退出，该宿主就永久不再响应**，
    /// 而退出的原因五花八门：监听器被意外关闭、端口被其它进程抢走、未预料的异常。
    /// 运维门户原实现是 <c>catch (Exception) { break; }</c>——任何一次意外异常都让"统一运维入口"
    /// 永久消失，且现场没有任何提示（只能看到门户打不开）。</para>
    ///
    /// <para><b>本类型做什么</b>：把"循环退出后到底该不该重启"的策略收敛到一处，并可单测：
    /// <list type="bullet">
    ///   <item>服务应停止（<c>shouldKeepRunning</c> 为 false）→ 直接结束，不重启（Stop/Dispose 路径）；</item>
    ///   <item>仍应运行 → 记日志、按**指数退避**等待后重启（避免异常条件下热自旋）；</item>
    ///   <item>循环自己抛出的异常**不被吞掉**：上报后同样走重启路径。</item>
    /// </list>
    /// 日志与副作用通过委托注入，因此本类没有任何日志/静态依赖，测试可完全控制时钟行为。</para>
    /// </summary>
    internal sealed class AcceptLoopSupervisor
    {
        private readonly Func<Task> _runLoop;
        private readonly Func<bool> _shouldKeepRunning;
        private readonly Action<int, TimeSpan> _onRestart;
        private readonly Action<Exception> _onLoopFailure;
        private readonly TimeSpan _initialDelay;
        private readonly TimeSpan _maxDelay;
        private int _restartCount;

        /// <param name="runLoop">跑一次接收循环（正常/异常返回都算一次结束）。</param>
        /// <param name="shouldKeepRunning">服务此刻是否应处于运行状态（Stop/Dispose 后返回 false）。</param>
        /// <param name="onRestart">重启前的上报：(第几次, 退避时长)。</param>
        /// <param name="onLoopFailure">接收循环抛出异常时的上报。</param>
        /// <param name="initialDelay">首次重启退避。</param>
        /// <param name="maxDelay">退避上限。</param>
        public AcceptLoopSupervisor(
            Func<Task> runLoop,
            Func<bool> shouldKeepRunning,
            Action<int, TimeSpan> onRestart,
            Action<Exception> onLoopFailure,
            TimeSpan initialDelay,
            TimeSpan maxDelay)
        {
            _runLoop = runLoop ?? throw new ArgumentNullException(nameof(runLoop));
            _shouldKeepRunning = shouldKeepRunning ?? throw new ArgumentNullException(nameof(shouldKeepRunning));
            _onRestart = onRestart ?? throw new ArgumentNullException(nameof(onRestart));
            _onLoopFailure = onLoopFailure ?? throw new ArgumentNullException(nameof(onLoopFailure));
            _initialDelay = initialDelay;
            _maxDelay = maxDelay;
        }

        /// <summary>已发生的重启次数（诊断与测试观察用）。</summary>
        public int RestartCount => Volatile.Read(ref _restartCount);

        /// <summary>监督循环；直到服务停止、或收到取消为止。</summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var delay = _initialDelay;

            while (_shouldKeepRunning() && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _runLoop().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return; // 取消属于正常停止
                }
                catch (Exception ex)
                {
                    // 不吞：上报后仍按"循环已结束"处理（该重启就重启）
                    _onLoopFailure(ex);
                }

                if (!_shouldKeepRunning() || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var attempt = Interlocked.Increment(ref _restartCount);
                _onRestart(attempt, delay);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                delay = Double(delay);
            }
        }

        private TimeSpan Double(TimeSpan current)
        {
            var doubled = current.Ticks * 2;
            return doubled >= _maxDelay.Ticks ? _maxDelay : TimeSpan.FromTicks(doubled);
        }
    }
}
