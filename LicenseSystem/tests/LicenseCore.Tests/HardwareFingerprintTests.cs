using System;
using LicenseCore;
using Xunit;

namespace LicenseCore.Tests;

/// <summary>
/// 硬件指纹成分的回归守卫（2026-09-16）。
///
/// 回归背景：指纹原先包含 <c>Environment.UserName</c> —— 用户名属于软件环境而非硬件，
/// 换 Windows 账户运行会让指纹整体变化，现场表现就是"激活成功后过一段时间又要求重新激活"。
/// 现已移除，旧算法以 <see cref="HardwareFingerprint.ComputeLegacyFingerprintBytes"/> 保留
/// 供校验侧兼容回退（避免已部署机器的激活码全部作废）。
///
/// 若日后有人把 UserName 加回当前算法，<c>CurrentFingerprint_DiffersFromLegacy</c> 会失败。
/// </summary>
public class HardwareFingerprintTests
{
    [Fact]
    public void CurrentFingerprint_HasNoUserNameComponent()
    {
        var parts = HardwareFingerprint.CollectRawFingerprint(includeUserName: false).Split('|');
        Assert.DoesNotContain(Environment.UserName, parts);
    }

    [Fact]
    public void LegacyFingerprint_StillContainsUserName()
    {
        // 旧算法的兼容能力必须保留：否则老机器上已签发的激活码无法再被识别
        var legacy = HardwareFingerprint.CollectRawFingerprint(includeUserName: true);
        Assert.Contains(Environment.UserName, legacy);
    }

    [Fact]
    public void CurrentFingerprint_DiffersFromLegacy()
    {
        var current = HardwareFingerprint.CollectRawFingerprint(includeUserName: false);
        var legacy = HardwareFingerprint.CollectRawFingerprint(includeUserName: true);
        Assert.NotEqual(current, legacy);
    }

    [Fact]
    public void ComputeFingerprintBytes_IsTwelveBytesAndDeterministic()
    {
        var a = HardwareFingerprint.ComputeFingerprintBytes();
        var b = HardwareFingerprint.ComputeFingerprintBytes();

        Assert.Equal(12, a.Length); // MachineCode 以 12 字节为约定长度
        Assert.Equal(a, b);         // 同一台机器上必须稳定
    }

    [Fact]
    public void Candidates_ReturnsCurrentThenLegacy()
    {
        var candidates = HardwareFingerprint.ComputeFingerprintCandidates();

        Assert.Equal(2, candidates.Count);
        Assert.Equal(HardwareFingerprint.ComputeFingerprintBytes(), candidates[0]);
        Assert.Equal(HardwareFingerprint.ComputeLegacyFingerprintBytes(), candidates[1]);
    }
}
