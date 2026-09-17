using System.IO;
using LicenseCore;
using MainAPP.Models;

namespace MainAPP.Services;

/// <summary>
/// 离线授权服务（LicenseSystem 集成）：启动时校验激活码，未激活/过期/换机则要求激活。
/// 激活码保存在统一数据根下（见 <see cref="DataPaths.LicenseFile"/>，旧 %LOCALAPPDATA% 位置仅作读取回退），
/// 公钥编译内置（防替换）。
/// </summary>
public static class LicenseService
{
    /// <summary>产品标识（与 LicenseManager/LicenseTool 生成激活码时的 --product 对应）。</summary>
    public const ushort ProductId = 1;

    /// <summary>
    /// 2026-09-16: 授权门禁的调试旁路环境变量。
    /// 置 <c>1</c>（或 true/yes/on）强制跳过门禁；置 <c>0</c>（或 false/no/off）强制启用门禁
    /// （即使配置文件里写了 false）。未设置时按配置项 <c>Security.LicenseRequired</c> 决定。
    /// </summary>
    public const string BypassEnvironmentVariable = "ITEMBINDING_LICENSE_BYPASS";

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
    /// 2026-09-16: 授权门禁是否启用——**运行期解析**，取代原先 App.xaml.cs 里的编译期常量
    /// <c>ActivationRequired</c>。原常量形态决定"现场临时停用"实为永久停用：恢复门禁必须改源码重编译，
    /// 因此不再保留任何编译期开关。
    /// <para>解析优先级：</para>
    /// <list type="number">
    ///   <item>环境变量 <see cref="BypassEnvironmentVariable"/>：显式 true 值 ⇒ 旁路；显式 false 值 ⇒ 强制启用（覆盖配置）。</item>
    ///   <item>配置项 <c>Security.LicenseRequired</c>（settings.json，默认 true）。</item>
    ///   <item>配置不可读取时按最严处理：启用门禁。</item>
    /// </list>
    /// </summary>
    public static bool IsGateEnabled
    {
        get
        {
            try
            {
                var raw = Environment.GetEnvironmentVariable(BypassEnvironmentVariable)?.Trim();
                if (!string.IsNullOrEmpty(raw))
                {
                    if (IsTruthy(raw))
                    {
                        return false;
                    }
                    if (IsFalsy(raw))
                    {
                        return true;
                    }
                    // 其它取值（如误填的路径）不改变判定，按配置处理
                }
            }
            catch
            {
                // 读环境变量失败不应影响启动，继续按配置判定
            }

            try
            {
                return Settings.Instance.LicenseRequired;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>门禁判定依据的可读说明，写入启动日志便于现场自查。</summary>
    public static string GateDecisionNote
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable(BypassEnvironmentVariable)?.Trim();
            if (!string.IsNullOrEmpty(raw) && (IsTruthy(raw) || IsFalsy(raw)))
            {
                return $"由环境变量 {BypassEnvironmentVariable}={raw} 决定";
            }

            try
            {
                return $"由设置项 Security.LicenseRequired={Settings.Instance.LicenseRequired} 决定"
                     + $"（配置文件 {DataPaths.SettingsFile}）";
            }
            catch
            {
                return "设置不可读取，按默认启用门禁处理";
            }
        }
    }

    private static bool IsTruthy(string value) =>
        value is "1" or "true" or "yes" or "on" ||
        value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("True", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalsy(string value) =>
        value is "0" or "false" or "no" or "off" ||
        value.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("False", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// 校验激活码（不持久化，供激活窗口实时反馈）。
    /// <para>2026-09-16: 指纹候选依次尝试——当前算法（不含用户名）优先，旧算法（含用户名）兜底，
    /// 避免本次指纹成分调整把已部署机器上的激活码全部作废。仅在"机器不匹配"时才回退：
    /// 签名失败 / 产品不符等其他结论与指纹无关，回退没有意义。</para>
    /// </summary>
    public static LicenseCheckResult ValidateCode(string code)
    {
        var candidates = HardwareFingerprint.ComputeFingerprintCandidates();
        var primary = LicenseValidator.Validate(code, candidates[0], EmbeddedPublicKeyPem, ProductId);
        if (primary.Status != LicenseStatus.MachineMismatch || candidates.Count < 2)
        {
            return primary;
        }

        var legacy = LicenseValidator.Validate(code, candidates[1], EmbeddedPublicKeyPem, ProductId);
        if (legacy.Status is LicenseStatus.Valid or LicenseStatus.Expired)
        {
            LogService.Instance.Warning(
                "[LIC] 激活码匹配的是旧版指纹（含 Windows 用户名）——已兼容放行。" +
                $"建议用新机器码重新签发激活码以彻底规避换账户失效：{MachineCodeText}");
            return legacy;
        }

        return primary;
    }

    private static bool Evaluate(string code)
    {
        LastResult = ValidateCode(code);
        IsActivated = LastResult.Status == LicenseStatus.Valid;
        return IsActivated;
    }
}
