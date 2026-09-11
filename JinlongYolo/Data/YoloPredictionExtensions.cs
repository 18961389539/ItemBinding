namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 为 YOLO 预测结果提供扩展方法。
/// Provides extension methods for YOLO prediction results.
/// </summary>
public static class YoloPredictionExtensions
{
    /// <summary>
    /// 获取分类结果中的最高置信度类别（Top-1 类别）。
    /// Gets the class with the highest confidence (Top-1 class) from the classification results.
    /// </summary>
    /// <param name="result">分类预测结果 / The classification prediction result.</param>
    /// <returns>置信度最高的分类对象 / The classification object with the highest confidence.</returns>
    public static Classification GetTopClass(this YoloResult<Classification> result) => result[0];
}