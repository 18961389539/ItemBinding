using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// ImageTool 与 YoloTools 配置模型测试，验证默认值和 JSON 别名兼容性。
/// </summary>
public class ToolConfigTests
{
    [Fact]
    public void ImageTool_Defaults()
    {
        var tool = new ImageTool();

        Assert.Equal(string.Empty, tool.DirectoryPath);
        Assert.False(tool.ReadFromScanner);
        Assert.Equal(0, tool.RemoveCount);
        Assert.Equal(200, tool.ExposureTime);
        Assert.Equal(1, tool.Gain);
    }

    [Fact]
    public void YoloTools_Defaults()
    {
        var tools = new YoloTools();

        Assert.NotNull(tools.EdgeDetection);
        Assert.NotNull(tools.AngleDetection);
        Assert.False(tools.IsAngleDetectionEnabled);
    }

    [Fact]
    public void YoloTool_Defaults()
    {
        var tool = new YoloTool();

        Assert.True(tool.IsResize);
        // P0-10: ResizeWidth→ResizeScale, ResizeHeight→ResizeScaleY 重命名后默认值改为缩放比例（4）
        Assert.Equal(4, tool.ResizeScale);
        Assert.Equal(4, tool.ResizeScaleY);
        Assert.False(tool.UseCuda);
        Assert.Equal(0, tool.CudaDeviceId);
        Assert.Equal(0.3f, tool.Confidence);
        Assert.Equal(0.45f, tool.IoU);
        Assert.True(tool.KeepAspectRatio);
        Assert.True(tool.ApplyAutoOrient);
        Assert.False(tool.SuppressParallelInference);
    }

    [Fact]
    public void YoloTools_AngleDetection_RetrocompatibleJsonAlias()
    {
        // 旧配方文件使用 "AngelDetection"（拼写错误），新属性应能反序列化旧 JSON
        const string legacyJson = """
            {
              "AngelDetection": { "Confidence": 0.5 },
              "IsEnableAngelDetection": true
            }
            """;

        var tools = System.Text.Json.JsonSerializer.Deserialize<YoloTools>(legacyJson);

        Assert.NotNull(tools);
        Assert.NotNull(tools!.AngleDetection);
        Assert.Equal(0.5f, tools.AngleDetection!.Confidence);
        Assert.True(tools.IsAngleDetectionEnabled);
    }

    [Fact]
    public void YoloTool_ResizeScale_RetrocompatibleJsonAlias()
    {
        // P0-10: 旧配方文件使用 "ResizeWidth"/"ResizeHeight"（语义为目标宽度/高度），
        // 重命名为 ResizeScale/ResizeScaleY 后应仍能通过 JSON 别名反序列化旧配方
        const string legacyJson = """
            {
              "ResizeWidth": 4,
              "ResizeHeight": 4
            }
            """;

        var tool = System.Text.Json.JsonSerializer.Deserialize<YoloTool>(legacyJson);

        Assert.NotNull(tool);
        // 旧 JSON 键 "ResizeWidth" 通过 JsonPropertyName 兼容映射到新属性 ResizeScale
        Assert.Equal(4, tool!.ResizeScale);
        Assert.Equal(4, tool.ResizeScaleY);
    }

    [Fact]
    public void YoloTools_RoundtripSerialization_PreservesAngleDetection()
    {
        var original = new YoloTools
        {
            AngleDetection = new YoloTool { Confidence = 0.7f },
            IsAngleDetectionEnabled = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<YoloTools>(json);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.AngleDetection);
        Assert.Equal(0.7f, deserialized.AngleDetection!.Confidence);
        Assert.True(deserialized.IsAngleDetectionEnabled);
    }
}
