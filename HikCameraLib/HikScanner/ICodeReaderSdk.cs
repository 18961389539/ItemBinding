using System;
using System.Runtime.InteropServices;
using MvCodeReaderSDKNet;

namespace HikScanner
{
    // ─── #13: 按职责拆分子接口，便于按需 Mock ───

    /// <summary>连接管理接口</summary>
    public interface ICodeReaderConnection
    {
        int MV_CODEREADER_CreateHandle_NET(ref MvCodeReader.MV_CODEREADER_DEVICE_INFO info);
        int MV_CODEREADER_CreateHandleBySerialNumber_NET(string serialNumber);
        int MV_CODEREADER_OpenDevice_NET();
        int MV_CODEREADER_CloseDevice_NET();
        int MV_CODEREADER_DestroyHandle_NET();
        int MV_CODEREADER_GetDeviceInfo_NET(ref MvCodeReader.MV_CODEREADER_DEVICE_INFO info);
    }

    /// <summary>采集控制接口</summary>
    public interface ICodeReaderGrabbing
    {
        int MV_CODEREADER_StartGrabbing_NET();
        int MV_CODEREADER_StopGrabbing_NET();
        int MV_CODEREADER_GetOneFrameTimeoutEx2_NET(ref IntPtr pData, IntPtr pstFrameInfo, uint nMsec);
        int MV_CODEREADER_RegisterImageCallBackEx2_NET(MvCodeReader.cbOutputEx2delegate callback, IntPtr pUser);
        int MV_CODEREADER_RegisterExceptionCallBack_NET(MvCodeReader.cbExceptiondelegate callback, IntPtr pUser);
    }

    /// <summary>参数读写接口</summary>
    public interface ICodeReaderParams
    {
        int MV_CODEREADER_SetEnumValue_NET(string key, uint value);
        int MV_CODEREADER_SetIntValue_NET(string key, long value);
        int MV_CODEREADER_GetIntValue_NET(string key, ref MvCodeReader.MV_CODEREADER_INTVALUE_EX value);
        int MV_CODEREADER_SetFloatValue_NET(string key, float value);
        int MV_CODEREADER_GetFloatValue_NET(string key, ref MvCodeReader.MV_CODEREADER_FLOATVALUE value);
        int MV_CODEREADER_GetBoolValue_NET(string key, ref bool value);
        int MV_CODEREADER_SetBoolValue_NET(string key, bool value);
        int MV_CODEREADER_GetStringValue_NET(string key, ref MvCodeReader.MV_CODEREADER_STRINGVALUE value);
        int MV_CODEREADER_SetStringValue_NET(string key, string value);
        int MV_CODEREADER_SetEnumValueByString_NET(string key, string value);
        int MV_CODEREADER_SetCommandValue_NET(string key);
        int MV_CODEREADER_GetEnumValue_NET(string key, ref MvCodeReader.MV_CODEREADER_ENUMVALUE value);
        int MV_CODEREADER_SetWayBillEnable_NET(bool enable);
        int MV_CODEREADER_Algorithm_SetIntValue_NET(string key, int value);
        int MV_CODEREADER_Algorithm_GetIntValue_NET(string key, ref int value);
    }

    /// <summary>文件存取接口</summary>
    public interface ICodeReaderFileAccess
    {
        int MV_CODEREADER_FileAccessRead_NET(ref MvCodeReader.MV_CODEREADER_FILE_ACCESS info);
        int MV_CODEREADER_FileAccessWrite_NET(ref MvCodeReader.MV_CODEREADER_FILE_ACCESS info);
        int MV_CODEREADER_GetFileAccessProgress_NET(ref MvCodeReader.MV_CODEREADER_FILE_ACCESS_PROGRESS progress);
    }

    /// <summary>GigE 网络配置接口</summary>
    public interface ICodeReaderGigE
    {
        int MV_CODEREADER_GetOptimalPacketSize_NET();
        int MV_CODEREADER_GIGE_ForceIp_NET(uint nIp, uint nSubnetMask, uint nGateway);
        int MV_CODEREADER_GIGE_SetIpConfig_NET(uint mode);
        int MV_CODEREADER_GIGE_SetGvcpTimeout_NET(uint timeout);
        int MV_CODEREADER_GIGE_GetGvcpTimeout_NET(IntPtr timeout);
    }

    /// <summary>MSC 多通道采集接口</summary>
    public interface ICodeReaderMsc
    {
        int MV_CODEREADER_MSC_GetOneFrameTimeout_NET(ref IntPtr pData, IntPtr pstFrameInfo, uint nChannelID, uint nMsec);
        int MV_CODEREADER_MSC_RegisterImageCallBack_NET(uint channelId, MvCodeReader.cbMSCOutputdelegate callback, IntPtr pUser);
    }

