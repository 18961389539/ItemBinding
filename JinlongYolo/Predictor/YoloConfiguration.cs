namespace JinlongYolo.YoloSharp;

/// <summary>
/// YoloPredictor 的配置设置。
/// 
/// 说明（中文）：
/// 本类封装了在运行 YOLO 模型推理时使用的各种可配置项。通常在创建
/// `YoloPredictor` 时通过 `YoloPredictorOptions.Configuration` 传入自定义实例，
/// 或使用 `YoloConfiguration.Default` 快速使用默认参数。
/// 
/// 设计考虑：
/// - 这些配置项影响预处理（如是否保持纵横比、是否应用自动旋转）和后处理（
///   如置信度过滤与 NMS 行为）的行为。修改这些值会改变最终检测/分割/分类结果
///   的数量和精度折衷。
/// - 配置对象为可变（非不可变），调用者在并发场景中应自行保证线程安全，或在
///   多线程共享前复制一份。
/// 
/// English summary:
/// Configuration settings for the YoloPredictor. Provides options that influence
/// image preprocessing and postprocessing (confidence threshold, NMS IoU, resizing
/// behavior, etc.).
/// </summary>
public class YoloConfiguration : IEquatable<YoloConfiguration>
{
    /// <summary>
    /// 默认 YOLO 配置。提供一个预设的默认实例以便快速使用。
    ///
    /// 说明（中文）：
    /// 这是一个共享的静态实例，包含库的默认参数。它主要用于快速入门场景。
    /// 注意：此实例是可变的（属性可写），如果在多线程或多个组件间共享时，调用者
    /// 应在修改前进行克隆或创建新的实例以避免并发修改带来的副作用。
    ///
    /// Default YOLO configuration.
    /// </summary>
    public static readonly YoloConfiguration Default = new();

    /// <summary>
    /// 指定包括检测结果所需的最小置信度值。默认值为 0.3f。
    ///
    /// 说明（中文）：
    /// 该阈值用于在后处理阶段过滤低置信度的预测框。取值范围通常为 0 到 1。
    /// - 增大（例如 0.5 -> 0.7）：会更严格地过滤结果，减少误检（精度上升），但可能降低召回率（漏检增多）。
    /// - 减小（例如 0.3 -> 0.1）：会放宽过滤条件，返回更多候选（召回率上升），但也可能增加误检。
    /// 根据模型和应用场景（例如安全监控或对召回敏感的应用）进行调整。
    ///
    /// Specify the minimum confidence value for including a result. Default is 0.3f.
    /// </summary>
    public float Confidence { get; set; } = .3f;

    /// <summary>
    /// 指定用于非极大值抑制 (NMS) 的最小 IoU 阈值。默认值为 0.45f。
    ///
    /// 说明（中文）：
    /// NMS 阈值用于在多个重叠框中去重。典型取值范围为 0.3 到 0.6。
    /// - 增大（例如 0.45 -> 0.6）：只有当两个框高度重叠时才会被视为重复，保留更多相邻但不完全重叠的框，适用于目标密集场景以避免漏检不同目标。
    /// - 减小（例如 0.45 -> 0.3）：更激进地删除重叠框，减少重复检测，但可能错误地删除靠得很近的不同目标（召回下降）。
    /// 根据目标尺寸、密集程度与具体任务进行调整以取得最佳折衷。
    ///
    /// Specify the minimum IoU value for Non-Maximum Suppression (NMS). Default is 0.45f.
    /// </summary>
    public float IoU { get; set; } = .45f;

    /// <summary>
    /// 指定在调整图像大小时是否保持原始纵横比。默认值为 true。
    ///
    /// 说明（中文）：
    /// - 为 true：使用 letterbox 填充保持纵横比，减小因拉伸带来的形变，从而通常提升检测精度；但会引入边缘填充，需要在后处理时进行坐标去填充校正。
    /// - 为 false：直接拉伸到目标尺寸，处理更简单且无需额外校正，但可能造成变形，影响模型对某些目标的识别精度。
    /// 根据模型训练时的预处理方式选择以获得一致性。
    ///
    /// Specify whether to keep the image aspect ratio when resizing. Default is true.
    /// </summary>
    public bool KeepAspectRatio { get; set; } = true;

