/// <summary>
/// 表示一个二维图像缓冲区（灰度），底层使用 Memory<float> 存储像素值，提供按 (y,x) 索引的访问。
/// Represents a 2D image buffer (grayscale) backed by Memory<float>, providing (y,x) indexed access.
/// </summary>
public class BitmapBuffer : IDisposable
{
    private readonly int _width;
    private readonly int _height;

    private readonly Memory<float> _buffer;

    // L501: 池化所有者；非空时表示本实例拥有该内存，Dispose 时需归还 ArrayPool。
    // Pool-backed owner; when non-null, this instance owns the memory and returns it to ArrayPool on Dispose.
    private readonly IMemoryOwner<float>? _owner;
    private bool _disposed;

    /// <summary>
    /// 按行列访问像素值（y: 行，x: 列）。
    /// Accesses the pixel value by row and column (y: row, x: column).
    /// </summary>
    /// <param name="y">行索引（Y 坐标） / Row index (Y coordinate).</param>
    /// <param name="x">列索引（X 坐标） / Column index (X coordinate).</param>
    /// <returns>该位置的像素值 / Pixel value at the specified location.</returns>
    public float this[int y, int x]
    {
        get => _buffer.Span[GetIndex(y, x)];
        set => _buffer.Span[GetIndex(y, x)] = value;
    }

    /// <summary>
    /// 图像宽度（列数）。
    /// Image width (number of columns).
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// 图像高度（行数）。
    /// Image height (number of rows).
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// 使用现有的内存缓冲区和指定尺寸初始化 <see cref="BitmapBuffer"/> 类的新实例。
    /// Initializes a new instance of the <see cref="BitmapBuffer"/> class with an existing memory buffer and specified dimensions.
    /// </summary>
    /// <param name="buffer">现有的内存缓冲区 / Existing memory buffer.</param>
    /// <param name="width">图像宽度 / Image width.</param>
    /// <param name="height">图像高度 / Image height.</param>
    /// <exception cref="InvalidOperationException">如果缓冲区长度不等于宽高之积 / Thrown if buffer length does not match width * height.</exception>
    public BitmapBuffer(Memory<float> buffer, int width, int height)
    {
        if (buffer.Length != width * height)
        {
            throw new InvalidOperationException();
        }

        _width = width;
        _height = height;
        _buffer = buffer;
    }

    /// <summary>
    /// 使用指定的尺寸分配新内存并初始化 <see cref="BitmapBuffer"/> 类的新实例。
    /// Initializes a new instance of the <see cref="BitmapBuffer"/> class by allocating new memory with specified dimensions.
    /// </summary>
    /// <param name="width">图像宽度 / Image width.</param>
    /// <param name="height">图像高度 / Image height.</param>
    public BitmapBuffer(int width, int height)
    {
        _width = width;
        _height = height;
        _buffer = new Memory<float>(new float[height * width]);
    }

    /// <summary>
    /// 使用池化内存所有者初始化 <see cref="BitmapBuffer"/> 类的新实例，Dispose 时归还到 ArrayPool。
    /// Initializes a new instance backed by a pooled memory owner; memory is returned to the ArrayPool on Dispose.
    /// </summary>
    /// <param name="owner">池化内存所有者 / The pooled memory owner.</param>
    /// <param name="width">图像宽度 / Image width.</param>
    /// <param name="height">图像高度 / Image height.</param>
    /// <exception cref="InvalidOperationException">缓冲区长度不等于 width*height / Thrown when buffer length mismatches width*height.</exception>
    public BitmapBuffer(IMemoryOwner<float> owner, int width, int height)
    {
        if (owner.Memory.Length != width * height)
        {
            // REVIEW-FIX: 长度校验失败时归还池化内存，避免内存泄漏。
            owner.Dispose();
            throw new InvalidOperationException();
        }

        _width = width;
        _height = height;
        _owner = owner;
        _buffer = owner.Memory;
    }

    /// <summary>
    /// 清空缓冲区，将所有像素值设为 0。
    /// </summary>
    public void Clear() => _buffer.Span.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetIndex(int y, int x)
    {
        if (y < 0 || y >= _height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        if (x < 0 || x >= _width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        return (y * _width) + x;
    }

    /// <summary>
    /// 返回底层缓冲区的只读 Span，便于高性能读取。
    /// </summary>
    public ReadOnlySpan<float> AsReadOnlySpan() => _buffer.Span;

    internal Span<float> Span => _buffer.Span;

    /// <summary>
    /// 返回底层缓冲区的只读 Memory 表示。
    /// </summary>
    public ReadOnlyMemory<float> AsReadOnlyMemory() => _buffer;

    /// <summary>
    /// 释放池化内存（如有），将缓冲区归还到 ArrayPool。幂等。
    /// Releases pooled memory (if any), returning the buffer to the ArrayPool. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _owner?.Dispose();
    }
}
