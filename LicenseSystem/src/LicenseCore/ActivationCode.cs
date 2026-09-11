using System.Security.Cryptography;

namespace LicenseCore;

/// <summary>
/// 激活码：载荷（21B）+ ECDSA P-256 签名（64B）拼接后 Base32 分组编码。
/// 文本形式如：XXXXX-XXXXX-XXXXX-...（5 字符一组，约 28 组）。
/// 生成侧（厂商）：用私钥签发；校验侧（客户端）：用内置公钥离线验签。
/// </summary>
public static class ActivationCode
{
    private const int PayloadBytes = 21;
    private const int SignatureBytes = 64; // ECDSA P-256 签名（DER 编码固定 64 字节的 R+S）

    /// <summary>
    /// 生成激活码文本。
    /// </summary>
    /// <param name="privateKey">厂商私钥。</param>
    /// <param name="payload">授权载荷。</param>
    /// <returns>分组可读的激活码文本。</returns>
    public static string Generate(ECDsa privateKey, ActivationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(privateKey);

        var payloadBytes = payload.Serialize();
        var signature = LicenseKeys.Sign(privateKey, payloadBytes);

        var full = new byte[PayloadBytes + SignatureBytes];
        payloadBytes.CopyTo(full, 0);
        signature.CopyTo(full, PayloadBytes);

        return Base32.EncodeGrouped(full, 5);
    }

    /// <summary>
    /// 解析激活码文本（容忍分隔符/大小写），返回 (载荷, 签名)。格式非法返回 null。
    /// </summary>
    public static (ActivationPayload Payload, byte[] Signature)? TryParse(string activationCode)
    {
        if (string.IsNullOrWhiteSpace(activationCode)) return null;
        try
        {
            var full = Base32.Decode(activationCode);
            if (full.Length != PayloadBytes + SignatureBytes) return null;
            var payload = ActivationPayload.Deserialize(full.AsSpan(0, PayloadBytes));
            var signature = full.AsSpan(PayloadBytes, SignatureBytes).ToArray();
            return (payload, signature);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
