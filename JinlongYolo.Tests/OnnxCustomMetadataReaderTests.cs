using System.Text;
using JinlongYolo.YoloSharp.Metadata;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// OnnxCustomMetadataReader 单元测试。
/// 这个类手写 protobuf 解析器，从 ONNX 模型文件的 metadata_props 字段（field 14）中读取 "task" 键。
/// 这里通过手工构造 protobuf 字节序列来测试 byte[] 重载。
/// </summary>
public class OnnxCustomMetadataReaderTests
{
    /// <summary>
    /// 构造包含 task 键值对的 ONNX ModelProto 片段（仅 metadata_props 字段）。
    /// protobuf 编码：
    ///   field 14 (metadata_props), wire type 2 (length-delimited): tag = (14&lt;&lt;3)|2 = 0x72
    ///   每个条目是一个 length-delimited 消息，包含：
    ///     field 1 (key), wire type 2: tag = (1&lt;&lt;3)|2 = 0x0A
    ///     field 2 (value), wire type 2: tag = (2&lt;&lt;3)|2 = 0x12
    /// </summary>
    private static byte[] BuildOnnxBytes(string taskValue)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 构造 entry 内容（key="task", value=taskValue）
        var keyBytes = Encoding.UTF8.GetBytes("task");
        var valueBytes = Encoding.UTF8.GetBytes(taskValue);

        using var entryStream = new MemoryStream();
        using var entryWriter = new BinaryWriter(entryStream);
        // field 1: key
        entryWriter.Write((byte)0x0A);  // tag: field 1, wire type 2
        WriteVarint(entryWriter, keyBytes.Length);
        entryWriter.Write(keyBytes);
        // field 2: value
        entryWriter.Write((byte)0x12);  // tag: field 2, wire type 2
        WriteVarint(entryWriter, valueBytes.Length);
        entryWriter.Write(valueBytes);

        var entryBytes = entryStream.ToArray();

        // 写入 ModelProto 的 field 14 (metadata_props)
        writer.Write((byte)0x72);  // tag: field 14, wire type 2
        WriteVarint(writer, entryBytes.Length);
        writer.Write(entryBytes);

