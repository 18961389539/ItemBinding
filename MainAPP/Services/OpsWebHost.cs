using MainAPP.Models;
using MainAPP.Services.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services;

/// <summary>
/// 统一远程运维门户（2026-09-13，端口 5191）。
/// <para>单页聚合：相机/读码器、编码器/VGT、系统状态卡 + 最近告警 + 最近检测 +
/// 快捷操作（生成日报、清空 AI 会话）。与 CameraWebHost/AiWebHost 同 HttpListener 模式。</para>
/// <para>路由：GET /（门户页，Web/OpsIndex.html 嵌入资源）、GET /api/ops（聚合 JSON）、
/// POST /api/report（生成近 24h 日报）、POST /api/ai/clear（重置 AI 会话）。</para>
/// </summary>
public sealed class OpsWebHost : IDisposable
{
    /// <summary>默认监听端口（与 5188/5190/8081 错开）。</summary>
    public const int DefaultPort = 5191;

    private const string IndexResourceName = "MainAPP.Web.OpsIndex.html";

    private readonly int _port;
    private HttpListener? _listener;
    private bool _disposed;

    public OpsWebHost(int port = DefaultPort) => _port = port;

    public bool IsRunning => _listener is { IsListening: true };

    public int Port => _port;

    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add($"http://+:{_port}/"); // 管理员/urlacl 才能绑 +；
            listener.Start();
        }
        catch (Exception)
        {
            // 非管理员回退仅本机可访问
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                listener.Start();
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[运维门户] 启动失败({_port}): {ex.Message}；手机访问请管理员执行 netsh http add urlacl url=http://+:{_port}/ user=Everyone");
                return;
            }
        }

        _listener = listener;
        _ = AcceptLoopAsync();
        LogService.Instance.Info($"[运维门户] 端口 {_port} 已启动（统一运维门户，局域网 URL: http://<本机IP>:{_port}/）");
    }

    public void Stop()
    {
        var l = Interlocked.Exchange(ref _listener, null);
        if (l is not null)
        {
            try { l.Close(); } catch { }
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (_listener is { IsListening: true })
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) { break; }
            _ = Task.Run(() => HandleAsync(ctx), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        try
        {
            switch (context.Request.HttpMethod?.ToUpperInvariant())
            {
                case "GET" when path is "/" or "/index.html":
                    await WriteIndexAsync(context.Response).ConfigureAwait(false);
                    break;
                case "GET" when path == "/api/ops":
                    await WriteOpsAsync(context.Response).ConfigureAwait(false);
                    break;
                case "POST" when path == "/api/report":
                    await WriteReportAsync(context.Response).ConfigureAwait(false);
                    break;
                case "POST" when path == "/api/ai/clear":
                    await WriteAiClearAsync(context.Response).ConfigureAwait(false);
                    break;
                default:
                    context.Response.StatusCode = 404;
                    await WriteBytesAsync(context.Response, Encoding.UTF8.GetBytes("404 not found"), "text/plain; charset=utf-8").ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpListenerException) { }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"[运维门户] 请求处理异常 [{path}]: {ex.Message}");
        }
        finally
        {
            try { context.Response.Close(); } catch { }
        }
    }

    // ───────────────────────── 路由实现 ─────────────────────────

    private static async Task WriteIndexAsync(HttpListenerResponse response)
    {
        var html = await ReadEmbeddedAsync(IndexResourceName).ConfigureAwait(false) ?? "<h1>运维门户资源缺失</h1>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-store";
        await response.OutputStream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<string?> ReadEmbeddedAsync(string name)
    {
        try
        {
            using var stream = typeof(OpsWebHost).Assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                LogService.Instance.Error($"未找到嵌入资源 {name}");
                return null;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"读取嵌入资源 {name} 失败: {ex.Message}");
            return null;
        }
    }

    private static async Task WriteOpsAsync(HttpListenerResponse response)
    {
        var payload = await BuildOpsAsync().ConfigureAwait(false);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
        response.StatusCode = 200;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-store";
        await response.OutputStream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<object> BuildOpsAsync()
    {
        var scanner = Devices.Scanners.HikScaner;
        IToVGTService toVgt;
        try { toVgt = App.Services.GetRequiredService<IToVGTService>(); }
        catch { toVgt = null!; }

        var recent = new List<object>();
        try
        {
            var rows = await BarcodeDataService.Instance.GetRecentAsync(8).ConfigureAwait(false);
            foreach (var m in rows)
            {
                recent.Add(new
                {
                    time = m.DetectTime.ToString("HH:mm:ss"),
                    barcode = m.Barcode,
                    x = Math.Round(m.WorldX, 2),
                    y = Math.Round(m.WorldY, 2),
                    a = Math.Round(m.Angle, 2),
                    result = m.Result ?? "",
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"[运维门户] 最近检测查询失败: {ex.Message}");
        }

        var alarms = new List<string>();
        try
        {
            await using var db = new LogDbContext();
            var rows = await db.Logs.AsNoTracking()
                .Where(l => l.Level == "Error" || l.Level == "Warning")
                .OrderByDescending(l => l.Id)
                .Take(10)
                .Select(l => new { l.Timestamp, l.Level, l.RenderedMessage })
                .ToListAsync().ConfigureAwait(false);
            foreach (var r in rows)
            {
                alarms.Add($"[{r.Timestamp:MM-dd HH:mm}] [{r.Level}] {r.RenderedMessage}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"[运维门户] 告警查询失败: {ex.Message}");
        }

        return new
        {
            camera = new
            {
                connected = scanner?.IsConnected ?? false,
                model = scanner?.DeviceInfo?.ModelName ?? string.Empty,
                serial = scanner?.DeviceInfo?.SerialNumber ?? string.Empty,
                ip = scanner?.DeviceInfo?.CurrentIp ?? string.Empty,
            },
            encoder = new
            {
                speed = toVgt?.Speed ?? 0,
                lastReceive = toVgt?.LastEncoderReceiveTime?.ToString("HH:mm:ss") ?? string.Empty,
            },
            vgt = new
            {
                robotLive = toVgt?.IsRobotOnLive ?? false,
                vgtLive = toVgt?.IsVGTOnLive ?? false,
            },
            paused = MainAPP.ViewModels.HomeViewModel.IsLoopPaused,
            memoryMb = (int)(GC.GetTotalMemory(false) / 1024 / 1024),
            now = DateTime.Now.ToString("HH:mm:ss"),
            recent,
            alarms,
        };
    }

    private static async Task WriteReportAsync(HttpListenerResponse response)
    {
        try
        {
            var now = DateTime.Now;
            var path = await ReportService.Instance.GenerateHtmlAsync(now.AddHours(-24), now).ConfigureAwait(false);
            await WriteJsonAsync(response, new { ok = true, msg = "日报已生成", path }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"[运维门户] 生成日报失败: {ex}");
            await WriteJsonAsync(response, new { ok = false, msg = ex.Message }).ConfigureAwait(false);
        }
    }

    private static async Task WriteAiClearAsync(HttpListenerResponse response)
    {
        try
        {
            await AiChatService.Instance.ResetConversationAsync().ConfigureAwait(false);
            await WriteJsonAsync(response, new { ok = true, msg = "AI 会话已清空" }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(response, new { ok = false, msg = ex.Message }).ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload)
    {
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
        response.StatusCode = 200;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.Headers["Cache-Control"] = "no-store";
        await response.OutputStream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task WriteBytesAsync(HttpListenerResponse response, byte[] bytes, string contentType)
    {
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}