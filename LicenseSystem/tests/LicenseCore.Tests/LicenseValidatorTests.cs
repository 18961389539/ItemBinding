using System.Security.Cryptography;
using LicenseCore;
using Xunit;

namespace LicenseCore.Tests;

public class LicenseValidatorTests
{
    private const ushort ProductId = 0x0100;

    private static readonly (string Priv, string Pub) Keys = LicenseKeys.CreateKeyPair();
    private static readonly byte[] MachineFp = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    private static readonly uint NowUnix = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string Generate(ActivationPayload payload) =>
        ActivationCode.Generate(LicenseKeys.LoadPrivateKey(Keys.Priv), payload);

    [Fact]
    public void Validate_UniversalPermanentCode_IsValid()
    {
        var code = Generate(ActivationPayload.Create(
            ActivationPayload.TypeUniversal, ProductId,
            new byte[8], // 通用码机器哈希全 0
            expireUnix: 0,
            maxMachines: 1));

        var result = LicenseValidator.Validate(code, MachineFp, Keys.Pub, ProductId);
        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Contains("永久", result.Message);
    }

    [Fact]
    public void Validate_SingleMachineCode_BindsToMachine()
    {
        var code = Generate(ActivationPayload.Create(
            ActivationPayload.TypeSingleMachine, ProductId,
            MachineCode.ComputeHash(MachineFp),
            expireUnix: 0,
            maxMachines: 1));

        Assert.Equal(LicenseStatus.Valid,
            LicenseValidator.Validate(code, MachineFp, Keys.Pub, ProductId).Status);

        // 换一台机器 → 绑定不匹配
        var otherFp = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 };
        Assert.Equal(LicenseStatus.MachineMismatch,
            LicenseValidator.Validate(code, otherFp, Keys.Pub, ProductId).Status);
    }

    [Fact]
    public void Validate_ExpiredCode_ReturnsExpired()
    {
        var code = Generate(ActivationPayload.Create(
            ActivationPayload.TypeUniversal, ProductId,
            new byte[8], expireUnix: NowUnix - 100, maxMachines: 1));

        var result = LicenseValidator.Validate(code, MachineFp, Keys.Pub, ProductId);
        Assert.Equal(LicenseStatus.Expired, result.Status);
    }

    [Fact]
    public void Validate_FutureCode_ReportsRemainingDays()
    {
        var code = Generate(ActivationPayload.Create(
            ActivationPayload.TypeUniversal, ProductId,
            new byte[8], expireUnix: NowUnix + 30 * 86400, maxMachines: 1));

        var result = LicenseValidator.Validate(code, MachineFp, Keys.Pub, ProductId);
        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.InRange(result.RemainingDays, 29, 31);
    }

    [Fact]
    public void Validate_WrongProduct_Rejected()
    {
        var code = Generate(ActivationPayload.Create(
            ActivationPayload.TypeUniversal, (ushort)(ProductId + 1),
            new byte[8], expireUnix: 0, maxMachines: 1));

        Assert.Equal(LicenseStatus.WrongProduct,
            LicenseValidator.Validate(code, MachineFp, Keys.Pub, ProductId).Status);
    }

    [Fact]
    public void Validate_TamperedCode_SignatureFails()
    {
        var code = Generate(ActivationPayload.Create(
            ActivationPayload.TypeUniversal, ProductId,
            new byte[8], expireUnix: 0, maxMachines: 1));

        // 篡改一个字符（把最后一个字符改成另一个合法字符）
        var chars = code.ToCharArray();
        chars[^1] = chars[^1] == 'A' ? 'B' : 'A';
        var tampered = new string(chars);

        Assert.Equal(LicenseStatus.InvalidSignature,
            LicenseValidator.Validate(tampered, MachineFp, Keys.Pub, ProductId).Status);
    }

    [Fact]
    public void Validate_InvalidFormat_Rejected()
    {
        Assert.Equal(LicenseStatus.InvalidFormat,
            LicenseValidator.Validate("THIS-IS-NOT-A-LICENSE", MachineFp, Keys.Pub, ProductId).Status);
    }

    [Fact]
    public void Validate_CodeSignedByOtherVendor_Rejected()
    {
        var otherKeys = LicenseKeys.CreateKeyPair();
        using var otherPriv = LicenseKeys.LoadPrivateKey(otherKeys.PrivateKeyPem);
        var code = ActivationCode.Generate(otherPriv, ActivationPayload.Create(
            ActivationPayload.TypeUniversal, ProductId,
            new byte[8], expireUnix: 0, maxMachines: 1));

        Assert.Equal(LicenseStatus.InvalidSignature,
            LicenseValidator.Validate(code, MachineFp, Keys.Pub, ProductId).Status);
    }

    [Fact]
    public void Validate_ProductIdZero_IsValidCode()
    {
        var code = Generate(ActivationPayload.Create(
            ActivationPayload.TypeUniversal, 0,
            new byte[8], expireUnix: 0, maxMachines: 0));

        Assert.Equal(LicenseStatus.Valid,
            LicenseValidator.Validate(code, MachineFp, Keys.Pub, 0).Status);
    }
}
