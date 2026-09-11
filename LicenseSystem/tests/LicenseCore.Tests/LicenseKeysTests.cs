using System.Security.Cryptography;
using LicenseCore;
using Xunit;

namespace LicenseCore.Tests;

public class LicenseKeysTests
{
    [Fact]
    public void CreateKeyPair_ProducesPemTexts()
    {
        var (privatePem, publicPem) = LicenseKeys.CreateKeyPair();
        Assert.Contains("PRIVATE KEY", privatePem);
        Assert.Contains("PUBLIC KEY", publicPem);
    }

    [Fact]
    public void PrivateAndPublicPem_RoundTripLoad()
    {
        var (privatePem, publicPem) = LicenseKeys.CreateKeyPair();

        using var priv = LicenseKeys.LoadPrivateKey(privatePem);
        using var pub = LicenseKeys.LoadPublicKey(publicPem);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sig = LicenseKeys.Sign(priv, data);
        Assert.True(LicenseKeys.Verify(pub, data, sig));
    }

    [Fact]
    public void Verify_RejectsTamperedData()
    {
        var (privatePem, publicPem) = LicenseKeys.CreateKeyPair();
        using var priv = LicenseKeys.LoadPrivateKey(privatePem);
        using var pub = LicenseKeys.LoadPublicKey(publicPem);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sig = LicenseKeys.Sign(priv, data);
        data[0] = 9;
        Assert.False(LicenseKeys.Verify(pub, data, sig));
    }

    [Fact]
    public void Verify_RejectsSignatureFromAnotherKey()
    {
        var (_, pubA) = LicenseKeys.CreateKeyPair();
        var (privB, _) = LicenseKeys.CreateKeyPair();
        using var priv = LicenseKeys.LoadPrivateKey(privB);
        using var pub = LicenseKeys.LoadPublicKey(pubA);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sig = LicenseKeys.Sign(priv, data);
        Assert.False(LicenseKeys.Verify(pub, data, sig));
    }

    [Fact]
    public void Load_RejectsInvalidPem()
    {
        Assert.Throws<ArgumentException>(() => LicenseKeys.LoadPublicKey("not-a-pem"));
    }
}
