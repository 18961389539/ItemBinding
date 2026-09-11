using MainAPP.Services;
using OpenCvSharp;
using OpenCvSharp.XImgProc;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using HikScanner;
using HikScannerType = HikScanner.HikScanner;

namespace MainAPP.Devices
{
    /// <summary>
    /// 扫码枪设备管理静态类。
    /// 负责枚举、初始化和打开海康威视工业读码器设备，
    /// 并将其切换到硬触发模式（由外部信号触发采集）。
    /// </summary>
    public static class Scanners
    {
        // REVIEW-FIX: 静态可变设备实例改为 volatile 字段 + 只读属性。原实现为普通静态属性，
        // Initialize 在后台线程执行 Dispose+重建，检测线程同步读取可能拿到已 Dispose 的旧实例
        // 或读到半初始化状态（内存可见性问题）。volatile 保证跨线程发布可见；读取侧仍应采用
        // "先取局部引用再使用"模式（现有调用点均已如此）。
        private static volatile HikScannerType? _hikScaner;
        private static volatile bool _hasScanners;

        /// <summary>
        /// 海康威视扫码枪设备实例，初始化成功后可用
        /// </summary>
        public static HikScannerType? HikScaner => _hikScaner;

        /// <summary>
        /// 枚举设备时的最大重试次数
        /// </summary>
        const int maxTryCount = 5;

        /// <summary>
        /// 是否检测到扫码枪设备
        /// </summary>
        public static bool HasScanners => _hasScanners;

        static Scanners()
        {
        }

        /// <summary>
        /// 异步初始化扫码枪设备。
        /// 枚举所有可用的海康威视读码器，取第一个设备打开并切换到硬触发模式。
        /// 如果未检测到设备，会重试最多 maxTryCount 次（每次间隔1秒）。
        /// </summary>
        /// <param name="cancellationToken">用于在应用退出时取消等待</param>
        public static async Task Initialize(CancellationToken cancellationToken = default)
        {
            int hasTryCount = 0;
            while (true)
            {
                hasTryCount++;
                // 枚举所有可用的海康威视读码器设备
                var scanners = HikScannerType.EnumerateDevices(HikDeviceType.GigE);
                if (scanners.Count == 0)
                {
                    if (hasTryCount > maxTryCount)
                    {
                        break;
                    }
                    _hasScanners = false;
                    // L22: 传入 token 以便取消时立即退出重试等待
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }
                _hasScanners = true;
                // 记录找到的所有设备IP
                string tips = string.Empty;
                foreach (var item in scanners)
                {
                    tips += (item.CurrentIp ?? string.Empty) + "\n";
                }
                // 使用第一个设备，打开并切换到硬触发模式
                _hikScaner?.Dispose();
                _hikScaner = new HikScannerType();
                try
                {
                    // 启用自动重连：网络抖动或设备重启后自动恢复，避免必须重启程序
                    _hikScaner.AutoReconnect = true;
                    _hikScaner.ReconnectIntervalMs = 2000;
                    _hikScaner.MaxReconnectAttempts = 0; // 0 表示无限重试
                    // VSTHRD103: 使用 ConnectAsync 避免同步阻塞（内部用 Task.Run 包装 Connect）
                    await _hikScaner.ConnectAsync(scanners[0]).ConfigureAwait(false);
                    _hikScaner.SwitchToHardwareTrigger();
                    LogService.Instance.Info($"找到扫码枪:\n{tips}");
                    break; // 连接成功，退出重试循环
                }
                catch (Exception ex)
                {
                    // 连接失败时清理实例，避免持有未连接的设备对象
                    LogService.Instance.Error($"连接扫码枪失败(第{hasTryCount}次): {ex}");
                    _hikScaner?.Dispose();
                    _hikScaner = null;
                    _hasScanners = false;
                    // H12: 连接失败时重试，而非直接放弃
                    if (hasTryCount > maxTryCount)
                    {
                        break;
                    }
                    // L22: 传入 token 以便取消时立即退出重试等待
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }
            }
            if (!_hasScanners)
            {
                LogService.Instance.Warning("未检测到扫码器设备！");
            }
        }
    }
}
