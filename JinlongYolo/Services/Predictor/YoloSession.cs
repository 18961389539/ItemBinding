namespace JinlongYolo.YoloSharp.Services;

/// <summary>
/// 封装与单个模型会话相关的元数据与输入输出形状信息。
///
/// 说明（中文）：
/// 该类型用于在服务层传递与模型运行相关的上下文信息，包括解析后的模型元数据、
/// ONNX Runtime 的 `InferenceSession` 实例以及输入/输出的形状信息。
/// </summary>
internal class YoloSession(YoloMetadata metadata, InferenceSession session, SessionIoShapeInfo shapeInfo)
{
    public YoloMetadata Metadata => metadata;

    public InferenceSession Session => session;

    public SessionIoShapeInfo ShapeInfo => shapeInfo;
}
