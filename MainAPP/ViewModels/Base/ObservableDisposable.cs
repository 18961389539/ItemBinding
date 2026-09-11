using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace MainAPP.ViewModels.Base
{
    /// <summary>
    /// ViewModel 基类，统一 IDisposable 模式。
    /// 子类通过重写 Dispose(bool) 释放资源，
    /// 通过 <see cref="AddDisposable"/> 注册一次性资源自动释放。
    /// </summary>
    public abstract class ObservableDisposable : ObservableObject, IDisposable
    {
        private readonly List<IDisposable> _disposables = new();
        private bool _disposed;
        private CancellationTokenSource? _cts;

        /// <summary>是否已释放</summary>
        public bool IsDisposed => _disposed;

        /// <summary>全局取消令牌源，子类长操作可使用</summary>
        protected CancellationTokenSource Cts
        {
            get => _cts ??= new CancellationTokenSource();
            set => _cts = value;
        }

        /// <summary>注册一次性资源，Dispose 时自动释放</summary>
        protected T AddDisposable<T>(T resource) where T : IDisposable
        {
            if (resource != null)
                _disposables.Add(resource);
            return resource!;
        }

        /// <summary>取消所有长操作</summary>
        protected void CancelRunningTasks()
        {
            try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                CancelRunningTasks();
                foreach (var d in _disposables)
                {
                    try { d.Dispose(); }
                    catch (ObjectDisposedException) { }
                    catch (Exception) { /* 忽略释放异常 */ }
                }
                _disposables.Clear();
                try { _cts?.Dispose(); } catch (ObjectDisposedException) { }
                _cts = null;
            }
            _disposed = true;
        }
    }
}
