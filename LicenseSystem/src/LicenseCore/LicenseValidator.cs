using System.Security.Cryptography;

namespace LicenseCore;

/// <summary>校验结果状态。</summary>
public enum LicenseStatus
{
    /// <summary>激活码有效且授权可用。</summary>
    Valid,
    /// <summary>激活码格式非法。</summary>
    InvalidFormat,
    /// <summary>签名校验失败（激活码被篡改或非本厂商签发）。</summary>
    InvalidSignature,
    /// <summary>产品标识不匹配（激活码不是给本产品/本客户端的）。</summary>
    WrongProduct,
    /// <summary>机器不匹配（单机绑定码与当前机器指纹不符）。</summary>
    MachineMismatch,
    /// <summary>已过期。</summary>
    Expired,
}

/// <summary>校验结果详情。</summary>
public sealed record LicenseCheckResult(
    LicenseStatus Status,
    ActivationPayload? Payload = null,
    DateTime? ExpireAtLocal = null,
    int RemainingDays = 0,
    string Message = "");

/// <summary>
/// 客户端离线校验器：验签（内置公钥）→ 产品匹配 → 机器绑定 → 到期。
/// 集成方式：被保护软件启动时调用 <see cref="Validate"/>，非 Valid 即拒绝运行。
/// </summary>
public static class LicenseValidator
{
    /// <summary>
    /// 校验激活码。
    /// </summary>
    /// <param name="activationCode">用户输入的激活码文本。</param>
    /// <param name="machineFingerprintBytes">本机指纹字节（<see cref="HardwareFingerprint.ComputeFingerprintBytes"/>）。</param>
    /// <param name="publicKeyPem">内置公钥（客户端随程序分发）。</param>
    /// <param name="productId">本产品标识。</param>
    /// <param name="now">当前时间（默认 UTC 现在，便于测试注入）。</param>
    public static LicenseCheckResult Validate(
        string activationCode,
        byte[] machineFingerprintBytes,
        string publicKeyPem,
        ushort productId,
        DateTime? now = null)
    {
        if (machineFingerprintBytes.Length != 12)
            return new LicenseCheckResult(LicenseStatus.InvalidFormat, Message: "机器指纹长度无效");

        var parsed = ActivationCode.TryParse(activationCode);
        if (parsed is null)
            return new LicenseCheckResult(LicenseStatus.InvalidFormat, Message: "激活码格式非法");

        var (payload, signature) = parsed.Value;

        // 1) 验签（防篡改/防伪造）
        try
        {
            using var publicKey = LicenseKeys.LoadPublicKey(publicKeyPem);
            if (!LicenseKeys.Verify(publicKey, payload.Serialize(), signature))
                return new LicenseCheckResult(LicenseStatus.InvalidSignature, Message: "签名校验失败");
        }
        catch (ArgumentException ex)
        {
            return new LicenseCheckResult(LicenseStatus.InvalidSignature, Message: ex.Message);
        }

        // 2) 产品匹配
        if (payload.ProductId != productId)
            return new LicenseCheckResult(LicenseStatus.WrongProduct, Payload: payload,
                Message: $"产品标识不匹配（激活码属于产品 {payload.ProductId}，本程序为 {productId}）");

        // 3) 机器绑定（仅单机绑定码）
        if (payload.Type == ActivationPayload.TypeSingleMachine)
        {
            var localHash = MachineCode.ComputeHash(machineFingerprintBytes);
            if (!localHash.AsSpan().SequenceEqual(payload.MachineHash))
                return new LicenseCheckResult(LicenseStatus.MachineMismatch, Payload: payload,
                    Message: "激活码与本机不匹配（激活码绑定了其他机器）");
        }

        // 4) 到期
        var checkTime = (now ?? DateTime.UtcNow);
        if (payload.ExpireUnix != 0)
        {
            var expireUtc = DateTimeOffset.FromUnixTimeSeconds(payload.ExpireUnix).UtcDateTime;
            if (checkTime > expireUtc)
                return new LicenseCheckResult(LicenseStatus.Expired, Payload: payload,
                    ExpireAtLocal: expireUtc.ToLocalTime(),
                    Message: $"授权已过期（{expireUtc.ToLocalTime():yyyy-MM-dd}）");

            var remaining = (int)Math.Floor((expireUtc - checkTime).TotalDays);
            return new LicenseCheckResult(LicenseStatus.Valid, Payload: payload,
                ExpireAtLocal: expireUtc.ToLocalTime(),
                RemainingDays: remaining,
                Message: $"授权有效，剩余 {remaining} 天");
        }

        return new LicenseCheckResult(LicenseStatus.Valid, Payload: payload,
            Message: "授权有效（永久）");
    }
}
