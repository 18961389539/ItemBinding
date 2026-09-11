using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace HikScanner
{
    /// <summary>
    /// 多相机管理器，支持同时管理多个海康读码器实例。
    /// </summary>
    public class ScannerManager : IDisposable
    {
        // #7: 使用 lock 保护 _cameras，防止多线程并发操作抛 InvalidOperationException
        private readonly List<HikScanner> _cameras = new List<HikScanner>();
        private readonly object _listLock = new();
        private bool _disposed;

        /// <summary>结构化日志记录器，新创建的相机将自动继承此 Logger</summary>
        public ILogger Logger { get; set; }

        /// <summary>当前管理的相机列表</summary>
        public IReadOnlyList<HikScanner> Cameras { get { lock (_listLock) return _cameras.AsReadOnly(); } }

        /// <summary>相机数量</summary>
        public int Count { get { lock (_listLock) return _cameras.Count; } }

        /// <summary>索引访问</summary>
        public HikScanner this[int index] { get { lock (_listLock) { return index >= 0 && index < _cameras.Count ? _cameras[index] : throw new ArgumentOutOfRangeException(nameof(index), $"索引 {index} 超出范围 (0-{Math.Max(0, _cameras.Count - 1)})"); } } }

        /// <summary>
        /// 枚举设备并连接所有找到的设备，返回已连接的管理器。
        /// </summary>
        /// <param name="deviceType">设备类型（GigE/USB）</param>
        /// <param name="maxCount">最多连接设备数，0=不限</param>
        /// <returns>已连接设备的管理器实例</returns>
        public static ScannerManager Create(HikDeviceType deviceType, int maxCount = 0)
        {
            var manager = new ScannerManager();
            manager.ConnectAll(maxCount > 0 ? maxCount : int.MaxValue, deviceType);
            return manager;
        }

        /// <summary>
        /// 枚举设备但不连接，返回设备信息列表供调用方自行连接。
        /// </summary>
        public static List<HikDeviceInfo> EnumerateDevices(HikDeviceType deviceType)
        {
            return HikScanner.EnumerateDevices(deviceType);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ScannerManager));
        }

        /// <summary>#7: 获取 _cameras 的线程安全快照</summary>
        private HikScanner[] GetSnapshot() { lock (_listLock) return _cameras.ToArray(); }

        /// <summary>#15: 安全获取指定索引的相机，不抛异常</summary>
        public bool TryGetCamera(int index, out HikScanner camera)
        {
            lock (_listLock)
            {
                if (index >= 0 && index < _cameras.Count)
                {
                    camera = _cameras[index];
                    return true;
                }
            }
            camera = null;
            return false;
        }

        /// <summary>
        /// 连接到所有枚举到的设备(最多maxCount个)
        /// </summary>
        public int ConnectAll(int maxCount = 4, HikDeviceType deviceType = HikDeviceType.GigE)
        {
            ThrowIfDisposed();
            var devices = HikScanner.EnumerateDevices(deviceType);
            int connected = 0;

            foreach (var dev in devices.Take(maxCount))
            {
                HikScanner camera = null;
                try
                {
                    camera = new HikScanner();
                    if (Logger != null) camera.Logger = Logger;
                    camera.Connect(dev);
                    lock (_listLock) _cameras.Add(camera);
                    connected++;
                }
                catch (Exception ex)
                {
                    // REVIEW-FIX: Connect 抛异常时释放新建的 HikScanner 实例，防止 SDK 句柄泄漏
                    camera?.Dispose();
                    // #7: 直接用 Logger 属性，不借用 firstCam.Log
                    if (Logger != null)
                        Logger.LogWarning(ex, "[ScannerManager.ConnectAll] 设备 {Device} 连接失败: {Message}", dev, ex.Message);
                    else
                        System.Diagnostics.Debug.WriteLine($"[ScannerManager.ConnectAll] 设备 {dev} 连接失败: {ex.Message}");
                }
            }
            return connected;
        }

        /// <summary>
        /// 所有相机同时开始采集(轮询模式)
        /// </summary>
        public void StartGrabbingAll()
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected && !cam.IsGrabbing)
                    cam.StartGrabbing();
            }
        }

        /// <summary>
        /// 所有相机同时开始采集(回调模式)
        /// </summary>
        public void StartGrabbingWithCallbackAll()
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected && !cam.IsGrabbing)
                    cam.StartGrabbingWithCallback();
            }
        }

        /// <summary>
        /// 所有相机同时开始 MSC 双通道采集
        /// </summary>
        /// <param name="onFrameGrabbed">帧回调</param>
        /// <param name="timeoutMs">单帧超时</param>
        public void StartMscDualChannelGrabAll(Action<HikGrabResult> onFrameGrabbed, int timeoutMs = 1000)
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected && !cam.IsGrabbing)
                    cam.StartMscDualChannelGrab(onFrameGrabbed, timeoutMs);
            }
        }

        /// <summary>
        /// 所有相机同时停止采集
        /// </summary>
        public void StopGrabbingAll()
        {
            foreach (var cam in GetSnapshot())
            {
                try { cam.StopGrabbing(); }
                catch (ObjectDisposedException) { /* 已释放，跳过 */ }
            }
        }

        /// <summary>
        /// 所有相机同时执行软触发
        /// </summary>
        public void TriggerSoftwareAll()
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsGrabbing)
                    cam.TriggerSoftware();
            }
        }

        /// <summary>
        /// 为所有相机设置相同参数
        /// </summary>
        public void SetFloatParamAll(string key, float value)
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected)
                    cam.SetFloatParam(key, value);
            }
        }

        /// <summary>
        /// 为所有相机设置触发模式
        /// </summary>
        /// <param name="mode">触发模式</param>
        public void SetTriggerModeAll(HikTriggerMode mode)
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected)
                    cam.SetTriggerMode(mode);
            }
        }

        /// <summary>
        /// 为所有相机设置触发源
        /// </summary>
        /// <param name="source">触发源</param>
        public void SetTriggerSourceAll(HikTriggerSource source)
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected)
                    cam.SetTriggerSource(source);
            }
        }

        /// <summary>
        /// 对所有断线的相机触发自动重连（要求 AutoReconnect 已启用）
        /// </summary>
        public void ReconnectAll()
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                try
                {
                    if (!cam.IsConnected && cam.AutoReconnect && cam.DeviceInfo != null)
                        cam.Reconnect();
                }
                catch (Exception ex)
                {
                    if (Logger != null)
                        Logger.LogWarning(ex, "[ScannerManager.ReconnectAll] 重连失败");
                    else
                        System.Diagnostics.Debug.WriteLine($"[ScannerManager.ReconnectAll] 重连失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 设置所有相机的曝光时间（使用便捷属性，支持重连恢复）
        /// </summary>
        public void SetExposureTimeAll(float exposureTime)
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected)
                    cam.ExposureTime = exposureTime;
            }
        }

        /// <summary>
        /// 设置所有相机的增益（使用便捷属性，支持重连恢复）
        /// </summary>
        public void SetGainAll(float gain)
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected)
                    cam.Gain = gain;
            }
        }

        /// <summary>
        /// 设置所有相机的帧率（使用便捷属性，支持重连恢复）
        /// </summary>
        public void SetFrameRateAll(float frameRate)
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                if (cam.IsConnected)
                    cam.FrameRate = frameRate;
            }
        }

        /// <summary>
        /// 断开所有相机并清空列表。
        /// </summary>
        public void DisconnectAll()
        {
            DisconnectAll(false);
        }

        /// <summary>
        /// 断开所有相机。
        /// </summary>
        /// <param name="keepInstances">true 保留相机实例在列表中（支持后续重连）；false 清空列表（默认）</param>
        public void DisconnectAll(bool keepInstances)
        {
            foreach (var cam in GetSnapshot())
            {
                try { cam.Disconnect(); }
                catch (ObjectDisposedException) { /* 已释放，跳过 */ }
            }
            if (!keepInstances) { lock (_listLock) _cameras.Clear(); }
        }

        /// <summary>
        /// 订阅所有相机的图像事件到一个处理器
        /// </summary>
        public void SubscribeAllImageEvents(EventHandler<HikGrabResult> handler)
        {
            ThrowIfDisposed();
            foreach (var cam in GetSnapshot())
            {
                cam.ImageGrabbed += handler;
            }
        }

        /// <summary>
        /// 取消订阅所有相机的图像事件
        /// </summary>
        public void UnsubscribeAllImageEvents(EventHandler<HikGrabResult> handler)
        {
            foreach (var cam in GetSnapshot())
            {
                try { cam.ImageGrabbed -= handler; }
                catch (ObjectDisposedException) { /* 已释放，跳过 */ }
            }
        }

        /// <summary>
        /// 按序列号列表连接指定设备。
        /// </summary>
        /// <param name="serialNumbers">要连接的设备序列号列表</param>
        /// <returns>成功连接的设备数</returns>
        public int ConnectBySerialNumbers(string[] serialNumbers)
        {
            ThrowIfDisposed();
            if (serialNumbers == null) throw new ArgumentNullException(nameof(serialNumbers));
            int connected = 0;

            foreach (var sn in serialNumbers)
            {
                if (string.IsNullOrEmpty(sn)) continue;
                HikScanner camera = null;
                try
                {
                    camera = new HikScanner();
                    if (Logger != null) camera.Logger = Logger;
                    camera.ConnectBySerialNumber(sn);
                    lock (_listLock) _cameras.Add(camera);
                    connected++;
                }
                catch (Exception ex)
                {
                    // REVIEW-FIX: Connect 抛异常时释放新建的 HikScanner 实例，防止 SDK 句柄泄漏
                    // （与 ConnectAll 的修复保持一致）。
                    camera?.Dispose();
                    if (Logger != null)
                        Logger.LogWarning(ex, "[ScannerManager.ConnectBySerialNumbers] 序列号 {Serial} 连接失败", sn);
                    else
                        System.Diagnostics.Debug.WriteLine($"[ScannerManager.ConnectBySerialNumbers] 序列号 {sn} 连接失败: {ex.Message}");
                }
            }
            return connected;
        }

        /// <summary>
        /// 按序列号查找已管理的相机实例。
        /// </summary>
        /// <param name="serialNumber">设备序列号</param>
        /// <returns>匹配的相机实例，未找到返回 null</returns>
        public HikScanner GetCameraBySerialNumber(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber)) return null;
            foreach (var cam in GetSnapshot())
            {
                try
                {
                    if (cam.DeviceInfo?.SerialNumber == serialNumber)
                        return cam;
                }
                catch (ObjectDisposedException) { /* 已释放，跳过 */ }
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // #10: 并行释放所有相机，避免多设备串行等待（每个最多 5 秒）
            if (_cameras.Count <= 1)
            {
                foreach (var cam in GetSnapshot())
                {
                    try { cam.Dispose(); }
                    catch (ObjectDisposedException) { /* 已释放，跳过 */ }
                }
            }
            else
            {
                var exceptions = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
                // #9: 使用 GetSnapshot() 替代直接遍历 _cameras，保持一致性
                System.Threading.Tasks.Parallel.ForEach(GetSnapshot(), cam =>
                {
                    try { cam.Dispose(); }
                    catch (ObjectDisposedException) { /* 已释放，跳过 */ }
                    catch (Exception ex) { exceptions.Enqueue(ex); }
                });
            }
            lock (_listLock) _cameras.Clear();
        }
    }
}
