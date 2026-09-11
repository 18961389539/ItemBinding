using JinlongYolo.YoloSharp;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// Vector&lt;T&gt; 泛型向量结构体单元测试。
/// 支持元组隐式转换、ToString 格式化、默认值。
/// </summary>
public class VectorTests
{
    [Fact]
    public void Constructor_SetsXAndY()
    {
        var v = new Vector<int>(3, 7);

        Assert.Equal(3, v.X);
        Assert.Equal(7, v.Y);
    }

    [Fact]
    public void Default_ReturnsZeroVector()
    {
        var v = Vector<int>.Default;

        Assert.Equal(0, v.X);
        Assert.Equal(0, v.Y);
    }

    [Fact]
    public void ImplicitConversion_FromTuple_CreatesVector()
    {
        Vector<int> v = (5, 9);

        Assert.Equal(5, v.X);
        Assert.Equal(9, v.Y);
    }

    [Fact]
    public void ImplicitConversion_FromTuple_FloatType()
    {
        Vector<float> v = (1.5f, 2.5f);

        Assert.Equal(1.5f, v.X);
        Assert.Equal(2.5f, v.Y);
    }

    [Fact]
    public void ImplicitConversion_FromTuple_DoubleType()
    {
        Vector<double> v = (3.14, 2.71);

        Assert.Equal(3.14, v.X);
        Assert.Equal(2.71, v.Y);
    }

    [Fact]
    public void ToString_ReturnsExpectedFormat()
    {
        var v = new Vector<int>(42, 99);

        Assert.Equal("X = 42, Y = 99", v.ToString());
    }

    [Fact]
    public void ToString_FloatType_ReturnsExpectedFormat()
    {
        var v = new Vector<float>(1.5f, 2.5f);

        Assert.Equal("X = 1.5, Y = 2.5", v.ToString());
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Vector<T> 未重写 == / != 运算符，也未重写 Equals；
        // 默认 ValueType.Equals 按字段反射比较，相同字段值的实例视为相等。
        var v1 = new Vector<int>(1, 2);
        var v2 = new Vector<int>(1, 2);

        Assert.Equal(v1, v2);
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var v1 = new Vector<int>(1, 2);
        var v2 = new Vector<int>(3, 4);

        Assert.NotEqual(v1, v2);
    }
}
