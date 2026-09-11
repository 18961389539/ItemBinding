using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MvCodeReaderSDKNet;

namespace HikScanner
{
    /// <summary>断线重连</summary>
    public partial class HikScanner
    {
        /// <summary>SDK 异常回调：设备断线时触发重连流程</summary>
        private void OnExceptionCallback(uint nMsgType, IntPtr pUser)
        {
            // #4: Dispose 后不再处理回调，防止修改已释放对象的状态
            if (_disposed) return;
            if (nMsgType != MvCodeReader.MV_CODEREADER_EXCEPTION_DEV_DISCONNECT) return;
            // #5: 重连进行中不重复触发断线流程
            if (_isReconnecting) return;
            OnDeviceDisconnectedInternal();
            if (_autoReconnect && _currentDeviceInfo != null) StartReconnect();
        }

        /// <summary>手动触发重连（要求设备已断开且有设备信息）</summary>
        public void Reconnect()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HikScanner));
            if (_isConnected) throw new InvalidOperationException("设备已连接，无需重连");
            if (_currentDeviceInfo == null) throw new InvalidOperationException("无设备信息，无法重连");
            StartReconnect();
        }

        /// <summary>启动自动重连任务（指数退避）</summary>
        private void StartReconnect()
        {
            // #1/#10: 加锁保护 _reconnectCts，Dispose/Cancel 并发安全
            CancellationTokenSource oldCts;
            oldCts = Interlocked.Exchange(ref _reconnectCts, new CancellationTokenSource());
            // #1: Dispose 旧 CTS，防止反复触发重连导致 CTS 内存泄漏
            if (oldCts != null) { oldCts.Cancel(); oldCts.Dispose(); }
            CancellationToken token;
            token = _reconnectCts.Token;

            _isReconnecting = true;
            OnReconnectingInternal();

            Task.Run(() =>
            {
                // #14: 初始延迟，使用可配置的 ReconnectInitialDelayMs
                // #3: 使用 WaitHandle.WaitOne 响应取消令牌
                if (token.WaitHandle.WaitOne(_reconnectInitialDelayMs)) { _isReconnecting = false; return; }
                int attempt = 0;
                int currentInterval = _reconnectIntervalMs;

                while (!token.IsCancellationRequested && !_isConnected)
                {
                    attempt++;

                    // 检查最大重试次数
                    if (MaxReconnectAttempts > 0 && attempt > MaxReconnectAttempts)
                    {
                        Log(LogLevel.Warning, $"[Reconnect] 已达最大重试次数 {MaxReconnectAttempts}，停止重连");
                        // #10: 达到最大重试次数时触发 ReconnectError 事件
                        OnReconnectError("最大重试次数", MvCodeReader.MV_CODEREADER_E_NODATA);
                        _isReconnecting = false;
                        break;
                    }

                    try
                    {
                        _device?.MV_CODEREADER_DestroyHandle_NET();
                        _device = new CodeReaderSdkAdapter();

                        var rawInfo = _currentDeviceInfo.RawDeviceInfo;
                        int nRet = _device.MV_CODEREADER_CreateHandle_NET(ref rawInfo);
                        if (nRet != MvCodeReader.MV_CODEREADER_OK)
                        {
                            // #8: 失败时置 _device = null，确保下一次循环创建全新实例
                            _device = null;
                            OnReconnectError("创建句柄", nRet);
                            currentInterval = GetNextBackoffInterval(attempt);
                            // #1: 使用 WaitHandle.WaitOne 响应取消令牌
                            if (token.WaitHandle.WaitOne(currentInterval)) break;
                            continue;
                        }

                        nRet = _device.MV_CODEREADER_OpenDevice_NET();
                        if (nRet == MvCodeReader.MV_CODEREADER_OK)
                        {
                            // #3: 检查 TriggerMode 返回值，与 Connect 一致
                            int triggerRet = _device.MV_CODEREADER_SetEnumValue_NET("TriggerMode",
                                (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_OFF);
                            if (triggerRet != MvCodeReader.MV_CODEREADER_OK)
                                Log(LogLevel.Warning, $"[Reconnect] 关闭触发模式失败: 0x{triggerRet:X8}");

                            // 重新注册回调
                            _imageCallback = new MvCodeReader.cbOutputEx2delegate(OnImageCallback);
                            // #2: 检查回调注册返回值
                            int regRet = _device.MV_CODEREADER_RegisterImageCallBackEx2_NET(_imageCallback, IntPtr.Zero);
                            if (regRet != MvCodeReader.MV_CODEREADER_OK)
                            {
                                Log(LogLevel.Error, $"[Reconnect] 注册图像回调失败: 0x{regRet:X8}");
                                _device.MV_CODEREADER_DestroyHandle_NET();
                                OnReconnectError("注册回调", regRet);
                                currentInterval = GetNextBackoffInterval(attempt);
                                if (token.WaitHandle.WaitOne(currentInterval)) break;
                                continue;
                            }

                            _exceptionCallback = new MvCodeReader.cbExceptiondelegate(OnExceptionCallback);
                            // #8: 检查异常回调注册返回值
                            int excRet = _device.MV_CODEREADER_RegisterExceptionCallBack_NET(_exceptionCallback, IntPtr.Zero);
                            if (excRet != MvCodeReader.MV_CODEREADER_OK)
                            {
                                Log(LogLevel.Warning, $"[Reconnect] 注册异常回调失败: 0x{excRet:X8}，自动重连将不可用");
                            }
                            GC.KeepAlive(_exceptionCallback);

                            // #2: 检查开始采集返回值
                            int grabRet = _device.MV_CODEREADER_StartGrabbing_NET();
                            if (grabRet != MvCodeReader.MV_CODEREADER_OK)
                            {
                                Log(LogLevel.Error, $"[Reconnect] 开始采集失败: 0x{grabRet:X8}");
                                _device.MV_CODEREADER_DestroyHandle_NET();
                                OnReconnectError("开始采集", grabRet);
                                currentInterval = GetNextBackoffInterval(attempt);
                                if (token.WaitHandle.WaitOne(currentInterval)) break;
                                continue;
                            }

                            // #1/#9: 先设置 _isConnected/_isGrabbing = true，再恢复采集模式
                            // 否则 Polling/MSC 线程的 while(_isGrabbing) 循环会立即退出
                            // #3: 先恢复用户参数配置
                            RestoreParamsAfterReconnect();

                            lock (_grabLock)
                            {
                                _isConnected = true;
                                _isGrabbing = true;
                            }
                            _isReconnecting = false;
                            OnDeviceReconnectedInternal();

                            // #6: 恢复采集模式（此时 _isGrabbing 已为 true，线程可正常运行）
                            // #9: 单独 try-catch，防止 RestoreGrabModeAfterReconnect 异常导致 _device=null 资源泄漏
                            try
                            {
                                RestoreGrabModeAfterReconnect();
                            }
                            catch (Exception ex)
                            {
                                Log(LogLevel.Warning, "[Reconnect] 恢复采集模式失败，设备已连接但采集未恢复", ex);
                            }

                            Log(LogLevel.Information, $"[Reconnect] 第 {attempt} 次重连成功");
                            break;
                        }

                        _device.MV_CODEREADER_DestroyHandle_NET();
                        // #2: 失败后置 _device = null，确保下一次循环创建全新实例
                        _device = null;
                        OnReconnectError("打开设备", nRet);
                        currentInterval = GetNextBackoffInterval(attempt);
                        // #1: 使用 WaitHandle.WaitOne 响应取消令牌
                        if (token.WaitHandle.WaitOne(currentInterval)) break;
                    }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Error, $"[Reconnect] 第 {attempt} 次重连异常", ex);
                        // #5: 异常时清理 _device，确保下一次循环创建全新实例
                        _device = null;
                        currentInterval = GetNextBackoffInterval(attempt);
                        // #1: 使用 WaitHandle.WaitOne 响应取消令牌
                        if (token.WaitHandle.WaitOne(currentInterval)) break;
                    }
                }
            }, token);
        }

        /// <summary>#6: 重连成功后恢复采集模式</summary>
        private void RestoreGrabModeAfterReconnect()
        {
            // #1: 重连后重建 Channel
            StartImageChannel();
            try
            {
                switch (_activeGrabMode)
                {
                    case HikGrabMode.Polling:
                        // Polling 模式需要重建轮询循环（#2: 使用保存的 timeout 值）
                        int pollingTimeout = _grabLoopTimeoutMs;
                        // #6: 覆盖前 Cancel/Dispose 旧 CTS，防止泄漏
                        var newCts = new CancellationTokenSource();
                        CancellationTokenSource oldCts;
                        oldCts = Interlocked.Exchange(ref _grabCts, newCts);
                        // REVIEW-FIX: 旧 CTS 只 Cancel 不 Dispose，由旧采集循环任务结束后兜底释放，避免 use-after-dispose
                        if (oldCts != null) { try { oldCts.Cancel(); } catch (ObjectDisposedException) { } }
                        var pollingTask = Task.Run(() =>
                        {
                            // REVIEW-FIX: 采集循环改用 token 结构体副本，避免释放 CTS 后的 use-after-dispose
                            var token = newCts.Token;
                            while (!token.IsCancellationRequested && _isGrabbing)
                            {
                                try
                                {
                                    // #2: 调用前检查 _isConnected，断线后不访问已断开的 _device
                                    if (!_isConnected || _disposed) break;
                                    var result = GrabOneFrameInternal((uint)pollingTimeout);
                                    if (result != null) OnImageGrabbed(result);
                                }
                                catch (Exception ex)
                                {
                                    Log(LogLevel.Error, "[Reconnect] Polling 采集异常", ex);
                                    // #1: 设备断线时停止循环，避免无限重试空转
                                    if (!_isConnected || _disposed) break;
                                    // #1: 使用 WaitHandle.WaitOne 响应取消令牌，与 StartGrabbingLoop 一致
                                    try { if (token.WaitHandle.WaitOne(100)) break; }
                                    catch (ObjectDisposedException) { break; }
                                }
                            }
                        }, newCts.Token);
                        // REVIEW-FIX: 采集循环是 CTS 最后的持有者，任务结束后兜底释放
                        pollingTask.ContinueWith(t =>
                        {
                            Interlocked.CompareExchange(ref _grabCts, null, newCts);
                            try { newCts.Dispose(); } catch (ObjectDisposedException) { }
                        }, TaskScheduler.Default);
                        Log(LogLevel.Information, "[Reconnect] Polling 模式已恢复");
                        break;

                    case HikGrabMode.Callback:
                        // Callback 模式：回调已在重连流程中注册，无需额外操作
                        Log(LogLevel.Information, "[Reconnect] Callback 模式已恢复");
                        break;

                    case HikGrabMode.MscDualChannel:
                        // #5: 检查 _mscCallback null，防止静默丢弃帧
                        if (_mscCallback == null)
                        {
                            Log(LogLevel.Warning, "[Reconnect] MSC 回调为 null，无法恢复 MSC 双通道模式");
                            break;
                        }
                        // #9: MSC 恢复持锁，防止 Dispose 并发竞态
                        lock (_grabLock)
                        {
                            _mscThread0 = CreateMscGrabThread(0, _mscCallback, 1000);
                            _mscThread1 = CreateMscGrabThread(1, _mscCallback, 1000);
                            _mscThread0.Start();
                            _mscThread1.Start();
                        }
                        Log(LogLevel.Information, "[Reconnect] MSC 双通道模式已恢复");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "[Reconnect] 恢复采集模式失败", ex);
            }
        }

        /// <summary>计算下一次重连间隔（指数退避）</summary>
        private int GetNextBackoffInterval(int attempt)
        {
            if (!ExponentialBackoff) return _reconnectIntervalMs;
            long val = _reconnectIntervalMs * (1L << Math.Min(attempt - 1, 30));
            return (int)Math.Min(val, MaxReconnectIntervalMs);
        }

        /// <summary>#3: 重连成功后恢复用户参数配置</summary>
        /// <remarks>#15: 此方法在 _isConnected=true 之前调用，因此只能直接调 _device 的 SDK 方法，
        /// 不能调 public 方法（如 SetTriggerMode），因为 public 方法会调 EnsureConnected 检查 _isConnected。</remarks>
        private void RestoreParamsAfterReconnect()
        {
            if (!_hasSavedParams) return;
            try
            {
                // #3: 检查每个 SDK 返回值，失败时记录日志但不中断恢复流程
                if (_savedExposure >= 0f)
                {
                    int r1 = _device.MV_CODEREADER_SetEnumValue_NET("ExposureAuto", 0u);
                    int r2 = _device.MV_CODEREADER_SetFloatValue_NET("ExposureTime", _savedExposure);
                    if (r1 != MvCodeReader.MV_CODEREADER_OK) Log(LogLevel.Warning, $"[Reconnect] 恢复 ExposureAuto 失败: 0x{r1:X8}");
                    if (r2 != MvCodeReader.MV_CODEREADER_OK) Log(LogLevel.Warning, $"[Reconnect] 恢复 ExposureTime 失败: 0x{r2:X8}");
                }
                if (_savedGain >= 0f)
                {
                    int r1 = _device.MV_CODEREADER_SetEnumValue_NET("GainAuto", 0u);
                    int r2 = _device.MV_CODEREADER_SetFloatValue_NET("Gain", _savedGain);
                    if (r1 != MvCodeReader.MV_CODEREADER_OK) Log(LogLevel.Warning, $"[Reconnect] 恢复 GainAuto 失败: 0x{r1:X8}");
                    if (r2 != MvCodeReader.MV_CODEREADER_OK) Log(LogLevel.Warning, $"[Reconnect] 恢复 Gain 失败: 0x{r2:X8}");
                }
                if (_savedFrameRate >= 0f)
                {
                    int r = _device.MV_CODEREADER_SetFloatValue_NET("AcquisitionFrameRate", _savedFrameRate);
                    if (r != MvCodeReader.MV_CODEREADER_OK) Log(LogLevel.Warning, $"[Reconnect] 恢复 FrameRate 失败: 0x{r:X8}");
                }
                {
                    int r = _device.MV_CODEREADER_SetEnumValue_NET("TriggerMode", (uint)_savedTriggerMode);
                    if (r != MvCodeReader.MV_CODEREADER_OK) Log(LogLevel.Warning, $"[Reconnect] 恢复 TriggerMode 失败: 0x{r:X8}");
                }
                {
                    int r = _device.MV_CODEREADER_SetEnumValue_NET("TriggerSource", (uint)_savedTriggerSource);
                    if (r != MvCodeReader.MV_CODEREADER_OK) Log(LogLevel.Warning, $"[Reconnect] 恢复 TriggerSource 失败: 0x{r:X8}");
                }
                {
                    int r = _device.MV_CODEREADER_SetWayBillEnable_NET(_savedWayBillEnable);
                    if (r != MvCodeReader.MV_CODEREADER_OK) Log(LogLevel.Warning, $"[Reconnect] 恢复 WayBillEnable 失败: 0x{r:X8}");
                }
                Log(LogLevel.Information, "[Reconnect] 用户参数已恢复");
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "[Reconnect] 恢复用户参数失败", ex);
            }
        }

        /// <summary>重连步骤失败时触发事件，携带具体错误码供外部诊断</summary>
        private void OnReconnectError(string step, int errorCode)
        {
            var e = new HikReconnectErrorEventArgs
            {
                Step = step,
                ErrorCode = errorCode,
                ErrorDescription = HikScannerException.GetErrorDescription(errorCode)
            };
            EventHandler<HikReconnectErrorEventArgs> handler;
            lock (_eventLock) { handler = ReconnectError; }
            handler?.Invoke(this, e);
        }
    }
}
