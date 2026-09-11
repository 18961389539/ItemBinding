namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 姿态检测预测结果，包含关键点数组并继承检测信息（边界框）。
/// Pose prediction result containing keypoints and inheriting detection info (bounds).
/// </summary>
public class Pose(Keypoint[] keypoints) : Detection, IYoloPrediction<Pose>, IEnumerable<Keypoint>
{
    /// <summary>
    /// 获取指定索引处的关键点。
    /// Gets the keypoint at the specified index.
    /// </summary>
    /// <param name="index">要获取的关键点的从零开始的索引 / The zero-based index of the keypoint to get.</param>
    /// <returns>指定索引处的关键点 / The keypoint at the specified index.</returns>
    public Keypoint this[int index] => keypoints[index];

    /// <summary>
    /// 返回姿态预测结果的描述摘要。
    /// Returns a summary describing the pose prediction results.
    /// </summary>
    static string IYoloPrediction<Pose>.Describe(Pose[] predictions) => predictions.Summary();

    #region Enumerator

    public IEnumerator<Keypoint> GetEnumerator()
    {
        foreach (var item in keypoints)
        {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion
}