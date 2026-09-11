using MvCodeReaderSDKNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static MvCodeReaderSDKNet.MvCodeReader;

namespace HikCodeReader
{
    /// <summary>
    /// 表示一个MvCodeReader设备，封装设备的完整生命周期管理
    /// </summary>
    public class MvCodeReaderDevice : IDisposable
    {
        #region Statics
        /// <summary>
        /// 设置强制IP参数
        /// </summary>
        /// <param name="serialNumber">设备序列号</param>
        /// <param name="ip">新IP地址</param>
        /// <param name="subnetMask">子网掩码</param>
        /// <param name="gateway">网关</param>
        /// <param name="timeout">超时时间</param>
        public static void ForceIp(string serialNumber, string ip, string subnetMask, string gateway, uint timeout = 2000)
        {
            uint nIp = ConvertIpToUint(ip);
            uint nSubNetMask = ConvertIpToUint(subnetMask);
            uint nDefaultGateWay = ConvertIpToUint(gateway);

            var tempDevice = new MvCodeReaderSDKNet.MvCodeReader();
            try
            {
                // REVIEW-FIX: 校验 CreateHandleBySerialNumber 返回值，避免对无效句柄执行 ForceIp
                int ret = tempDevice.MV_CODEREADER_CreateHandleBySerialNumber_NET(serialNumber);
                if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                {
                    throw new MvCodeReaderException($"创建设备句柄失败，错误码: 0x{ret:X8}", ret);
                }

                ret = tempDevice.MV_CODEREADER_GIGE_ForceIp_NET(nIp, nSubNetMask, nDefaultGateWay);
                if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                {
                    throw new MvCodeReaderException($"强制设置IP失败，错误码: 0x{ret:X8}", ret);
                }
            }
            finally
            {
                // REVIEW-FIX: 确保临时句柄被销毁，防止句柄泄漏
                tempDevice.MV_CODEREADER_DestroyHandle_NET();
            }
        }

        /// <summary>
        /// 转换IP地址为uint
        /// </summary>
        /// <param name="ip">IP地址字符串</param>
        /// <returns>uint形式的IP地址</returns>
        private static uint ConvertIpToUint(string ip)
        {
            var parts = ip.Split('.');
            if (parts.Length != 4)
                throw new ArgumentException("Invalid IP address format", nameof(ip));

            byte[] bytes = new byte[4];
            for (int i = 0; i < 4; i++)
                bytes[i] = byte.Parse(parts[i]);

            // 手动计算uint值，确保正确的字节顺序（big-endian）
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        }

