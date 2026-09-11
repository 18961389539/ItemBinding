using SixLabors.ImageSharp;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// ImageTransform、KeypointShape、RawBoundingBox.CompareTo 的单元测试。
/// 这些是 JinlongYolo 中的简单值类型/结构体。
/// </summary>
public class SimpleTypesTests
{
    /// <summary>
    /// ImageTransform 单元测试。
    /// </summary>
    public class ImageTransformTests
    {
        [Fact]
        public void CanSet_PaddingAndRatio()
        {
            var transform = new ImageTransform
            {
                Padding = new Vector<int>(10, 20),
                Ratio = new Vector<float>(0.5f, 0.75f)
            };

            Assert.Equal(10, transform.Padding.X);
            Assert.Equal(20, transform.Padding.Y);
            Assert.Equal(0.5f, transform.Ratio.X);
            Assert.Equal(0.75f, transform.Ratio.Y);
        }

        [Fact]
        public void Default_HasZeroValues()
        {
            var transform = default(ImageTransform);

            Assert.Equal(0, transform.Padding.X);
            Assert.Equal(0, transform.Padding.Y);
            Assert.Equal(0f, transform.Ratio.X);
            Assert.Equal(0f, transform.Ratio.Y);
        }
    }

    /// <summary>
    /// KeypointShape 单元测试。
    /// </summary>
    public class KeypointShapeTests
    {
        [Fact]
        public void Constructor_SetsCountAndChannels()
        {
            var shape = new KeypointShape(17, 3);

            Assert.Equal(17, shape.Count);
            Assert.Equal(3, shape.Channels);
        }

        [Fact]
        public void Constructor_TwoChannels_Valid()
        {
            // 2 通道表示 [x, y]
            var shape = new KeypointShape(5, 2);

            Assert.Equal(5, shape.Count);
            Assert.Equal(2, shape.Channels);
        }

        [Fact]
        public void Constructor_ThreeChannels_Valid()
        {
            // 3 通道表示 [x, y, confidence]
            var shape = new KeypointShape(17, 3);

            Assert.Equal(17, shape.Count);
            Assert.Equal(3, shape.Channels);
        }

        [Fact]
        public void Constructor_ZeroCount_Valid()
        {
            var shape = new KeypointShape(0, 3);

            Assert.Equal(0, shape.Count);
            Assert.Equal(3, shape.Channels);
        }
    }

    /// <summary>
    /// RawBoundingBox.CompareTo 单元测试。
    /// CompareTo 按 Confidence 进行比较，用于排序。
    /// </summary>
    public class RawBoundingBoxCompareToTests
    {
        [Fact]
        public void CompareTo_HigherConfidence_ReturnsPositive()
        {
            var high = CreateBox(confidence: 0.9f);
            var low = CreateBox(confidence: 0.5f);

            // high.CompareTo(low)：high.Confidence > low.Confidence，应返回正数
            Assert.True(high.CompareTo(low) > 0);
        }

        [Fact]
        public void CompareTo_LowerConfidence_ReturnsNegative()
        {
            var low = CreateBox(confidence: 0.3f);
            var high = CreateBox(confidence: 0.8f);

            // low.CompareTo(high)：low.Confidence < high.Confidence，应返回负数
            Assert.True(low.CompareTo(high) < 0);
        }

        [Fact]
        public void CompareTo_EqualConfidence_ReturnsZero()
        {
            var a = CreateBox(confidence: 0.75f);
            var b = CreateBox(confidence: 0.75f);

            Assert.Equal(0, a.CompareTo(b));
        }

        [Fact]
        public void CompareTo_SortsByConfidenceDescending()
        {
            var boxes = new[]
            {
                CreateBox(confidence: 0.5f),
                CreateBox(confidence: 0.9f),
                CreateBox(confidence: 0.3f),
                CreateBox(confidence: 0.7f),
            };

            Array.Sort(boxes);

            // 升序排序后，Confidence 应从小到大
            Assert.Equal(0.3f, boxes[0].Confidence);
            Assert.Equal(0.5f, boxes[1].Confidence);
            Assert.Equal(0.7f, boxes[2].Confidence);
            Assert.Equal(0.9f, boxes[3].Confidence);
        }

        [Fact]
        public void CompareTo_IgnoresOtherFields()
        {
            // CompareTo 只比较 Confidence，不比较 Index/NameIndex/Bounds
            var a = CreateBox(index: 0, nameIndex: 0, confidence: 0.5f);
            var b = CreateBox(index: 999, nameIndex: 999, confidence: 0.5f);

            Assert.Equal(0, a.CompareTo(b));
        }

        private static RawBoundingBox CreateBox(int index = 0, int nameIndex = 0, float confidence = 0.5f)
        {
            return new RawBoundingBox
            {
                Index = index,
                NameIndex = nameIndex,
                Confidence = confidence,
                Bounds = new RectangleF(0, 0, 10, 10)
            };
        }
    }
}
