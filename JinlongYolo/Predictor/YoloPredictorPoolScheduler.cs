using System.Diagnostics;
using System.Threading;

namespace JinlongYolo.YoloSharp;

/// <summary>
/// 多池调度使用的单条路由。
/// Route entry used by the multi-pool scheduler.
/// </summary>
public sealed class YoloPredictorPoolRoute
{
    public YoloPredictorPoolRoute(string name, YoloPredictorPool pool)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Pool = pool ?? throw new ArgumentNullException(nameof(pool));
        Name = name;
    }

    /// <summary>
    /// 路由名称。
    /// Route name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 对应的预测器池。
    /// Backing predictor pool.
    /// </summary>
    public YoloPredictorPool Pool { get; }
}

/// <summary>
/// 基于优先级顺序的多池调度器。
///
/// 说明（中文）：
/// - 适用于“优先 GPU，忙了再退 OpenVINO CPU，再退纯 CPU”这类场景。
/// - 调度器会按路由顺序尝试非阻塞借用；若当前轮没有可用实例，则按轮询间隔继续探测直到超时。
/// - 可直接包装已有池，也可根据 `YoloPredictorPoolLayout` 自动创建并持有内部池。
///
/// Priority-based multi-pool scheduler.
/// </summary>
public sealed class YoloPredictorPoolScheduler : IDisposable
{
    private readonly IReadOnlyList<YoloPredictorPoolRoute> _routes;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly object _disposeLock = new();
    private readonly bool _ownsPools;

    private bool _disposed;

