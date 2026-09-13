using MainAPP.Models;
using MainAPP.ViewModels;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// 相机远程调试 Web 宿主（进程内，零新增框架依赖）。
    ///
    /// 背景：原 WebLiveView 是独立 Blazor 进程，自己 <c>new HikScanner</c> 连接同一台 GigE 读码器，
    /// 与 MainAPP 争抢设备，且逐像素 <c>Bitmap.SetPixel</c> 转 JPEG、帧率写死 5 FPS。
    /// 本项目移除该工程后，把「手机实时看图 + 调参」能力以最小实现并入 MainAPP：
    ///
    /// 职责边界：
    /// - 相机句柄全局唯一，由 <see cref="Devices.Scanners.HikScaner"/> 持有；本类**禁止**自行 new HikScanner。
    /// - 图像与参数一律经 <see cref="RecipeScannerService"/> 访问，与配方页、主页走同一条链路。
    /// - 使用 <see cref="HttpListener"/> 而非 Kestrel：MainAPP 是 WinExe + Microsoft.NET.Sdk，
    ///   引入 ASP.NET Core 会要求产线机补装 ASP.NET Core Runtime，成本不划算。
    ///
    /// 路由（P1 只读预览）：
    ///   GET /            内嵌静态页面（原生 &lt;img src="/mjpeg"&gt;，无需装 App）
    ///   GET /mjpeg       MJPEG 流（multipart/x-mixed-replace），一帧只编一次、多客户端共享同一份字节
    ///   GET /api/status  曝光/增益/范围/触发模式/型号/序列号/IP 的 JSON
    ///
    /// 相机互斥时序（关键，顺序不可颠倒）：
    ///   PauseLoop() → WaitForMainLoopDrainAsync() → SwitchToSoftTriggerAsync()
    /// 颠倒会在主循环仍取帧时设置 TriggerMode，触发 0x80020106（GenICam 节点访问错误）。
    /// </summary>
    public sealed class CameraWebHost : IDisposable
    {
        /// <summary>默认监听端口。原 WebLiveView 使用同一端口，移除该项目后由本宿主接管。</summary>
        public const int DefaultPort = 5188;

        // 单帧取图超时（毫秒），与 RecipeScannerService 实时显示链路保持一致量级
        private const uint FrameTimeoutMs = 5_000;
        // 手机端调试不需要原始分辨率，限宽可大幅降低带宽与编码耗时
        private const int PhoneMaxWidth = 1280;
        // JPEG 质量：调试场景 80 足够，兼顾清晰度与体积
        private const int JpegQuality = 80;
        // 帧间隔（毫秒）：10 FPS。手机调试够用，也给推理侧留出余量
        private const int FrameIntervalMs = 100;
        // 主循环排空等待上限（秒）：覆盖主循环单帧取图超时 10s + 余量
        private const int DrainWaitSec = 12;
        // 最后一路客户端断开后的释放宽限期（毫秒）：容忍手机刷新页面/切后台
        private const int ClientReleaseGraceMs = 15_000;
        // 单帧取图异常后的退避（毫秒）
        private const int FrameErrorBackoffMs = 500;

        private static readonly byte[] CrLfBytes = Encoding.ASCII.GetBytes("\r\n");
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly int _port;
        // RecipeScannerService 无状态（所有方法都转发到 Devices.Scanners 单例），可安全多实例
        private readonly RecipeScannerService _scannerService = new();

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoop;

        // 相机接管串行化：接管、释放必须互斥，避免并发请求把触发模式切乱
        private readonly SemaphoreSlim _cameraGate = new(1, 1);

        // 帧生产循环（仅在有客户端接入时运行）
        private CancellationTokenSource? _frameCts;
        private Task? _frameLoop;

        // 最近一帧 JPEG 与版本号：多客户端共享同一份字节
        private readonly object _frameLock = new();
        private byte[]? _latestJpeg;
        private int _frameVersion;

        // 客户端计数与释放代号：代号用于让"断开→立刻重连"取消已排队的延迟释放
        private int _clientCount;
        private long _releaseGeneration;

        private bool _cameraTakenOver;
        // 暂停是否由本宿主发起；false 表示暂停本由配方页持有，释放时不得触碰主循环状态
        private bool _pausedByHost;
        private bool _disposed;

        public CameraWebHost(int port = DefaultPort)
        {
            _port = port;
        }

        /// <summary>监听是否已启动。</summary>
        public bool IsRunning => _listener is { IsListening: true };

        /// <summary>监听端口。</summary>
        public int Port => _port;

        // ─────────────────────────────────────────────────────────────
        // 生命周期
        // ─────────────────────────────────────────────────────────────

        /// <summary>启动监听（非阻塞）。全部绑定方式失败时记录日志并保持停止状态，不影响主程序。</summary>
        public void Start()
        {
            if (_listener is not null)
            {
                return;
            }

            var prefixes = BuildCandidatePrefixes(_port);
            for (var index = 0; index < prefixes.Length; index++)
            {
                var prefix = prefixes[index];
                var listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                try
                {
                    listener.Start();
                }
                catch (Exception ex)
                {
                    try { listener.Close(); } catch { }
                    LogService.Instance.Warning($"相机调试 Web 服务绑定 {prefix} 失败: {ex.Message}");
                    continue;
                }

                _listener = listener;
                _cts = new CancellationTokenSource();
                _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
                LogService.Instance.Info($"相机调试 Web 服务已启动: {prefix}");

                if (index > 0)
                {
                    // 回退到回环地址：功能可用但手机（局域网）访问不到，必须显式告知操作员如何放开
                    LogService.Instance.Warning(
                        $"相机调试 Web 服务仅绑定本机回环（{prefix}），手机无法访问。" +
                        $"如需手机访问，请以管理员执行后重启本程序：" +
                        $"netsh http add urlacl url=http://+:{_port}/ user=Everyone");
                }
                return;
            }

            LogService.Instance.Error(
                $"相机调试 Web 服务启动失败：端口 {_port} 全部绑定方式均不可用。" +
                $"如需手机（局域网）访问，请以管理员执行：netsh http add urlacl url=http://+:{_port}/ user=Everyone");
        }

        /// <summary>停止监听、释放相机接管状态。同步入口（App.OnExit 调用）。</summary>
        public void Stop()
        {
            if (_listener is null && !_cameraTakenOver)
            {
                return;
            }

            // VSTHRD103: CancellationTokenSource.Cancel() 本身是同步方法，分析器误报
#pragma warning disable VSTHRD103
            try { _cts?.Cancel(); } catch { }
#pragma warning restore VSTHRD103
            try { _listener?.Close(); } catch { }

            // 关闭监听会让正在写流的响应抛异常退出；此处做有限的同步收尾，确保相机归还
            try
            {
#pragma warning disable VSTHRD002 // Stop 是同步退出入口，此处为有限阻塞的收尾动作
                ReleaseCameraAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"相机调试 Web 服务释放相机失败: {ex.Message}");
            }

            _listener = null;
            _acceptLoop = null;
            _cts?.Dispose();
            _cts = null;
            LogService.Instance.Info("相机调试 Web 服务已停止");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _cameraGate.Dispose();
        }

        /// <summary>
        /// 绑定前缀候选。优先绑定全部网卡（手机可访问，需管理员权限或 URL ACL），
        /// 失败则退化为仅本机回环（无需任何权限，便于先用桌面浏览器验证功能）。
        /// </summary>
        private static string[] BuildCandidatePrefixes(int port)
        {
            return new[]
            {
                $"http://+:{port}/",
                $"http://127.0.0.1:{port}/",
            };
        }

        // ─────────────────────────────────────────────────────────────
        // 请求分发
        // ─────────────────────────────────────────────────────────────

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            var listener = _listener;
            if (listener is null)
            {
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"相机调试 Web 服务接收请求异常: {ex.Message}");
                    continue;
                }

                _ = Task.Run(() => HandleRequestAsync(context, ct), CancellationToken.None);
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            try
            {
                switch (path)
                {
                    case "/":
                    case "/index.html":
                        await WriteHtmlAsync(context.Response, ct).ConfigureAwait(false);
                        break;
                    case "/mjpeg":
                        await WriteMjpegAsync(context.Response, ct).ConfigureAwait(false);
                        break;
                    case "/api/status":
                        await WriteStatusAsync(context.Response, ct).ConfigureAwait(false);
                        break;
                    case "/api/recent":
                        await WriteRecentAsync(context, ct).ConfigureAwait(false);
                        break;
                    case "/api/set":
                        await WriteSetAsync(context, ct).ConfigureAwait(false);
                        break;
                    default:
                        context.Response.StatusCode = 404;
                        await WriteTextAsync(context.Response, "404 not found", ct).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // 客户端主动断开或服务停止，属正常路径
            }
            catch (HttpListenerException)
            {
                // 连接被对端关闭（手机锁屏/切后台），属正常路径
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"相机调试 Web 请求处理异常 [{path}]: {ex.Message}");
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 路由实现
        // ─────────────────────────────────────────────────────────────

        private const string IndexResourceName = "MainAPP.Web.CameraIndex.html";

        private static async Task WriteHtmlAsync(HttpListenerResponse response, CancellationToken ct)
        {
            var html = await ReadEmbeddedAsync(IndexResourceName, ct).ConfigureAwait(false) ?? "<h1>相机页面资源缺失</h1>";
            var bytes = Encoding.UTF8.GetBytes(html);
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.Headers["Cache-Control"] = "no-store";
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        /// <summary>读取嵌入资源文本（2026-09-13 起页面为独立 Web/*.html 嵌入资源）。</summary>
        private static async Task<string?> ReadEmbeddedAsync(string name, CancellationToken ct)
        {
            try
            {
                using var stream = typeof(CameraWebHost).Assembly.GetManifestResourceStream(name);
                if (stream is null)
                {
                    LogService.Instance.Error($"未找到嵌入资源 {name}");
                    return null;
                }

                using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"读取嵌入资源 {name} 失败: {ex.Message}");
                return null;
            }
        }

        private static async Task WriteTextAsync(HttpListenerResponse response, string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        private async Task WriteStatusAsync(HttpListenerResponse response, CancellationToken ct)
        {
            var status = await BuildStatusAsync(ct).ConfigureAwait(false);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(status, JsonOptions);
            response.StatusCode = 200;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.Headers["Cache-Control"] = "no-store";
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        private async Task<Dictionary<string, object?>> BuildStatusAsync(CancellationToken ct)
        {
            var connected = Devices.Scanners.HikScaner?.IsConnected ?? false;
            var status = new Dictionary<string, object?>
            {
                ["hasScanner"] = _scannerService.HasScanner,
                ["connected"] = connected,
                ["clients"] = Volatile.Read(ref _clientCount),
                ["streaming"] = Volatile.Read(ref _clientCount) > 0,
                ["paused"] = HomeViewModel.IsLoopPaused,
                ["model"] = Devices.Scanners.HikScaner?.DeviceInfo?.ModelName ?? string.Empty,
                ["serial"] = Devices.Scanners.HikScaner?.DeviceInfo?.SerialNumber ?? string.Empty,
                ["ip"] = Devices.Scanners.HikScaner?.DeviceInfo?.CurrentIp ?? string.Empty,
            };

            if (!connected)
            {
                return status;
            }

            try
            {
                status["triggerMode"] = Devices.Scanners.HikScaner?.GetCurrentTriggerMode().ToString() ?? string.Empty;
                status["exposure"] = await _scannerService.GetExposureTimeAsync(ct).ConfigureAwait(false);
                status["gain"] = await _scannerService.GetGainAsync(ct).ConfigureAwait(false);
                var exposureRange = await _scannerService.GetExposureRangeAsync(ct).ConfigureAwait(false);
                var gainRange = await _scannerService.GetGainRangeAsync(ct).ConfigureAwait(false);
                status["exposureMin"] = exposureRange.Min;
                status["exposureMax"] = exposureRange.Max;
                status["gainMin"] = gainRange.Min;
                status["gainMax"] = gainRange.Max;
            }
            catch (Exception ex)
            {
                status["error"] = ex.Message;
            }

            return status;
        }

        /// <summary>
        /// 最近检测概览：GET /api/recent?count=N（默认 10，上限 50）。
        /// 取落库最近 N 条（BarcodeDataService），供网页端仪表盘实时展示。
        /// </summary>
        private static async Task WriteRecentAsync(HttpListenerContext context, CancellationToken ct)
        {
            var q = context.Request.QueryString;
            int count = int.TryParse(q["count"], out var c) ? Math.Clamp(c, 1, 50) : 10;
            var items = await BarcodeDataService.Instance.GetRecentAsync(count, ct).ConfigureAwait(false);
            var payload = items.Select(m => new
            {
                time = m.DetectTime.ToString("HH:mm:ss.fff"),
                barcode = string.IsNullOrEmpty(m.Barcode) ? "" : m.Barcode,
                x = Math.Round(m.WorldX, 2),
                y = Math.Round(m.WorldY, 2),
                a = Math.Round(m.Angle, 2),
                enc = m.Encode,
                result = m.Result ?? "",
            });

            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// 相机参数写入：GET /api/set?exposure=&lt;us&gt;&amp;gain=&lt;dB&gt;（可选，至少一个）。
        /// 沿用既有互斥时序（与 AI 审计同源）：主循环未暂停时先暂停 → 排空在途推理 → 写参数 → 恢复；
        /// 本宿主接管中（主循环已暂停）则直接写、不触碰主循环状态。
        /// </summary>
        private static async Task WriteSetAsync(HttpListenerContext context, CancellationToken ct)
        {
            var q = context.Request.QueryString;
            var exposureRaw = q["exposure"];
            var gainRaw = q["gain"];
            if (string.IsNullOrEmpty(exposureRaw) && string.IsNullOrEmpty(gainRaw))
            {
                await WriteJsonAsync(context.Response, new { ok = false, msg = "参数缺失：应提供 exposure 或 gain" }, 400, ct).ConfigureAwait(false);
                return;
            }

            var connected = Devices.Scanners.HikScaner?.IsConnected ?? false;
            if (!connected)
            {
                await WriteJsonAsync(context.Response, new { ok = false, msg = "读码器未连接" }, 409, ct).ConfigureAwait(false);
                return;
            }

            float? exposure = float.TryParse(exposureRaw, System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : null;
            float? gain = float.TryParse(gainRaw, System.Globalization.CultureInfo.InvariantCulture, out var g) ? g : null;

            var wasPaused = HomeViewModel.IsLoopPaused;
            if (!wasPaused)
            {
                HomeViewModel.PauseLoop();
            }

            try
            {
                var drained = await HomeViewModel.WaitForMainLoopDrainAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
                if (!drained)
                {
                    LogService.Instance.Warning("[相机调试 Web] 参数写入前排空主循环超时，仍继续");
                }

                var service = new RecipeScannerService();
                if (exposure is not null)
                {
                    await service.SetExposureTimeAsync(exposure.Value, ct).ConfigureAwait(false);
                }

                if (gain is not null)
                {
                    await service.SetGainAsync(gain.Value, ct).ConfigureAwait(false);
                }

                await WriteJsonAsync(context.Response, new { ok = true, msg = "参数已生效" }, 200, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[相机调试 Web] 参数写入失败: {ex.Message}");
                await WriteJsonAsync(context.Response, new { ok = false, msg = ex.Message }, 500, ct).ConfigureAwait(false);
            }
            finally
            {
                if (!wasPaused)
                {
                    HomeViewModel.ResumeLoop();
                }
            }
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, int status, CancellationToken ct)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            response.StatusCode = status;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.Headers["Cache-Control"] = "no-store";
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// MJPEG 流。首个客户端接入时接管相机（暂停主循环 + 切软触发），
        /// 最后一路客户端断开后经宽限期归还相机。
        /// </summary>
        private async Task WriteMjpegAsync(HttpListenerResponse response, CancellationToken ct)
        {
            if (!_scannerService.HasScanner)
            {
                response.StatusCode = 503;
                await WriteTextAsync(response, "未检测到扫码枪设备", ct).ConfigureAwait(false);
                return;
            }

            BeginClient();
            try
            {
                if (!await EnsureCameraTakenOverAsync(ct).ConfigureAwait(false))
                {
                    response.StatusCode = 503;
                    await WriteTextAsync(response, "相机当前不可用，请稍后重试", ct).ConfigureAwait(false);
                    return;
                }

                response.StatusCode = 200;
                response.ContentType = "multipart/x-mixed-replace; boundary=frame";
                response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
                response.Headers["Pragma"] = "no-cache";
                response.KeepAlive = true;
                // 流无固定长度，必须走分块传输；每帧显式 Flush 才能真正推给手机
                response.SendChunked = true;

                var output = response.OutputStream;
                var lastVersion = -1;
                while (!ct.IsCancellationRequested)
                {
                    var packet = await WaitForNextJpegAsync(lastVersion, ct).ConfigureAwait(false);
                    if (packet is null)
                    {
                        continue;
                    }

                    var header = Encoding.ASCII.GetBytes(
                        $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {packet.Value.Bytes.Length}\r\n\r\n");
                    await output.WriteAsync(header, ct).ConfigureAwait(false);
                    await output.WriteAsync(packet.Value.Bytes, ct).ConfigureAwait(false);
                    await output.WriteAsync(CrLfBytes, ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                    lastVersion = packet.Value.Version;
                }
            }
            finally
            {
                EndClient();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 相机接管 / 释放
        // ─────────────────────────────────────────────────────────────

        private void BeginClient()
        {
            var count = Interlocked.Increment(ref _clientCount);
            // 有客户端接入即取消可能已排队的延迟释放
            Interlocked.Increment(ref _releaseGeneration);
            if (count == 1)
            {
                LogService.Instance.Info("相机调试 Web：首个客户端已接入");
            }
        }

        private void EndClient()
        {
            var count = Interlocked.Decrement(ref _clientCount);
            if (count > 0)
            {
                return;
            }

            LogService.Instance.Info($"相机调试 Web：最后一路客户端已断开，相机将在 {ClientReleaseGraceMs / 1000} 秒后释放");
            var generation = Interlocked.Increment(ref _releaseGeneration);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(ClientReleaseGraceMs).ConfigureAwait(false);
                    // 宽限期内有新客户端接入（代号变化）或有客户端在场，则不释放
                    if (generation != Volatile.Read(ref _releaseGeneration))
                    {
                        return;
                    }
                    if (Volatile.Read(ref _clientCount) > 0)
                    {
                        return;
                    }
                    await ReleaseCameraAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"相机调试 Web：延迟释放相机异常: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 接管相机：暂停主循环 → 等在途推理租约归还 → 切软触发 → 启动帧生产循环。
        /// 顺序不可颠倒（见类注释的 0x80020106 约束）。
        /// </summary>
        private async Task<bool> EnsureCameraTakenOverAsync(CancellationToken ct)
        {
            await _cameraGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_cameraTakenOver)
                {
                    return true;
                }

                // 暂停若已由配方页持有，则本次释放时不得恢复主循环（否则会破坏配方页）
                _pausedByHost = !HomeViewModel.IsLoopPaused;
                HomeViewModel.PauseLoop();

                var drained = await HomeViewModel
                    .WaitForMainLoopDrainAsync(TimeSpan.FromSeconds(DrainWaitSec), ct)
                    .ConfigureAwait(false);
                if (!drained)
                {
                    LogService.Instance.Warning("相机调试 Web：等待主循环排空超时，仍继续切换软触发");
                }

                await _scannerService.SwitchToSoftTriggerAsync(ct).ConfigureAwait(false);
                await StartFrameLoopAsync().ConfigureAwait(false);
                _cameraTakenOver = true;
                LogService.Instance.Info(_pausedByHost
                    ? "相机调试 Web：已接管相机（主循环由本服务暂停并切至软触发）"
                    : "相机调试 Web：已接管相机（主循环原本已由配方页暂停，释放时不恢复该状态）");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"相机调试 Web：接管相机失败: {ex}");
                await ReleaseCameraCoreAsync().ConfigureAwait(false);
                return false;
            }
            finally
            {
                _cameraGate.Release();
            }
        }

        private async Task ReleaseCameraAsync()
        {
            await _cameraGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await ReleaseCameraCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _cameraGate.Release();
            }
        }

        /// <summary>释放相机。调用方必须已持有 <see cref="_cameraGate"/>。</summary>
        private async Task ReleaseCameraCoreAsync()
        {
            if (!_cameraTakenOver)
            {
                return;
            }

            await StopFrameLoopAsync().ConfigureAwait(false);

            var restoreMainLoop = _pausedByHost;
            if (restoreMainLoop)
            {
                try
                {
                    await _scannerService.SwitchToHardTriggerAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"相机调试 Web：恢复硬触发失败: {ex.Message}");
                }

                HomeViewModel.ResumeLoop();
            }

            _cameraTakenOver = false;
            _pausedByHost = false;
            LogService.Instance.Info(restoreMainLoop
                ? "相机调试 Web：已释放相机（恢复硬触发并恢复主循环）"
                : "相机调试 Web：已释放相机（暂停与软触发仍由配方页持有，未做改动）");
        }

        // ─────────────────────────────────────────────────────────────
        // 帧生产
        // ─────────────────────────────────────────────────────────────

        private async Task StartFrameLoopAsync()
        {
            await StopFrameLoopAsync().ConfigureAwait(false);

            var cts = new CancellationTokenSource();
            _frameCts = cts;
            _frameLoop = Task.Run(() => FrameLoopAsync(cts.Token), cts.Token);
        }

        private async Task StopFrameLoopAsync()
        {
            var cts = _frameCts;
            var loop = _frameLoop;
            _frameCts = null;
            _frameLoop = null;

            // VSTHRD103: CancellationTokenSource.Cancel() 本身是同步方法，分析器误报
#pragma warning disable VSTHRD103
            try { cts?.Cancel(); } catch { }
#pragma warning restore VSTHRD103
            if (loop is not null && !loop.IsCompleted)
            {
                try { await loop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (Exception ex) { LogService.Instance.Warning($"相机调试 Web：等待取帧循环退出异常: {ex.Message}"); }
            }
            cts?.Dispose();
        }

        private async Task FrameLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var frame = await _scannerService.GetImageAsync(FrameTimeoutMs, ct).ConfigureAwait(false);
                    using (frame)
                    {
                        var jpeg = EncodeJpeg(frame);
                        if (jpeg is not null)
                        {
                            PublishFrame(jpeg);
                        }
                    }

                    await Task.Delay(FrameIntervalMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"相机调试 Web：取帧异常: {ex.Message}");
                    try
                    {
                        await Task.Delay(FrameErrorBackoffMs, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 编码为 JPEG。读码器若配置为 JPEG 直出（<see cref="FrameResult.IsJpeg"/>）则直接转发原始字节，
        /// 编码成本接近零；否则用 OpenCvSharp 解码后按手机可用分辨率重编码。
        /// 注意：<see cref="FrameResult"/> 的 ImageData 来自 ArrayPool，Dispose 后即归还，
        /// 因此两条路径都必须返回**自有**数组（ToArray / ImEncode 均已拷贝）。
        /// </summary>
        private static byte[]? EncodeJpeg(FrameResult frame)
        {
            if (frame.ImageData is not { Length: > 0 } data)
            {
                return null;
            }

            if (frame.IsJpeg)
            {
                return data.ToArray();
            }

            using var mat = Mat.ImDecode(data, ImreadModes.Color);
            if (mat.Empty())
            {
                return null;
            }

            if (mat.Width > PhoneMaxWidth)
            {
                var height = (int)Math.Round(mat.Height * (PhoneMaxWidth / (double)mat.Width));
                using var resized = new Mat();
                Cv2.Resize(mat, resized, new Size(PhoneMaxWidth, height), 0, 0, InterpolationFlags.Area);
                return resized.ImEncode(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
            }

            return mat.ImEncode(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
        }

        private void PublishFrame(byte[] jpeg)
        {
            lock (_frameLock)
            {
                _latestJpeg = jpeg;
                _frameVersion++;
            }
        }

        /// <summary>等待比 <paramref name="lastVersion"/> 更新的帧；多客户端因此共享同一份 JPEG 字节。</summary>
        private async Task<FramePacket?> WaitForNextJpegAsync(int lastVersion, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                lock (_frameLock)
                {
                    if (_latestJpeg is not null && _frameVersion != lastVersion)
                    {
                        return new FramePacket(_frameVersion, _latestJpeg);
                    }
                }

                await Task.Delay(30, ct).ConfigureAwait(false);
            }

            return null;
        }

        private readonly record struct FramePacket(int Version, byte[] Bytes);

        // ─────────────────────────────────────────────────────────────
    }
}