        return ms.ToArray();
    }

    private static void WriteVarint(BinaryWriter writer, int value)
    {
        // protobuf varint 编码（小端 7-bit 分组）
        while (value > 0x7F)
        {
            writer.Write((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        writer.Write((byte)value);
    }

    [Theory]
    [InlineData("detect", YoloTask.Detect)]
    [InlineData("obb", YoloTask.Obb)]
    [InlineData("pose", YoloTask.Pose)]
    [InlineData("segment", YoloTask.Segment)]
    [InlineData("classify", YoloTask.Classify)]
    public void TryReadTask_ValidTaskString_ReturnsCorrectTask(string taskText, YoloTask expected)
    {
        var bytes = BuildOnnxBytes(taskText);

        var result = OnnxCustomMetadataReader.TryReadTask(bytes, out var task);

        Assert.True(result);
        Assert.Equal(expected, task);
    }

    [Theory]
    [InlineData("DETECT")]      // 大写
    [InlineData("Detect")]      // 首字母大写
    [InlineData(" detect ")]    // 前后空格
    [InlineData("\tdetect\n")]  // 制表符和换行
    public void TryReadTask_CaseInsensitiveAndTrimmed_ParsesSuccessfully(string taskText)
    {
        var bytes = BuildOnnxBytes(taskText);

        var result = OnnxCustomMetadataReader.TryReadTask(bytes, out var task);

        Assert.True(result);
        Assert.Equal(YoloTask.Detect, task);
    }

    [Fact]
    public void TryReadTask_UnknownTaskString_ReturnsFalse()
    {
        var bytes = BuildOnnxBytes("unknown");

        var result = OnnxCustomMetadataReader.TryReadTask(bytes, out var task);

        Assert.False(result);
        Assert.Equal(default(YoloTask), task);
    }

    [Fact]
    public void TryReadTask_EmptyTaskString_ReturnsFalse()
    {
        var bytes = BuildOnnxBytes("");

        var result = OnnxCustomMetadataReader.TryReadTask(bytes, out var task);

        Assert.False(result);
        Assert.Equal(default(YoloTask), task);
    }

    [Fact]
    public void TryReadTask_EmptyByteArray_ReturnsFalse()
    {
        var result = OnnxCustomMetadataReader.TryReadTask([], out var task);

        Assert.False(result);
        Assert.Equal(default(YoloTask), task);
    }

    [Fact]
    public void TryReadTask_NoMetadataPropsField_ReturnsFalse()
    {
        // 构造一个不包含 field 14 的字节数组
        // 只包含一个其他字段（例如 field 1, wire type 0 / varint）
        var bytes = new byte[] { 0x08, 0x01 };  // field 1, varint, value=1

        var result = OnnxCustomMetadataReader.TryReadTask(bytes, out var task);

        Assert.False(result);
    }

    [Fact]
    public void TryReadTask_MetadataPropsWithoutTaskKey_ReturnsFalse()
    {
        // 构造包含其他 key 但不含 "task" 的 metadata_props
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var keyBytes = Encoding.UTF8.GetBytes("author");
        var valueBytes = Encoding.UTF8.GetBytes("jinlong");

        using var entryStream = new MemoryStream();
        using var entryWriter = new BinaryWriter(entryStream);
        entryWriter.Write((byte)0x0A);
        WriteVarint(entryWriter, keyBytes.Length);
        entryWriter.Write(keyBytes);
        entryWriter.Write((byte)0x12);
        WriteVarint(entryWriter, valueBytes.Length);
        entryWriter.Write(valueBytes);

        var entryBytes = entryStream.ToArray();
        writer.Write((byte)0x72);
        WriteVarint(writer, entryBytes.Length);
        writer.Write(entryBytes);

        var result = OnnxCustomMetadataReader.TryReadTask(ms.ToArray(), out var task);

        Assert.False(result);
    }

    [Fact]
    public void TryReadTask_MultipleEntries_FindsTaskKey()
    {
        // 构造包含多个 metadata_props 条目，task 在中间
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 第一个 entry: author = jinlong
        WriteEntry(writer, "author", "jinlong");
        // 第二个 entry: task = detect
        WriteEntry(writer, "task", "detect");
        // 第三个 entry: version = 1.0
        WriteEntry(writer, "version", "1.0");

        var result = OnnxCustomMetadataReader.TryReadTask(ms.ToArray(), out var task);

        Assert.True(result);
        Assert.Equal(YoloTask.Detect, task);
    }

    [Fact]
    public void TryReadTask_TruncatedData_ReturnsFalse()
    {
        // 截断的 protobuf 数据不应导致异常
        var bytes = new byte[] { 0x72, 0x05, 0x0A, 0x04 };  // 声明长度 5 但数据不足

        var result = OnnxCustomMetadataReader.TryReadTask(bytes, out var task);

        Assert.False(result);
    }

    [Fact]
    public void TryReadTask_InvalidVarint_ReturnsFalse()
    {
        // 无限延长的 varint（每个字节高位为 1 表示继续）
        var bytes = new byte[] { 0x72, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 };

        var result = OnnxCustomMetadataReader.TryReadTask(bytes, out var task);

        Assert.False(result);
    }

    private static void WriteEntry(BinaryWriter writer, string key, string value)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var valueBytes = Encoding.UTF8.GetBytes(value);

        using var entryStream = new MemoryStream();
        using var entryWriter = new BinaryWriter(entryStream);
        entryWriter.Write((byte)0x0A);
        WriteVarint(entryWriter, keyBytes.Length);
        entryWriter.Write(keyBytes);
        entryWriter.Write((byte)0x12);
        WriteVarint(entryWriter, valueBytes.Length);
        entryWriter.Write(valueBytes);

        var entryBytes = entryStream.ToArray();
        writer.Write((byte)0x72);  // field 14, wire type 2
        WriteVarint(writer, entryBytes.Length);
        writer.Write(entryBytes);
    }
}
