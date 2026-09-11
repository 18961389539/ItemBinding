/// <summary>
/// 检测结果集合的扩展方法，提供便捷的摘要输出等工具。
/// Extension methods for detection result collections, providing tools like convenient summary outputs.
/// </summary>
internal static class DetectionBoxesExtensions
{
    /// <summary>
    /// 将检测结果按类别聚合并输出简短摘要，例如 "2 person, 1 dog"。
    /// 用于快速展示一帧内各类别的计数统计，便于日志或简单 UI 展示。
    /// Aggregates detection results by class and outputs a short summary, e.g., "2 person, 1 dog".
    /// Used for quickly displaying count statistics of various classes in a single frame, facilitating logging or simple UI display.
    /// </summary>
    /// <param name="boxes">检测结果集合 / The collection of detection results.</param>
    /// <returns>包含类别计数摘要的字符串 / A string containing the class count summary.</returns>
    public static string Summary(this IEnumerable<Detection> boxes)
    {
        var sort = boxes.Select(x => x.Name)
                        .GroupBy(x => x.Id)
                        .OrderBy(x => x.Key)
                        .Select(x => $"{x.Count()} {x.First().Name}");

        return string.Join(", ", sort);
    }
}
