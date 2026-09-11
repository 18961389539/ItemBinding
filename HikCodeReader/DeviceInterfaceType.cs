using System;
using System.Collections.Generic;
using System.Text;

namespace HikCodeReader
{
    /// <summary>
    /// 设备接口类型
    /// </summary>
    public enum DeviceInterfaceType
    {
        /// <summary>
        /// 千兆以太网接口
        /// </summary>
        GigE = 0,
        /// <summary>
        /// USB接口
        /// </summary>
        USB = 1,
        /// <summary>
        /// CameraLink接口
        /// </summary>
        CameraLink = 2
    }
}
