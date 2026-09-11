namespace JinlongYolo.YoloSharp.Utilities;

/// <summary>
/// 管理多个可释放对象的复合可释放模式，统一进行释放。
/// Composite disposable pattern that manages multiple disposable objects and disposes them uniformly.
/// </summary>
/// <param name="disposables">要管理的可释放对象集合 / The collection of disposable objects to manage.</param>
internal class CompositeDisposable(IEnumerable<IDisposable> disposables) : IDisposable
{
    private int _disposed;

    ~CompositeDisposable() => Dispose();

    /// <summary>
    /// 释放所有管理的可释放对象。
    /// 单个对象释放异常不会中断后续对象的释放，确保所有资源都被回收。
    /// </summary>
    public void Dispose()
    {
        // REVIEW-FIX: 进入时用 Interlocked.Exchange 原子置位，
        // 显式 Dispose 与终结器并发时只执行一次释放逻辑。
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // P1-FIX: 用 try/catch 包裹每个 Dispose，避免单点异常导致后续资源泄漏
        foreach (var disposable in disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                // 记录但不中断后续释放
                System.Diagnostics.Trace.WriteLine($"CompositeDisposable 释放对象失败: {ex}");
            }
        }

        GC.SuppressFinalize(this);
    }
}