using System.IO;
using LicenseCore;

namespace MainAPP.Services;

/// <summary>
/// 离线授权服务（LicenseSystem 集成）：启动时校验激活码，未激活/过期/换机则要求激活。
/// 激活码保存在 %LOCALAPPDATA%\ItemBinding\license.txt，公钥编译内置（防替换）。
/// </summary>
public static class LicenseService
{
    /// <summary>产品标识（与 LicenseManager/LicenseTool 生成激活码时的 --product 对应）。</summary>
    public const ushort ProductId = 1;

    // REVIEW(2026-08-06): 厂商公钥（LicenseSystem 密钥对），编译内置防止被替换。
    // 对应私钥由厂商保管（D:\ItemBinding\LicenseSystem\keys\license_private.pem），勿提交仓库。
    private const string EmbeddedPublicKeyPem =
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEpWH809o3oE4wgNYJJcDfXqmtPQbi
        6h1YXvPOUzs4LFRYvCKSa7juaUzb83hwI+CU/lobrRO/Qea5KuXSFtS6SA==
        -----END PUBLIC KEY-----
        """;

    // 2026-09-15: 授权文件从 %LOCALAPPDATA%（按用户隔离）迁到统一数据根（按机器）。
    // 原先"业务数据跟机器走、授权文件跟人走"的作用域错配，正是现场
    // "换 Windows 账户运行就要求重新激活"（授权文件与指纹双重失配）的根因。
    // 旧位置仅作为读取回退保留，保证老机器上已激活的文件仍能被识别；写入只写新位置。
    private static string LicenseFilePath => DataPaths.LicenseFile;

    /// <summary>最近一次校验结果（供 UI 展示授权状态）。</summary>
    public static LicenseCheckResult? LastResult { get; private set; }

    /// <summary>本机机器码（分组可读文本，激活窗口展示用）。</summary>
    public static string MachineCodeText => MachineCode.Create();

    /// <summary>是否已激活且当前有效（缓存启动时结果，避免每帧采集硬件指纹）。</summary>
    public static bool IsActivated { get; private set; }

    /// <summary>
    /// 读取已保存的激活码。优先新位置（数据根），为空时回退旧位置（%LOCALAPPDATA%），
    /// 兼容升级前已激活的机器。
    /// </summary>
    private static string? ReadStoredCode()
    {
        if (File.Exists(LicenseFilePath))
        {
            var current = File.ReadAllText(LicenseFilePath).Trim();
            if (!string.IsNullOrEmpty(current))
            {
                return current;
            }
        }

        // 兼容回退：旧版按用户存放的授权文件
        var legacyPath = DataPaths.LegacyLicenseFile;
        if (File.Exists(legacyPath))
        {
            var legacy = File.ReadAllText(legacyPath).Trim();
            if (!string.IsNullOrEmpty(legacy))
            {
                LogService.Instance.Info($"[LIC] 在旧位置发现授权文件，已沿用：{legacyPath}（下次激活将写入数据根 {LicenseFilePath}）");
                return legacy;
            }
        }

        return null;
    }

    /// <summary>程序启动时调用：加载已保存激活码并校验。有效则记录激活状态。</summary>
    public static bool ValidateAtStartup()
    {
        string? code;
        try
        {
            code = ReadStoredCode();
        }
        catch
        {
            code = null;
        }

        if (string.IsNullOrEmpty(code))
        {
            LastResult = new LicenseCheckResult(LicenseStatus.InvalidFormat, Message: "尚未激活");
            IsActivated = false;
            return false;
        }

        return Evaluate(code);
    }

    /// <summary>用户输入激活码并激活。成功返回 true 并持久化。</summary>
    public static bool TryActivate(string activationCode, out string message)
    {
        var code = activationCode?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            message = "请输入激活码";
            return false;
        }

        var result = ValidateCode(code);
        if (result.Status == LicenseStatus.Valid)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LicenseFilePath)!);
                File.WriteAllText(LicenseFilePath, code);
            }
            catch (Exception ex)
            {
                message = $"保存激活码失败: {ex.Message}";
                return false;
            }
            LastResult = result;
            IsActivated = true;
            message = result.Message;
            return true;
        }

        message = result.Message;
        return false;
    }

    /// <summary>校验激活码（不持久化，供激活窗口实时反馈）。</summary>
    public static LicenseCheckResult ValidateCode(string code) =>
        LicenseValidator.Validate(code, HardwareFingerprint.ComputeFingerprintBytes(), EmbeddedPublicKeyPem, ProductId);

    private static bool Evaluate(string code)
    {
        LastResult = ValidateCode(code);
        IsActivated = LastResult.Status == LicenseStatus.Valid;
        return IsActivated;
    }
}
