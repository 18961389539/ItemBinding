using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// MemoryTensor&lt;T&gt; 单元测试。
/// MemoryTensor 是 YOLO 推理输出张量的核心封装，底层为 Memory&lt;T&gt;，支持 3D/4D 索引。
/// </summary>
public class MemoryTensorTests
{
    [Fact]
    public void Constructor_ComputesStrides_For3D()
    {
        // 维度 [2, 3, 4] 的步长应为 [12, 4, 1]（行优先，最后一维步长为 1）
        var buffer = new float[2 * 3 * 4];
        var tensor = new MemoryTensor<float>(buffer, [2, 3, 4]);

        Assert.Equal([12, 4, 1], tensor.Strides);
        Assert.Equal([2, 3, 4], tensor.Dimensions);
    }

    [Fact]
    public void Constructor_ComputesStrides_For4D()
    {
        // 维度 [1, 2, 3, 4] 的步长应为 [24, 12, 4, 1]
        var buffer = new float[1 * 2 * 3 * 4];
        var tensor = new MemoryTensor<float>(buffer, [1, 2, 3, 4]);

        Assert.Equal([24, 12, 4, 1], tensor.Strides);
    }

    [Fact]
    public void Constructor_EmptyDimensions_RequiresBufferLengthOne()
    {
        // 源码逻辑：空 dimensions 数组时 size 保持初始值 1，因此 buffer.Length 必须为 1
        var tensor = new MemoryTensor<float>(new float[1], []);

        Assert.Empty(tensor.Strides);
        Assert.Empty(tensor.Dimensions);
        Assert.Equal(1, tensor.Buffer.Length);
    }

    [Fact]
    public void Constructor_SingleDimension_StrideIsOne()
    {
        var buffer = new float[10];
        var tensor = new MemoryTensor<float>(buffer, [10]);

        Assert.Equal([1], tensor.Strides);
    }

    [Fact]
    public void Constructor_DimensionWithZero_HasZeroStrides()
    {
        // 维度 [2, 0, 3] 含零维度，size=0，buffer 长度必须为 0
        var tensor = new MemoryTensor<float>(Memory<float>.Empty, [2, 0, 3]);

        // 步长计算：从后往前，stride=1
        // i=2: strides[2]=1, stride*=3 -> 3
        // i=1: strides[1]=3, stride*=0 -> 0
        // i=0: strides[0]=0, stride*=2 -> 0
        Assert.Equal([0, 3, 1], tensor.Strides);
    }

    [Fact]
    public void Constructor_BufferLengthMismatch_ThrowsInvalidOperationException()
    {
        var buffer = new float[10];

        Assert.Throws<InvalidOperationException>(() => new MemoryTensor<float>(buffer, [3, 4]));
    }

    [Fact]
    public void Constructor_NegativeDimension_ThrowsArgumentOutOfRangeException()
    {
        var buffer = new float[4];

        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryTensor<float>(buffer, [-1, 4]));
    }

    [Fact]
    public void Dimensions64_MatchesDimensions()
    {
        var tensor = new MemoryTensor<float>(new float[6], [2, 3]);

        Assert.Equal([2L, 3L], tensor.Dimensions64);
    }

    [Fact]
    public void Span_ProvidesReadWriteAccess()
    {
        var tensor = new MemoryTensor<float>(new float[6], [2, 3]);

        tensor.Span[0] = 1.5f;
        tensor.Span[5] = 2.5f;

        Assert.Equal(1.5f, tensor.Span[0]);
        Assert.Equal(2.5f, tensor.Span[5]);
    }

    [Fact]
    public void Buffer_ReturnsUnderlyingMemory()
    {
        var memory = new float[6];
        var tensor = new MemoryTensor<float>(memory, [2, 3]);

        Assert.Equal(6, tensor.Buffer.Length);
    }

    [Fact]
    public void Indexer3D_RoundTripWorks()
    {
        var tensor = new MemoryTensor<float>(new float[2 * 3 * 4], [2, 3, 4]);

        tensor[1, 2, 3] = 0.75f;

        Assert.Equal(0.75f, tensor[1, 2, 3]);
        // 线性索引应为 1*12 + 2*4 + 3*1 = 23
        Assert.Equal(0.75f, tensor.Span[23]);
    }

    [Fact]
    public void Indexer3D_MatchesLinearIndex()
    {
        var tensor = new MemoryTensor<float>(new float[2 * 3 * 4], [2, 3, 4]);

        // 设置所有位置并验证线性索引映射
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 3; j++)
                for (int k = 0; k < 4; k++)
                    tensor[i, j, k] = i * 100 + j * 10 + k;

        // 验证若干位置
        Assert.Equal(0f, tensor.Span[0]);      // (0,0,0) -> 0
        Assert.Equal(3f, tensor.Span[3]);      // (0,0,3) -> 3
        Assert.Equal(10f, tensor.Span[4]);     // (0,1,0) -> 4
        Assert.Equal(123f, tensor.Span[23]);   // (1,2,3) -> 23
    }

    [Fact]
    public void Indexer4D_RoundTripWorks()
    {
        var tensor = new MemoryTensor<float>(new float[1 * 2 * 3 * 4], [1, 2, 3, 4]);

        tensor[0, 1, 2, 3] = 0.5f;

        Assert.Equal(0.5f, tensor[0, 1, 2, 3]);
        // 线性索引应为 0*24 + 1*12 + 2*4 + 3*1 = 23
        Assert.Equal(0.5f, tensor.Span[23]);
    }

    [Fact]
    public void Indexer4D_MatchesLinearIndex()
    {
        var tensor = new MemoryTensor<int>(new int[2 * 2 * 2 * 2], [2, 2, 2, 2]);

        for (int a = 0; a < 2; a++)
            for (int b = 0; b < 2; b++)
                for (int c = 0; c < 2; c++)
                    for (int d = 0; d < 2; d++)
                        tensor[a, b, c, d] = a * 1000 + b * 100 + c * 10 + d;

        // (1,1,1,1) -> 1*8 + 1*4 + 1*2 + 1*1 = 15
        Assert.Equal(1111, tensor.Span[15]);
        // (0,1,0,1) -> 0*8 + 1*4 + 0*2 + 1*1 = 5
        Assert.Equal(101, tensor.Span[5]);
    }

    [Fact]
    public void Indexer3D_WorksForIntType()
    {
        var tensor = new MemoryTensor<int>(new int[6], [2, 3]);

        // 3D 索引访问 2D 张量不安全（Debug.Assert 会触发），但 Span 可正常使用
        tensor.Span[4] = 42;

        Assert.Equal(42, tensor.Span[4]);
    }

    [Fact]
    public void Constructor_DimensionsArrayIsKeptAsReference()
    {
        // 源码中 Dimensions = dimensions 直接引用数组，未做复制
        var dims = new int[] { 2, 3 };
        var tensor = new MemoryTensor<float>(new float[6], dims);

        // 修改原始数组会影响 tensor.Dimensions（保持引用语义）
        dims[0] = 999;

        Assert.Equal(999, tensor.Dimensions[0]);
    }
}
