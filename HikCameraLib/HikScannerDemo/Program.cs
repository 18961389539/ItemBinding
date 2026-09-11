using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HikScanner;
using HikCam = HikScanner.HikScanner;

namespace HikCameraDemo
{
    class Program
    {
        private static Logger _log = null!;

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 初始化日志：输出到控制台 + 文件
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, $"demo_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _log = new Logger(logFile);

            _log.Info("=== HikScanner 硬触发持续采集 + 内存监控 ===");
            _log.Info($"日志文件: {logFile}");
            _log.Info("");

            try
            {
                Run();
            }
            catch (HikScannerException ex)
            {
                _log.Error($"[SDK错误] {ex.Message}");
            }
            catch (Exception ex)
            {
                _log.Error($"[异常] {ex}");
            }
            finally
            {
                _log.Info("程序结束，日志已关闭。");
                _log.Dispose();
            }
        }

        static void Run()
        {
            // ── 1. 枚举设备 ──
            _log.Info("[1] 枚举 GigE 设备...");
            var devices = HikCam.EnumerateDevices(HikDeviceType.GigE);
            if (devices.Count == 0)
            {
                _log.Info("  未发现 GigE 设备，尝试 USB...");
                devices = HikCam.EnumerateDevices(HikDeviceType.USB);
            }
            if (devices.Count == 0)
            {
                _log.Info("  未发现任何设备，退出");
                return;
            }
            _log.Info($"  发现 {devices.Count} 台设备，使用第一台:");
            var dev = devices[0];
            _log.Info($"  {dev}");
            _log.Info($"  SN:{dev.SerialNumber}  IP:{dev.CurrentIp}  MAC:{dev.MacAddress}");
            _log.Info("");

            // ── 2. 连接设备 ──
            _log.Info("[2] 连接设备...");
            using var cam = new HikCam();
            cam.AutoReconnect = true;
            cam.DeviceDisconnected += (s, e) =>
                _log.Warn($"[{DateTime.Now:HH:mm:ss}] [事件] 设备断开，等待自动重连...");
            cam.DeviceReconnected += (s, e) =>
                _log.Info($"[{DateTime.Now:HH:mm:ss}] [事件] 设备重连成功");

            try
            {
                cam.Connect(dev);
                _log.Info($"  连接成功: {cam.DeviceInfo}");
                _log.Info("");
            }
            catch (HikScannerException ex)
            {
                _log.Error($"  连接失败: {ex.Message}");
                return;
            }

            // ── 3. 设置硬触发模式 ──
            _log.Info("[3] 设置硬触发模式 (TriggerMode=On, TriggerSource=Line0)");
            try
            {
                cam.SetTriggerMode(HikTriggerMode.Trigger);
                cam.SetTriggerSource(HikTriggerSource.Line0);
                _log.Info($"  ExposureTime = {cam.ExposureTime:F1} us");
                _log.Info($"  Gain         = {cam.Gain:F1}");
                _log.Info($"  FrameRate    = {cam.FrameRate:F1} fps");
            }
            catch (HikScannerException ex)
            {
                _log.Error($"  触发模式设置失败: {ex.Message}");
                return;
            }
            _log.Info("");

            // ── 4. 开始持续采集 ──
            _log.Info("[4] 开始持续采集 (硬触发)");
            _log.Info("  请对 Line0 施加触发信号，按 Ctrl+C 停止");
            _log.Info("");

            try
            {
                cam.StartGrabbing();
            }
            catch (HikScannerException ex)
            {
                _log.Error($"  启动采集失败: {ex.Message}");
                return;
            }

            // ── 5. 持续采集 + 内存监控 ──
            int frameCount = 0;
            int barcodeCount = 0;
            int errorCount = 0;
            long totalImageBytes = 0;
            var sw = Stopwatch.StartNew();
            var memSw = Stopwatch.StartNew();

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _running = false;
                _log.Info("[Ctrl+C] 收到停止信号，正在停止...");
            };

