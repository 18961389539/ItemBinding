using System.Buffers;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// MemoryTensorOwner&lt;T&gt; 和 YoloRawOutput 的 Dispose 语义单元测试。
/// 这两个类型管理推理输出张量的内存生命周期。
/// </summary>
public class MemoryTensorOwnerTests
{
    [Fact]
    public void Constructor_CreatesTensorWithProvidedMemory()
    {
        var owner = new OwnedMemory<float>(new float[6]);
        var tensorOwner = new MemoryTensorOwner<float>(owner, [2, 3]);

        Assert.NotNull(tensorOwner.Tensor);
        Assert.Equal([2, 3], tensorOwner.Tensor.Dimensions);
    }

    [Fact]
    public void Tensor_AfterDispose_ThrowsObjectDisposedException()
    {
        var owner = new OwnedMemory<float>(new float[6]);
        var tensorOwner = new MemoryTensorOwner<float>(owner, [2, 3]);

        tensorOwner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tensorOwner.Tensor);
    }

    [Fact]
    public void Dispose_ReleasesUnderlyingOwner()
    {
        var owner = new OwnedMemory<float>(new float[4]);
        var tensorOwner = new MemoryTensorOwner<float>(owner, [4]);

        tensorOwner.Dispose();

        Assert.True(owner.IsDisposed);
    }

    [Fact]
    public void Dispose_IsIdempotent_DoesNotThrowOnSecondCall()
    {
        var owner = new OwnedMemory<float>(new float[4]);
        var tensorOwner = new MemoryTensorOwner<float>(owner, [4]);

        tensorOwner.Dispose();
        tensorOwner.Dispose();  // 第二次调用不应抛异常

        Assert.True(owner.IsDisposed);
    }

    [Fact]
    public void Tensor_BeforeDispose_AllowsAccess()
    {
        var data = new float[] { 1f, 2f, 3f, 4f };
        var owner = new OwnedMemory<float>(data);
        var tensorOwner = new MemoryTensorOwner<float>(owner, [2, 2]);

        var tensor = tensorOwner.Tensor;

        Assert.Equal(1f, tensor.Span[0]);
        Assert.Equal(4f, tensor.Span[3]);
    }
}

/// <summary>
/// YoloRawOutput 单元测试。
/// </summary>
public class YoloRawOutputTests
{
    private static MemoryTensorOwner<float> CreateOwner(int length, int[] dims)
    {
        return new MemoryTensorOwner<float>(new OwnedMemory<float>(new float[length]), dims);
    }

    [Fact]
    public void Constructor_WithSingleOutput_ProvidesOutput0()
    {
        var output0 = CreateOwner(6, [2, 3]);
        var raw = new YoloRawOutput(output0, null);

        Assert.NotNull(raw.Output0);
        Assert.Equal([2, 3], raw.Output0.Dimensions);
        Assert.Null(raw.Output1);
    }

    [Fact]
    public void Constructor_WithBothOutputs_ProvidesBothOutputs()
    {
        var output0 = CreateOwner(6, [2, 3]);
        var output1 = CreateOwner(4, [2, 2]);
        var raw = new YoloRawOutput(output0, output1);

        Assert.NotNull(raw.Output0);
        Assert.NotNull(raw.Output1);
        Assert.Equal([2, 2], raw.Output1!.Dimensions);
    }

    [Fact]
    public void Dispose_ReleasesBothOutputs()
    {
        var output0 = CreateOwner(6, [2, 3]);
        var output1 = CreateOwner(4, [2, 2]);
        var raw = new YoloRawOutput(output0, output1);

        raw.Dispose();

        Assert.Throws<ObjectDisposedException>(() => raw.Output0);
        Assert.Throws<ObjectDisposedException>(() => raw.Output1);
    }

    [Fact]
    public void Dispose_IsIdempotent_DoesNotThrowOnSecondCall()
    {
        var raw = new YoloRawOutput(CreateOwner(6, [2, 3]), null);

        raw.Dispose();
        raw.Dispose();  // 幂等
    }

    [Fact]
    public void Output0_AfterDispose_ThrowsObjectDisposedException()
    {
        var raw = new YoloRawOutput(CreateOwner(6, [2, 3]), null);

        raw.Dispose();

        Assert.Throws<ObjectDisposedException>(() => raw.Output0);
    }

    [Fact]
    public void Output1_AfterDispose_ThrowsObjectDisposedException()
    {
        var raw = new YoloRawOutput(CreateOwner(6, [2, 3]), CreateOwner(4, [2, 2]));

        raw.Dispose();

        Assert.Throws<ObjectDisposedException>(() => raw.Output1);
    }

    [Fact]
    public void Dispose_WithNullOutput1_OnlyDisposesOutput0()
    {
        var raw = new YoloRawOutput(CreateOwner(6, [2, 3]), null);

        // 不应抛 NullReferenceException
        raw.Dispose();
    }
}

/// <summary>
/// 简单的可计数 IMemoryOwner 实现，用于测试 Dispose 语义。
/// </summary>
internal sealed class OwnedMemory<T>(Memory<T> memory) : IMemoryOwner<T>
{
    public Memory<T> Memory { get; } = memory;
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