    /// <summary>图像编码接口</summary>
    public interface ICodeReaderImage
    {
        int MV_CODEREADER_SaveImage_NET(ref MvCodeReader.MV_CODEREADER_SAVE_IMAGE_PARAM_EX info);
    }

    /// <summary>
    /// 读码器 SDK 抽象接口，用于解耦 HikScanner 与 MvCodeReader 的直接依赖。
    /// 生产环境使用 CodeReaderSdkAdapter 包装真实 SDK；测试环境可注入 Mock 实现。
    /// #13: 按职责拆分为子接口，ICodeReaderSdk 继承全部以保持向后兼容。
    /// </summary>
    public interface ICodeReaderSdk
        : ICodeReaderConnection, ICodeReaderGrabbing, ICodeReaderParams,
          ICodeReaderFileAccess, ICodeReaderGigE, ICodeReaderMsc, ICodeReaderImage
    {
    }

    /// <summary>
    /// 默认适配器，委托给真实 MvCodeReader SDK 实例。
    /// </summary>
    public sealed class CodeReaderSdkAdapter : ICodeReaderSdk
    {
        private readonly MvCodeReader _inner;

        public CodeReaderSdkAdapter() { _inner = new MvCodeReader(); }
        public CodeReaderSdkAdapter(MvCodeReader existing) { _inner = existing; }

        /// <summary>获取底层 MvCodeReader 实例（仅供需要直接访问 SDK 的高级场景）</summary>
        public MvCodeReader Inner => _inner;

