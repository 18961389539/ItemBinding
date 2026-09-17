using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace LicenseCore;

/// <summary>
/// 硬件指纹采集：CPU 标识 + 系统盘卷序列号 + 首个物理网卡 MAC + 机器名。
/// 零外部依赖（WMI 换用注册表 + P/Invoke）。
/// <para>2026-09-16: 移除 <c>Environment.UserName</c>。原因：Windows 用户名属于**软件环境**而非硬件，
/// 换账户运行（如从管理员账户改为操作员账户）会让指纹整体变化，表现就是"激活成功后过一段时间
/// 又要求重新激活"。指纹只应包含与机器绑定的成分。</para>
/// <para>为不使已部署机器上按旧算法签发的激活码全部作废，旧算法以
/// <see cref="ComputeLegacyFingerprintBytes"/> 保留，由校验侧做兼容回退。</para>
/// </summary>
public static class HardwareFingerprint
{
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer, uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer, uint fileSystemNameSize);

    /// <summary>
    /// 采集硬件指纹并拼接为原始字符串（未哈希）。当前算法不含用户名。
    /// </summary>
    public static string CollectRawFingerprint() => CollectRawFingerprint(includeUserName: false);

    /// <summary>
    /// 采集硬件指纹原始字符串（未哈希）。
    /// </summary>
    /// <param name="includeUserName">
    /// 是否把 <see cref="Environment.UserName"/> 计入指纹。
    /// 仅用于兼容 2026-09-16 之前签发的激活码，新签发一律用 <c>false</c>。
    /// </param>
    public static string CollectRawFingerprint(bool includeUserName)
    {
        var parts = new List<string>
        {
            GetCpuProcessorId(),      // CPU 标识（注册表）
            GetSystemDriveSerial(),   // 系统盘卷序列号
            GetFirstPhysicalMac(),    // 首个物理网卡 MAC
            Environment.MachineName,  // 机器名
        };

        if (includeUserName)
        {
            // 旧算法遗留成分：换 Windows 账户即失效，仅用于兼容旧激活码
            parts.Add(Environment.UserName);
        }

        return string.Join("|", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// 生成机器码原始字节（SHA256 指纹前 12 字节 = 96 bit 熵，足够区分设备且输出可读）。
    /// 返回的字节作为机器码的内部表示，展示层用 <see cref="MachineCode"/> 编码。
    /// </summary>
    public static byte[] ComputeFingerprintBytes() =>
        Hash(CollectRawFingerprint(includeUserName: false));

    /// <summary>
    /// 2026-09-16 之前的旧算法指纹（含 Windows 用户名）。
    /// 仅用于校验时的兼容回退，不要用于签发新激活码（新机器码请用 <see cref="ComputeFingerprintBytes"/>）。
    /// </summary>
    public static byte[] ComputeLegacyFingerprintBytes() =>
        Hash(CollectRawFingerprint(includeUserName: true));

    /// <summary>
    /// 校验用的指纹候选序列：当前算法优先，旧算法兜底。
    /// 调用方应依次尝试，命中即视为匹配（保证升级后旧激活码仍可用）。
    /// </summary>
    public static IReadOnlyList<byte[]> ComputeFingerprintCandidates()
    {
        var current = ComputeFingerprintBytes();
        var legacy = ComputeLegacyFingerprintBytes();

        // 两者相同时（理论上不会，因为拼串不同）只返回一个，避免重复校验
        return current.AsSpan().SequenceEqual(legacy)
            ? new[] { current }
            : new[] { current, legacy };
    }

    private static byte[] Hash(string raw) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(raw))[..12];

    /// <summary>读取 CPU 标识（HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0 → ProcessorId）。</summary>
    private static string GetCpuProcessorId()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorId")?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>获取系统盘（安装 Windows 的分区）卷序列号。</summary>
    private static string GetSystemDriveSerial()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            if (!GetVolumeInformation(root, null, 0, out uint serial, out _, out _, null, 0))
                return string.Empty;
            // 格式化为 4 位十六进制段，与 dir 命令显示一致
            return $"{serial >> 16:X4}-{serial & 0xFFFF:X4}";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>获取首个物理网卡（非虚拟、非回环）的 MAC 地址。</summary>
    private static string GetFirstPhysicalMac()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;
                if (nic.Description?.Contains("Virtual", StringComparison.OrdinalIgnoreCase) == true)
                    continue;
                if (nic.Description?.Contains("Loopback", StringComparison.OrdinalIgnoreCase) == true)
                    continue;
                var mac = nic.GetPhysicalAddress().ToString();
                if (!string.IsNullOrEmpty(mac))
                    return mac;
            }
        }
        catch
        {
            // 忽略采集失败（该部分不参与指纹）
        }
        return string.Empty;
    }
}
