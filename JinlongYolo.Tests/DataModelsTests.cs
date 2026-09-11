using SixLabors.ImageSharp;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// YoloPrediction 及其派生类的单元测试。
/// 覆盖 YoloPrediction.ToString、Classification、Detection、ObbDetection、Pose、Segmentation、Keypoint、SegmentationMask。
/// </summary>
public class YoloPredictionTests
{
    [Fact]
    public void ToString_ReturnsNameAndConfidencePercentage()
    {
        var prediction = new TestablePrediction
        {
            Name = new YoloName(0, "person"),
            Confidence = 0.85f
        };

        Assert.Equal("person (85%)", prediction.ToString());
    }

    [Fact]
    public void ToString_ZeroConfidence_ReturnsZeroPercent()
    {
        var prediction = new TestablePrediction
        {
            Name = new YoloName(0, "bg"),
            Confidence = 0f
        };

        Assert.Equal("bg (0%)", prediction.ToString());
    }

    [Fact]
    public void ToString_FullConfidence_ReturnsHundredPercent()
    {
        var prediction = new TestablePrediction
        {
            Name = new YoloName(0, "obj"),
            Confidence = 1f
        };

        Assert.Equal("obj (100%)", prediction.ToString());
    }

    /// <summary>
    /// 最小可实例化的 YoloPrediction 派生类，用于测试基类行为。
    /// </summary>
    private class TestablePrediction : YoloPrediction { }
}

/// <summary>
/// Classification 单元测试。
/// </summary>
public class ClassificationTests
{
    [Fact]
    public void CanCreate_WithNameAndConfidence()
    {
        var classification = new Classification
        {
            Name = new YoloName(0, "cat"),
            Confidence = 0.92f
        };

        Assert.Equal("cat", classification.Name.Name);
        Assert.Equal(0.92f, classification.Confidence);
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        var classification = new Classification
        {
            Name = new YoloName(0, "dog"),
            Confidence = 0.75f
        };

        Assert.Equal("dog (75%)", classification.ToString());
    }
}

/// <summary>
/// Detection 单元测试。
/// </summary>
public class DetectionTests
{
    [Fact]
    public void CanCreate_WithBounds()
    {
        var detection = new Detection
        {
            Name = new YoloName(0, "car"),
            Confidence = 0.9f,
            Bounds = new Rectangle(10, 20, 100, 50)
        };

        Assert.Equal(new Rectangle(10, 20, 100, 50), detection.Bounds);
        Assert.Equal("car", detection.Name.Name);
    }

    [Fact]
    public void Inherits_FromYoloPrediction()
    {
        var detection = new Detection
        {
            Name = new YoloName(0, "x"),
            Confidence = 0.5f,
            Bounds = Rectangle.Empty
        };

        Assert.IsAssignableFrom<YoloPrediction>(detection);
    }
}

/// <summary>
/// ObbDetection 单元测试。
/// </summary>
public class ObbDetectionTests
{
    [Fact]
    public void CanCreate_WithAngle()
    {
        var obb = new ObbDetection
        {
            Name = new YoloName(0, "box"),
            Confidence = 0.8f,
            Bounds = new Rectangle(0, 0, 50, 30),
            Angle = 45f
        };

        Assert.Equal(45f, obb.Angle);
        Assert.Equal(new Rectangle(0, 0, 50, 30), obb.Bounds);
    }

    [Fact]
    public void Inherits_FromDetection()
    {
        var obb = new ObbDetection
        {
            Name = new YoloName(0, "x"),
            Confidence = 0.5f,
            Bounds = Rectangle.Empty,
            Angle = 0f
        };

        Assert.IsAssignableFrom<Detection>(obb);
    }
}

/// <summary>
/// Keypoint 单元测试。
/// </summary>
public class KeypointTests
{
    [Fact]
    public void CanCreate_WithAllProperties()
    {
        var keypoint = new Keypoint
        {
            Index = 5,
            Point = new Point(100, 200),
            Confidence = 0.88f
        };

        Assert.Equal(5, keypoint.Index);
        Assert.Equal(new Point(100, 200), keypoint.Point);
        Assert.Equal(0.88f, keypoint.Confidence);
    }