        /// <summary>
        /// 枚举所有可用的扫码设备
        /// </summary>
        /// <returns>设备信息列表</returns>
        public static List<DeviceInfo> EnumDevices()
        {
            var deviceList = new MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST();
            int ret = MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_EnumDevices_NET(ref deviceList, MvCodeReader.MV_CODEREADER_GIGE_DEVICE);

            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
            {
                throw new MvCodeReaderException($"枚举设备失败，错误码: 0x{ret:X8}", ret);
            }

            var devices = new List<DeviceInfo>();
            for (int i = 0; i < deviceList.nDeviceNum; i++)
            {
                var deviceInfoPtr = deviceList.pDeviceInfo[i];
                var deviceInfo = (MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_DEVICE_INFO)Marshal.PtrToStructure(deviceInfoPtr, typeof(MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_DEVICE_INFO));

                switch (deviceInfo.nTLayerType)
                {
                    case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_GIGE_DEVICE:
                        {
                            var gigeInfo = Utilities.ByteToStruct<MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_GIGE_DEVICE_INFO>(deviceInfo.SpecialInfo.stGigEInfo);
                            string ipAddress = string.Empty;
                            string subnetMask = string.Empty;
                            string gateway = string.Empty;
                            string macAddress = string.Empty;

                            ulong macValue = ((ulong)deviceInfo.nMacAddrHigh << 32) | deviceInfo.nMacAddrLow;
                            macAddress =
                                $"{(macValue >> 40) & 0xff:X2}-{(macValue >> 32) & 0xff:X2}-{(macValue >> 24) & 0xff:X2}-" +
                                $"{(macValue >> 16) & 0xff:X2}-{(macValue >> 8) & 0xff:X2}-{macValue & 0xff:X2}";

                            uint nIp1 = ((gigeInfo.nCurrentIp & 0xff000000) >> 24);
                            uint nIp2 = ((gigeInfo.nCurrentIp & 0x00ff0000) >> 16);
                            uint nIp3 = ((gigeInfo.nCurrentIp & 0x0000ff00) >> 8);
                            uint nIp4 = (gigeInfo.nCurrentIp & 0x000000ff);
                            ipAddress = $"{nIp1}.{nIp2}.{nIp3}.{nIp4}";

                            uint nMask1 = ((gigeInfo.nCurrentSubNetMask & 0xff000000) >> 24);
                            uint nMask2 = ((gigeInfo.nCurrentSubNetMask & 0x00ff0000) >> 16);
                            uint nMask3 = ((gigeInfo.nCurrentSubNetMask & 0x0000ff00) >> 8);
                            uint nMask4 = (gigeInfo.nCurrentSubNetMask & 0x000000ff);
                            subnetMask = $"{nMask1}.{nMask2}.{nMask3}.{nMask4}";

                            uint nGate1 = ((gigeInfo.nDefultGateWay & 0xff000000) >> 24);
                            uint nGate2 = ((gigeInfo.nDefultGateWay & 0x00ff0000) >> 16);
                            uint nGate3 = ((gigeInfo.nDefultGateWay & 0x0000ff00) >> 8);
                            uint nGate4 = (gigeInfo.nDefultGateWay & 0x000000ff);
                            gateway = $"{nGate1}.{nGate2}.{nGate3}.{nGate4}";

                            devices.Add(new DeviceInfo
                            {
                                Index = i,
                                ModelName = gigeInfo.chModelName,
                                SerialNumber = gigeInfo.chSerialNumber,
                                UserDefinedName = gigeInfo.chUserDefinedName,
                                DeviceVersion = gigeInfo.chDeviceVersion,
                                MacAddress = macAddress,
                                IpAddress = ipAddress,
                                SubnetMask = subnetMask,
                                DefaultGateway = gateway,
                                InterfaceType = DeviceInterfaceType.GigE
                            });
                            break;
                        }
                    case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_USB_DEVICE:
                        {
                            var usbInfo = Utilities.ByteToStruct<MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_USB3_DEVICE_INFO>(deviceInfo.SpecialInfo.stUsb3VInfo);

                            devices.Add(new DeviceInfo
                            {
                                Index = i,
                                ModelName = usbInfo.chModelName,
                                SerialNumber = usbInfo.chSerialNumber,
                                UserDefinedName = usbInfo.chUserDefinedName,
                                DeviceVersion = usbInfo.chDeviceVersion,
                                InterfaceType = DeviceInterfaceType.USB
                            });
                            break;
                        }
                }
            }

            return devices;
        }
        #endregion
        #region Props
        private readonly MvCodeReader _deviceHandle;
        private bool _isOpen = false;
        private bool _isGrabbing = false;
        private bool _disposed = false;

        // REVIEW-FIX: 单帧图像最大字节数上限（256MB），参考 HikScanner.MaxFrameSizeBytes 思路，防止异常 nFrameLen 导致 OOM
        private const long MaxFrameSizeBytes = 256L * 1024 * 1024;

        /// <summary>
        /// 获取设备句柄
        /// </summary>
        public MvCodeReader DeviceHandle => _deviceHandle;

        /// <summary>
        /// 获取设备是否已打开
        /// </summary>
        public bool IsOpen => System.Threading.Volatile.Read(ref _isOpen);

        /// <summary>
        /// 获取设备是否正在采集图像
        /// </summary>
        public bool IsGrabbing => System.Threading.Volatile.Read(ref _isGrabbing);

        /// <summary>
        /// 获取设备信息
        /// </summary>
        public DeviceInfo DeviceInfo { get; private set; }

