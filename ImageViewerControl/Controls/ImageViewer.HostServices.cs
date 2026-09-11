using System;
using System.Windows.Threading;
using ImageViewer.Abstractions;

namespace ImageViewer.Controls
{
    public interface IImageViewerDispatcherTimerFactory
    {
        DispatcherTimer Create(Dispatcher dispatcher, DispatcherPriority priority, TimeSpan interval);
    }

    public interface IImageViewerDeferredRefreshScheduler : IDisposable
    {
        void Request(bool immediate = false);

        void BeginBatch();

        void EndBatch(bool immediate = false);
    }

    public interface IImageViewerForcedRefreshScheduler : IDisposable
    {
        void Request(bool force = false, bool immediate = false);

        void BeginBatch();

        void EndBatch(bool immediate = false);
    }

    public interface IImageViewerRefreshSchedulerFactory
    {
        IImageViewerDeferredRefreshScheduler CreateDeferred(
            Action refreshAction,
            Dispatcher dispatcher,
            DispatcherPriority priority,
            TimeSpan interval);

        IImageViewerForcedRefreshScheduler CreateForced(
            Action<bool> refreshAction,
            Dispatcher dispatcher,
            DispatcherPriority priority,
            TimeSpan interval);
    }

    public interface IImageViewerLatestTaskScheduler : IDisposable
    {
        Task ScheduleAsync(Func<CancellationToken, Task> work, int delayMilliseconds = 0);

        void Cancel();
    }

    public interface IImageViewerLatestTaskSchedulerFactory
    {
        IImageViewerLatestTaskScheduler Create();
    }

    public interface IImageViewerPeriodicTaskScheduler : IDisposable
    {
        void Start();

        void StopScheduling();
    }

    public interface IImageViewerPeriodicTaskSchedulerFactory
    {
        IImageViewerPeriodicTaskScheduler Create(
            Func<Task> callback,
            Dispatcher dispatcher,
            DispatcherPriority priority,
            TimeSpan interval);
    }

    public interface IImageViewerAnalysisDiagnostics
    {
        void LogNonCriticalError(IImageViewerLogger logger, string message, Exception exception);
    }

    public sealed class ImageViewerHostServices
    {
        public ImageViewerHostServices(
            IImageViewerDispatcherTimerFactory dispatcherTimerFactory,
            IImageViewerRefreshSchedulerFactory refreshSchedulerFactory,
            IImageViewerLatestTaskSchedulerFactory latestTaskSchedulerFactory,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerAnalysisDiagnostics analysisDiagnostics)
            : this(
                dispatcherTimerFactory,
                refreshSchedulerFactory,
                latestTaskSchedulerFactory,
                periodicTaskSchedulerFactory,
                analysisDiagnostics,
                new LocalAppDataImageViewerSessionStoragePolicy())
        {
        }

        public ImageViewerHostServices(
            IImageViewerDispatcherTimerFactory dispatcherTimerFactory,
            IImageViewerRefreshSchedulerFactory refreshSchedulerFactory,
            IImageViewerLatestTaskSchedulerFactory latestTaskSchedulerFactory,
            IImageViewerPeriodicTaskSchedulerFactory periodicTaskSchedulerFactory,
            IImageViewerAnalysisDiagnostics analysisDiagnostics,
            IImageViewerSessionStoragePolicy sessionStoragePolicy)
        {
            DispatcherTimerFactory = dispatcherTimerFactory ?? throw new ArgumentNullException(nameof(dispatcherTimerFactory));
            RefreshSchedulerFactory = refreshSchedulerFactory ?? throw new ArgumentNullException(nameof(refreshSchedulerFactory));
            LatestTaskSchedulerFactory = latestTaskSchedulerFactory ?? throw new ArgumentNullException(nameof(latestTaskSchedulerFactory));
            PeriodicTaskSchedulerFactory = periodicTaskSchedulerFactory ?? throw new ArgumentNullException(nameof(periodicTaskSchedulerFactory));
            AnalysisDiagnostics = analysisDiagnostics ?? throw new ArgumentNullException(nameof(analysisDiagnostics));
            SessionStoragePolicy = sessionStoragePolicy ?? throw new ArgumentNullException(nameof(sessionStoragePolicy));
        }

