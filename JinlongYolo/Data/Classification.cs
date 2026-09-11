
namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 图像分类的预测结果类型，通常只包含一个最高置信度的分类。
/// Classification prediction result type, typically contains a single top classification.
/// </summary>
public class Classification : YoloPrediction, IYoloPrediction<Classification>
{
    /// <summary>
    /// 返回分类预测结果的描述摘要。
    /// Returns a summary describing the classification prediction results.
    /// </summary>
    static string IYoloPrediction<Classification>.Describe(Classification[] predictions) => predictions[0].ToString();
}