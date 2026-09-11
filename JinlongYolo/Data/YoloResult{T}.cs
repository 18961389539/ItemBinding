namespace JinlongYolo.YoloSharp.Data;

/// <summary>
/// 包含具体预测数组的结果类型，枚举器可用于遍历预测项。
/// Result type that contains an array of specific predictions and implements enumeration.
/// </summary>
public class YoloResult<TPrediction>(TPrediction[] predictions) : YoloResult, IEnumerable<TPrediction>, IDisposable where TPrediction : IYoloPrediction<TPrediction>
{
    private bool _disposed;

    /// <summary>
    /// 获取指定索引处的预测项。
    /// Gets the prediction item at the specified index.
    /// </summary>
    /// <param name="index">要获取的项的从零开始的索引 / The zero-based index of the item to get.</param>
    /// <returns>位于指定索引处的预测项 / The prediction item at the specified index.</returns>
    public TPrediction this[int index] => predictions[index];

    /// <summary>
    /// 获取当前结果中包含的预测项总数。
    /// Gets the total number of prediction items contained in this result.
    /// </summary>
    public int Count => predictions.Length;

    /// <summary>
    /// 返回描述预测结果的字符串表示。
    /// Returns a string representation describing the predictions.
    /// </summary>
    /// <returns>预测结果的描述字符串 / A description string of the predictions.</returns>
    public override string ToString() => TPrediction.Describe(predictions);

    /// <summary>
    /// 释放所有预测项持有的资源（如 Segmentation.Mask）。幂等。
    /// Releases resources held by each prediction (e.g., Segmentation.Mask). Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var item in predictions)
        {
            (item as IDisposable)?.Dispose();
        }
    }

    #region Enumerator

    public IEnumerator<TPrediction> GetEnumerator()
    {
        foreach (var item in predictions)
        {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion
}