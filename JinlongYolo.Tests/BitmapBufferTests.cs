using JinlongYolo.YoloSharp.Memory;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// BitmapBuffer 单元测试。
/// BitmapBuffer 是 YOLO 分割结果的掩码缓冲区，底层为 float 数组（行优先 y*width+x）。
/// </summary>
public class BitmapBufferTests
{
    [Fact]
    public void Constructor_WidthHeight_AllocatesBuffer()
    {
        var buffer = new BitmapBuffer(10, 5);

        Assert.Equal(10, buffer.Width);
        Assert.Equal(5, buffer.Height);
        // 默认全部为 0
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 10; x++)
                Assert.Equal(0f, buffer[y, x]);
    }

    [Fact]
    public void Constructor_ExistingBuffer_UsesProvidedMemory()
    {
        var data = new float[15];
        for (int i = 0; i < 15; i++) data[i] = i * 0.1f;

        var buffer = new BitmapBuffer(data, 5, 3);

        Assert.Equal(5, buffer.Width);
        Assert.Equal(3, buffer.Height);
        Assert.Equal(0f, buffer[0, 0]);
        // data[5] = 0.5f，行优先索引对应 y=1, x=0
        Assert.Equal(0.5f, buffer[1, 0]);
        // data[14] = 1.4f，行优先索引对应 y=2, x=4
        Assert.Equal(1.4f, buffer[2, 4]);
    }

    [Fact]
    public void Constructor_BufferSizeMismatch_ThrowsInvalidOperationException()
    {
        var data = new float[10];

        Assert.Throws<InvalidOperationException>(() => new BitmapBuffer(data, 5, 3));
    }

    [Fact]
    public void Indexer_GetSet_RoundTripWorks()
    {
        var buffer = new BitmapBuffer(10, 10);

        buffer[3, 7] = 0.85f;
        Assert.Equal(0.85f, buffer[3, 7]);
        Assert.Equal(0f, buffer[3, 6]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    public void Indexer_OutOfRange_ThrowsArgumentOutOfRangeException(int x, int y)
    {
        var buffer = new BitmapBuffer(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[y, x]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[y, x] = 1f);
    }

    [Fact]
    public void Clear_SetsAllPixelsToZero()
    {
        var data = new float[25];
        Array.Fill(data, 0.9f);
        var buffer = new BitmapBuffer(data, 5, 5);

        buffer.Clear();

        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++)
                Assert.Equal(0f, buffer[y, x]);
    }

    [Fact]
    public void AsReadOnlySpan_ReturnsAllElements()
    {
        var data = new float[6];
        for (int i = 0; i < 6; i++) data[i] = i;
        var buffer = new BitmapBuffer(data, 3, 2);

        var span = buffer.AsReadOnlySpan();

        Assert.Equal(6, span.Length);
        for (int i = 0; i < 6; i++)
            Assert.Equal(i, span[i]);
    }

    [Fact]
    public void AsReadOnlyMemory_ReturnsAllElements()
    {
        var buffer = new BitmapBuffer(4, 2);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
                buffer[y, x] = y * 4 + x;

        var memory = buffer.AsReadOnlyMemory();

        Assert.Equal(8, memory.Length);
        for (int i = 0; i < 8; i++)
            Assert.Equal(i, memory.Span[i]);
    }

    [Fact]
    public void Indexer_RowMajorOrder_MatchesLinearIndex()
    {
        // 验证行优先索引：buffer[y,x] == data[y*width + x]
        var data = new float[20];
        var buffer = new BitmapBuffer(data, 5, 4);

        buffer[2, 3] = 0.5f;
        Assert.Equal(0.5f, data[2 * 5 + 3]);
    }

    [Fact]
    public void Constructor_ZeroSize_AllowsEmptyBuffer()
    {
        var buffer = new BitmapBuffer(0, 0);

        Assert.Equal(0, buffer.Width);
        Assert.Equal(0, buffer.Height);
        Assert.Equal(0, buffer.AsReadOnlySpan().Length);
    }
}
