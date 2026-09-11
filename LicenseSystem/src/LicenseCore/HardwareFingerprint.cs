using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace LicenseCore;

/// <summary>
/// 硬件指纹采集：CPU 标识 + 系统盘卷序列号 + 首个物理网卡 MAC + 机器/用户名。
/// 零外部依赖（WMI 换用注册表 + P/Invoke）。
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

    /// <summary>采集硬件指纹并拼接为原始字符串（未哈希）。</summary>
    public static string CollectRawFingerprint()
    {
        var parts = new List<string>
        {
            GetCpuProcessorId(),      // CPU 标识（注册表）
            GetSystemDriveSerial(),   // 系统盘卷序列号
            GetFirstPhysicalMac(),    // 首个物理网卡 MAC
            Environment.MachineName,  // 机器名
            Environment.UserName,     // 当前用户名
        };

        return string.Join("|", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// 生成机器码原始字节（SHA256 指纹前 12 字节 = 96 bit 熵，足够区分设备且输出可读）。
    /// 返回的字节作为机器码的内部表示，展示层用 <see cref="MachineCode"/> 编码。
    /// </summary>
    public static byte[] ComputeFingerprintBytes()
    {
        var raw = CollectRawFingerprint();
        return SHA256.HashData(Encoding.UTF8.GetBytes(raw))[..12];
    }

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
