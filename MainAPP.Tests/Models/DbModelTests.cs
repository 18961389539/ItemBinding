using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// DbModel 默认值与字段语义测试。
/// </summary>
public class DbModelTests
{
    [Fact]
    public void Defaults_AreSetCorrectly()
    {
        var before = DateTime.Now.AddSeconds(-1);
        var model = new DbModel();
        var after = DateTime.Now.AddSeconds(1);

        Assert.Equal(string.Empty, model.Barcode);
        Assert.Equal(0u, model.Encode);
        Assert.Equal(0, model.WorldX);
        Assert.Equal(0, model.WorldY);
        Assert.Equal(0, model.ImageX);
        Assert.Equal(0, model.ImageY);
        Assert.Equal(0, model.Angle);
        Assert.Equal(0, model.Area);
        Assert.Equal(0, model.Width);
        Assert.Equal(0, model.Height);
        Assert.Equal(0, model.CostTime);
        Assert.Equal(string.Empty, model.ImageFullName);
        Assert.Equal(-1, model.Speed); // M335b: 默认 -1 表示首次接收
        Assert.InRange(model.DetectTime, before, after);
        Assert.InRange(model.EncodeTime, before, after);
        Assert.InRange(model.ImageReceivedTime, before, after);
    }

    [Fact]
    public void Properties_CanBeAssigned()
    {
        var model = new DbModel
        {
            Id = 42,
            Barcode = "ABC123",
            Encode = 12345u,
            WorldX = 100.5,
            WorldY = 200.5,
            ImageX = 50.0,
            ImageY = 60.0,
            Angle = 45.0,
            Area = 1200.0,
            Width = 40.0,
            Height = 30.0,
            CostTime = 35,
            ImageBarcodeX = 55.0,
            ImageBarcodeY = 65.0,
            BarcodeScore = 0.95,
            Score = 0.85,
            ImageFullName = @"C:\images\test.jpg",
            Speed = 200
        };

        Assert.Equal(42, model.Id);
        Assert.Equal("ABC123", model.Barcode);
        Assert.Equal(12345u, model.Encode);
        Assert.Equal(100.5, model.WorldX);
        Assert.Equal(200.5, model.WorldY);
        Assert.Equal(45.0, model.Angle);
        Assert.Equal(1200.0, model.Area);
        Assert.Equal(0.95, model.BarcodeScore);
        Assert.Equal(0.85, model.Score);
        Assert.Equal(200, model.Speed);
    }
}
