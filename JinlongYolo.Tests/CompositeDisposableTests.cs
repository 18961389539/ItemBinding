using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// CompositeDisposable 单元测试。
/// CompositeDisposable 管理多个 IDisposable 的统一释放，支持幂等 Dispose。
/// </summary>
public class CompositeDisposableTests
{
    [Fact]
    public void Dispose_DisposesAllManagedDisposables()
    {
        var a = new TrackingDisposable();
        var b = new TrackingDisposable();
        var c = new TrackingDisposable();
        var composite = new CompositeDisposable(new IDisposable[] { a, b, c });

        composite.Dispose();

        Assert.True(a.IsDisposed);
        Assert.True(b.IsDisposed);
        Assert.True(c.IsDisposed);
    }

    [Fact]
    public void Dispose_IsIdempotent_DoesNotRedispose()
    {
        var a = new TrackingDisposable();
        var composite = new CompositeDisposable(new IDisposable[] { a });

        composite.Dispose();
        composite.Dispose();

        Assert.Equal(1, a.DisposeCount);
    }

    [Fact]
    public void Dispose_EmptyCollection_DoesNotThrow()
    {
        var composite = new CompositeDisposable(Array.Empty<IDisposable>());

        composite.Dispose();
    }

    [Fact]
    public void Dispose_SingleDisposable_DisposesIt()
    {
        var a = new TrackingDisposable();
        var composite = new CompositeDisposable(new IDisposable[] { a });

        composite.Dispose();

        Assert.True(a.IsDisposed);
    }

    [Fact]
    public void Dispose_PreservesOrder_FirstInFirstDisposed()
    {
        var order = new List<int>();
        var disposables = new IDisposable[]
        {
            new OrderTrackingDisposable(1, order),
            new OrderTrackingDisposable(2, order),
            new OrderTrackingDisposable(3, order),
        };
        var composite = new CompositeDisposable(disposables);

        composite.Dispose();

        Assert.Equal([1, 2, 3], order);
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            DisposeCount++;
        }
    }

    private sealed class OrderTrackingDisposable(int id, List<int> orderLog) : IDisposable
    {
        public void Dispose() => orderLog.Add(id);
    }
}
