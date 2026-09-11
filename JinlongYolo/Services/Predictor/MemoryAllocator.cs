namespace JinlongYolo.YoloSharp.Services;

internal class MemoryAllocator : IMemoryAllocator
{
    #region ArrayMemoryPoolBuffer<T>

    private class ArrayMemoryPoolBuffer<T> : IMemoryOwner<T>
    {
        private readonly int _length;

        private T[]? _buffer;

        public Memory<T> Memory
        {
            get
            {
                ObjectDisposedException.ThrowIf(_buffer == null, this);
                return new Memory<T>(_buffer, 0, _length);
            }
        }

        public ArrayMemoryPoolBuffer(int length, bool clean)
        {
            var source = ArrayPool<T>.Shared.Rent(length);

            if (clean)
            {
                Array.Clear(source, 0, length);
            }

            _length = length;
            _buffer = source;
        }

        ~ArrayMemoryPoolBuffer() => Dispose();

        public void Dispose()
        {
            // REVIEW-FIX: 用 Interlocked.Exchange 原子取出引用后再归还，
            // 终结器与显式 Dispose 并发时数组只会被 ArrayPool.Return 一次。
            var buffer = Interlocked.Exchange(ref _buffer, null);

            if (buffer is not null)
            {
                ArrayPool<T>.Shared.Return(buffer);
            }

            GC.SuppressFinalize(this);
        }
    }

    #endregion

    public IMemoryOwner<T> Allocate<T>(int length, bool clean = false)
    {
        return new ArrayMemoryPoolBuffer<T>(length, clean);
    }
}