            while (_running)
            {
                HikGrabResult result;
                try
                {
                    result = cam.GrabOneFrame(5000);
                }
                catch (HikScannerException ex)
                {
                    Interlocked.Increment(ref errorCount);
                    if (errorCount % 5 == 0)
                        _log.Warn($"[{DateTime.Now:HH:mm:ss}] [警告] 采集异常 #{errorCount}: {ex.Message}");
                    continue;
                }

                if (result.Status == HikGrabStatus.Success)
                {
                    if (result.Image == null) continue;
                    Interlocked.Increment(ref frameCount);
                    Interlocked.Add(ref totalImageBytes, result.Image.RawData?.Length ?? 0);
                    if (result.HasBarcode)
                        Interlocked.Increment(ref barcodeCount);

                    // 每 10 帧输出一次简要日志
                    if (frameCount % 10 == 0)
                    {
                        var img = result.Image;
                        var bc = result.HasBarcode ? result.Barcodes![0].Code : "无";
                        _log.Info($"[{DateTime.Now:HH:mm:ss.fff}] Frame#{frameCount} " +
                            $"{img?.Width}x{img?.Height} {img?.RawData?.Length ?? 0}B | 条码:{bc}");
                    }
                }
                else if (result.Status == HikGrabStatus.Timeout)
                {
                    // 超时不算错误，硬触发未到信号
                }
                else
                {
                    Interlocked.Increment(ref errorCount);
                    if (errorCount % 5 == 0)
                        _log.Warn($"[{DateTime.Now:HH:mm:ss}] [警告] 采集错误 #{errorCount}: {result.Status} (0x{result.RawErrorCode:X8})");
                }

                // 每 5 秒输出内存监控日志
                if (memSw.ElapsedMilliseconds >= 5000)
                {
                    PrintMemoryStats(frameCount, barcodeCount, errorCount, totalImageBytes, sw.Elapsed.TotalSeconds);
                    memSw.Restart();
                }
            }

            // ── 6. 停止 + 汇总 ──
            _log.Info("");
            _log.Info("=== 采集汇总 ===");
            try { cam.StopGrabbing(); }
            catch (Exception ex) { _log.Error($"  停止采集异常: {ex.Message}"); }
            sw.Stop();
            double elapsed = sw.Elapsed.TotalSeconds;

            _log.Info($"  总时长:     {elapsed:F1} 秒");
            _log.Info($"  总帧数:     {frameCount}");
            _log.Info($"  条码数:     {barcodeCount}");
            _log.Info($"  错误数:     {errorCount}");
            _log.Info($"  平均 FPS:   {(elapsed > 0 ? frameCount / elapsed : 0):F2}");
            _log.Info($"  图像总量:   {totalImageBytes / 1024.0 / 1024.0:F2} MB");
            PrintMemoryStats(frameCount, barcodeCount, errorCount, totalImageBytes, elapsed);

            _log.Info("  已断开，再见!");
        }

        private static volatile bool _running = true;

        /// <summary>输出内存消耗统计</summary>
        private static void PrintMemoryStats(int frames, int barcodes, int errors, long imgBytes, double elapsedSec)
        {
            var proc = Process.GetCurrentProcess();
            var gcMem = GC.GetTotalMemory(false);
            var workingSet = proc.WorkingSet64;
            var privateMem = proc.PrivateMemorySize64;
            var managedMb = gcMem / 1024.0 / 1024.0;
            var workingMb = workingSet / 1024.0 / 1024.0;
            var privateMb = privateMem / 1024.0 / 1024.0;
            var imgMb = imgBytes / 1024.0 / 1024.0;
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            var fps = elapsedSec > 0 ? frames / elapsedSec : 0;

            _log.Info($"  ── 内存监控 [{DateTime.Now:HH:mm:ss}] ──", ConsoleColor.DarkCyan);
            _log.Info($"  托管堆内存:   {managedMb,8:F2} MB | 工作集: {workingMb,8:F2} MB | 私有内存: {privateMb,8:F2} MB", ConsoleColor.DarkCyan);
            _log.Info($"  GC Gen0/1/2:  {gen0}/{gen1}/{gen2} | 图像累计: {imgMb,8:F2} MB | 帧数: {frames} | 条码: {barcodes} | 错误: {errors} | FPS: {fps:F1}", ConsoleColor.DarkCyan);
        }
    }

    /// <summary>
    /// 日志助手：同时输出到控制台（带颜色）和文件（带时间戳），支持多线程安全写入。
    /// </summary>
    sealed class Logger : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new();
        private bool _disposed;

        public Logger(string filePath)
        {
            _writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
        }

        public void Info(string msg, ConsoleColor color = ConsoleColor.Gray)
        {
            Write("INFO ", msg, color);
        }

        public void Warn(string msg)
        {
            Write("WARN ", msg, ConsoleColor.Yellow);
        }

        public void Error(string msg)
        {
            Write("ERROR", msg, ConsoleColor.Red);
        }

        private void Write(string level, string msg, ConsoleColor color)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {msg}";
            lock (_lock)
            {
                if (_disposed) return;
                // 控制台输出（带颜色）
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ResetColor();
                // 文件输出
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _writer?.Dispose();
            }
        }
    }
}
