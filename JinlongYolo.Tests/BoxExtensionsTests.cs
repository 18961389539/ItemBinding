using SixLabors.ImageSharp;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// OrientedBoundingBoxExtensions 单元测试。
/// GetCornerPoints 根据边界框和角度计算四个角点，并确保 points[0] 是距离原点最近的角点。
/// </summary>
public class OrientedBoundingBoxExtensionsTests
{
    [Fact]
    public void GetCornerPoints_FromObbDetection_ReturnsFourPoints()
    {
        var obb = new ObbDetection
        {
            Name = new YoloName(0, "box"),
            Confidence = 0.9f,
            Bounds = new Rectangle(100, 100, 50, 30),
            Angle = 0f
        };

        var points = obb.GetCornerPoints();

        Assert.Equal(4, points.Length);
    }

    [Fact]
    public void GetCornerPoints_FromRawBoundingBox_ReturnsFourPoints()
    {
        var box = new RawBoundingBox
        {
            Index = 0,
            NameIndex = 0,
            Confidence = 0.9f,
            Bounds = new RectangleF(50, 50, 40, 20),
            Angle = 0f
        };

        var points = box.GetCornerPoints();

        Assert.Equal(4, points.Length);
    }

    [Fact]
    public void GetCornerPoints_ZeroAngle_RectangleCorners()
    {
        // 角度 0 时，四角应围绕中心对称
        var box = new RawBoundingBox
        {
            Index = 0,
            NameIndex = 0,
            Confidence = 1f,
            Bounds = new RectangleF(100, 100, 40, 20),
            Angle = 0f
        };

        var points = box.GetCornerPoints();

        // 中心点应为 (100, 100)
        // 四个角关于中心对称
        var centerX = (points[0].X + points[2].X) / 2.0;
        var centerY = (points[0].Y + points[2].Y) / 2.0;
        Assert.Equal(100, centerX);
        Assert.Equal(100, centerY);

        // 另一组对角也关于中心对称
        var centerX2 = (points[1].X + points[3].X) / 2.0;
        var centerY2 = (points[1].Y + points[3].Y) / 2.0;
        Assert.Equal(100, centerX2);
        Assert.Equal(100, centerY2);
    }

    [Fact]
    public void GetCornerPoints_Rotated90Degrees_ReturnsRotatedCorners()
    {
        // 旋转 90 度后，宽高互换效果
        var box0 = new RawBoundingBox
        {
            Index = 0,
            NameIndex = 0,
            Confidence = 1f,
            Bounds = new RectangleF(100, 100, 40, 20),
            Angle = 0f
        };
        var box90 = new RawBoundingBox
        {
            Index = 0,
            NameIndex = 0,
            Confidence = 1f,
            Bounds = new RectangleF(100, 100, 40, 20),
            Angle = 90f
        };

        var points0 = box0.GetCornerPoints();
        var points90 = box90.GetCornerPoints();

        // 旋转 90 度后，中心不变，但角点位置不同
        // 验证中心点一致
        var centerX0 = (points0[0].X + points0[2].X) / 2.0;
        var centerX90 = (points90[0].X + points90[2].X) / 2.0;
        Assert.Equal(centerX0, centerX90);
    }

    [Fact]
    public void GetCornerPoints_PointZeroIsNearestToOrigin()
    {
        // points[0] 应是距离原点 (0,0) 最近的点
        var box = new RawBoundingBox
        {
            Index = 0,
            NameIndex = 0,
            Confidence = 1f,
            Bounds = new RectangleF(200, 200, 50, 30),
            Angle = 30f
        };

        var points = box.GetCornerPoints();

        var distance0 = Distance(points[0]);
        for (int i = 1; i < 4; i++)
        {
            Assert.True(distance0 <= Distance(points[i]) + 0.5,
                $"points[0] 应是距离原点最近的点，但 points[{i}] 更近");
        }
    }

    [Fact]
    public void GetCornerPoints_SymmetricAboutCenter()
    {
        // 任意角度下，对角点应关于中心对称
        var box = new RawBoundingBox
        {
            Index = 0,
            NameIndex = 0,
            Confidence = 1f,
            Bounds = new RectangleF(150, 150, 60, 40),
            Angle = 45f
        };

        var points = box.GetCornerPoints();

        // 对角线对：(0,2) 和 (1,3)
        var centerX02 = (points[0].X + points[2].X) / 2.0;
        var centerY02 = (points[0].Y + points[2].Y) / 2.0;
        var centerX13 = (points[1].X + points[3].X) / 2.0;
        var centerY13 = (points[1].Y + points[3].Y) / 2.0;

        Assert.Equal(centerX02, centerX13, 0.5);
        Assert.Equal(centerY02, centerY13, 0.5);
    }

    private static double Distance(Point p)
    {
        return Math.Sqrt(p.X * p.X + p.Y * p.Y);
    }
}

/// <summary>
/// DetectionBoxesExtensions.Summary 单元测试。
/// Summary 将检测结果按类别聚合并输出 "N className" 格式的摘要。
/// </summary>
public class DetectionBoxesExtensionsTests
{
    [Fact]
    public void Summary_EmptyList_ReturnsEmptyString()
    {
        var detections = Array.Empty<Detection>();

        Assert.Equal(string.Empty, detections.Summary());
    }

    [Fact]
    public void Summary_SingleDetection_ReturnsCountAndName()
    {
        var detections = new[]
        {
            new Detection
            {
                Name = new YoloName(0, "person"),
                Confidence = 0.9f,
                Bounds = Rectangle.Empty
            }
        };

        Assert.Equal("1 person", detections.Summary());
    }

    [Fact]
    public void Summary_MultipleSameClass_AggregatesCount()
    {
        var detections = new[]
        {
            new Detection { Name = new YoloName(0, "person"), Confidence = 0.9f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(0, "person"), Confidence = 0.8f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(0, "person"), Confidence = 0.7f, Bounds = Rectangle.Empty },
        };

        Assert.Equal("3 person", detections.Summary());
    }

    [Fact]
    public void Summary_MultipleClasses_SeparatesByComma()
    {
        var detections = new[]
        {
            new Detection { Name = new YoloName(0, "person"), Confidence = 0.9f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(1, "dog"), Confidence = 0.8f, Bounds = Rectangle.Empty },
        };

        // 按 Id 排序：0 (person), 1 (dog)
        Assert.Equal("1 person, 1 dog", detections.Summary());
    }

    [Fact]
    public void Summary_MultipleClasses_SortedById()
    {
        var detections = new[]
        {
            new Detection { Name = new YoloName(2, "car"), Confidence = 0.9f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(0, "person"), Confidence = 0.8f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(1, "dog"), Confidence = 0.7f, Bounds = Rectangle.Empty },
        };

        // 按 Id 升序：0 (person), 1 (dog), 2 (car)
        Assert.Equal("1 person, 1 dog, 1 car", detections.Summary());
    }

    [Fact]
    public void Summary_MixedClasses_AggregatesByClassId()
    {
        var detections = new[]
        {
            new Detection { Name = new YoloName(0, "person"), Confidence = 0.9f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(1, "dog"), Confidence = 0.8f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(0, "person"), Confidence = 0.7f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(1, "dog"), Confidence = 0.6f, Bounds = Rectangle.Empty },
            new Detection { Name = new YoloName(1, "dog"), Confidence = 0.5f, Bounds = Rectangle.Empty },
        };

        // person: 2, dog: 3
        Assert.Equal("2 person, 3 dog", detections.Summary());
    }
}