        #endregion
        #region Ctor
        /// <summary>
        /// 从设备索引创建MvCodeReaderDevice实例
        /// </summary>
        /// <param name="deviceInfo">可选的设备信息（如果为null，将从枚举设备中获取）</param>
        public MvCodeReaderDevice(DeviceInfo deviceInfo)
        {
            DeviceInfo = deviceInfo;
            _deviceHandle = new MvCodeReaderSDKNet.MvCodeReader();
            int ret = _deviceHandle.MV_CODEREADER_CreateHandleBySerialNumber_NET(DeviceInfo.SerialNumber);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"创建设备句柄失败，错误码: 0x{ret:X8}", ret);
        }
        #endregion
        #region Open
        /// <summary>
        /// 打开设备
        /// </summary>
        public void Open()
        {
            if (_isOpen)
                return;
            int ret = _deviceHandle.MV_CODEREADER_OpenDevice_NET();
            if (ret != MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"打开设备失败，错误码: 0x{ret:X8}", ret);

            SetEnumValue("TriggerMode", 0);
            _triggerCallback = OnHardwareTrigger;
            ret = _deviceHandle.MV_CODEREADER_RegisterTriggerCallBack_NET(_triggerCallback, IntPtr.Zero);
            if (ret != MvCodeReader.MV_CODEREADER_OK)
            {
                throw new MvCodeReaderException($"注册硬件触发回调失败，错误码: 0x{ret:X8}", ret);
            }
            _isOpen = true;

        }
        #endregion
        #region Close
        /// <summary>
        /// 关闭设备
        /// </summary>
        public void Close()
        {
            if (!_isOpen) return;

            StopGrabbing();

            int ret = _deviceHandle.MV_CODEREADER_CloseDevice_NET();
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"警告: 关闭设备时出错，错误码: 0x{ret:X8}", ret);

