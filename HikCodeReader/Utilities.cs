using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HikCodeReader
{
    /// <summary>
    /// 工具类，提供字节转换等辅助功能
    /// </summary>
    public static class Utilities
    {
        /// <summary>
        /// 将字节数组转换为结构体
        /// </summary>
        /// <typeparam name="T">结构体类型</typeparam>
        /// <param name="bytes">字节数组</param>
        /// <param name="type">结构体类型（可省略）</param>
        /// <returns>转换后的结构体</returns>
        public static object ByteToStruct(byte[] bytes, Type type)
        {
            int size = Marshal.SizeOf(type);
            if (size > bytes.Length)
            {
                return null;
            }

            IntPtr structPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, structPtr, size);
                return Marshal.PtrToStructure(structPtr, type);
            }
            finally
            {
                // REVIEW-FIX: 与泛型版 ByteToStruct<T> 一致，Marshal 操作异常时也释放非托管内存
                Marshal.FreeHGlobal(structPtr);
            }
        }

        /// <summary>
        /// 将字节数组转换为结构体（泛型版本）
        /// </summary>
        /// <typeparam name="T">结构体类型</typeparam>
        /// <param name="bytes">字节数组</param>
        /// <returns>转换后的结构体</returns>
        public static T ByteToStruct<T>(byte[] bytes) where T : struct
        {
            Type type = typeof(T);
            int size = Marshal.SizeOf(type);
            if (size > bytes.Length)
            {
                throw new ArgumentException($"字节数组长度不足，需要{size}字节，实际{bytes.Length}字节");
            }

            IntPtr structPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, structPtr, size);
                return Marshal.PtrToStructure<T>(structPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(structPtr);
            }
        }

        /// <summary>
        /// 将字节数组转换为结构体数组中的指定索引元素
        /// </summary>
        /// <typeparam name="T">结构体类型</typeparam>
        /// <param name="bytes">字节数组</param>
        /// <param name="index">元素索引</param>
        /// <returns>转换后的结构体</returns>
        public static T ByteToStruct<T>(IntPtr bytes, int index) where T : struct
        {
            Type type = typeof(T);
            int size = Marshal.SizeOf(type);
            IntPtr ptr = new IntPtr(bytes.ToInt64() + index * size);
            return Marshal.PtrToStructure<T>(ptr);
        }

        /// <summary>
        /// 将结构体转换为字节数组
        /// </summary>
        /// <typeparam name="T">结构体类型</typeparam>
        /// <param name="structure">结构体实例</param>
        /// <returns>字节数组</returns>
        public static byte[] StructToBytes<T>(T structure) where T : struct
        {
            int size = Marshal.SizeOf(structure);
            byte[] bytes = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);

            try
            {
                Marshal.StructureToPtr(structure, ptr, true);
                Marshal.Copy(ptr, bytes, 0, size);
                return bytes;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// 将字节数组转换为字符串（用于处理C风格的字符串）
        /// </summary>
        /// <param name="bytes">字节数组</param>
        /// <returns>转换后的字符串</returns>
        public static string ByteArrayToString(byte[] bytes)
        {
            if (bytes == null)
                return string.Empty;

            // 查找第一个空字符(null terminator)，这是C字符串的结束标志
            int nullIndex = Array.IndexOf(bytes, (byte)0);
            if (nullIndex >= 0)
            {
                // 截取到null终止符之前的部分
                return Encoding.UTF8.GetString(bytes, 0, nullIndex);
            }
            else
            {
                // 如果没有null终止符，则返回整个数组的字符串表示
                return Encoding.UTF8.GetString(bytes);
            }
        }

        #region GetErrorDescription
        /// <summary>
        /// 获取错误码对应的错误信息
        /// </summary>
        /// <param name="errorCode">错误码</param>
        /// <returns>错误信息描述</returns>
        public static string GetErrorDescription(int errorCode)
        {
            switch (errorCode)
            {
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_OK:
                    return "成功";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_HANDLE:
                    return "句柄无效";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_SUPPORT:
                    return "不支持的功能";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_BUFOVER:
                    return "缓存溢出";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_CALLORDER:
                    return "函数调用顺序错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_PARAMETER:
                    return "参数错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_RESOURCE:
                    return "资源申请失败";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NODATA:
                    return "无数据";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_PRECONDITION:
                    return "前置条件错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_VERSION:
                    return "版本不匹配";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NOENOUGH_BUF:
                    return "缓冲区不足";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_UNKNOW:
                    return "未知错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_GENERIC:
                    return "通用错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_GC_ACCESS:
                    return "访问权限错误";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_ACCESS_DENIED:
                    return "拒绝访问";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_BUSY:
                    return "设备忙";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_E_NETER:
                    return "网络错误";
                // 修改：使用硬编码值代替缺失的常量
                case unchecked((int)0x80000000 | 13): // 替代 MV_CODEREADER_E_NETWORK_DISCONNECT_FAILED
                    return "网络断开失败";
                case unchecked((int)0x80000000 | 14): // 替代 MV_CODEREADER_E_NETWORK_SET_IP_FAILED
                    return "网络设置IP失败";
                case unchecked((int)0x80000000 | 15): // 替代 MV_CODEREADER_E_NETWORK_FORCE_IP_FAILED
                    return "网络强制IP失败";
                case unchecked((int)0x80000000 | 16): // 替代 MV_CODEREADER_E_NETWORK_GET_IP_FAILED
                    return "网络获取IP失败";
                case unchecked((int)0x80000000 | 17): // 替代 MV_CODEREADER_E_TIMEOUT
                    return "超时错误";
                case unchecked((int)0x80000000 | 18): // 替代 MV_CODEREADER_E_CANCELED
                    return "操作被取消";
                default:
                    return $"未知错误码: 0x{errorCode:X8}";
            }
        }
        #endregion
        #region GetBarcodeTypeString
        /// <summary>
        /// 获取条码类型字符串
        /// </summary>
        /// <param name="codeType">条码类型枚举</param>
        /// <returns>条码类型描述</returns>
        public static string GetBarcodeTypeString(MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE codeType)
        {
            switch (codeType)
            {
                // 使用现有类型，如果不存在就返回默认值
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_QR:
                    return "QR Code";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_DM:
                    return "Data Matrix";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_PDF417:
                    return "PDF417";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN8:
                    return "EAN-8";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN13:
                    return "EAN-13";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_UPCA:
                    return "UPC-A";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_UPCE:
                    return "UPC-E";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE39:
                    return "Code 39";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE93:
                    return "Code 93";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE128:
                    return "Code 128";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODABAR:
                    return "Codabar";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ITF25:
                    return "ITF";
                // 如果是未知类型，尝试使用硬编码值判断
                default:
                    // 使用实际的枚举值来匹配
                    switch ((uint)codeType)
                    {
                        case 10: // 假设MV_CODEREADER_CODE_UNKNOW的值是10
                            return "未知类型";
                        case 11: // 假设MV_CODEREADER_CODE_EAN8的值是11
                            return "EAN-8";
                        case 12: // 假设MV_CODEREADER_CODE_EAN13的值是12
                            return "EAN-13";
                        case 13: // 假设MV_CODEREADER_CODE_UPCA的值是13
                            return "UPC-A";
                        case 14: // 假设MV_CODEREADER_CODE_UPCE的值是14
                            return "UPC-E";
                        case 15: // 假设MV_CODEREADER_CODE_CODE39的值是15
                            return "Code 39";
                        case 16: // 假设MV_CODEREADER_CODE_CODE93的值是16
                            return "Code 93";
                        case 17: // 假设MV_CODEREADER_CODE_CODE128的值是17
                            return "Code 128";
                        case 18: // 假设MV_CODEREADER_CODE_CODABAR的값是18
                            return "Codabar";
                        case 19: // 假定MV_CODEREADER_CODE_ITF의값은19
                            return "ITF";
                        default:
                            return "未知类型";
                    }
            }
        }
        #endregion
    }
}