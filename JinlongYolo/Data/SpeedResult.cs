namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 记录推理各个阶段的耗时。
/// Records the elapsed time for various stages of inference.
/// </summary>
/// <param name="preprocess">预处理耗时 / Preprocessing time.</param>
/// <param name="inference">模型推理耗时 / Model inference time.</param>
/// <param name="postprocess">后处理耗时 / Postprocessing time.</param>
public readonly struct SpeedResult(TimeSpan preprocess,
                                   TimeSpan inference,
                                   TimeSpan postprocess)
{
    /// <summary>
    /// 获取预处理阶段的耗时。
    /// Gets the time spent on preprocessing.
    /// </summary>
    public TimeSpan Preprocess { get; } = preprocess;

    /// <summary>
    /// 获取模型推理阶段的耗时。
    /// Gets the time spent on model inference.
    /// </summary>
    public TimeSpan Inference { get; } = inference;

    /// <summary>
    /// 获取后处理阶段的耗时。
    /// Gets the time spent on postprocessing.
    /// </summary>
    public TimeSpan Postprocess { get; } = postprocess;

    /// <summary>
    /// 返回速度结果的字符串表示，包含各阶段的秒数。
    /// Returns a string representation of the speed result in seconds.
    /// </summary>
    public override string ToString()
    {
        return $"Preprocess: {Preprocess.TotalSeconds},\tInference: {Inference.TotalSeconds},\tPostprocess: {Postprocess.TotalSeconds}";
    }
}