            ret = _deviceHandle.MV_CODEREADER_DestroyHandle_NET();
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"警告: 销毁设备句柄时出错，错误码: 0x{ret:X8}", ret);

            _isOpen = false;
        }
        #endregion
        #region StartGrabbing
        private MvCodeReader.cbTriggerdelegate _triggerCallback = null;
        /// <summary>
        /// 开始图像采集
        /// </summary>
        public void StartGrabbing()
        {
            if (!_isOpen)
                Open();

            if (_isGrabbing)
                return;

            int ret = _deviceHandle.MV_CODEREADER_StartGrabbing_NET();

            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"开始图像采集失败，错误码: 0x{ret:X8}", ret);

            _isGrabbing = true;

        }

        private void OnHardwareTrigger(nint pstTriggerInfo, nint pUser)
        {
            Console.WriteLine("Trigger...");
            try
            {
                // 非托管指针转换为结构体
                if (pstTriggerInfo == IntPtr.Zero) return;
                MV_CODEREADER_TRIGGER_INFO_DATA stTriggerInfo =
                    (MV_CODEREADER_TRIGGER_INFO_DATA)Marshal.PtrToStructure(
                        pstTriggerInfo, typeof(MV_CODEREADER_TRIGGER_INFO_DATA));

                // 只处理触发开始事件（硬触发上升沿时刻）
                if (stTriggerInfo.nTriggerFlag != 1) return;

                // ===================== 核心：解析硬触发时间 =====================
                // 1. 拼接设备端64位硬触发原始时间戳（单位：通常为微秒，设备内部时钟）
                ulong nDeviceTriggerTick = ((ulong)stTriggerInfo.nTriggerTimeHigh << 32) | stTriggerInfo.nTriggerTimeLow;

                // 2. 主机端接收时间戳（int64，单位：100纳秒，对应Windows FILETIME）
                long nHostTimeStamp = stTriggerInfo.nHostTimeStamp;
                DateTime dtHostTime = DateTime.FromFileTime(nHostTimeStamp);

                // 打印时间信息
                Debug.WriteLine($"\n===== 收到硬触发信号 =====");
                Debug.WriteLine($"触发编号：{stTriggerInfo.nTriggerIndex}");
                Debug.WriteLine($"设备端触发时间戳：{nDeviceTriggerTick}");
                Debug.WriteLine($"主机端接收时间：{dtHostTime:yyyy-MM-dd HH:mm:ss.fff}");
                Debug.WriteLine($"原始触发通道号：{stTriggerInfo.nOriginalTrigger}");
                Debug.WriteLine($"PC当前时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"回调解析异常：{ex.Message}");
            }

        }
        #endregion
        #region StopGrabbing
        /// <summary>
        /// 停止图像采集
        /// </summary>
        public void StopGrabbing()
        {
            if (!_isGrabbing) return;
            int ret = _deviceHandle.MV_CODEREADER_StopGrabbing_NET();
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                Console.WriteLine($"警告: 停止图像采集时出错，错误码: 0x{ret:X8}");
            _deviceHandle.MV_CODEREADER_RegisterTriggerCallBack_NET(null, IntPtr.Zero);
            _isGrabbing = false;
        }
        #endregion
        #region GetImage
        /// <summary>
        /// 获取一帧图像数据
        /// </summary>
        /// <returns>图像结果对象</returns>
        public async Task<ImageResult> GetImageAsync(uint timeoutMs = 3600_000)
        {
            StartGrabbing();
            IntPtr pData = IntPtr.Zero;
            // FrameInfo 结构体需要申请内存来接收信息
            IntPtr pstFrameInfoEx2 = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)));

            try
            {
                int ret = await Task.Run(() => _deviceHandle.MV_CODEREADER_GetOneFrameTimeoutEx2_NET(ref pData, pstFrameInfoEx2, timeoutMs));
                //Debug.WriteLine($"相机完成采集时间:{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                {
                    throw new MvCodeReaderException($"获取图像数据失败，错误码: 0x{ret:X8}", ret);
                }
                var imageInfo = Marshal.PtrToStructure<MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2>(pstFrameInfoEx2);

                //var moex = Marshal.PtrToStructure<MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO>(pstFrameInfoEx2);

                // 拷贝数据：从 SDK 的内部内存(pData) 拷贝到 托管数组(buffer)
                // REVIEW-FIX: 限制单帧缓冲大小，防止异常 nFrameLen 导致内存溢出
                if (imageInfo.nFrameLen > MaxFrameSizeBytes)
                {
                    throw new MvCodeReaderException($"图像帧长度超限: {imageInfo.nFrameLen} 字节，上限 {MaxFrameSizeBytes} 字节", MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_BUFOVER);
                }
                var buffer = new byte[imageInfo.nFrameLen];
                if (pData != IntPtr.Zero)
                {
                    Marshal.Copy(pData, buffer, 0, (int)imageInfo.nFrameLen);
                }
                return new ImageResult(buffer, imageInfo);
            }
            finally
            {
                Marshal.FreeHGlobal(pstFrameInfoEx2);
            }
        }
        #endregion
        #region Dispose
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                if (_isGrabbing)
                    StopGrabbing();

                if (_isOpen)
                    Close();
                else
                {
                    // REVIEW-FIX: Open() 失败时 _isOpen 保持 false，Close() 不会执行，
                    // 这里兜底关闭/销毁句柄，避免句柄泄漏。
                    _deviceHandle.MV_CODEREADER_CloseDevice_NET();
                    _deviceHandle.MV_CODEREADER_DestroyHandle_NET();
                }
            }
            catch (Exception)
            {
                // REVIEW-FIX: 清理阶段的异常不应从 Dispose 冒泡（如设备已物理断开导致关闭/销毁失败）
            }
            finally
            {
                _disposed = true;
            }
        }
        #endregion
        #region Parameter
        /// <summary>
        /// 设置设备参数
        /// </summary>
        /// <param name="paramName">参数名</param>
        /// <param name="value">参数值</param>
        private void SetParameter(string paramName, object value)
        {
            if (!_isOpen)
                throw new InvalidOperationException("设备未打开");

            // 根据参数类型选择相应的方法
            if (value is int intValue)
            {
                SetIntValue(paramName, intValue);
            }
            else if (value is float floatValue)
            {
                SetFloatValue(paramName, floatValue);
            }
            else if (value is bool boolValue)
            {
                SetBoolValue(paramName, boolValue);
            }
            else if (value is string strValue)
            {
                SetStringValue(paramName, strValue);
            }
            else
            {
                throw new ArgumentException("不支持的参数类型", nameof(value));
            }
        }

        /// <summary>
        /// 获取设备参数
        /// </summary>
        /// <param name="paramName">参数名</param>
        /// <returns>参数值</returns>
        private object GetParameter(string paramName)
        {
            if (!_isOpen)
                throw new InvalidOperationException("设备未打开");

            // 此处应该根据参数类型调用相应的方法
            // 为了简化，这里只展示获取整型参数的示例
            var intValue = new MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_INTVALUE_EX();
            int ret = _deviceHandle.MV_CODEREADER_GetIntValue_NET(paramName, ref intValue);
            if (ret == MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
            {
                return intValue.nCurValue;
            }

            throw new MvCodeReaderException($"获取参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);
        }
        #endregion
        /// <summary>
        /// 检查设备状态，如果设备未打开则抛出异常
        /// </summary>
        /// <exception cref="InvalidOperationException">设备未打开时抛出</exception>
        private void CheckDeviceState()
        {
            //if (!_isOpen)
            //    throw new InvalidOperationException("设备未打开");
        }

        #region FloatValue
        /// <summary>
        /// 设置浮点型参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <param name="value">参数值</param>
        private void SetFloatValue(string paramName, float value)
        {
            CheckDeviceState();

            int ret = _deviceHandle.MV_CODEREADER_SetFloatValue_NET(paramName, value);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"设置浮点参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);
        }
        /// <summary>
        /// 获取浮点型参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <returns>参数值</returns>
        private float GetFloatValue(string paramName)
        {
            CheckDeviceState();

            var floatValue = new MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_FLOATVALUE();
            int ret = _deviceHandle.MV_CODEREADER_GetFloatValue_NET(paramName, ref floatValue);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"获取浮点参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);

            return floatValue.fCurValue;
        }
        #endregion
        #region IntValue
        /// <summary>
        /// 设置整型参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <param name="value">参数值</param>
        private void SetIntValue(string paramName, long value)
        {
            CheckDeviceState();

            // 注意：SDK中可能使用MV_CODEREADER_INTVALUE结构，这里简化处理
            // 实际实现可能需要根据SDK调整
            int ret = _deviceHandle.MV_CODEREADER_SetIntValue_NET(paramName, value);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"设置整型参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);
        }

        /// <summary>
        /// 获取整型参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <returns>参数值</returns>
        private long GetIntValue(string paramName)
        {
            CheckDeviceState();

            var intValue = new MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_INTVALUE_EX();
            int ret = _deviceHandle.MV_CODEREADER_GetIntValue_NET(paramName, ref intValue);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"获取整型参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);

            return intValue.nCurValue;
        }
        #endregion
        #region EnumValue
        /// <summary>
        /// 设置枚举型参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <param name="value">枚举值</param>
        private void SetEnumValue(string paramName, uint value)
        {
            CheckDeviceState();

            int ret = _deviceHandle.MV_CODEREADER_SetEnumValue_NET(paramName, value);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"设置枚举参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);
        }

        /// <summary>
        /// 获取枚举型参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <returns>枚举值</returns>
        private uint GetEnumValue(string paramName)
        {
            CheckDeviceState();

            var enumValue = new MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_ENUMVALUE();
            int ret = _deviceHandle.MV_CODEREADER_GetEnumValue_NET(paramName, ref enumValue);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"获取枚举参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);

            return enumValue.nCurValue;
        }

        #endregion
        #region BoolValue
        /// <summary>
        /// 设置布尔型参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <param name="value">布尔值</param>
        private void SetBoolValue(string paramName, bool value)
        {
            CheckDeviceState();

            int ret = _deviceHandle.MV_CODEREADER_SetBoolValue_NET(paramName, value);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"设置布尔参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);
        }

        /// <summary>
        /// 获取布尔型参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <returns>布尔值</returns>
        private bool GetBoolValue(string paramName)
        {
            CheckDeviceState();

            bool value = false;
            int ret = _deviceHandle.MV_CODEREADER_GetBoolValue_NET(paramName, ref value);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"获取布尔参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);

            return value;
        }
        #endregion
        #region StringValue
        /// <summary>
        /// 设置字符串参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <param name="value">字符串值</param>
        private void SetStringValue(string paramName, string value)
        {
            CheckDeviceState();

            int ret = _deviceHandle.MV_CODEREADER_SetStringValue_NET(paramName, value);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"设置字符串参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);
        }

        /// <summary>
        /// 获取字符串参数值
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <returns>字符串值</returns>
        private string GetStringValue(string paramName)
        {
            CheckDeviceState();

            var stringValue = new MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_STRINGVALUE();
            int ret = _deviceHandle.MV_CODEREADER_GetStringValue_NET(paramName, ref stringValue);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"获取字符串参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);

            return stringValue.chCurValue;
        }
        #endregion
        #region Command
        /// <summary>
        /// 执行命令型参数
        /// </summary>
        /// <param name="paramName">命令名称</param>
        public void ExecuteCommand(string paramName)
        {
            CheckDeviceState();

            int ret = _deviceHandle.MV_CODEREADER_SetCommandValue_NET(paramName);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"执行命令参数 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);
        }
        #endregion
        #region Range
        /// <summary>
        /// 获取参数范围（浮点型）
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <returns>参数范围（最小值和最大值）</returns>
        public (float Min, float Max) GetFloatRange(string paramName)
        {
            CheckDeviceState();

            var floatValue = new MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_FLOATVALUE();
            int ret = _deviceHandle.MV_CODEREADER_GetFloatValue_NET(paramName, ref floatValue);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"获取浮点参数范围 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);

            return (floatValue.fMin, floatValue.fMax);
        }

        /// <summary>
        /// 获取参数范围（整型）
        /// </summary>
        /// <param name="paramName">参数名称</param>
        /// <returns>参数范围（最小值和最大值）</returns>
        public (long Min, long Max) GetIntRange(string paramName)
        {
            CheckDeviceState();

            var intValue = new MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_INTVALUE_EX();
            int ret = _deviceHandle.MV_CODEREADER_GetIntValue_NET(paramName, ref intValue);
            if (ret != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new MvCodeReaderException($"获取整型参数范围 '{paramName}' 失败，错误码: 0x{ret:X8}", ret);

            return (intValue.nMin, intValue.nMax);
        }
        #endregion
        #region Exposure

        /// <summary>
        /// 设置曝光时间（微秒）
        /// </summary>
        /// <param name="exposureTime">曝光时间（微秒）</param>
        public void SetExposureTime(float exposureTime)
        {
            SetFloatValue("ExposureTime", exposureTime);
        }

        /// <summary>
        /// 获取曝光时间（微秒）
        /// </summary>
        /// <returns>曝光时间（微秒）</returns>
        public float GetExposureTime()
        {
            return GetFloatValue("ExposureTime");
        }
        #endregion
        #region Gain
        /// <summary>
        /// 设置增益
        /// </summary>
        /// <param name="gain">增益值</param>
        public void SetGain(float gain)
        {
            SetFloatValue("Gain", gain);
        }

        /// <summary>
        /// 获取增益
        /// </summary>
        /// <returns>增益值</returns>
        public float GetGain()
        {
            return GetFloatValue("Gain");
        }
        #endregion
        #region AutoExposure
        /// <summary>
        /// 设置自动曝光模式
        /// </summary>
        /// <param name="enabled">是否启用</param>
        public void SetAutoExposure(bool enabled)
        {
            // REVIEW-FIX: ExposureAuto 是 GenICam 枚举节点，改用枚举接口设置（0=Off, 2=Continuous），
            // 与 HikScanner.SetEnumParam("ExposureAuto", 0u) 的用法一致
            SetEnumValue("ExposureAuto", enabled ? 2u : 0u);
        }

        /// <summary>
        /// 获取自动曝光模式状态
        /// </summary>
        /// <returns>是否启用</returns>
        public bool GetAutoExposure()
        {
            // REVIEW-FIX: ExposureAuto 是 GenICam 枚举节点，改用枚举接口读取
            return GetEnumValue("ExposureAuto") != 0;
        }
        #endregion

        #region Trigger

        /// <summary>
        /// 触发模式类型
        /// </summary>
        public enum TriggerModeType
        {
            /// <summary>
            /// 连续采集模式
            /// </summary>
            Continuous,
            /// <summary>
            /// 软件触发模式
            /// </summary>
            SoftwareTrigger,
            /// <summary>
            /// 硬件触发模式
            /// </summary>
            HardwareTrigger
        }

        /// <summary>
        /// 设置触发模式
        /// </summary>
        /// <param name="enabled">是否启用触发模式</param>
        public void SetTriggerMode(bool enabled)
        {
            uint triggerMode = enabled ?
                (uint)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_ON :
                (uint)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_OFF;

            SetEnumValue("TriggerMode", triggerMode);
        }

        /// <summary>
        /// 获取触发模式状态
        /// </summary>
        /// <returns>是否启用触发模式</returns>
        public bool GetTriggerMode()
        {
            uint triggerMode = GetEnumValue("TriggerMode");
            return triggerMode == (uint)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_ON;
        }

        /// <summary>
        /// 设置触发源（软触发或硬触发）
        /// </summary>
        /// <param name="trigger">true表示软触发，false表示硬触发（LINE0）</param>
        public void SetTriggerSource(bool trigger)
        {
            uint triggerSource = trigger ?
                (uint)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_TRIGGER_SOURCE.MV_CODEREADER_TRIGGER_SOURCE_SOFTWARE :
                (uint)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_TRIGGER_SOURCE.MV_CODEREADER_TRIGGER_SOURCE_LINE0;

            SetEnumValue("TriggerSource", triggerSource);
        }

        /// <summary>
        /// 获取触发源
        /// </summary>
        /// <returns>true表示软触发，false表示硬触发</returns>
        public bool GetTriggerSource()
        {
            uint triggerSource = GetEnumValue("TriggerSource");
            return triggerSource == (uint)MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_TRIGGER_SOURCE.MV_CODEREADER_TRIGGER_SOURCE_SOFTWARE;
        }

        /// <summary>
        /// 切换到软触发模式
        /// </summary>
        public void SwitchToSoftwareTrigger()
        {
            if (IsGrabbing)
            {
                StopGrabbing();
            }
            if (TryGetCurrentTriggerMode(out var currentMode) && currentMode == TriggerModeType.SoftwareTrigger)
            {
                return;
            }
            SetTriggerMode(true);
            SetTriggerSource(true);

        }

        /// <summary>
        /// 切换到程序模式。
        /// 当前项目中程序模式等价于软件触发模式，由上位机主动下发触发命令。
        /// </summary>
        public void SwitchToProgramMode()
        {
            SwitchToSoftwareTrigger();
        }

        /// <summary>
        /// 切换到硬触发模式（LINE0）
        /// </summary>
        public void SwitchToHardwareTrigger()
        {
            if (IsGrabbing)
            {
                StopGrabbing();
            }
            if (TryGetCurrentTriggerMode(out var currentMode) && currentMode == TriggerModeType.HardwareTrigger)
            {
                return;
            }
            SetTriggerMode(true);
            SetTriggerSource(false);

        }

        /// <summary>
        /// 切换到连续采集模式（关闭触发）
        /// </summary>
        public void SwitchToContinuousMode()
        {
            if (IsGrabbing)
            {
                StopGrabbing();
            }
            if (TryGetCurrentTriggerMode(out var currentMode) && currentMode == TriggerModeType.Continuous)
            {
                return;
            }
            SetTriggerMode(false);
        }

        /// <summary>
        /// 执行软触发命令
        /// </summary>
        public void ExecuteSoftwareTrigger()
        {
            CheckDeviceState();
            if (!IsGrabbing)
            {
                StartGrabbing();
            }
            ExecuteCommand("TriggerSoftware");
        }

        /// <summary>
        /// 获取当前触发模式
        /// </summary>
        /// <returns>触发模式类型</returns>
        public TriggerModeType GetCurrentTriggerMode()
        {
            bool triggerMode = GetTriggerMode();
            if (!triggerMode)
            {
                return TriggerModeType.Continuous;
            }

            bool triggerSource = GetTriggerSource();
            return triggerSource ? TriggerModeType.SoftwareTrigger : TriggerModeType.HardwareTrigger;
        }

        /// <summary>
        /// 尝试获取当前触发模式；如果设备不支持读取或当前状态异常，则返回 false。
        /// </summary>
        private bool TryGetCurrentTriggerMode(out TriggerModeType triggerMode)
        {
            try
            {
                triggerMode = GetCurrentTriggerMode();
                return true;
            }
            catch (MvCodeReaderException)
            {
                triggerMode = TriggerModeType.Continuous;
                return false;
            }
        }
        #endregion
    }
}