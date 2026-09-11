using System;
using System.Runtime.InteropServices;
using MvCodeReaderSDKNet;

namespace HikScanner
{
    /// <summary>GigE 网络配置</summary>
    public partial class HikScanner
    {
        /// <summary>
        /// 强制设置 GigE 设备 IP 地址（完整流程，支持不可达设备）。
        /// 参照 SDK ForceIpDemo 的 9 步流程实现，无需事先连接设备。
        /// </summary>
        /// <param name="deviceInfo">枚举到的设备信息（含原始 RawDeviceInfo 结构体）</param>
        /// <param name="ip">目标 IP，如 "192.168.1.100"</param>
        /// <param name="subnetMask">子网掩码，如 "255.255.255.0"</param>
        /// <param name="gateway">默认网关，如 "192.168.1.1"</param>
        public static void ForceIpDevice(HikDeviceInfo deviceInfo, string ip, string subnetMask, string gateway)
        {
            // #5: 检查设备类型，非 GigE 设备不支持 ForceIp
            if (deviceInfo.TLayerType != MvCodeReader.MV_CODEREADER_GIGE_DEVICE)
                throw new ArgumentException($"ForceIp 仅支持 GigE 设备，当前设备类型: TLayerType={deviceInfo.TLayerType}", nameof(deviceInfo));

            // 从 HikDeviceInfo 中取出原始 SDK 结构体（值类型，后续可安全修改）
            var rawInfo = deviceInfo.RawDeviceInfo;
            uint nIp = IpStringToUint(ip);
            uint nMask = IpStringToUint(subnetMask);
            uint nGateway = IpStringToUint(gateway);

            // #9: tempDevice 不实现 IDisposable，用 try/finally 确保销毁句柄
            var tempDevice = new MvCodeReader();

            // 步骤 1：创建临时句柄
            int nRet = tempDevice.MV_CODEREADER_CreateHandle_NET(ref rawInfo);
            HikScannerException.Check(nRet, "ForceIp: 创建句柄");

            try
            {
                // 步骤 2：判断设备 IP 是否可达
                bool accessible = MvCodeReader.MV_CODEREADER_IsDeviceAccessible_NET(
                    ref rawInfo, MvCodeReader.MV_CODEREADER_ACCESS_Exclusive);

                if (accessible)
                {
                    // ─── 可达分支：先设静态 IP 配置 → 再 ForceIp ───
                    nRet = tempDevice.MV_CODEREADER_GIGE_SetIpConfig_NET(
                        MvCodeReader.MV_CODEREADER_IP_CFG_STATIC);
                    HikScannerException.Check(nRet, "ForceIp: 设置IP配置(可达)");

                    nRet = tempDevice.MV_CODEREADER_GIGE_ForceIp_NET(nIp, nMask, nGateway);
                    HikScannerException.Check(nRet, "ForceIp: 强制设IP");
                }
                else
                {
                    // ─── 不可达分支：先 ForceIp → 重建设备信息 → 再保存静态 IP ───
                    nRet = tempDevice.MV_CODEREADER_GIGE_ForceIp_NET(nIp, nMask, nGateway);
                    HikScannerException.Check(nRet, "ForceIp: 强制设IP(不可达)");

                    // 步骤 3：销毁旧句柄，准备用新 IP 重建
                    tempDevice.MV_CODEREADER_DestroyHandle_NET();

                    // 步骤 4：更新原生结构体中的 IP 字段
                    IntPtr pGigEInfo = Marshal.UnsafeAddrOfPinnedArrayElement(
                        rawInfo.SpecialInfo.stGigEInfo, 0);
                    var stGigEDev = (MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO)
                        Marshal.PtrToStructure(pGigEInfo, typeof(MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO));

                    stGigEDev.nCurrentIp = nIp;
                    stGigEDev.nCurrentSubNetMask = nMask;
                    stGigEDev.nDefultGateWay = nGateway;

                    // 步骤 5：将更新后的 GigE 信息写回 rawInfo 的字节数组
                    // #8: 使用 stackalloc 替代 AllocHGlobal，避免堆分配
                    int gigEInfoSize = Marshal.SizeOf(rawInfo.SpecialInfo);
                    unsafe
                    {
                        byte* pTemp = stackalloc byte[gigEInfoSize];
                        Marshal.StructureToPtr(stGigEDev, (IntPtr)pTemp, false);
                        rawInfo.SpecialInfo.stGigEInfo = new byte[gigEInfoSize];
                        Marshal.Copy((IntPtr)pTemp, rawInfo.SpecialInfo.stGigEInfo, 0, gigEInfoSize);
                    }

                    // 步骤 6：用新 IP 信息重建句柄
                    nRet = tempDevice.MV_CODEREADER_CreateHandle_NET(ref rawInfo);
                    HikScannerException.Check(nRet, "ForceIp: 用新IP重建句柄");

                    // 步骤 7：持久化为静态 IP 配置
                    nRet = tempDevice.MV_CODEREADER_GIGE_SetIpConfig_NET(
                        MvCodeReader.MV_CODEREADER_IP_CFG_STATIC);
                    HikScannerException.Check(nRet, "ForceIp: 设置静态IP配置(不可达)");
                }
            }
            finally
            {
                // 清理临时句柄
                tempDevice.MV_CODEREADER_DestroyHandle_NET();
            }
        }

        /// <summary>强制设置已连接设备的 IP（简化版，要求设备已连接）</summary>
        public void ForceIp(string ip, string subnetMask, string gateway)
        {
            EnsureConnected();
            uint nIp = IpStringToUint(ip), nMask = IpStringToUint(subnetMask), nGateway = IpStringToUint(gateway);
            HikScannerException.Check(_device.MV_CODEREADER_GIGE_ForceIp_NET(nIp, nMask, nGateway), "ForceIp");
        }

        public void SetIpConfig(HikIpConfigType configType)
        {
            EnsureConnected();
            HikScannerException.Check(_device.MV_CODEREADER_GIGE_SetIpConfig_NET((uint)configType), "设置IP配置");
        }

        public int GetOptimalPacketSize()
        {
            EnsureConnected();
            return _device.MV_CODEREADER_GetOptimalPacketSize_NET();
        }

        public void SetGvcpTimeout(uint milliseconds)
        {
            EnsureConnected();
            // #4: 检查返回值，与其他 GigE 方法一致
            HikScannerException.Check(_device.MV_CODEREADER_GIGE_SetGvcpTimeout_NET(milliseconds), "设置GVCP超时");
        }

        public uint GetGvcpTimeout()
        {
            EnsureConnected();
            // #14: 使用 stackalloc 替代 AllocHGlobal，避免堆分配（AllowUnsafeBlocks 已开启）
            unsafe
            {
                uint* pMillisec = stackalloc uint[1];
                HikScannerException.Check(_device.MV_CODEREADER_GIGE_GetGvcpTimeout_NET((IntPtr)pMillisec), "获取GVCP超时");
                return *pMillisec;
            }
        }
    }
}
