using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AIAgent.Mcp;

/// <summary>
/// 扫码枪相关 MCP 工具集。
/// 提供扫码枪数量查询、设备信息读取、参数调整、抓拍和二维码绘制等工具，
/// 供 AI Agent 通过 MCP 协议调用。
/// </summary>
[McpServerToolType]
public static class ScannerMcpTools
{
    /// <summary>查看当前已发现的扫码枪数量</summary>
    [McpServerTool, Description("查看当前已发现的扫码枪数量")]
    public static Task<ScannerCountDto> GetScannerCount()
    {
        return VisionWorkspace.GetScannerCountAsync();
    }

    /// <summary>读取当前扫码枪设备信息（型号、序列号、IP等）</summary>
    [McpServerTool, Description("读取当前扫码枪设备信息")]
    public static Task<ScannerDeviceInfoDto> GetScannerDeviceInfo()
    {
        return VisionWorkspace.GetScannerDeviceInfoAsync();
    }

    /// <summary>查询当前扫码枪的曝光时间、增益和触发模式设置</summary>
    [McpServerTool, Description("查询当前扫码枪曝光、增益和触发设置")]
    public static Task<ScannerSettingsDto> GetScannerSettings()
    {
        return VisionWorkspace.GetScannerSettingsAsync();
    }

    /// <summary>调整扫码枪的曝光时间和增益参数</summary>
    [McpServerTool, Description("调整扫码枪曝光时间和增益")]
    public static Task<ScannerSettingsDto> AdjustScannerExposureGain(double exposureTime, double gain)
    {
        return VisionWorkspace.AdjustScannerExposureGainAsync(exposureTime, gain);
    }

    /// <summary>执行软触发抓拍，自动保存图像并显示在 MainWindow 的 ImageViewer</summary>
    [McpServerTool, Description("抓拍并自动保存，同时显示在 MainWindow 的 ImageViewer")]
    public static Task<CaptureDto> CaptureImage()
    {
        return VisionWorkspace.CaptureAsync();
    }

    /// <summary>将最新抓拍图像中的二维码识别结果绘制叠加到 ImageViewer</summary>
    [McpServerTool, Description("将最新拍照中的二维码结果绘制到 ImageViewer")]
    public static Task<OverlayDto> DrawQrResults()
    {
        return VisionWorkspace.DrawQrOverlayAsync();
    }
}

/// <summary>
/// YOLO 推理相关 MCP 工具集。
/// 提供 YOLO 模型推理工具，供 AI Agent 调用进行目标检测/分割。
/// </summary>
[McpServerToolType]
public static class YoloMcpTools
{
    /// <summary>使用指定模型对最新图像进行 YOLO 推理，绘制置信度最高的目标到 ImageViewer</summary>
    [McpServerTool, Description("使用 JinlongYolo 对最新图像进行推理，并只把置信度最高的目标绘制到 ImageViewer")]
    public static Task<InferenceDto> RunYoloInference(string modelPath)
    {
        return VisionWorkspace.RunYoloInferenceAsync(modelPath);
    }
}

/// <summary>
/// 图像处理相关 MCP 工具集。
/// 提供简单图像处理操作（如灰度化、二值化、模糊等），供 AI Agent 调用。
/// </summary>
[McpServerToolType]
public static class ImageProcessMcpTools
{
    /// <summary>对最新图像执行指定图像处理操作并显示到 ImageViewer</summary>
    [McpServerTool, Description("对最新图像执行简单图像处理并显示到 ImageViewer")]
    public static Task<ImageProcessDto> ProcessImage(string operation, double amount = 0)
    {
        return VisionWorkspace.ProcessLatestImageAsync(operation, amount);
    }
}