        public IImageViewerDispatcherTimerFactory DispatcherTimerFactory { get; }

        public IImageViewerRefreshSchedulerFactory RefreshSchedulerFactory { get; }

        public IImageViewerLatestTaskSchedulerFactory LatestTaskSchedulerFactory { get; }

        public IImageViewerPeriodicTaskSchedulerFactory PeriodicTaskSchedulerFactory { get; }

        public IImageViewerAnalysisDiagnostics AnalysisDiagnostics { get; }

        public IImageViewerSessionStoragePolicy SessionStoragePolicy { get; }
    }

    internal sealed class WpfImageViewerDispatcherTimerFactory : IImageViewerDispatcherTimerFactory
    {
        public DispatcherTimer Create(Dispatcher dispatcher, DispatcherPriority priority, TimeSpan interval)
        {
            ArgumentNullException.ThrowIfNull(dispatcher);

            return new DispatcherTimer(priority, dispatcher)
            {
                Interval = interval
            };
        }
    }

    internal sealed class DispatcherImageViewerRefreshSchedulerFactory : IImageViewerRefreshSchedulerFactory
    {
        private readonly IImageViewerDispatcherTimerFactory _timerFactory;

        public DispatcherImageViewerRefreshSchedulerFactory(IImageViewerDispatcherTimerFactory timerFactory)
        {
            _timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
        }

        public IImageViewerDeferredRefreshScheduler CreateDeferred(
            Action refreshAction,
            Dispatcher dispatcher,
            DispatcherPriority priority,
            TimeSpan interval)
        {
            return new DeferredRefreshScheduler(refreshAction, _timerFactory.Create(dispatcher, priority, interval));
        }

        public IImageViewerForcedRefreshScheduler CreateForced(
            Action<bool> refreshAction,
            Dispatcher dispatcher,
            DispatcherPriority priority,
            TimeSpan interval)
        {
            return new ForcedRefreshScheduler(refreshAction, _timerFactory.Create(dispatcher, priority, interval));
        }

        private sealed class DeferredRefreshScheduler : IImageViewerDeferredRefreshScheduler
        {
            private readonly Action _refreshAction;
            private readonly DispatcherTimer _timer;
            private bool _pending;
            private bool _suppressed;
            private bool _disposed;

            public DeferredRefreshScheduler(Action refreshAction, DispatcherTimer timer)
            {
                _refreshAction = refreshAction ?? throw new ArgumentNullException(nameof(refreshAction));
                _timer = timer ?? throw new ArgumentNullException(nameof(timer));
                _timer.Tick += OnTick;
            }

            public void Request(bool immediate = false)
            {
                ThrowIfDisposed();

                if (_suppressed)
                {
                    _pending = true;
                    return;
                }

                if (immediate)
                {
                    _timer.Stop();
                    _pending = false;
                    _refreshAction();
                    return;
                }

                if (_pending)
                {
                    return;
                }

                _pending = true;
                _timer.Start();
            }

            public void BeginBatch()
            {
                ThrowIfDisposed();
                _suppressed = true;
            }

            public void EndBatch(bool immediate = false)
            {
                ThrowIfDisposed();
                _suppressed = false;
                if (_pending)
                {
                    Request(immediate);
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timer.Stop();
                _timer.Tick -= OnTick;
            }

            private void OnTick(object? sender, EventArgs e)
            {
                _timer.Stop();
                if (!_pending)
                {
                    return;
                }

                _pending = false;
                _refreshAction();
            }

            private void ThrowIfDisposed()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }
        }

        private sealed class ForcedRefreshScheduler : IImageViewerForcedRefreshScheduler
        {
            private readonly Action<bool> _refreshAction;
            private readonly DispatcherTimer _timer;
            private bool _pending;
            private bool _forcePending;
            private bool _suppressed;
            private bool _disposed;

            public ForcedRefreshScheduler(Action<bool> refreshAction, DispatcherTimer timer)
            {
                _refreshAction = refreshAction ?? throw new ArgumentNullException(nameof(refreshAction));
                _timer = timer ?? throw new ArgumentNullException(nameof(timer));
                _timer.Tick += OnTick;
            }

