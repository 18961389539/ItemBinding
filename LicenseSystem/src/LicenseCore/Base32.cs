using System.Text;

namespace LicenseCore;

/// <summary>
/// Crockford Base32 编码（RFC 4648 变体，无填充）。
/// 字符表剔除易混淆的 I/L/O/U，避免人工抄写错误。
/// 用于机器码与激活码的可读文本表示。
/// </summary>
public static class Base32
{
    // Crockford 字符表：0-9 A-Z，去掉 I L O U
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int BitsPerChar = 5;

    /// <summary>将字节编码为 Base32 字符串（无填充、无分隔符）。</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return string.Empty;

        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bitsInBuffer = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsInBuffer += 8;
            while (bitsInBuffer >= BitsPerChar)
            {
                bitsInBuffer -= BitsPerChar;
                int index = (buffer >> bitsInBuffer) & 0x1F;
                sb.Append(Alphabet[index]);
            }
        }
        if (bitsInBuffer > 0)
        {
            int index = (buffer << (BitsPerChar - bitsInBuffer)) & 0x1F;
            sb.Append(Alphabet[index]);
        }
        return sb.ToString();
    }

    /// <summary>将字节编码为 Base32 并按 5 字符一组用 '-' 分隔（可读形式，如 XXXXX-XXXXX-XXXXX）。</summary>
    public static string EncodeGrouped(ReadOnlySpan<byte> data, int groupSize = 5)
    {
        var raw = Encode(data);
        if (groupSize <= 0 || raw.Length <= groupSize) return raw;

        var sb = new StringBuilder(raw.Length + raw.Length / groupSize);
        for (int i = 0; i < raw.Length; i++)
        {
            if (i > 0 && i % groupSize == 0) sb.Append('-');
            sb.Append(raw[i]);
        }
        return sb.ToString();
    }

    /// <summary>将 Base32 字符串解码为字节。容忍分隔符（'-'、空格）与大小写。</summary>
    /// <exception cref="FormatException">包含非法字符时抛出。</exception>
    public static byte[] Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded)) return Array.Empty<byte>();

        var sb = new StringBuilder(encoded.Length);
        foreach (char c in encoded)
        {
            if (c == '-' || c == ' ' || c == '\t') continue;
            sb.Append(c);
        }
        string clean = sb.ToString().ToUpperInvariant();

        int bits = 0;
        int buffer = 0;
        using var ms = new MemoryStream((clean.Length * BitsPerChar + 7) / 8);

        foreach (char c in clean)
        {
            int value = Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException($"Base32 字符串包含非法字符 '{c}'");

            buffer = (buffer << BitsPerChar) | value;
            bits += BitsPerChar;
            if (bits >= 8)
            {
                bits -= 8;
                ms.WriteByte((byte)((buffer >> bits) & 0xFF));
            }
        }
        return ms.ToArray();
    }

    /// <summary>去除分隔符，用于解析用户输入的机器码/激活码。</summary>
    public static string StripSeparators(string input) =>
        input is null ? string.Empty
            : new string(input.Where(c => c != '-' && c != ' ' && c != '\t').ToArray()).ToUpperInvariant();
}