    [Fact]
    public void CanCreate_WithZeroConfidence()
    {
        var keypoint = new Keypoint
        {
            Index = 0,
            Point = Point.Empty,
            Confidence = 0f
        };

        Assert.Equal(0f, keypoint.Confidence);
    }
}

/// <summary>
/// Pose 单元测试。
/// </summary>
public class PoseTests
{
    private static Keypoint[] CreateKeypoints(int count)
    {
        var keypoints = new Keypoint[count];
        for (int i = 0; i < count; i++)
        {
            keypoints[i] = new Keypoint
            {
                Index = i,
                Point = new Point(i * 10, i * 20),
                Confidence = 0.9f - i * 0.1f
            };
        }
        return keypoints;
    }

    [Fact]
    public void Indexer_ReturnsKeypointByIndex()
    {
        var keypoints = CreateKeypoints(3);
        var pose = new Pose(keypoints)
        {
            Name = new YoloName(0, "person"),
            Confidence = 0.9f,
            Bounds = new Rectangle(0, 0, 100, 200)
        };

        Assert.Equal(0, pose[0].Index);
        Assert.Equal(1, pose[1].Index);
        Assert.Equal(2, pose[2].Index);
    }

    [Fact]
    public void GetEnumerator_IteratesAllKeypoints()
    {
        var keypoints = CreateKeypoints(5);
        var pose = new Pose(keypoints)
        {
            Name = new YoloName(0, "person"),
            Confidence = 0.9f,
            Bounds = Rectangle.Empty
        };

        var collected = new List<Keypoint>();
        foreach (var kp in pose)
        {
            collected.Add(kp);
        }

        Assert.Equal(5, collected.Count);
        Assert.Equal(0, collected[0].Index);
        Assert.Equal(4, collected[4].Index);
    }

    [Fact]
    public void Inherits_FromDetection()
    {
        var pose = new Pose(Array.Empty<Keypoint>())
        {
            Name = new YoloName(0, "person"),
            Confidence = 0.9f,
            Bounds = Rectangle.Empty
        };

        Assert.IsAssignableFrom<Detection>(pose);
    }
}

/// <summary>
/// Segmentation 单元测试。
/// </summary>
public class SegmentationTests
{
    [Fact]
    public void CanCreate_WithMask()
    {
        var mask = new BitmapBuffer(10, 10);
        var seg = new Segmentation
        {
            Name = new YoloName(0, "object"),
            Confidence = 0.85f,
            Bounds = new Rectangle(0, 0, 10, 10),
            Mask = mask
        };

        Assert.Same(mask, seg.Mask);
        Assert.Equal(new Rectangle(0, 0, 10, 10), seg.Bounds);
    }

    [Fact]
    public void Inherits_FromDetection()
    {
        var seg = new Segmentation
        {
            Name = new YoloName(0, "x"),
            Confidence = 0.5f,
            Bounds = Rectangle.Empty,
            Mask = new BitmapBuffer(1, 1)
        };

        Assert.IsAssignableFrom<Detection>(seg);
    }
}

/// <summary>
/// SegmentationMask 单元测试。
/// </summary>
public class SegmentationMaskTests
{
    [Fact]
    public void Indexer_ReturnsMaskValue()
    {
        var data = new float[,] { { 0.1f, 0.2f }, { 0.3f, 0.4f } };
        var mask = new SegmentationMask { Mask = data };

        Assert.Equal(0.1f, mask[0, 0]);
        Assert.Equal(0.4f, mask[1, 1]);
    }

    [Fact]
    public void Width_ReturnsFirstDimension()
    {
        var mask = new SegmentationMask { Mask = new float[5, 3] };

        Assert.Equal(5, mask.Width);
    }

    [Fact]
    public void Height_ReturnsSecondDimension()
    {
        var mask = new SegmentationMask { Mask = new float[5, 3] };

        Assert.Equal(3, mask.Height);
    }

