namespace JinlongYolo.YoloSharp;

/// <summary>
/// 通用的二维向量/坐标结构，使用泛型以支持不同数值类型（如 int、float 等）。
/// Generic 2D vector/coordinate structure supporting different numeric types.
/// </summary>
/// <typeparam name="T">向量元素的类型 / The type of the vector elements.</typeparam>
/// <param name="x">X 分量的初始值 / Initial value of the X component.</param>
/// <param name="y">Y 分量的初始值 / Initial value of the Y component.</param>
public readonly struct Vector<T>(T x, T y)
{
    /// <summary>
    /// 获取默认的 Vector 实例。
    /// Gets the default Vector instance.
    /// </summary>
    public static Vector<T> Default { get; } = new();

    /// <summary>
    /// 获取 X 分量。
    /// Gets the X component.
    /// </summary>
    public T X => x;

    /// <summary>
    /// 获取 Y 分量。
    /// Gets the Y component.
    /// </summary>
    public T Y => y;

    /// <summary>
    /// 返回向量的字符串表示，便于调试输出。
    /// Returns a string representation of the vector for debugging.
    /// </summary>
    /// <returns>向量的字符串表示 / String representation of the vector.</returns>
    public override string ToString() => $"X = {x}, Y = {y}";

    /// <summary>
    /// 支持从元组隐式转换为 Vector。
    /// Supports implicit conversion from a tuple to a Vector.
    /// </summary>
    /// <param name="tuple">包含两个元素的元组 / Tuple containing two elements.</param>
    public static implicit operator Vector<T>(ValueTuple<T, T> tuple) => new(tuple.Item1, tuple.Item2);
}
