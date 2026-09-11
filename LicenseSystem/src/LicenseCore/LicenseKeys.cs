using System.Security.Cryptography;

namespace LicenseCore;

/// <summary>
/// ECDSA P-256 密钥对管理：厂商持私钥签发激活码，客户端内置公钥离线验签。
/// 密钥以 PEM 文本保存（私钥可加密，公钥客户端内置）。
/// </summary>
public static class LicenseKeys
{
    /// <summary>生成新的 ECDSA P-256 密钥对，返回 (私钥PEM, 公钥PEM)。</summary>
    public static (string PrivateKeyPem, string PublicKeyPem) CreateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePem = ecdsa.ExportPkcs8PrivateKeyPem();
        var publicPem = ecdsa.ExportSubjectPublicKeyInfoPem();
        return (privatePem, publicPem);
    }

    /// <summary>从 PKCS#8 PEM 加载私钥（厂商侧使用）。</summary>
    public static ECDsa LoadPrivateKey(string privateKeyPem)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            ecdsa.ImportFromPem(privateKeyPem);
            return ecdsa;
        }
        catch
        {
            ecdsa.Dispose();
            throw new ArgumentException("私钥 PEM 无效", nameof(privateKeyPem));
        }
    }

    /// <summary>从 SubjectPublicKeyInfo PEM 加载公钥（客户端侧使用）。</summary>
    public static ECDsa LoadPublicKey(string publicKeyPem)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            ecdsa.ImportFromPem(publicKeyPem);
            return ecdsa;
        }
        catch
        {
            ecdsa.Dispose();
            throw new ArgumentException("公钥 PEM 无效", nameof(publicKeyPem));
        }
    }

    /// <summary>使用私钥对数据签名（SHA256，IEEE P1363 r‖s 拼接，P-256 恒为 64 字节）。</summary>
    public static byte[] Sign(ECDsa privateKey, ReadOnlySpan<byte> data) =>
        privateKey.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <summary>使用公钥验证签名。</summary>
    public static bool Verify(ECDsa publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature) =>
        publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
}