        public int MV_CODEREADER_CreateHandle_NET(ref MvCodeReader.MV_CODEREADER_DEVICE_INFO info)
            => _inner.MV_CODEREADER_CreateHandle_NET(ref info);
        public int MV_CODEREADER_CreateHandleBySerialNumber_NET(string serialNumber)
            => _inner.MV_CODEREADER_CreateHandleBySerialNumber_NET(serialNumber);
        public int MV_CODEREADER_OpenDevice_NET() => _inner.MV_CODEREADER_OpenDevice_NET();
        public int MV_CODEREADER_CloseDevice_NET() => _inner.MV_CODEREADER_CloseDevice_NET();
        public int MV_CODEREADER_DestroyHandle_NET() => _inner.MV_CODEREADER_DestroyHandle_NET();
        public int MV_CODEREADER_StartGrabbing_NET() => _inner.MV_CODEREADER_StartGrabbing_NET();
        public int MV_CODEREADER_StopGrabbing_NET() => _inner.MV_CODEREADER_StopGrabbing_NET();
        public int MV_CODEREADER_GetOneFrameTimeoutEx2_NET(ref IntPtr pData, IntPtr pstFrameInfo, uint nMsec)
            => _inner.MV_CODEREADER_GetOneFrameTimeoutEx2_NET(ref pData, pstFrameInfo, nMsec);
        public int MV_CODEREADER_RegisterImageCallBackEx2_NET(MvCodeReader.cbOutputEx2delegate callback, IntPtr pUser)
            => _inner.MV_CODEREADER_RegisterImageCallBackEx2_NET(callback, pUser);
        public int MV_CODEREADER_RegisterExceptionCallBack_NET(MvCodeReader.cbExceptiondelegate callback, IntPtr pUser)
            => _inner.MV_CODEREADER_RegisterExceptionCallBack_NET(callback, pUser);
        public int MV_CODEREADER_SetEnumValue_NET(string key, uint value)
            => _inner.MV_CODEREADER_SetEnumValue_NET(key, value);
        public int MV_CODEREADER_SetIntValue_NET(string key, long value)
            => _inner.MV_CODEREADER_SetIntValue_NET(key, value);
        public int MV_CODEREADER_GetIntValue_NET(string key, ref MvCodeReader.MV_CODEREADER_INTVALUE_EX value)
            => _inner.MV_CODEREADER_GetIntValue_NET(key, ref value);
        public int MV_CODEREADER_GetOptimalPacketSize_NET()
            => _inner.MV_CODEREADER_GetOptimalPacketSize_NET();
        public int MV_CODEREADER_SetFloatValue_NET(string key, float value)
            => _inner.MV_CODEREADER_SetFloatValue_NET(key, value);
        public int MV_CODEREADER_GetFloatValue_NET(string key, ref MvCodeReader.MV_CODEREADER_FLOATVALUE value)
            => _inner.MV_CODEREADER_GetFloatValue_NET(key, ref value);
        public int MV_CODEREADER_GetBoolValue_NET(string key, ref bool value)
            => _inner.MV_CODEREADER_GetBoolValue_NET(key, ref value);
        public int MV_CODEREADER_SetBoolValue_NET(string key, bool value)
            => _inner.MV_CODEREADER_SetBoolValue_NET(key, value);
        public int MV_CODEREADER_GetStringValue_NET(string key, ref MvCodeReader.MV_CODEREADER_STRINGVALUE value)
            => _inner.MV_CODEREADER_GetStringValue_NET(key, ref value);
        public int MV_CODEREADER_SetStringValue_NET(string key, string value)
            => _inner.MV_CODEREADER_SetStringValue_NET(key, value);
        public int MV_CODEREADER_SetEnumValueByString_NET(string key, string value)
            => _inner.MV_CODEREADER_SetEnumValueByString_NET(key, value);
        public int MV_CODEREADER_SetCommandValue_NET(string key)
            => _inner.MV_CODEREADER_SetCommandValue_NET(key);
        public int MV_CODEREADER_GetEnumValue_NET(string key, ref MvCodeReader.MV_CODEREADER_ENUMVALUE value)
            => _inner.MV_CODEREADER_GetEnumValue_NET(key, ref value);
        public int MV_CODEREADER_GetDeviceInfo_NET(ref MvCodeReader.MV_CODEREADER_DEVICE_INFO info)
            => _inner.MV_CODEREADER_GetDeviceInfo_NET(ref info);
        public int MV_CODEREADER_SaveImage_NET(ref MvCodeReader.MV_CODEREADER_SAVE_IMAGE_PARAM_EX info)
            => _inner.MV_CODEREADER_SaveImage_NET(ref info);
        public int MV_CODEREADER_MSC_GetOneFrameTimeout_NET(ref IntPtr pData, IntPtr pstFrameInfo, uint nChannelID, uint nMsec)
            => _inner.MV_CODEREADER_MSC_GetOneFrameTimeout_NET(ref pData, pstFrameInfo, nChannelID, nMsec);
        public int MV_CODEREADER_MSC_RegisterImageCallBack_NET(uint channelId, MvCodeReader.cbMSCOutputdelegate callback, IntPtr pUser)
            => _inner.MV_CODEREADER_MSC_RegisterImageCallBack_NET(channelId, callback, pUser);
        public int MV_CODEREADER_FileAccessRead_NET(ref MvCodeReader.MV_CODEREADER_FILE_ACCESS info)
            => _inner.MV_CODEREADER_FileAccessRead_NET(ref info);
        public int MV_CODEREADER_FileAccessWrite_NET(ref MvCodeReader.MV_CODEREADER_FILE_ACCESS info)
            => _inner.MV_CODEREADER_FileAccessWrite_NET(ref info);
        public int MV_CODEREADER_GetFileAccessProgress_NET(ref MvCodeReader.MV_CODEREADER_FILE_ACCESS_PROGRESS progress)
            => _inner.MV_CODEREADER_GetFileAccessProgress_NET(ref progress);
        public int MV_CODEREADER_GIGE_ForceIp_NET(uint nIp, uint nSubnetMask, uint nGateway)
            => _inner.MV_CODEREADER_GIGE_ForceIp_NET(nIp, nSubnetMask, nGateway);
        public int MV_CODEREADER_GIGE_SetIpConfig_NET(uint mode)
            => _inner.MV_CODEREADER_GIGE_SetIpConfig_NET(mode);
        public int MV_CODEREADER_GIGE_SetGvcpTimeout_NET(uint timeout)
            => _inner.MV_CODEREADER_GIGE_SetGvcpTimeout_NET(timeout);
        public int MV_CODEREADER_GIGE_GetGvcpTimeout_NET(IntPtr timeout)
            => _inner.MV_CODEREADER_GIGE_GetGvcpTimeout_NET(timeout);
        public int MV_CODEREADER_SetWayBillEnable_NET(bool enable)
            => _inner.MV_CODEREADER_SetWayBillEnable_NET(enable);
        public int MV_CODEREADER_Algorithm_SetIntValue_NET(string key, int value)
            => _inner.MV_CODEREADER_Algorithm_SetIntValue_NET(key, value);
        public int MV_CODEREADER_Algorithm_GetIntValue_NET(string key, ref int value)
            => _inner.MV_CODEREADER_Algorithm_GetIntValue_NET(key, ref value);
    }
}