    [Fact]
    public void GetConfidence_ReturnsSameAsIndexer()
    {
        var data = new float[,] { { 0.5f, 0.6f } };
        var mask = new SegmentationMask { Mask = data };

        Assert.Equal(mask[0, 1], mask.GetConfidence(0, 1));
    }
}

/// <summary>
/// YoloResult 和 YoloResult&lt;T&gt; 单元测试。
/// </summary>
public class YoloResultTests
{
    [Fact]
    public void YoloResult_SetsImageSizeAndSpeed()
    {
        var result = new YoloResult
        {
            ImageSize = new Size(640, 480),
            Speed = new SpeedResult(
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(3))
        };

        Assert.Equal(new Size(640, 480), result.ImageSize);
        Assert.Equal(10, result.Speed.Inference.TotalMilliseconds);
    }

    [Fact]
    public void YoloResultT_Count_ReturnsPredictionCount()
    {
        var predictions = new[]
        {
            new Classification { Name = new YoloName(0, "a"), Confidence = 0.9f },
            new Classification { Name = new YoloName(1, "b"), Confidence = 0.8f },
            new Classification { Name = new YoloName(2, "c"), Confidence = 0.7f },
        };

        var result = new YoloResult<Classification>(predictions)
        {
            ImageSize = new Size(224, 224),
            Speed = new SpeedResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)
        };

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void YoloResultT_Indexer_ReturnsPredictionByIndex()
    {
        var predictions = new[]
        {
            new Classification { Name = new YoloName(0, "a"), Confidence = 0.9f },
            new Classification { Name = new YoloName(1, "b"), Confidence = 0.8f },
        };

        var result = new YoloResult<Classification>(predictions)
        {
            ImageSize = new Size(224, 224),
            Speed = new SpeedResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)
        };

        Assert.Equal("a", result[0].Name.Name);
        Assert.Equal("b", result[1].Name.Name);
    }

    [Fact]
    public void YoloResultT_ToString_CallsDescribe()
    {
        var predictions = new[]
        {
            new Classification { Name = new YoloName(0, "top"), Confidence = 0.95f },
        };

        var result = new YoloResult<Classification>(predictions)
        {
            ImageSize = new Size(224, 224),
            Speed = new SpeedResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)
        };

        // Classification.Describe 返回 predictions[0].ToString()
        Assert.Equal("top (95%)", result.ToString());
    }

    [Fact]
    public void YoloResultT_GetEnumerator_IteratesAllPredictions()
    {
        var predictions = new[]
        {
            new Classification { Name = new YoloName(0, "a"), Confidence = 0.9f },
            new Classification { Name = new YoloName(1, "b"), Confidence = 0.8f },
        };

        var result = new YoloResult<Classification>(predictions)
        {
            ImageSize = new Size(224, 224),
            Speed = new SpeedResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)
        };

        var collected = result.ToList();

        Assert.Equal(2, collected.Count);
        Assert.Equal("a", collected[0].Name.Name);
        Assert.Equal("b", collected[1].Name.Name);
    }

    [Fact]
    public void YoloResultT_Empty_CountIsZero()
    {
        var result = new YoloResult<Classification>(Array.Empty<Classification>())
        {
            ImageSize = new Size(224, 224),
            Speed = new SpeedResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)
        };

        Assert.Equal(0, result.Count);
        Assert.Empty(result);
    }
}

/// <summary>
/// YoloPredictionExtensions 单元测试。
/// </summary>
public class YoloPredictionExtensionsTests
{
    [Fact]
    public void GetTopClass_ReturnsFirstClassification()
    {
        var predictions = new[]
        {
            new Classification { Name = new YoloName(0, "top"), Confidence = 0.95f },
            new Classification { Name = new YoloName(1, "other"), Confidence = 0.3f },
        };

        var result = new YoloResult<Classification>(predictions)
        {
            ImageSize = new Size(224, 224),
            Speed = new SpeedResult(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)
        };

        var top = result.GetTopClass();

        Assert.Equal("top", top.Name.Name);
        Assert.Equal(0.95f, top.Confidence);
    }
}
