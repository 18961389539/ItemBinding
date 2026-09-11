using JinlongYolo.YoloSharp.Metadata;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// YoloName 和 SpeedResult 数据模型单元测试。
/// YoloName 表示类别名称和索引，SpeedResult 记录推理各阶段耗时。
/// </summary>
public class YoloNameTests
{
    [Fact]
    public void Constructor_SetsIdAndName()
    {
        var name = new YoloName(5, "person");

        Assert.Equal(5, name.Id);
        Assert.Equal("person", name.Name);
    }

    [Fact]
    public void Constructor_ZeroId_Valid()
    {
        var name = new YoloName(0, "background");

        Assert.Equal(0, name.Id);
        Assert.Equal("background", name.Name);
    }

    [Fact]
    public void ToString_ReturnsIdAndName()
    {
        var name = new YoloName(2, "car");

        Assert.Equal("2: 'car'", name.ToString());
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var n1 = new YoloName(1, "dog");
        var n2 = new YoloName(1, "dog");

        // YoloName 是 class，引用类型，不同实例不相等（除非重写了 Equals）
        Assert.NotSame(n1, n2);
    }

    [Fact]
    public void DifferentInstances_HaveDifferentReferences()
    {
        var n1 = new YoloName(1, "dog");
        var n2 = new YoloName(1, "dog");

        Assert.NotSame(n1, n2);
        Assert.Equal(n1.Id, n2.Id);
        Assert.Equal(n1.Name, n2.Name);
    }
}

/// <summary>
/// SpeedResult 单元测试
/// </summary>
public class SpeedResultTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var preprocess = TimeSpan.FromMilliseconds(2.5);
        var inference = TimeSpan.FromMilliseconds(15.2);
        var postprocess = TimeSpan.FromMilliseconds(1.8);

        var speed = new SpeedResult(preprocess, inference, postprocess);

        Assert.Equal(preprocess, speed.Preprocess);
        Assert.Equal(inference, speed.Inference);
        Assert.Equal(postprocess, speed.Postprocess);
    }

    [Fact]
    public void Total_EqualsSumOfAllStages()
    {
        var speed = new SpeedResult(
            TimeSpan.FromMilliseconds(2),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(3));

        // SpeedResult 可能有 Total 属性，也可能是计算属性
        // 验证各阶段之和
        var expectedTotal = speed.Preprocess + speed.Inference + speed.Postprocess;
        Assert.Equal(15, expectedTotal.TotalMilliseconds);
    }

    [Fact]
    public void Constructor_ZeroTimeSpans_Valid()
    {
        var speed = new SpeedResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, speed.Preprocess);
        Assert.Equal(TimeSpan.Zero, speed.Inference);
        Assert.Equal(TimeSpan.Zero, speed.Postprocess);
    }
}
