namespace JinlongYolo.YoloSharp;

/// <summary>
/// `YoloPredictor` 后端类型标记。
///
/// 说明（中文）：
/// - 该枚举用于描述池中实例在创建时所使用或声明的后端类型。
/// - 对于显式异构池，`CpuOnly` / `OpenVinoCpu` / `OpenVinoGpu` 表示确定的后端。
/// - 对于旧同构入口或自定义工厂，可能只能给出近似或声明式标记，而不一定代表运行时最终实际附加的执行提供器。
///
/// Backend kind tag for `YoloPredictor` instances.
/// </summary>
public enum YoloPredictorBackendKind
{
    /// <summary>
    /// 未知或未声明的后端。
    /// Unknown or unspecified backend.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 纯 CPU 后端。
    /// CPU-only backend.
    /// </summary>
    CpuOnly,

    /// <summary>
    /// OpenVINO CPU 后端。
    /// OpenVINO CPU backend.
    /// </summary>
    OpenVinoCpu,

    /// <summary>
    /// OpenVINO GPU 后端。
    /// OpenVINO GPU backend.
    /// </summary>
    OpenVinoGpu,

    /// <summary>
    /// OpenVINO 其他设备类型后端。
    /// OpenVINO backend targeting a non-CPU/GPU device type.
    /// </summary>
    OpenVino,

    /// <summary>
    /// CUDA 后端。
    /// CUDA backend.
    /// </summary>
    Cuda,

    /// <summary>
    /// 自动偏好 OpenVINO CPU 的声明式配置。
    /// Declarative configuration that may auto-prefer OpenVINO CPU.
    /// </summary>
    AutoPreferredOpenVinoCpu,

    /// <summary>
    /// 自定义 SessionOptions 后端。
    /// Backend created from custom SessionOptions.
    /// </summary>
    CustomSession,

    /// <summary>
    /// 自定义工厂后端。
    /// Backend created by a custom factory.
    /// </summary>
    CustomFactory,
}