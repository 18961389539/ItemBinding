namespace JinlongYolo.YoloSharp.Contracts.Services;

/// <summary>
/// 封装预处理和模型执行过程的服务接口。
/// Service interface encapsulating the preprocessing and model execution processes.
/// </summary>
internal interface ISessionRunner
{
    /// <summary>
    /// 对图像进行预处理并运行推理会话。
    /// Preprocesses the image and runs the inference session.
    /// </summary>
    /// <param name="image">要推理的输入图像 / The input image to infer.</param>
    /// <param name="timer">记录各阶段耗时的计时器 / Timer recording the duration of each phase.</param>
    /// <returns>模型推理的原始输出 / Raw output from the model inference.</returns>
    public IYoloRawOutput PreprocessAndRun(Image<Rgb24> image, out PredictorTimer timer);
}