using System.Security.Cryptography;
using System.Text;

namespace LicenseCore;

/// <summary>
/// 机器码：硬件指纹的可读文本表示（Crockford Base32 分组，如 XXXXX-XXXXX-XXXXX）。
/// 客户端生成、厂商侧输入激活码生成工具，二者约定一致即可。
/// </summary>
public static class MachineCode
{
    private const int RawBytes = 12; // 96 bit，与 HardwareFingerprint.ComputeFingerprintBytes 一致

    /// <summary>采集本机硬件指纹并生成机器码（分组可读文本）。</summary>
    public static string Create() =>
        Encode(HardwareFingerprint.ComputeFingerprintBytes());

    /// <summary>由指纹字节编码机器码。</summary>
    public static string Encode(byte[] fingerprintBytes)
    {
        if (fingerprintBytes is null) throw new ArgumentNullException(nameof(fingerprintBytes));
        return Base32.EncodeGrouped(fingerprintBytes, 5);
    }

    /// <summary>解析机器码文本（容忍分隔符），返回指纹字节。格式非法返回 null。</summary>
    public static byte[]? TryParse(string machineCode)
    {
        if (string.IsNullOrWhiteSpace(machineCode)) return null;
        try
        {
            var bytes = Base32.Decode(machineCode);
            if (bytes.Length != RawBytes) return null;
            return bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>比较两个机器码是否等价（忽略分隔符与大小写）。</summary>
    public static bool EqualsText(string a, string b) =>
        string.Equals(Base32.StripSeparators(a), Base32.StripSeparators(b), StringComparison.Ordinal);

    /// <summary>计算机器码哈希（激活码内嵌，用于离线校验绑定）。</summary>
    public static byte[] ComputeHash(byte[] fingerprintBytes) =>
        SHA256.HashData(fingerprintBytes)[..8];
}
