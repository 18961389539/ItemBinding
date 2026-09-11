using System.Globalization;
using System.Text;

namespace JinlongYolo.YoloSharp;

/// <summary>
/// OpenVINO 提供器的选项配置。
/// Options configuration for the OpenVINO execution provider.
///
/// 封装 OpenVINO 执行提供器需要的各类配置，如设备类型、性能与精度提示、线程数等。
/// 这些选项会被转换为 OpenVINO 提供器的提供参数或 load_config JSON。
/// Encapsulates various configurations needed for the OpenVINO execution provider, such as device type,
/// performance and precision hints, and thread counts. These options are converted into provider
/// parameters or a load_config JSON string for the OpenVINO provider.
/// </summary>
public sealed class OpenVinoOptions
{
    /// <summary>
    /// 获取或设置设备类型（如 "CPU"、"GPU"）。空白值会回退到 "CPU"。
    /// Gets or sets the device type (e.g., "CPU", "GPU"). Blank values fall back to "CPU".
    /// </summary>
    public string DeviceType { get; init; } = "CPU";

    /// <summary>
    /// 获取或设置缓存目录。
    /// Gets or sets the cache directory.
    /// </summary>
    public string? CacheDirectory { get; init; }

    /// <summary>
    /// 获取或设置性能提示（如 "LATENCY"、"THROUGHPUT"）。
    /// Gets or sets the performance hint (e.g., "LATENCY", "THROUGHPUT").
    /// </summary>
    public string? PerformanceHint { get; init; } = "LATENCY";

    /// <summary>
    /// 获取或设置精度提示（如 "f32"、"f16"、"bf16"）。兼容传入 "FP32"、"FP16" 等常见写法。
    /// Gets or sets the precision hint (e.g., "f32", "f16", "bf16"). Common aliases such as "FP32" and "FP16" are normalized.
    /// </summary>
    public string? PrecisionHint { get; init; }

    /// <summary>
    /// 获取或设置流的数量。
    /// Gets or sets the number of streams.
    /// </summary>
    public int? NumberOfStreams { get; init; }

    /// <summary>
    /// 获取或设置推理线程的数量。
    /// Gets or sets the number of inference threads.
    /// </summary>
    public int? InferenceThreads { get; init; }

    /// <summary>
    /// 获取或设置自定义的加载配置 JSON 字符串。
    /// Gets or sets a custom load configuration JSON string.
    /// </summary>
    public string? LoadConfig { get; init; }

    // 参数影响说明（中文）：
    // - DeviceType: 常见取值为 "CPU"、"GPU" 或设备特定标识。改为 GPU 可在支持的设备上提升性能，但需确保 OpenVINO 支持对应设备。
    // - PerformanceHint: 如 "LATENCY" 或 "THROUGHPUT"，选择不同策略会影响调度与吞吐/延迟权衡。
    // - PrecisionHint: 指定推理精度（例如 FP32/FP16），降低精度通常能提高速度但可能略降准确度。
    // - NumberOfStreams / InferenceThreads: 增加这些值可能提升并发推理吞吐，但会增加 CPU/设备资源占用；过高可能造成上下文切换损耗。

    internal Dictionary<string, string> CreateProviderOptions()
    {
        var normalizedDeviceType = NormalizeDeviceType(DeviceType);
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["device_type"] = normalizedDeviceType
        };

        var loadConfig = string.IsNullOrWhiteSpace(LoadConfig)
                         ? BuildLoadConfig(normalizedDeviceType)
                         : LoadConfig.Trim();

        if (!string.IsNullOrWhiteSpace(loadConfig))
        {
            options["load_config"] = loadConfig;
        }

        return options;
    }

    private string? BuildLoadConfig(string normalizedDeviceType)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(PerformanceHint))
        {
            settings["PERFORMANCE_HINT"] = PerformanceHint.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(PrecisionHint))
        {
            settings["INFERENCE_PRECISION_HINT"] = NormalizePrecisionHint(PrecisionHint);
        }

        if (NumberOfStreams is int streams && streams > 0)
        {
            settings["NUM_STREAMS"] = streams.ToString(CultureInfo.InvariantCulture);
        }

        if (InferenceThreads is int threads && threads > 0)
        {
            settings["INFERENCE_NUM_THREADS"] = threads.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(CacheDirectory))
        {
            settings["CACHE_DIR"] = CacheDirectory.Trim();
        }

        if (settings.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder(128);

        builder.Append('{');
    AppendJsonString(builder, GetDeviceConfigKey(normalizedDeviceType));
        builder.Append(':');
        builder.Append('{');

        var first = true;

        foreach (var pair in settings)
        {
            if (!first)
            {
                builder.Append(',');
            }

            AppendJsonString(builder, pair.Key);
            builder.Append(':');
            AppendJsonString(builder, pair.Value);

            first = false;
        }

        builder.Append('}');
        builder.Append('}');

        return builder.ToString();
    }

    private static string NormalizeDeviceType(string deviceType)
    {
        return string.IsNullOrWhiteSpace(deviceType)
            ? "CPU"
            : deviceType.Trim();
    }

    private static string NormalizePrecisionHint(string precisionHint)
    {
        var normalized = precisionHint.Trim();

        return normalized.ToUpperInvariant() switch
        {
            "FP16" => "f16",
            "FP32" => "f32",
            "BF16" => "bf16",
            _ => normalized,
        };
    }

    private static string GetDeviceConfigKey(string deviceType)
    {
        var normalized = deviceType.Trim();

        if (string.IsNullOrEmpty(normalized))
        {
            return "CPU";
        }

        var separator = normalized.IndexOfAny([':', '.']);

        return (separator < 0 ? normalized : normalized[..separator]).ToUpperInvariant();
    }

    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;

                case '"':
                    builder.Append("\\\"");
                    break;

                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
    }
}