using System;
using System.Threading.Tasks;
using MvCodeReaderSDKNet;

namespace HikScanner
{
    /// <summary>
    /// HikScanner 兼容性扩展：为旧版 HikCodeReader 消费方提供等价 API。
    /// </summary>
    public partial class HikScanner
    {
        /// <summary>获取浮点型参数的范围（最小值和最大值）</summary>
        public (float Min, float Max) GetFloatRange(string key)
        {
            EnsureConnected();
            var p = new MvCodeReader.MV_CODEREADER_FLOATVALUE();
            HikScannerException.Check(_device.MV_CODEREADER_GetFloatValue_NET(key, ref p), $"获取浮点参数范围 {key}");
            return (p.fMin, p.fMax);
        }

        /// <summary>获取整型参数的范围（最小值和最大值）</summary>
        public (long Min, long Max) GetIntRange(string key)
        {
            EnsureConnected();
            var p = new MvCodeReader.MV_CODEREADER_INTVALUE_EX();
            HikScannerException.Check(_device.MV_CODEREADER_GetIntValue_NET(key, ref p), $"获取整型参数范围 {key}");
            return (p.nMin, p.nMax);
        }

        /// <summary>
        /// 使用上次连接的设备信息重连设备。需先成功调用 Connect(HikDeviceInfo) 一次。
        /// </summary>
        public void Connect()
        {
            ThrowIfDisposed();
            if (_currentDeviceInfo == null)
                throw new InvalidOperationException("没有可用的设备信息，请先调用 Connect(HikDeviceInfo)");
            Connect(_currentDeviceInfo);
        }

        /// <summary>切换到硬触发模式（Line0）。
        /// 如果当前已经是硬触发模式，直接返回，避免不必要的枚举节点访问触发 0x80020106。</summary>
        public void SwitchToHardwareTrigger()
        {
            if (IsGrabbing) StopGrabbing();
            if (TryGetTriggerConfiguration(out var mode, out var source) &&
                mode == HikTriggerMode.Trigger && source == HikTriggerSource.Line0)
                return;
            SetTriggerMode(HikTriggerMode.Trigger);
            SetTriggerSource(HikTriggerSource.Line0);
        }

        /// <summary>切换到软触发模式。
        /// 如果当前已经是软触发模式，直接返回，避免不必要的枚举节点访问触发 0x80020106。</summary>
        public void SwitchToSoftwareTrigger()
        {
            if (IsGrabbing) StopGrabbing();
            if (TryGetTriggerConfiguration(out var mode, out var source) &&
                mode == HikTriggerMode.Trigger && source == HikTriggerSource.Software)
                return;
            SetTriggerMode(HikTriggerMode.Trigger);
            SetTriggerSource(HikTriggerSource.Software);
        }

        /// <summary>切换到连续采集模式。
        /// 如果当前已经是连续模式，直接返回，避免不必要的枚举节点访问触发 0x80020106。</summary>
        public void SwitchToContinuousMode()
        {
            if (IsGrabbing) StopGrabbing();
            if (TryGetTriggerConfiguration(out var mode, out _) &&
                mode == HikTriggerMode.Continuous)
                return;
            SetTriggerMode(HikTriggerMode.Continuous);
        }

        /// <summary>
        /// 尝试获取当前触发模式和触发源。读取失败时返回 false，不抛出异常。
        /// </summary>
        private bool TryGetTriggerConfiguration(out HikTriggerMode mode, out HikTriggerSource source)
        {
            try
            {
                EnsureConnected();
                mode = (HikTriggerMode)GetEnumValue("TriggerMode").CurrentValue;
                source = (HikTriggerSource)GetEnumValue("TriggerSource").CurrentValue;
                return true;
            }
            catch
            {
                mode = default;
                source = default;
                return false;
            }
        }

        /// <summary>执行软触发命令（确保已开始采集）</summary>
        public void ExecuteSoftwareTrigger()
        {
            if (!IsGrabbing) StartGrabbing();
            TriggerSoftware();
        }

        /// <summary>异步获取一帧图像。内部调用 StartGrabbing + GrabOneFrame。</summary>
        /// <param name="timeoutMs">超时毫秒数</param>
        // REVIEW-FIX: 默认超时从 3_600_000（1 小时，疑为笔误）改为 30 秒。
        // 原默认值会让线程池线程在无触发帧时长时间阻塞；需要长等待的调用方应显式传参
        // （生产主链路均已显式传参或由 WaitAsync 二次限时，不受影响）。
        public async Task<HikGrabResult> GetImageAsync(uint timeoutMs = 30_000)
        {
            if (!IsGrabbing) StartGrabbing();
            return await Task.Run(() => GrabOneFrame((int)timeoutMs)).ConfigureAwait(false);
        }

        /// <summary>获取曝光时间（兼容旧版 GetExposureTime 方法）</summary>
        public float GetExposureTime() => GetFloatParam("ExposureTime");

        /// <summary>设置曝光时间（兼容旧版 SetExposureTime 方法）。
        /// 直接设置 ExposureTime 浮点值，不修改 ExposureAuto 枚举，
        /// 以避免设备处于采集状态时修改枚举节点触发 0x80020106 错误。</summary>
        public void SetExposureTime(float value) => SetFloatParam("ExposureTime", value);

        /// <summary>获取增益（兼容旧版 GetGain 方法）</summary>
        public float GetGain() => GetFloatParam("Gain");

        /// <summary>设置增益（兼容旧版 SetGain 方法）。
        /// 直接设置 Gain 浮点值，不修改 GainAuto 枚举，
        /// 以避免设备处于采集状态时修改枚举节点触发 0x80020106 错误。</summary>
        public void SetGain(float value) => SetFloatParam("Gain", value);

        /// <summary>获取当前触发模式（兼容旧版 GetCurrentTriggerMode 方法）</summary>
        public HikTriggerMode GetCurrentTriggerMode()
        {
            EnsureConnected();
            return (HikTriggerMode)GetEnumValue("TriggerMode").CurrentValue;
        }

        /// <summary>获取触发模式是否为开启（兼容旧版 GetTriggerMode 方法，返回 bool）</summary>
        /// <returns>true 表示 Trigger 模式（On），false 表示 Continuous 模式（Off）</returns>
        public bool GetTriggerMode()
        {
            return GetCurrentTriggerMode() == HikTriggerMode.Trigger;
        }

        /// <summary>获取触发源是否为软触发（兼容旧版 GetTriggerSource 方法，返回 bool）</summary>
        /// <returns>true 表示 Software 触发源，false 表示其他（硬件）触发源</returns>
        public bool GetTriggerSource()
        {
            EnsureConnected();
            return (HikTriggerSource)GetEnumValue("TriggerSource").CurrentValue == HikTriggerSource.Software;
        }

        /// <summary>设备是否已打开（兼容旧版 IsOpen 属性，等价于 IsConnected）</summary>
        public bool IsOpen => IsConnected;

        /// <summary>关闭设备（兼容旧版 Close 方法，等价于 Disconnect）</summary>
        public void Close() => Disconnect();
    }
}
