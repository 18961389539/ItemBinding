using System.Collections.Concurrent;
using System.Text;

namespace JinlongYolo.YoloSharp.Metadata;

internal static class OnnxCustomMetadataReader
{
    private readonly record struct CachedTask(long FileLength, long LastWriteTimeUtcTicks, bool HasValue, YoloTask Task);

    private const int MetadataPropsFieldNumber = 14;
    private const int StringKeyFieldNumber = 1;
    private const int StringValueFieldNumber = 2;
    private static readonly ConcurrentDictionary<string, CachedTask> TaskCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryReadTask(string path, out YoloTask task)
    {
        task = default;

        try
        {
            var fileInfo = new FileInfo(path);
            var cacheKey = fileInfo.FullName;
            var fileLength = fileInfo.Length;
            var lastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks;

            if (TaskCache.TryGetValue(cacheKey, out var cached)
                && cached.FileLength == fileLength
                && cached.LastWriteTimeUtcTicks == lastWriteTimeUtcTicks)
            {
                task = cached.Task;
                return cached.HasValue;
            }

            var hasValue = TryReadTask(File.ReadAllBytes(path), out task);

            TaskCache[cacheKey] = new CachedTask(fileLength, lastWriteTimeUtcTicks, hasValue, task);

            return hasValue;
        }
        catch (Exception) when (path.Length > 0)
        {
            return false;
        }
    }

    public static bool TryReadTask(byte[] model, out YoloTask task)
    {
        if (!TryGetCustomMetadataValue(model, "task", out var taskText))
        {
            task = default;
            return false;
        }

        return TryParseTask(taskText, out task);
    }

    private static bool TryParseTask(string taskText, out YoloTask task)
    {
        switch (taskText.Trim().ToLowerInvariant())
        {
            case "detect":
                task = YoloTask.Detect;
                return true;

            case "obb":
                task = YoloTask.Obb;
                return true;

            case "pose":
                task = YoloTask.Pose;
                return true;

            case "segment":
                task = YoloTask.Segment;
                return true;

            case "classify":
                task = YoloTask.Classify;
                return true;

            default:
                task = default;
                return false;
        }
    }

    private static bool TryGetCustomMetadataValue(ReadOnlySpan<byte> model, string key, out string value)
    {
        var offset = 0;

        while (offset < model.Length)
        {
            if (!TryReadTag(model, ref offset, out var fieldNumber, out var wireType))
            {
                break;
            }

            if (fieldNumber == MetadataPropsFieldNumber && wireType == 2)
            {
                if (!TryReadLengthDelimited(model, ref offset, out var entryData))
                {
                    break;
                }

                if (TryReadMetadataEntry(entryData, key, out value))
                {
                    return true;
                }

                continue;
            }

            if (!SkipField(model, ref offset, wireType))
            {
                break;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadMetadataEntry(ReadOnlySpan<byte> entryData, string expectedKey, out string value)
    {
        string? entryKey = null;
        string? entryValue = null;
        var offset = 0;

        while (offset < entryData.Length)
        {
            if (!TryReadTag(entryData, ref offset, out var fieldNumber, out var wireType))
            {
                break;
            }

            if (wireType == 2)
            {
                if (!TryReadLengthDelimited(entryData, ref offset, out var stringData))
                {
                    break;
                }

                if (fieldNumber == StringKeyFieldNumber)
                {
                    entryKey = Encoding.UTF8.GetString(stringData);
                    continue;
                }

                if (fieldNumber == StringValueFieldNumber)
                {
                    entryValue = Encoding.UTF8.GetString(stringData);
                    continue;
                }

                continue;
            }

            if (!SkipField(entryData, ref offset, wireType))
            {
                break;
            }
        }

        if (string.Equals(entryKey, expectedKey, StringComparison.Ordinal) && entryValue is not null)
        {
            value = entryValue;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadTag(ReadOnlySpan<byte> data, ref int offset, out int fieldNumber, out int wireType)
    {
        if (!TryReadVarint(data, ref offset, out var tag))
        {
            fieldNumber = 0;
            wireType = 0;
            return false;
        }

        fieldNumber = (int)(tag >> 3);
        wireType = (int)(tag & 0x07);

        return fieldNumber > 0;
    }

    private static bool TryReadLengthDelimited(ReadOnlySpan<byte> data, ref int offset, out ReadOnlySpan<byte> content)
    {
        if (!TryReadVarint(data, ref offset, out var length) || length > (ulong)(data.Length - offset))
        {
            content = default;
            return false;
        }

        content = data.Slice(offset, (int)length);
        offset += (int)length;

        return true;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int offset, out ulong value)
    {
        value = 0;
        var shift = 0;

        while (offset < data.Length && shift < 64)
        {
            var current = data[offset++];
            value |= (ulong)(current & 0x7F) << shift;

            if ((current & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        value = 0;
        return false;
    }

    private static bool SkipField(ReadOnlySpan<byte> data, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case 0:
                return TryReadVarint(data, ref offset, out _);

            case 1:
                if (data.Length - offset < 8)
                {
                    return false;
                }

                offset += 8;
                return true;

            case 2:
                return TryReadLengthDelimited(data, ref offset, out _);

            case 5:
                if (data.Length - offset < 4)
                {
                    return false;
                }

                offset += 4;
                return true;

            default:
                return false;
        }
    }
}