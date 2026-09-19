namespace JinlongYolo.YoloSharp.Services;

/// <summary>
/// 2026-09-18: OpenVINO EP 可用性的公开探测入口。
/// 用途：调用方（如 MainAPP 的后端回退链）在尝试 OpenVINO 级**之前**先探测，
/// 不可用直接跳过该级——避免每次启动都走"建池 → 抛 NotSupportedException → 捕获 → 下一级"
/// 路径（调试器首机会中断，日志噪音大）。
/// 探测结果进程级缓存（见 <see cref="OpenVinoSessionConfigurator.IsAvailable"/>），多次询问零开销。
/// </summary>
public static class OpenVinoAvailability
{
    /// <summary>
    /// 当前 ONNX Runtime native 构建是否包含 OpenVINO 执行提供器。
    /// Microsoft.ML.OnnxRuntime / .Gpu 等官方包的构建**不含** OpenVINO EP（需 Intel 自家构建）；
    /// 首次询问会做一次进程内探测（内部异常已吞），此后走缓存。
    /// </summary>
    public static bool IsProviderAvailable() => OpenVinoSessionConfigurator.IsAvailable();
}
