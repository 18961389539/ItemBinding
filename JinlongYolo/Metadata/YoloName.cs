namespace JinlongYolo.YoloSharp.Metadata;

/// <summary>
/// 表示 YOLO 模型的类别名称及其对应的 ID。
/// Represents a class name of the YOLO model and its corresponding ID.
/// </summary>
/// <param name="id">类别的唯一标识符 / The unique identifier of the class.</param>
/// <param name="name">类别的名称 / The name of the class.</param>
public class YoloName(int id, string name)
{
    /// <summary>
    /// 获取类别的 ID。
    /// Gets the ID of the class.
    /// </summary>
    public int Id { get; } = id;

    /// <summary>
    /// 获取类别的名称。
    /// Gets the name of the class.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// 返回类别 ID 和名称的字符串表示形式。
    /// Returns a string representation of the class ID and name.
    /// </summary>
    /// <returns>包含 ID 和名称的字符串 / A string containing the ID and name.</returns>
    public override string ToString()
    {
        return $"{Id}: '{Name}'";
    }
}