using System.Collections.Concurrent;
using System.Threading;

namespace JinlongYolo.YoloSharp;

/// <summary>
/// `YoloPredictorPool` 的异构布局配置。
///
/// 说明（中文）：
/// - 创建池时需要显式指定各类后端实例数量。
/// - `CpuOnlyCount` 会创建纯 CPU 实例，并自动禁用默认的 OpenVINO 检测优先策略。
/// - `OpenVinoCpuCount` / `OpenVinoGpuCount` 会创建显式绑定到 OpenVINO CPU / GPU 的实例。
/// - `Configuration` 可作为各类实例共享的默认推理配置；若某类实例单独提供了 `YoloPredictorOptions.Configuration`，则以后者为准。
///
/// Heterogeneous layout configuration for `YoloPredictorPool`.
/// </summary>
public sealed class YoloPredictorPoolLayout
{
    /// <summary>
    /// 纯 CPU 实例数量。
    /// Number of CPU-only predictors.
    /// </summary>
    public int CpuOnlyCount { get; init; }

    /// <summary>
    /// OpenVINO CPU 实例数量。
    /// Number of OpenVINO CPU predictors.
    /// </summary>
    public int OpenVinoCpuCount { get; init; }

    /// <summary>
    /// OpenVINO GPU 实例数量。
    /// Number of OpenVINO GPU predictors.
    /// </summary>
    public int OpenVinoGpuCount { get; init; }

    /// <summary>
    /// 所有实例共享的默认推理配置。
    /// Shared default predictor configuration for all predictors.
    /// </summary>
    public YoloConfiguration? Configuration { get; init; }

    /// <summary>
    /// 纯 CPU 实例的可选创建选项。
    /// Optional predictor options for CPU-only predictors.
    /// </summary>
    public YoloPredictorOptions? CpuOnlyOptions { get; init; }

    /// <summary>
    /// OpenVINO CPU 实例的可选创建选项。
    /// Optional predictor options for OpenVINO CPU predictors.
    /// </summary>
    public YoloPredictorOptions? OpenVinoCpuOptions { get; init; }

    /// <summary>
    /// OpenVINO GPU 实例的可选创建选项。
    /// Optional predictor options for OpenVINO GPU predictors.
    /// </summary>
    public YoloPredictorOptions? OpenVinoGpuOptions { get; init; }

    /// <summary>
    /// 池中实例总数。
    /// Total predictors in the pool.
    /// </summary>
    public int TotalCount => checked(CpuOnlyCount + OpenVinoCpuCount + OpenVinoGpuCount);

    internal void Validate()
    {
        if (CpuOnlyCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CpuOnlyCount), "CpuOnlyCount cannot be negative.");
        }

        if (OpenVinoCpuCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OpenVinoCpuCount), "OpenVinoCpuCount cannot be negative.");
        }

        if (OpenVinoGpuCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OpenVinoGpuCount), "OpenVinoGpuCount cannot be negative.");
        }

        if (TotalCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TotalCount), "At least one predictor count must be greater than zero.");
        }
    }
}

/// <summary>
/// `YoloPredictor` 的对象池。
///
/// 说明（中文）：
/// - 适合推理并发场景下复用多个 `YoloPredictor` 实例，避免多个线程争用同一个预测器。
/// - 池内实例在创建时会预热完成，调用方通过 <see cref="Acquire"/> 借出一个预测器，使用后释放回池。
/// - 也可以直接使用 <see cref="Use{TResult}"/> 在一次调用内自动完成借还。
/// - 支持同构池（所有实例配置一致）和异构池（显式指定 CPU / OpenVINO CPU / OpenVINO GPU 数量）。
///
/// `YoloPredictor` object pool.
/// </summary>
public sealed class YoloPredictorPool : IDisposable
{
    private readonly BlockingCollection<PooledPredictor> _predictors;
    private readonly ConcurrentDictionary<YoloPredictor, PooledPredictor> _leasedPredictors = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly object _disposeLock = new();

