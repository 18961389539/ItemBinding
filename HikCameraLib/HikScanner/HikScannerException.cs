using System;
using System.Runtime.Serialization;

// ReSharper disable once InconsistentNaming
#pragma warning disable SYSLIB0011, CS0672, SYSLIB0051 // 序列化在 .NET 8+ 中已过时，但保留以支持旧场景

namespace HikScanner
{
    /// <summary>
    /// HikScanner 异常类，包含SDK错误码及中文描述。
    /// #15: 添加序列化构造函数，支持跨 AppDomain 序列化。
    /// </summary>
    [Serializable]
    public class HikScannerException : Exception
    {
        /// <summary>SDK错误码</summary>
        public int ErrorCode { get; }

        /// <summary>仅消息构造（无错误码，用于非 SDK 异常场景）</summary>
        public HikScannerException(string message) : base(message) { ErrorCode = 0; }

        /// <summary>消息 + SDK错误码构造</summary>
        public HikScannerException(string message, int errorCode)
            : base($"{message}: Error=0x{errorCode:X8} ({GetErrorDescription(errorCode)})")
        {
            ErrorCode = errorCode;
        }

        /// <summary>消息 + 内部异常构造（用于异常包装）</summary>
        public HikScannerException(string message, Exception innerException) : base(message, innerException) { ErrorCode = 0; }

        /// <summary>消息 + SDK错误码 + 内部异常构造</summary>
        public HikScannerException(string message, int errorCode, Exception innerException)
            : base($"{message}: Error=0x{errorCode:X8} ({GetErrorDescription(errorCode)})", innerException)
        {
            ErrorCode = errorCode;
        }

        /// <summary>#15: 序列化构造函数，支持跨 AppDomain 序列化</summary>
        protected HikScannerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            ErrorCode = info.GetInt32(nameof(ErrorCode));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(ErrorCode), ErrorCode);
        }

        /// <summary>根据SDK错误码返回中文描述</summary>
        public static string GetErrorDescription(int errorCode)
        {
            switch (errorCode)
            {
                // ─── 通用错误码 (0x80020000-0x800200FF) ───
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_HANDLE: return "错误或无效的句柄";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_SUPPORT: return "不支持的功能";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_BUFOVER: return "缓存已满";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_CALLORDER: return "函数调用顺序错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_PARAMETER: return "参数错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_RESOURCE: return "资源申请失败";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NODATA: return "无数据";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_PRECONDITION: return "前置条件错误或运行环境已变化";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_VERSION: return "版本不匹配";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NOENOUGH_BUF: return "传入的内存空间不足";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_ABNORMAL_IMAGE: return "异常图像，可能是丢包导致图像不完整";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_LOAD_LIBRARY: return "动态导入DLL失败，缺少运行时库或驱动未安装";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NOOUTBUF: return "没有可输出的缓存";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_FILE_PATH: return "文件路径错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_UNKNOW: return "未知错误";

                // ─── GenICam 系列错误 (0x80020100-0x800201FF) ───
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_GENERIC: return "GenICam通用错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_ARGUMENT: return "GenICam参数非法";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_RANGE: return "GenICam值超出范围";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_PROPERTY: return "GenICam属性错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_RUNTIME: return "GenICam运行环境有问题";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_LOGICAL: return "GenICam逻辑错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_ACCESS: return "GenICam节点访问条件错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_TIMEOUT: return "GenICam超时";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_DYNAMICCAST: return "GenICam转换异常";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_UNKNOW: return "GenICam未知错误";

                // ─── GigE 网络错误 (0x80020200-0x800202FF) ───
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NOT_IMPLEMENTED: return "命令不被设备支持";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_INVALID_ADDRESS: return "访问的目标地址不存在";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_WRITE_PROTECT: return "目标地址不可写";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_ACCESS_DENIED: return "设备无访问权限";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_BUSY: return "设备忙或网络断开";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_PACKET: return "网络包数据错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NETER: return "网络错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_IP_CONFLICT: return "设备IP冲突";

                // ─── USB 错误 (0x80020300-0x800203FF) ───
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_USB_READ: return "USB读取出错";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_USB_WRITE: return "USB写入出错";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_USB_DEVICE: return "USB设备异常";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_USB_GENICAM: return "USB GenICam相关错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_USB_BANDWIDTH: return "USB带宽不足";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_USB_DRIVER: return "USB驱动不匹配或未安装";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_USB_UNKNOW: return "USB未知错误";

                // ─── 升级错误 (0x80020400-0x800204FF) ───
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_UPG_FILE_MISMATCH: return "升级固件不匹配";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_UPG_LANGUSGE_MISMATCH: return "升级固件语言不匹配";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_UPG_CONFLICT: return "设备已在升级中";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_UPG_INNER_ERR: return "升级时相机内部错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_UPG_UNKNOW: return "升级未知错误";

                // ─── 网络组件错误 (0x80020500-0x800205FF) ───
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_CREAT_SOCKET: return "创建Socket失败";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_BIND_SOCKET: return "绑定Socket失败";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NET_WRITE: return "网络写入失败";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NET_READ: return "网络读取失败";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NET_TIMEOUT: return "网络超时";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NET_UNKNOW: return "网络组件未知错误";

                default: return "未知错误码";
            }
        }

        internal static void Check(int errorCode, string operation)
        {
            if (errorCode != MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK)
                throw new HikScannerException(operation, errorCode);
        }
    }
}