    private YoloPredictorPoolScheduler(IReadOnlyList<YoloPredictorPoolRoute> routes, int pollIntervalMilliseconds, bool ownsPools)
    {
        if (pollIntervalMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pollIntervalMilliseconds), "Poll interval must be positive.");
        }

        if (routes.Count == 0)
        {
            throw new ArgumentException("At least one route is required.", nameof(routes));
        }

        _routes = routes;
        PollIntervalMilliseconds = pollIntervalMilliseconds;
        _ownsPools = ownsPools;
    }

    /// <summary>
    /// 当前调度顺序的路由列表。
    /// Ordered route list used by the scheduler.
    /// </summary>
    public IReadOnlyList<YoloPredictorPoolRoute> Routes => _routes;

    /// <summary>
    /// 轮询探测间隔（毫秒）。
    /// Polling interval in milliseconds.
    /// </summary>
    public int PollIntervalMilliseconds { get; }

    /// <summary>
    /// 使用已有池创建调度器，按传入顺序决定优先级。
    /// Create a scheduler from existing pools, preserving the given priority order.
    /// </summary>
    public static YoloPredictorPoolScheduler Create(IEnumerable<YoloPredictorPoolRoute> routes, int pollIntervalMilliseconds = 10)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var routeList = routes.ToArray();
        return new YoloPredictorPoolScheduler(routeList, pollIntervalMilliseconds, ownsPools: false);
    }

    /// <summary>
    /// 使用模型路径和布局自动创建内部池，并按 OpenVINO GPU -> OpenVINO CPU -> CPU Only 顺序调度。
    /// Create internal pools from a model path and schedule them in OpenVINO GPU -> OpenVINO CPU -> CPU Only order.
    /// </summary>
    public static YoloPredictorPoolScheduler Create(string path, YoloPredictorPoolLayout layout, int pollIntervalMilliseconds = 10)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(layout);

        return CreateOwned(BuildOwnedRoutes(options => new YoloPredictor(path, options), layout), pollIntervalMilliseconds);
    }

    /// <summary>
    /// 使用模型字节数组和布局自动创建内部池，并按 OpenVINO GPU -> OpenVINO CPU -> CPU Only 顺序调度。
    /// Create internal pools from model bytes and schedule them in OpenVINO GPU -> OpenVINO CPU -> CPU Only order.
    /// </summary>
    public static YoloPredictorPoolScheduler Create(byte[] model, YoloPredictorPoolLayout layout, int pollIntervalMilliseconds = 10)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(layout);

        return CreateOwned(BuildOwnedRoutes(options => new YoloPredictor(model, options), layout), pollIntervalMilliseconds);
    }

    /// <summary>
    /// 借出一个预测器，优先尝试高优先级池。
    /// Lease a predictor, preferring higher-priority pools first.
    /// </summary>
    public YoloPredictorSchedulerLease Acquire(int timeoutMilliseconds = Timeout.Infinite, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        var stopwatch = timeoutMilliseconds == Timeout.Infinite ? null : Stopwatch.StartNew();

        while (true)
        {
            foreach (var route in _routes)
            {
                try
                {
                    var lease = route.Pool.Acquire(0, linkedTokenSource.Token);
                    return new YoloPredictorSchedulerLease(route, lease);
                }
                catch (TimeoutException)
                {
                }
            }

            var remaining = GetRemainingTimeout(timeoutMilliseconds, stopwatch);
            if (remaining == 0)
            {
                throw new TimeoutException($"Timed out waiting for a YoloPredictor across {_routes.Count} pool(s) after {timeoutMilliseconds}ms.");
            }

            WaitForNextProbe(Math.Min(remaining, PollIntervalMilliseconds), linkedTokenSource.Token);
        }
    }

    /// <summary>
    /// 在一次调用内自动借出和归还预测器。
    /// Automatically lease and return a predictor for a single operation.
    /// </summary>
    public void Use(Action<YoloPredictor> action, int timeoutMilliseconds = Timeout.Infinite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var lease = Acquire(timeoutMilliseconds, cancellationToken);
        action(lease.Predictor);
    }

    /// <summary>
    /// 在一次调用内自动借出和归还预测器，并返回结果。
    /// Automatically lease and return a predictor for a single operation and return a result.
    /// </summary>
    public TResult Use<TResult>(Func<YoloPredictor, TResult> action, int timeoutMilliseconds = Timeout.Infinite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var lease = Acquire(timeoutMilliseconds, cancellationToken);
        return action(lease.Predictor);
    }

    /// <summary>
    /// 释放调度器；若内部池由调度器创建，则一并释放这些池。
    /// Dispose the scheduler and any internally owned pools.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellationTokenSource.Cancel();
        }

        if (_ownsPools)
        {
            foreach (var pool in _routes.Select(route => route.Pool).Distinct())
            {
                pool.Dispose();
            }
        }

        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// 表示由调度器借出的预测器。
    /// Represents a predictor leased from the scheduler.
    /// </summary>
    public sealed class YoloPredictorSchedulerLease : IDisposable
    {
        private readonly YoloPredictorPool.YoloPredictorLease _lease;
        private int _disposed;

        internal YoloPredictorSchedulerLease(YoloPredictorPoolRoute route, YoloPredictorPool.YoloPredictorLease lease)
        {
            Route = route;
            _lease = lease;
            Predictor = lease.Predictor;
            BackendKind = lease.BackendKind;
            RouteName = route.Name;
            Pool = route.Pool;
        }

        /// <summary>
        /// 来源路由。
        /// Source route.
        /// </summary>
        public YoloPredictorPoolRoute Route { get; }

        /// <summary>
        /// 来源路由名称。
        /// Source route name.
        /// </summary>
        public string RouteName { get; }

        /// <summary>
        /// 来源池。
        /// Source pool.
        /// </summary>
        public YoloPredictorPool Pool { get; }

        /// <summary>
        /// 借出的预测器实例。
        /// Leased predictor instance.
        /// </summary>
        public YoloPredictor Predictor { get; }

        /// <summary>
        /// 借出实例的后端类型标记。
        /// Backend kind tag of the leased predictor.
        /// </summary>
        public YoloPredictorBackendKind BackendKind { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _lease.Dispose();
        }
    }

    private static YoloPredictorPoolScheduler CreateOwned(IReadOnlyList<YoloPredictorPoolRoute> routes, int pollIntervalMilliseconds)
    {
        return new YoloPredictorPoolScheduler(routes, pollIntervalMilliseconds, ownsPools: true);
    }

    private static IReadOnlyList<YoloPredictorPoolRoute> BuildOwnedRoutes(Func<YoloPredictorOptions, YoloPredictor> predictorFactory,
                                                                         YoloPredictorPoolLayout layout)
    {
        layout.Validate();

        var routes = new List<YoloPredictorPoolRoute>(3);

        if (layout.OpenVinoGpuCount > 0)
        {
            routes.Add(new YoloPredictorPoolRoute(
                "openvino-gpu",
                YoloPredictorPool.Create(
                    () => predictorFactory(YoloPredictorPool.CreateOpenVinoOptions(layout, layout.OpenVinoGpuOptions, "GPU", nameof(layout.OpenVinoGpuOptions))),
                    layout.OpenVinoGpuCount,
                    YoloPredictorBackendKind.OpenVinoGpu)));
        }

        if (layout.OpenVinoCpuCount > 0)
        {
            routes.Add(new YoloPredictorPoolRoute(
                "openvino-cpu",
                YoloPredictorPool.Create(
                    () => predictorFactory(YoloPredictorPool.CreateOpenVinoOptions(layout, layout.OpenVinoCpuOptions, "CPU", nameof(layout.OpenVinoCpuOptions))),
                    layout.OpenVinoCpuCount,
                    YoloPredictorBackendKind.OpenVinoCpu)));
        }

        if (layout.CpuOnlyCount > 0)
        {
            routes.Add(new YoloPredictorPoolRoute(
                "cpu-only",
                YoloPredictorPool.Create(
                    () => predictorFactory(YoloPredictorPool.CreateCpuOnlyOptions(layout)),
                    layout.CpuOnlyCount,
                    YoloPredictorBackendKind.CpuOnly)));
        }

        return routes;
    }

    private static int GetRemainingTimeout(int timeoutMilliseconds, Stopwatch? stopwatch)
    {
        if (timeoutMilliseconds == Timeout.Infinite)
        {
            return int.MaxValue;
        }

        var remaining = timeoutMilliseconds - (int)stopwatch!.ElapsedMilliseconds;
        return remaining > 0 ? remaining : 0;
    }

    private void WaitForNextProbe(int delayMilliseconds, CancellationToken cancellationToken)
    {
        if (delayMilliseconds <= 0)
        {
            return;
        }

        try
        {
            Task.Delay(delayMilliseconds, cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(YoloPredictorPoolScheduler));
        }
    }
}