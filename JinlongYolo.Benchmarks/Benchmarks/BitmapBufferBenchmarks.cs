using BenchmarkDotNet.Attributes;

namespace JinlongYolo.Benchmarks.Benchmarks;

/// <summary>
/// BitmapBuffer 性能基准测试。
/// BitmapBuffer 是分割掩码的底层存储，每帧推理都会创建和填充。
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class BitmapBufferBenchmarks
{
    private BitmapBuffer _buffer = null!;
    private float[] _externalData = null!;

    [Params(64, 128, 256, 512)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new BitmapBuffer(Size, Size);
        _externalData = new float[Size * Size];
        for (int i = 0; i < _externalData.Length; i++)
            _externalData[i] = i * 0.0001f;
    }

    [Benchmark(Description = "构造 - 分配新内存")]
    public BitmapBuffer Constructor_Allocate()
    {
        return new BitmapBuffer(Size, Size);
    }

    [Benchmark(Description = "构造 - 使用现有缓冲区")]
    public BitmapBuffer Constructor_ExistingBuffer()
    {
        return new BitmapBuffer(_externalData, Size, Size);
    }

    [Benchmark(Description = "填充 - 逐像素赋值")]
    public void Fill_PixelByPixel()
    {
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                _buffer[y, x] = 0.9f;
    }

    [Benchmark(Description = "填充 - Span 批量复制")]
    public void Fill_SpanCopy()
    {
        // BitmapBuffer.Span 是 internal，通过 AsReadOnlySpan 读取
        // 这里测试从 buffer 批量复制到外部数组
        _buffer.AsReadOnlySpan().CopyTo(_externalData);
    }

    [Benchmark(Description = "读取 - 逐像素求和")]
    public float Sum_PixelByPixel()
    {
        float sum = 0;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                sum += _buffer[y, x];
        return sum;
    }

    [Benchmark(Description = "读取 - Span 求和")]
    public float Sum_Span()
    {
        float sum = 0;
        var span = _buffer.AsReadOnlySpan();
        for (int i = 0; i < span.Length; i++)
            sum += span[i];
        return sum;
    }

    [Benchmark(Description = "Clear - 清空缓冲区")]
    public void Clear_Buffer()
    {
        _buffer.Clear();
    }
}