    /// <summary>
    /// 指定在加载图像时是否应用自动方向校正（依据 EXIF 信息）。默认值为 true。
    ///
    /// 说明（中文）：
    /// - 为 true：会根据 EXIF 的方向标签自动旋转图像，从而保证方向一致；这通常能提高检测准确性（尤其是从相机/手机拍摄的图片）。
    /// - 为 false：保留原始像素顺序，适用于已在外部统一方向或没有可靠 EXIF 的场景。
    /// 将此项设为 true 通常是安全的，但在某些自定义数据流中（已提前完成方向处理）可以禁用以节省少量 CPU。
    ///
    /// Specify whether to apply automatic image orientation correction on load. Default is true.
    /// </summary>
    public bool ApplyAutoOrient { get; set; } = true;

    /// <summary>
    /// 指定是否禁止并行推理（默认情况下预处理和后处理可并行运行）。设置为 true 可强制串行化以避免并发问题。默认值为 false。
    ///
    /// 说明（中文）：
    /// - 为 false：允许预处理与后处理并行执行，可提升吞吐量（更高的每秒帧数）。
    /// - 为 true：强制串行化操作以提高稳定性并避免线程安全问题，但会降低吞吐量并可能增加每帧的延迟。
    /// 在资源受限或遇到并发问题时设置为 true；在需要最大吞吐量时保持 false。
    ///
    /// Specify whether to suppress parallel inference (pre-processing and post-processing will run in parallelly). Default is false.
    /// </summary>
    public bool SuppressParallelInference { get; set; } = false;

    /// <summary>
    /// 限制 NMS 之后返回的最终检测数量上限。默认值为 300。
    ///
    /// 说明（中文）：
    /// - 增大：允许返回更多检测结果，适用于需要完整检测列表的场景，但会增加后续处理与传输开销。
    /// - 减小：限制返回数量以降低后续处理负载，但可能截断较低置信度但有用的检测结果。
    /// 根据系统性能与业务需求平衡该值。
    ///
    /// Limits the number of final detections returned after NMS or by end-to-end models. Default is 300.
    /// </summary>
    public int MaximumDetections { get; set; } = 300;

    /// <summary>
    /// 限制在执行 NMS 之前保留的候选框数量上限。默认值为 1024。
    ///
    /// 说明（中文）：
    /// - 增大：保留更多候选框，提升在 NMS 前保留潜在正样本的能力，可能提升召回但会增加 NMS 与内存/计算开销。
    /// - 减小：在 NMS 前裁剪掉更多候选框，可显著降低计算量和延迟，但可能丢失部分正确目标（召回下降）。
    /// 根据模型输出密度和实时性需求进行调整。
    ///
    /// Limits the number of candidate boxes retained before NMS for detection and segmentation models. Default is 1024.
    /// </summary>
    public int MaximumCandidateBoxes { get; set; } = 1024;

    /// <summary>
    /// 比较当前配置与另一个配置是否相等。
    ///
    /// 说明（中文）：
    /// 逐字段比较配置值以判定两份配置是否等价。请注意此比较基于值相等，
    /// 并不考虑对象引用或对等语义以外的上下文信息。
    ///
    /// Compare this configuration with another for equality.
    /// </summary>
    public bool Equals(YoloConfiguration? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Confidence == other.Confidence
               && IoU == other.IoU
               && KeepAspectRatio == other.KeepAspectRatio
               && ApplyAutoOrient == other.ApplyAutoOrient
             && SuppressParallelInference == other.SuppressParallelInference
             && MaximumDetections == other.MaximumDetections
             && MaximumCandidateBoxes == other.MaximumCandidateBoxes;
    }

    /// <summary>
    /// 重写对象相等比较，尝试将对象转换为 <see cref="YoloConfiguration"/> 并进行比较。
    ///
    /// 说明（中文）：
    /// 允许通过基类引用进行相等性比较，便于集合或字典等使用。
    ///
    /// Override of object equality check.
    /// </summary>
    public override bool Equals(object? obj) => Equals(obj as YoloConfiguration);

    /// <summary>
    /// 为当前配置生成哈希代码，基于各个字段的哈希值组合。
    ///
    /// 说明（中文）：
    /// 为了在哈希集合中正确使用本配置对象，基于关键字段生成哈希码。若添加新的
    /// 字段影响相等性比较，请同时更新此方法以保持一致性。
    ///
    /// Generate a hash code for the current configuration.
    /// </summary>
    public override int GetHashCode()
    {
        return Confidence.GetHashCode()
               ^ IoU.GetHashCode()
               ^ KeepAspectRatio.GetHashCode()
               ^ ApplyAutoOrient.GetHashCode()
             ^ SuppressParallelInference.GetHashCode()
             ^ MaximumDetections.GetHashCode()
             ^ MaximumCandidateBoxes.GetHashCode();
    }
}