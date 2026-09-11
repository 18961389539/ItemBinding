using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using MvCodeReaderSDKNet;

namespace HikScanner
{
    /// <summary>MSC 多通道采集</summary>
    public partial class HikScanner
    {
        private Thread _mscThread0;
        private Thread _mscThread1;
        // 每个 MSC 通道独立的预分配缓冲区，避免每帧 AllocHGlobal/FreeHGlobal
        private IntPtr _mscBuffer0 = IntPtr.Zero;
        private IntPtr _mscBuffer1 = IntPtr.Zero;
        private readonly object _mscBufferLock0 = new();
        private readonly object _mscBufferLock1 = new();

        public HikGrabResult MscGrabOneFrame(uint channelId, int timeoutMs = 1000)
        {
            // #2: 加 _isConnected 检查，与 GrabOneFrame 一致
            if (!_isGrabbing || !_isConnected || _disposed) return new HikGrabResult { Status = HikGrabStatus.Error };
            // #3: 检查 _device null，防止 Dispose 后竞态访问
            if (_device == null) return new HikGrabResult { Status = HikGrabStatus.Error };

            // #2: 使用 MSC 通道独立缓冲区，不与 GrabOneFrameInternal 的 _pFrameInfo 竞争
            var bufferLock = channelId == 0 ? _mscBufferLock0 : _mscBufferLock1;
            lock (bufferLock)
            {
                IntPtr pData = IntPtr.Zero;

                // 使用通道独立的预分配缓冲区
                ref IntPtr pBuf = ref (channelId == 0 ? ref _mscBuffer0 : ref _mscBuffer1);
                int frameInfoSize = Marshal.SizeOf(typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));
                if (pBuf == IntPtr.Zero)
                    pBuf = Marshal.AllocHGlobal(frameInfoSize);
                IntPtr pstFrameInfo = pBuf;

                Marshal.StructureToPtr(new MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2(), pstFrameInfo, false);

                int nRet = _device.MV_CODEREADER_MSC_GetOneFrameTimeout_NET(ref pData, pstFrameInfo, channelId, (uint)timeoutMs);
                if (nRet != MvCodeReader.MV_CODEREADER_OK)
                {
                    bool isTimeout = (nRet == MvCodeReader.MV_CODEREADER_E_NODATA);
                    return new HikGrabResult
                    {
                        Status = isTimeout ? HikGrabStatus.Timeout : HikGrabStatus.Error,
                        RawErrorCode = nRet
                    };
                }

                var stFrameInfo = (MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)Marshal.PtrToStructure(pstFrameInfo, typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));
                if (stFrameInfo.nFrameLen == 0) return new HikGrabResult { Status = HikGrabStatus.NoData };

                return ParseFrameData(pData, pstFrameInfo, stFrameInfo);
            }
        }

        public void MscRegisterImageCallback(uint channelId)
        {
            // #5: 加连接检查，与其他公共方法一致
            EnsureConnected();
            var callback = new MvCodeReader.cbMSCOutputdelegate((pData, pstFrameInfo, pUser) =>
            {
                // #3: 捕获回调异常，防止 SDK 回调线程崩溃
                try
                {
                    var st = (MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)Marshal.PtrToStructure(pstFrameInfo, typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));
                    if (st.nFrameLen == 0) return;
                    OnImageGrabbed(ParseFrameData(pData, pstFrameInfo, st));
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, $"[MSC] 通道{channelId} 回调解析异常", ex);
                }
            });

            HikScannerException.Check(_device.MV_CODEREADER_MSC_RegisterImageCallBack_NET(channelId, callback, IntPtr.Zero), "MSC注册回调");
            if (channelId == 0) _mscCallback0 = callback; else _mscCallback1 = callback;
            GC.KeepAlive(callback);
        }

        public void StartMscDualChannelGrab(Action<HikGrabResult> onFrameGrabbed, int timeoutMs = 1000)
        {
            ThrowIfDisposed();
            lock (_grabLock)
            {
                if (!_isConnected) throw new InvalidOperationException("设备未连接");
                // #4: 统一行为：已在采集时抛异常，与其他 StartGrabbing 方法一致
                if (_isGrabbing) throw new InvalidOperationException("设备已在采集状态，请先调用 StopGrabbing");
                // #3: 检查 StartGrabbing 返回值
                int nRet = _device.MV_CODEREADER_StartGrabbing_NET();
                if (nRet != MvCodeReader.MV_CODEREADER_OK) throw new HikScannerException("开始采集失败", nRet);
                _isGrabbing = true;
                _activeGrabMode = HikGrabMode.MscDualChannel;
                _mscCallback = onFrameGrabbed;
            }

            _mscThread0 = CreateMscGrabThread(0, onFrameGrabbed, timeoutMs);
            _mscThread1 = CreateMscGrabThread(1, onFrameGrabbed, timeoutMs);
            _mscThread0.Start();
            _mscThread1.Start();
        }

        private Thread CreateMscGrabThread(uint channelId, Action<HikGrabResult> onFrameGrabbed, int timeoutMs)
        {
            var thread = new Thread(() =>
            {
                // #1: 预分配缓冲区（仅初始化时持锁），采集循环每帧获取/释放锁
                var bufferLock = channelId == 0 ? _mscBufferLock0 : _mscBufferLock1;
                int frameInfoSize = Marshal.SizeOf(typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));
                lock (bufferLock)
                {
                    ref IntPtr pBuf = ref (channelId == 0 ? ref _mscBuffer0 : ref _mscBuffer1);
                    if (pBuf == IntPtr.Zero)
                        pBuf = Marshal.AllocHGlobal(frameInfoSize);
                }

                while (_isGrabbing && !_disposed)
                {
                    try
                    {
                        // #2: 检查 _device null，防止 Dispose 后竞态访问
                        if (_device == null) break;
                        // #1: 每帧获取/释放锁，不长期持有
                        lock (bufferLock)
                        {
                            ref IntPtr pBuf = ref (channelId == 0 ? ref _mscBuffer0 : ref _mscBuffer1);
                            IntPtr pstInfo = pBuf;
                            Marshal.StructureToPtr(new MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2(), pstInfo, false);
                            IntPtr pData = IntPtr.Zero;
                            if (_device.MV_CODEREADER_MSC_GetOneFrameTimeout_NET(ref pData, pstInfo, channelId, (uint)timeoutMs) == MvCodeReader.MV_CODEREADER_OK)
                            {
                                var st = (MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)Marshal.PtrToStructure(pstInfo, typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));
                                if (st.nFrameLen > 0) onFrameGrabbed?.Invoke(ParseFrameData(pData, pstInfo, st));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Error, $"[MSC] 通道{channelId} 单帧解析失败", ex);
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = $"HikScanner-MSC-{channelId}";
            return thread;
        }

        /// <summary>等待 MSC 采集线程退出（StopGrabbing 后调用）</summary>
        internal void JoinMscThreads(int timeoutMs = 3000)
        {
            _mscThread0?.Join(timeoutMs);
            _mscThread1?.Join(timeoutMs);
            _mscThread0 = null;
            _mscThread1 = null;

            // 释放 MSC 预分配缓冲区
            lock (_mscBufferLock0)
            {
                if (_mscBuffer0 != IntPtr.Zero) { Marshal.FreeHGlobal(_mscBuffer0); _mscBuffer0 = IntPtr.Zero; }
            }
            lock (_mscBufferLock1)
            {
                if (_mscBuffer1 != IntPtr.Zero) { Marshal.FreeHGlobal(_mscBuffer1); _mscBuffer1 = IntPtr.Zero; }
            }
        }
    }
}
