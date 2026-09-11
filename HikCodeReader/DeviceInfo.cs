using System;
using System.Collections.Generic;
using System.Text;

namespace HikCodeReader
{
    /// <summary>
    /// 设备信息，描述海康威视工业读码器的基本属性。
    /// 包含设备型号、序列号、网络配置、接口类型等信息，
    /// 通常通过 <see cref="MvCodeReaderDevice.EnumDevices"/> 方法枚举获取。
    /// </summary>
    public class DeviceInfo
    {
        /// <summary>
        /// 获取或设置设备索引
        /// </summary>
        public int Index { get; set; }
        /// <summary>
        /// 获取或设置设备型号名称
        /// </summary>
        public string ModelName { get; set; } = string.Empty;
        /// <summary>
        /// 获取或设置设备序列号
        /// </summary>
        public string SerialNumber { get; set; } = string.Empty;
        /// <summary>
        /// 获取或设置用户自定义设备名称
        /// </summary>
        public string UserDefinedName { get; set; } = string.Empty;
        /// <summary>
        /// 获取或设置设备固件版本
        /// </summary>
        public string DeviceVersion { get; set; } = string.Empty;
        /// <summary>
        /// 获取或设置设备MAC地址
        /// </summary>
        public string MacAddress { get; set; } = string.Empty;
        /// <summary>
        /// 获取或设置设备IP地址
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;
        /// <summary>
        /// 获取或设置设备子网掩码
        /// </summary>
        public string SubnetMask { get; set; } = string.Empty;
        /// <summary>
        /// 获取或设置设备默认网关
        /// </summary>
        public string DefaultGateway { get; set; } = string.Empty;
        /// <summary>
        /// 获取或设置设备接口类型
        /// </summary>
        public DeviceInterfaceType InterfaceType { get; set; }

        /// <summary>
        /// 返回设备信息的字符串表示，包含所有非空属性值。用于调试和日志输出。
        /// </summary>
        /// <returns>格式化的设备信息字符串</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"DeviceInfo[Index={Index}");

            if (!string.IsNullOrEmpty(ModelName))
                sb.AppendLine($", ModelName={ModelName}");

            if (!string.IsNullOrEmpty(SerialNumber))
                sb.AppendLine($", SerialNumber={SerialNumber}");

            if (!string.IsNullOrEmpty(UserDefinedName))
                sb.AppendLine($", UserDefinedName={UserDefinedName}");

            if (!string.IsNullOrEmpty(DeviceVersion))
                sb.AppendLine($", DeviceVersion={DeviceVersion}");

            if (!string.IsNullOrEmpty(IpAddress))
                sb.AppendLine($", IpAddress={IpAddress}");

            if (!string.IsNullOrEmpty(SubnetMask))
                sb.AppendLine($", SubnetMask={SubnetMask}");

            if (!string.IsNullOrEmpty(DefaultGateway))
                sb.AppendLine($", DefaultGateway={DefaultGateway}");

            sb.AppendLine($", InterfaceType={InterfaceType}]");

            return sb.ToString();
        }
    }
}