    // REVIEW-FIX: 保护“取出队列”与“Dispose 清理队列”互斥的锁，以及对应的可用实例信号量。
    // Acquire 在锁外等待信号量，在锁内做 TryTake；Dispose 在锁内清理队列，二者不会交错。
    private readonly object _sync = new();
    private readonly SemaphoreSlim _available;

    private bool _disposed;

    /// <summary>
    /// 池中预测器总数。
    /// Total number of predictors in the pool.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// 当前可借出的预测器数量。
    /// Number of predictors currently available for lease.
    /// </summary>
    public int AvailableCount => _predictors.Count;

    /// <summary>
    /// 当前已借出的预测器数量。
    /// Number of predictors currently leased out.
    /// </summary>
    public int LeasedCount => _leasedPredictors.Count;

    private YoloPredictorPool(int capacity)
    {
        Capacity = capacity;
        _predictors = new BlockingCollection<PooledPredictor>(new ConcurrentQueue<PooledPredictor>(), capacity);
        _available = new SemaphoreSlim(capacity);
    }

    /// <summary>
    /// 使用模型文件路径创建并预热一个异构 `YoloPredictor` 池。
    /// Create and warm up a heterogeneous predictor pool from a model file path.
    /// </summary>
    /// <param name="path">ONNX 模型文件路径 / Path to the ONNX model file.</param>
    /// <param name="layout">显式指定各后端实例数量的布局 / Layout with explicit per-backend counts.</param>
    public static YoloPredictorPool Create(string path, YoloPredictorPoolLayout layout)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return Create(layout, options => new YoloPredictor(path, options));
    }

    /// <summary>
    /// 使用内存中的模型字节数组创建并预热一个同构 `YoloPredictor` 池。
    /// Create and warm up a homogeneous predictor pool from an in-memory model byte array.
    /// </summary>
    /// <param name="model">ONNX 模型字节数组 / Byte array containing the ONNX model.</param>
    /// <param name="poolSize">池中预测器数量 / Number of predictors in the pool.</param>
    /// <param name="options">用于创建每个预测器的相同选项 / The same options used for every predictor.</param>
    [Obsolete("Use Create(model, YoloPredictorPoolLayout) to explicitly specify heterogeneous predictor counts. This overload creates a homogeneous pool.")]
    public static YoloPredictorPool Create(byte[] model, int poolSize, YoloPredictorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var resolvedOptions = options ?? YoloPredictorOptions.Default;

        return Create(() => new YoloPredictor(model, resolvedOptions), poolSize, ResolveBackendKind(resolvedOptions));
    }

    /// <summary>
    /// 使用内存中的模型字节数组创建并预热一个异构 `YoloPredictor` 池。
    /// Create and warm up a heterogeneous predictor pool from an in-memory model byte array.
    /// </summary>
    /// <param name="model">ONNX 模型字节数组 / Byte array containing the ONNX model.</param>
    /// <param name="layout">显式指定各后端实例数量的布局 / Layout with explicit per-backend counts.</param>
    public static YoloPredictorPool Create(byte[] model, YoloPredictorPoolLayout layout)
    {
        ArgumentNullException.ThrowIfNull(model);

        return Create(layout, options => new YoloPredictor(model, options));
    }

    /// <summary>
    /// 使用自定义工厂创建并预热一个 `YoloPredictor` 池。
    /// Create and warm up a predictor pool using a custom factory.
    /// </summary>
    /// <param name="factory">创建单个预测器的工厂方法 / Factory used to create each predictor.</param>
    /// <param name="poolSize">池中预测器数量 / Number of predictors in the pool.</param>
    public static YoloPredictorPool Create(Func<YoloPredictor> factory, int poolSize)
    {
        return Create(factory, poolSize, YoloPredictorBackendKind.CustomFactory);
    }

    /// <summary>
    /// 使用自定义工厂创建并预热一个带已知后端类型标记的 `YoloPredictor` 池。
    /// Create and warm up a predictor pool using a custom factory with an explicit backend kind.
    /// </summary>
    internal static YoloPredictorPool Create(Func<YoloPredictor> factory, int poolSize, YoloPredictorBackendKind backendKind)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return CreateCore(() => new PooledPredictor(factory(), backendKind), poolSize);
    }

    private static YoloPredictorPool CreateCore(Func<PooledPredictor> factory, int poolSize)
    {
        if (poolSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(poolSize), "Pool size must be positive.");
        }

        var pool = new YoloPredictorPool(poolSize);

        try
        {
            for (var i = 0; i < poolSize; i++)
            {
                pool._predictors.Add(factory());
            }

            return pool;
        }
        catch
        {
            pool.Dispose();
            throw;
        }
    }

    private static YoloPredictorPool Create(YoloPredictorPoolLayout layout, Func<YoloPredictorOptions, YoloPredictor> predictorFactory)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(predictorFactory);

        layout.Validate();

        var pool = new YoloPredictorPool(layout.TotalCount);

        try
        {
            AddPredictors(pool, layout.CpuOnlyCount, YoloPredictorBackendKind.CpuOnly, () => predictorFactory(CreateCpuOnlyOptions(layout)));
            AddPredictors(pool, layout.OpenVinoCpuCount, YoloPredictorBackendKind.OpenVinoCpu, () => predictorFactory(CreateOpenVinoOptions(layout, layout.OpenVinoCpuOptions, "CPU", nameof(layout.OpenVinoCpuOptions))));
            AddPredictors(pool, layout.OpenVinoGpuCount, YoloPredictorBackendKind.OpenVinoGpu, () => predictorFactory(CreateOpenVinoOptions(layout, layout.OpenVinoGpuOptions, "GPU", nameof(layout.OpenVinoGpuOptions))));

            return pool;
        }
        catch
        {
            pool.Dispose();
            throw;
        }
    }

    private static void AddPredictors(YoloPredictorPool pool, int count, YoloPredictorBackendKind backendKind, Func<YoloPredictor> factory)
    {
        for (var i = 0; i < count; i++)
        {
            pool._predictors.Add(new PooledPredictor(factory(), backendKind));
        }
    }

    internal static YoloPredictorOptions CreateCpuOnlyOptions(YoloPredictorPoolLayout layout)
    {
        var options = layout.CpuOnlyOptions;

        if (options is not null)
        {
            if (options.UseCuda)
            {
                throw new InvalidOperationException("'CpuOnlyOptions' cannot enable CUDA.");
            }

            if (options.OpenVino is not null)
            {
                throw new InvalidOperationException("'CpuOnlyOptions' cannot set 'OpenVino'.");
            }
        }

        return new YoloPredictorOptions
        {
            UseCuda = false,
            CudaDeviceId = options?.CudaDeviceId ?? 0,
            PreferOpenVinoCpuForDetection = false,
            SessionOptions = options?.SessionOptions,
            Configuration = options?.Configuration ?? layout.Configuration,
        };
    }

    internal static YoloPredictorOptions CreateOpenVinoOptions(YoloPredictorPoolLayout layout,
                                                               YoloPredictorOptions? options,
                                                               string defaultDeviceType,
                                                               string optionName)
    {
        if (options is not null)
        {
            if (options.UseCuda)
            {
                throw new InvalidOperationException($"'{optionName}' cannot enable CUDA.");
            }

            if (options.SessionOptions is not null)
            {
                throw new InvalidOperationException($"'{optionName}' cannot set 'SessionOptions'. Configure OpenVINO via 'OpenVino' instead.");
            }
        }

        return new YoloPredictorOptions
        {
            UseCuda = false,
            CudaDeviceId = options?.CudaDeviceId ?? 0,
            PreferOpenVinoCpuForDetection = false,
            OpenVino = CloneOpenVinoOptions(options?.OpenVino, defaultDeviceType),
            Configuration = options?.Configuration ?? layout.Configuration,
        };
    }

    internal static YoloPredictorBackendKind ResolveBackendKind(YoloPredictorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.UseCuda)
        {
            return YoloPredictorBackendKind.Cuda;
        }

        if (options.OpenVino is not null)
        {
            return ResolveOpenVinoBackendKind(options.OpenVino);
        }

        if (options.SessionOptions is not null)
        {
            return YoloPredictorBackendKind.CustomSession;
        }

        if (options.PreferOpenVinoCpuForDetection)
        {
            return YoloPredictorBackendKind.AutoPreferredOpenVinoCpu;
        }

        return YoloPredictorBackendKind.CpuOnly;
    }

    private static YoloPredictorBackendKind ResolveOpenVinoBackendKind(OpenVinoOptions openVinoOptions)
    {
        var deviceType = openVinoOptions.DeviceType?.Trim();

        if (string.IsNullOrWhiteSpace(deviceType))
        {
            return YoloPredictorBackendKind.OpenVinoCpu;
        }

        if (deviceType.StartsWith("GPU", StringComparison.OrdinalIgnoreCase))
        {
            return YoloPredictorBackendKind.OpenVinoGpu;
        }

        if (deviceType.StartsWith("CPU", StringComparison.OrdinalIgnoreCase))
        {
            return YoloPredictorBackendKind.OpenVinoCpu;
        }

        return YoloPredictorBackendKind.OpenVino;
    }

    private static OpenVinoOptions CloneOpenVinoOptions(OpenVinoOptions? options, string defaultDeviceType)
    {
        return new OpenVinoOptions
        {
            DeviceType = string.IsNullOrWhiteSpace(options?.DeviceType) ? defaultDeviceType : options.DeviceType,
            CacheDirectory = options?.CacheDirectory,
            PerformanceHint = options?.PerformanceHint,
            PrecisionHint = options?.PrecisionHint,
            NumberOfStreams = options?.NumberOfStreams,
            InferenceThreads = options?.InferenceThreads,
            LoadConfig = options?.LoadConfig,
        };
    }

    /// <summary>
    /// 借出一个预测器实例，使用完毕后需要释放返回池中。
    /// Lease a predictor instance and return it to the pool when done.
    /// </summary>
    /// <param name="timeoutMilliseconds">等待超时（毫秒），`-1` 表示无限等待 / Timeout in milliseconds, `-1` means infinite wait.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <exception cref="TimeoutException">当等待超时且没有可用实例时抛出 / Thrown when the wait times out.</exception>
    /// <exception cref="ObjectDisposedException">当池已释放时抛出 / Thrown when the pool has been disposed.</exception>
    public YoloPredictorLease Acquire(int timeoutMilliseconds = Timeout.Infinite, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);

        try
        {
            // REVIEW-FIX: 先获取锁检查 _disposed 与队列，TryTake 用 0 超时在锁内尝试；
            // 取不到则在锁外等待信号量（或超时/取消），避免与 Dispose 的清理竞态取出已释放的 predictor。
            if (!_available.Wait(timeoutMilliseconds, linkedTokenSource.Token))
            {
                throw new TimeoutException($"Timed out waiting for a YoloPredictor after {timeoutMilliseconds}ms.");
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(YoloPredictorPool));
                }

                if (!_predictors.TryTake(out var pooledPredictor))
                {
                    // 信号量计数与队列不一致（理论不应发生），归还计数以避免泄漏
                    _available.Release();
                    throw new InvalidOperationException("Internal state error: Pool availability mismatch.");
                }

                if (!_leasedPredictors.TryAdd(pooledPredictor.Predictor, pooledPredictor))
                {
                    _predictors.Add(pooledPredictor);
                    _available.Release();
                    throw new InvalidOperationException("Internal state error: Predictor already leased.");
                }

                return new YoloPredictorLease(this, pooledPredictor);
            }
        }
        catch (OperationCanceledException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(YoloPredictorPool));
        }
    }

    /// <summary>
    /// 在一次调用内自动借出和归还预测器。
    /// Automatically lease and return a predictor for a single operation.
    /// </summary>
    /// <param name="action">使用预测器执行的操作 / Action to execute with the predictor.</param>
    /// <param name="timeoutMilliseconds">等待超时（毫秒），`-1` 表示无限等待 / Timeout in milliseconds, `-1` means infinite wait.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
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
    /// <typeparam name="TResult">返回结果类型 / Result type.</typeparam>
    /// <param name="action">使用预测器执行的操作 / Action to execute with the predictor.</param>
    /// <param name="timeoutMilliseconds">等待超时（毫秒），`-1` 表示无限等待 / Timeout in milliseconds, `-1` means infinite wait.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    public TResult Use<TResult>(Func<YoloPredictor, TResult> action, int timeoutMilliseconds = Timeout.Infinite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var lease = Acquire(timeoutMilliseconds, cancellationToken);
        return action(lease.Predictor);
    }

    internal void Return(PooledPredictor pooledPredictor)
    {
        if (pooledPredictor is null)
        {
            throw new ArgumentNullException(nameof(pooledPredictor));
        }

        if (_disposed)
        {
            pooledPredictor.Predictor.Dispose();
            return;
        }

        if (!_leasedPredictors.TryRemove(pooledPredictor.Predictor, out _))
        {
            throw new InvalidOperationException("Attempted to return a predictor that is not currently leased by this pool.");
        }

        try
        {
            // REVIEW-FIX: 在 _sync 锁内完成 Add 与信号量释放，与 Dispose 的清理互斥，
            // 保证信号量计数与队列保持一致，且不会在池释放后访问已释放的信号量。
            lock (_sync)
            {
                if (!_predictors.IsAddingCompleted)
                {
                    _predictors.Add(pooledPredictor);
                    _available.Release();
                }
                else
                {
                    pooledPredictor.Predictor.Dispose();
                }
            }
        }
        catch (InvalidOperationException)
        {
            pooledPredictor.Predictor.Dispose();
        }
    }

    /// <summary>
    /// 释放池中资源。
    /// Dispose pool resources.
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

        _predictors.CompleteAdding();

        // REVIEW-FIX: 队列清理与 Acquire 的 TryTake 在同一把锁内完成，确保不会取出已被释放的 predictor。
        lock (_sync)
        {
            foreach (var predictor in _predictors)
            {
                predictor.Predictor.Dispose();
            }

            foreach (var predictor in _leasedPredictors.Keys)
            {
                predictor.Dispose();
            }

            _leasedPredictors.Clear();
            _available.Dispose();
        }

        _predictors.Dispose();
        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// 表示从池中租出的 `YoloPredictor`。
    /// Represents a leased `YoloPredictor` from the pool.
    /// </summary>
    public sealed class YoloPredictorLease : IDisposable
    {
        private int _disposed;
        private readonly PooledPredictor _pooledPredictor;

        internal YoloPredictorLease(YoloPredictorPool pool, PooledPredictor pooledPredictor)
        {
            Pool = pool;
            _pooledPredictor = pooledPredictor;
            Predictor = pooledPredictor.Predictor;
            BackendKind = pooledPredictor.BackendKind;
        }

        /// <summary>
        /// 租出的预测器实例。
        /// The leased predictor instance.
        /// </summary>
        public YoloPredictor Predictor { get; }

        /// <summary>
        /// 租出的预测器后端类型标记。
        /// Backend kind tag of the leased predictor.
        /// </summary>
        public YoloPredictorBackendKind BackendKind { get; }

        internal YoloPredictorPool Pool { get; }

        /// <summary>
        /// 归还预测器到池中。
        /// Return the predictor to the pool.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Pool.Return(_pooledPredictor);
        }
    }

    internal sealed class PooledPredictor
    {
        public PooledPredictor(YoloPredictor predictor, YoloPredictorBackendKind backendKind)
        {
            Predictor = predictor ?? throw new ArgumentNullException(nameof(predictor));
            BackendKind = backendKind;
        }

        public YoloPredictor Predictor { get; }

        public YoloPredictorBackendKind BackendKind { get; }
    }
}