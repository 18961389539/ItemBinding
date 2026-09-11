using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Extensions
{
    /// <summary>
    /// Windows 进程电源管理辅助类。
    /// 通过调用 Windows kernel32 API 禁用操作系统的效率模式（EcoQoS）降频，
    /// 确保工业视觉检测进程获得持续的高性能 CPU 调度，避免因系统节流导致推理延迟增大。
    /// </summary>
    public static class PCPowerHelper
    {

        /// <summary>
        /// 进程电源节流状态结构体，对应 Windows PROCESS_POWER_THROTTLING_STATE
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            /// <summary>结构体版本号，必须为1</summary>
            public uint Version;
            /// <summary>控制掩码，指定要修改的节流策略</summary>
            public uint ControlMask;
            /// <summary>状态掩码，指定节流策略的开启/关闭状态</summary>
            public uint StateMask;
        }

        /// <summary>ProcessInformationClass 参数值：进程电源节流</summary>
        private const int ProcessPowerThrottling = 4;
        /// <summary>执行速度节流标志位</summary>
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        // L324: 结构体版本号提取为常量，避免硬编码
        private const uint ProcessPowerThrottlingVersion = 1;
        /// <summary>
        /// Windows API：设置进程信息（电源节流策略）
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr hProcess, int ProcessInformationClass, ref PROCESS_POWER_THROTTLING_STATE ProcessInformation, uint ProcessInformationSize);
        /// <summary>
        /// 禁止操作系统对本进程进行效率模式（EcoQoS）降频。
        /// 调用后 Windows 将不再对此进程施加执行速度节流，确保推理性能稳定。
        /// 应在应用启动时调用（见 App.OnStartup）。
        /// </summary>
        public static void DisablePowerThrottling()
        {
            var throttleState = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = ProcessPowerThrottlingVersion,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED, // 控制"执行速度"这一项
                StateMask = 0 // 设置为 0 (Off)，即"不进行节流/不启用EcoQoS"
            };

            // L253: Process 对象实现 IDisposable，需 using 确保释放
            using var process = Process.GetCurrentProcess();
            // M131: 检查 P/Invoke 返回值，失败时记录错误码
            bool success = SetProcessInformation(
                process.Handle,
                ProcessPowerThrottling,
                ref throttleState,
                (uint)Marshal.SizeOf(throttleState));

            if (!success)
            {
                int errorCode = Marshal.GetLastWin32Error();
                // L384a: 使用 Trace.WriteLine 替代 Debug.WriteLine，确保 Release 构建也能输出错误信息
                System.Diagnostics.Trace.WriteLine($"SetProcessInformation 失败，错误码: {errorCode}");
            }
        }
    }
}