            public void Request(bool force = false, bool immediate = false)
            {
                ThrowIfDisposed();

                if (_suppressed)
                {
                    _pending = true;
                    _forcePending |= force;
                    return;
                }

                if (immediate)
                {
                    _timer.Stop();
                    bool effectiveForce = force || _forcePending;
                    _pending = false;
                    _forcePending = false;
                    _refreshAction(effectiveForce);
                    return;
                }

                _pending = true;
                _forcePending |= force;
                if (!_timer.IsEnabled)
                {
                    _timer.Start();
                }
            }

            public void BeginBatch()
            {
                ThrowIfDisposed();
                _suppressed = true;
            }

            public void EndBatch(bool immediate = false)
            {
                ThrowIfDisposed();
                _suppressed = false;
                if (_pending)
                {
                    Request(_forcePending, immediate);
                }
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timer.Stop();
                _timer.Tick -= OnTick;
            }

            private void OnTick(object? sender, EventArgs e)
            {
                _timer.Stop();
                if (!_pending)
                {
                    return;
                }

                bool force = _forcePending;
                _pending = false;
                _forcePending = false;
                _refreshAction(force);
            }

            private void ThrowIfDisposed()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }
        }
    }

    internal sealed class LatestImageViewerTaskSchedulerFactory : IImageViewerLatestTaskSchedulerFactory
    {
        public IImageViewerLatestTaskScheduler Create()
        {
            return new LatestTaskScheduler();
        }

        private sealed class LatestTaskScheduler : IImageViewerLatestTaskScheduler
        {
            private CancellationTokenSource? _cancellationTokenSource;
            private bool _disposed;

            public Task ScheduleAsync(Func<CancellationToken, Task> work, int delayMilliseconds = 0)
            {
                ArgumentNullException.ThrowIfNull(work);
                ThrowIfDisposed();

                Cancel();
                var cancellationTokenSource = new CancellationTokenSource();
                _cancellationTokenSource = cancellationTokenSource;
                return ExecuteAsync(work, cancellationTokenSource, delayMilliseconds);
            }

            public void Cancel()
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                Cancel();
            }

            private static async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationTokenSource cancellationTokenSource, int delayMilliseconds)
            {
                try
                {
                    if (delayMilliseconds > 0)
                    {
                        await Task.Delay(delayMilliseconds, cancellationTokenSource.Token);
                    }

                    await work(cancellationTokenSource.Token);
                }
                catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
                {
                }
            }

            private void ThrowIfDisposed()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }
        }
    }

    internal sealed class DispatcherImageViewerPeriodicTaskSchedulerFactory : IImageViewerPeriodicTaskSchedulerFactory
    {
        private readonly IImageViewerDispatcherTimerFactory _timerFactory;

        public DispatcherImageViewerPeriodicTaskSchedulerFactory(IImageViewerDispatcherTimerFactory timerFactory)
        {
            _timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
        }

        public IImageViewerPeriodicTaskScheduler Create(
            Func<Task> callback,
            Dispatcher dispatcher,
            DispatcherPriority priority,
            TimeSpan interval)
        {
            return new PeriodicTaskScheduler(callback, _timerFactory.Create(dispatcher, priority, interval));
        }

        private sealed class PeriodicTaskScheduler : IImageViewerPeriodicTaskScheduler
        {
            private readonly Func<Task> _callback;
            private readonly DispatcherTimer _timer;
            private bool _disposed;

            public PeriodicTaskScheduler(Func<Task> callback, DispatcherTimer timer)
            {
                _callback = callback ?? throw new ArgumentNullException(nameof(callback));
                _timer = timer ?? throw new ArgumentNullException(nameof(timer));
                _timer.Tick += OnTick;
            }

            public void Start()
            {
                ThrowIfDisposed();
                _timer.Start();
            }

            public void StopScheduling()
            {
                _timer.Stop();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _timer.Stop();
                _timer.Tick -= OnTick;
            }

            private async void OnTick(object? sender, EventArgs e)
            {
                await _callback();
            }

            private void ThrowIfDisposed()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }
        }
    }

    internal sealed class LoggerImageViewerAnalysisDiagnostics : IImageViewerAnalysisDiagnostics
    {
        public void LogNonCriticalError(IImageViewerLogger logger, string message, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(exception);

            logger.LogError(message, exception);
        }
    }
}