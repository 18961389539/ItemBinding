using JinlongYolo.YoloSharp.Memory;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// TensorShape 结构体单元测试。
/// 描述 ONNX 张量形状，支持动态维度（值为 -1）。
/// </summary>
public class TensorShapeTests
{
    [Fact]
    public void Constructor_StaticShape_ComputesLength()
    {
        var shape = new TensorShape(new[] { 1, 3, 224, 224 });

        Assert.False(shape.IsDynamic);
        Assert.Equal(1 * 3 * 224 * 224, shape.Length);
        Assert.Equal(4, shape.Dimensions.Count);
        Assert.Equal(new[] { 1, 3, 224, 224 }, shape.Dimensions);
    }

    [Fact]
    public void Constructor_DynamicShape_SetsIsDynamicAndLengthMinusOne()
    {
        var shape = new TensorShape(new[] { 1, 3, -1, -1 });

        Assert.True(shape.IsDynamic);
        Assert.Equal(-1, shape.Length);
    }

    [Fact]
    public void Constructor_SingleDimension_StaticShape()
    {
        var shape = new TensorShape(new[] { 100 });

        Assert.False(shape.IsDynamic);
        Assert.Equal(100, shape.Length);
        Assert.Single(shape.Dimensions);
    }

    [Fact]
    public void Constructor_EmptyDimensions_StaticZeroLength()
    {
        var shape = new TensorShape(Array.Empty<int>());

        Assert.False(shape.IsDynamic);
        // 空数组的乘积为 1（无维度相乘的默认值）
        Assert.Equal(1, shape.Length);
    }

    [Fact]
    public void Constructor_PartiallyDynamic_SetsIsDynamic()
    {
        var shape = new TensorShape(new[] { 1, -1, 640 });

        Assert.True(shape.IsDynamic);
        Assert.Equal(-1, shape.Length);
    }

    [Fact]
    public void Dimensions64_ReturnsLongArray()
    {
        var shape = new TensorShape(new[] { 1, 3, 640, 640 });

        Assert.Equal(4, shape.Dimensions64.Count);
        Assert.Equal(new long[] { 1, 3, 640, 640 }, shape.Dimensions64);
    }

    [Fact]
    public void Dimensions_ReturnsOriginalArrayValues()
    {
        var dims = new[] { 2, 4, 6 };
        var shape = new TensorShape(dims);

        Assert.Equal(dims, shape.Dimensions);
        Assert.Equal(2 * 4 * 6, shape.Length);
    }
}
