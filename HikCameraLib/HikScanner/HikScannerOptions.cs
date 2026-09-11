using System;
using Microsoft.Extensions.Logging;

namespace HikScanner
{
    /// <summary>
    /// HikScanner 配置选项，连接前一次性设置。
    /// </summary>
    public class HikScannerOptions
    {
        /// <summary>是否启用断线自动重连(仅回调采集模式生效)</summary>
        public bool AutoReconnect { get; set; }

        /// <summary>重连初始间隔毫秒数，默认 2000。启用指数退避后作为基础间隔。</summary>
        public int ReconnectIntervalMs { get; set; } = 2000;

        /// <summary>最大重连尝试次数，0=无限重试（默认）</summary>
        public int MaxReconnectAttempts { get; set; } = 0;

        /// <summary>是否启用指数退避策略，默认 true。每次重连间隔翻倍，直到 MaxReconnectIntervalMs。</summary>
        public bool ExponentialBackoff { get; set; } = true;

        /// <summary>指数退避时的最大间隔毫秒数</summary>
        public int MaxReconnectIntervalMs { get; set; } = 30000;

        /// <summary>重连前初始等待毫秒数，默认 500</summary>
        public int ReconnectInitialDelayMs { get; set; } = 500;

        /// <summary>采集超时毫秒数，默认 1000</summary>
        public int DefaultTimeoutMs { get; set; } = 1000;

        /// <summary>单帧图像最大字节数，超过则返回 BufferOverflow。默认 100MB，可根据相机分辨率调整。</summary>
        public long MaxFrameSizeBytes { get; set; } = 100 * 1024 * 1024;

        /// <summary>采集模式</summary>
        public HikGrabMode GrabMode { get; set; } = HikGrabMode.Polling;

        /// <summary>结构化日志记录器，默认 null</summary>
        public ILogger Logger { get; set; }

        /// <summary>设备断开回调</summary>
        public Action OnDeviceDisconnected { get; set; }

        /// <summary>设备重连成功回调</summary>
        public Action OnDeviceReconnected { get; set; }

        /// <summary>帧数据回调(回调模式/MSC模式下使用)</summary>
        public Action<HikGrabResult> OnFrameGrabbed { get; set; }

        /// <summary>应用配置到相机实例</summary>
        internal void ApplyTo(HikScanner camera)
        {
            camera.AutoReconnect = AutoReconnect;
            camera.ReconnectIntervalMs = ReconnectIntervalMs;
            camera.MaxReconnectAttempts = MaxReconnectAttempts;
            camera.ExponentialBackoff = ExponentialBackoff;
            camera.MaxReconnectIntervalMs = MaxReconnectIntervalMs;
            camera.ReconnectInitialDelayMs = ReconnectInitialDelayMs;
            camera.MaxFrameSizeBytes = MaxFrameSizeBytes;
            camera.DefaultTimeoutMs = DefaultTimeoutMs;

            // #13: 应用 Logger 和 DefaultTimeoutMs
            if (Logger != null) camera.Logger = Logger;

            // #5: 移除旧的事件处理器，避免重复调用时叠加
            if (camera._appliedDisconnectedHandler != null)
                camera.DeviceDisconnected -= camera._appliedDisconnectedHandler;
            if (camera._appliedReconnectedHandler != null)
                camera.DeviceReconnected -= camera._appliedReconnectedHandler;
            if (camera._appliedFrameGrabbedHandler != null)
                camera.ImageGrabbed -= camera._appliedFrameGrabbedHandler;

            // 重新注册
            if (OnDeviceDisconnected != null)
            {
                camera._appliedDisconnectedHandler = (_, _) => OnDeviceDisconnected();
                camera.DeviceDisconnected += camera._appliedDisconnectedHandler;
            }
            if (OnDeviceReconnected != null)
            {
                camera._appliedReconnectedHandler = (_, _) => OnDeviceReconnected();
                camera.DeviceReconnected += camera._appliedReconnectedHandler;
            }
            if (OnFrameGrabbed != null)
            {
                camera._appliedFrameGrabbedHandler = (_, r) => OnFrameGrabbed(r);
                camera.ImageGrabbed += camera._appliedFrameGrabbedHandler;
            }
        }
    }
}
