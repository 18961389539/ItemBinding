using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services.AI
{
    /// <summary>
    /// AI 对话 Web 宿主（进程内 <see cref="HttpListener"/>，零新增框架依赖）。
    ///
    /// <para>提供浏览器级对话体验：<c>GET /</c> 返回一个内嵌页面，页面里跑
    /// <b>Deep Chat</b>（MIT，Web Component）——气泡、Markdown、代码高亮、流式输出全部由它负责，
    /// C# 侧<b>一行前端都不用写</b>。Deep Chat 脚本以嵌入资源随程序集分发，产线离线可用。</para>
    ///
    /// <para>设计上照抄 <see cref="CameraWebHost"/> 的模式（前缀回退、生命周期、路由），
    /// 并刻意复用同一思路：<b>同一份页面既供 WPF 内嵌 WebView2 使用，也直接暴露给手机浏览器</b>，
    /// 一份前端两个入口。相机调试走 5188，本服务用 5190，互不干扰。</para>
    ///
    /// <para>路由：</para>
    /// <list type="bullet">
    ///   <item><c>GET /</c> —— 内嵌对话页面（含 Deep Chat 配置）</item>
    ///   <item><c>GET /deepchat.js</c> —— Deep Chat 单文件脚本（嵌入资源，378 KB）</item>
    ///   <item><c>POST /ai/chat</c> —— Deep Chat 协议桥：收 <c>{messages:[{role,text}]}</c>，
    ///        取最后一条用户消息调 <see cref="AiChatService.AskAsync"/>，回 <c>{text:"..."}</c></item>
    /// </list>
    /// </summary>
    public sealed class AiWebHost : IDisposable
    {
        /// <summary>默认监听端口（相机调试 5188、配方 TCP 5000，本服务错开用 5190）。</summary>
        public const int DefaultPort = 5190;

        private const string BundleResourceName = "MainAPP.Services.AI.Resources.deepChat.bundle.js";

        private readonly int _port;
        private readonly AiChatService _ai;

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoop;

        // Deep Chat 单文件脚本只有 378 KB，进程内读一次缓存即可（嵌入资源不会变）
        private byte[]? _bundleBytes;

        private bool _disposed;

        public AiWebHost(int port = DefaultPort, AiChatService? aiService = null)
        {
            _port = port;
            _ai = aiService ?? AiChatService.Instance;
        }

        /// <summary>监听是否已启动。</summary>
        public bool IsRunning => _listener is { IsListening: true };

        /// <summary>监听端口。</summary>
        public int Port => _port;

        // ─────────────────────────────────────────────────────────────
        // 生命周期
        // ─────────────────────────────────────────────────────────────

        /// <summary>启动监听（非阻塞）。绑定失败时逐级回退并记录日志，不影响主程序。</summary>
        public void Start()
        {
            if (_listener is not null)
            {
                return;
            }

            var prefixes = new[] { $"http://+:{_port}/", $"http://127.0.0.1:{_port}/" };
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
                    LogService.Instance.Warning($"AI 对话 Web 服务绑定 {prefix} 失败: {ex.Message}");
                    continue;
                }

                _listener = listener;
                _cts = new CancellationTokenSource();
                _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts!.Token));
                LogService.Instance.Info($"AI 对话 Web 服务已启动: {prefix}");

                if (index > 0)
                {
                    // 回退到回环：本机可用但手机访问不到，必须显式告知如何放开
                    LogService.Instance.Warning(
                        $"AI 对话 Web 服务仅绑定本机回环（{prefix}），手机无法访问。" +
                        $"如需手机访问，请以管理员执行后重启本程序：" +
                        $"netsh http add urlacl url=http://+:{_port}/ user=Everyone");
                }
                return;
            }

            LogService.Instance.Error(
                $"AI 对话 Web 服务启动失败：端口 {_port} 全部绑定方式均不可用。" +
                $"如需手机（局域网）访问，请以管理员执行：netsh http add urlacl url=http://+:{_port}/ user=Everyone");
        }

        /// <summary>停止监听。同步入口（App.OnExit 调用）。</summary>
        public void Stop()
        {
            if (_listener is null)
            {
                return;
            }

            try { _cts?.Cancel(); } catch { }
            try { _listener?.Close(); } catch { }

            _listener = null;
            _acceptLoop = null;
            _cts?.Dispose();
            _cts = null;
            LogService.Instance.Info("AI 对话 Web 服务已停止");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
        }

        private static string[] BuildCandidatePrefixes(int port) => new[]
        {
            $"http://+:{port}/",
            $"http://127.0.0.1:{port}/",
        };

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
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch (Exception ex)
                {
                    LogService.Instance.Warning($"AI 对话 Web 服务接收请求异常: {ex.Message}");
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
                switch (context.Request.HttpMethod?.ToUpperInvariant())
                {
                    case "GET" when path is "/" or "/index.html":
                        await WriteHtmlAsync(context.Response, ct).ConfigureAwait(false);
                        break;
                    case "GET" when path == "/deepchat.js":
                        await WriteBundleAsync(context.Response, ct).ConfigureAwait(false);
                        break;
                    case "GET" when path == "/ai/image":
                        await WriteImageAsync(context, ct).ConfigureAwait(false);
                        break;
                    case "POST" when path == "/ai/chat":
                        await WriteChatAsync(context, ct).ConfigureAwait(false);
                        break;
                    case "POST" when path == "/ai/chat/stream":
                        await WriteChatStreamAsync(context, ct).ConfigureAwait(false);
                        break;
                    case "GET" when path == "/ai/info":
                        await WriteInfoAsync(context.Response, ct).ConfigureAwait(false);
                        break;
                    case "POST" when path == "/ai/clear":
                        await WriteClearAsync(context.Response, ct).ConfigureAwait(false);
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
                LogService.Instance.Warning($"AI 对话 Web 请求处理异常 [{path}]: {ex.Message}");
            }
            finally
            {
                try { context.Response.Close(); } catch { }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 路由实现
        // ─────────────────────────────────────────────────────────────

        private static async Task WriteHtmlAsync(HttpListenerResponse response, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(IndexHtml);
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.Headers["Cache-Control"] = "no-store";
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        private async Task WriteBundleAsync(HttpListenerResponse response, CancellationToken ct)
        {
            _bundleBytes ??= await ReadBundleAsync().ConfigureAwait(false);
            if (_bundleBytes is null || _bundleBytes.Length == 0)
            {
                response.StatusCode = 500;
                await WriteTextAsync(response, "Deep Chat 脚本资源缺失", ct).ConfigureAwait(false);
                return;
            }

            response.StatusCode = 200;
            response.ContentType = "application/javascript; charset=utf-8";
            response.ContentLength64 = _bundleBytes.Length;
            // 脚本随程序集走，会话内不变，允许缓存以减少每次刷新的传输
            response.Headers["Cache-Control"] = "private, max-age=3600";
            await response.OutputStream.WriteAsync(_bundleBytes, ct).ConfigureAwait(false);
        }

        private static async Task<byte[]?> ReadBundleAsync()
        {
            try
            {
                using var stream = typeof(AiWebHost).Assembly.GetManifestResourceStream(BundleResourceName);
                if (stream is null)
                {
                    LogService.Instance.Error($"未找到嵌入资源 {BundleResourceName}，请检查 csproj 的 EmbeddedResource 声明");
                    return null;
                }

                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms).ConfigureAwait(false);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"读取 Deep Chat 脚本资源失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 检测图片端点：GET /ai/image?path=&lt;完整路径&gt;。
        /// ★ 安全：只允许「图片保存目录（PicturesSaveFolder）」下的 .jpg/.jpeg/.png，
        ///   且做 GetFullPath 归一化防目录穿越——聊天框里的 markdown 图片都经此端点出图。
        /// </summary>
        private static async Task WriteImageAsync(HttpListenerContext context, CancellationToken ct)
        {
            var response = context.Response;
            var raw = System.Net.WebUtility.UrlDecode(context.Request.QueryString["path"] ?? string.Empty);
            string path;
            try
            {
                path = Path.GetFullPath(raw);
            }
            catch
            {
                path = string.Empty;
            }

            var allowedRoot = MainAPP.Models.Settings.Instance.Storage.PicturesSaveFolder?.Trim() ?? string.Empty;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var rootOk = allowedRoot.Length > 0 &&
                         path.StartsWith(Path.GetFullPath(allowedRoot), StringComparison.OrdinalIgnoreCase);
            var extOk = ext is ".jpg" or ".jpeg" or ".png";

            if (string.IsNullOrEmpty(path) || !rootOk || !extOk || !File.Exists(path))
            {
                response.StatusCode = 404;
                await WriteTextAsync(response, "image not found", ct).ConfigureAwait(false);
                return;
            }

            response.StatusCode = 200;
            response.ContentType = ext == ".png" ? "image/png" : "image/jpeg";
            await using var fs = File.OpenRead(path);
            response.ContentLength64 = fs.Length;
            await fs.CopyToAsync(response.OutputStream, ct).ConfigureAwait(false);
        }

        private static async Task WriteTextAsync(HttpListenerResponse response, string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Deep Chat 协议桥。
        /// 请求：<c>{"messages":[{"role":"user","text":"..."}]}</c>
        /// 响应：<c>{"text":"..."}</c>；异常回 <c>{"error":"..."}</c>（Deep Chat 会自行在气泡里显示错误）。
        /// </summary>
        private async Task WriteChatAsync(HttpListenerContext context, CancellationToken ct)
        {
            var response = context.Response;
            var question = await ReadQuestionAsync(context.Request).ConfigureAwait(false);
            if (question is null)
            {
                await WriteJsonAsync(response, new { error = "请求缺少 messages 数组或没有可处理的问题" }, 400, ct).ConfigureAwait(false);
                return;
            }

            if (!MainAPP.Models.Settings.Instance.Ai.Enabled)
            {
                await WriteJsonAsync(response, new { error = "AI 对话未启用（Settings.Ai.Enabled = false）" }, 200, ct).ConfigureAwait(false);
                return;
            }

            try
            {
                var answer = await _ai.AskAsync(question, ct).ConfigureAwait(false);
                await WriteJsonAsync(response, new { text = string.IsNullOrWhiteSpace(answer) ? "（AI 未返回内容）" : answer }, 200, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"AI 对话处理失败: {ex}");
                await WriteJsonAsync(response, new { error = $"AI 处理失败: {ex.Message}" }, 200, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 解析 Deep Chat 请求体，返回最后一条用户消息文本。
        /// 请求体不合法时返回 null（由调用方决定如何应答）。
        /// </summary>
        private static async Task<string?> ReadQuestionAsync(HttpListenerRequest request)
        {
            try
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("messages", out var messages)
                    || messages.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var question = ExtractLastUserText(messages);
                return string.IsNullOrWhiteSpace(question) ? null : question;
            }
            catch (JsonException ex)
            {
                LogService.Instance.Warning($"AI 对话请求体解析失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 流式对话端点（SSE）。
        /// Deep Chat 约定：<c>Content-Type: text/event-stream</c>，
        /// 每块 <c>data: {"text":"..."}</c>，结束发 <c>data: [DONE]</c>。
        /// </summary>
        private async Task WriteChatStreamAsync(HttpListenerContext context, CancellationToken ct)
        {
            var response = context.Response;

            if (!MainAPP.Models.Settings.Instance.Ai.Enabled)
            {
                response.StatusCode = 200;
                response.ContentType = "application/json; charset=utf-8";
                await WriteJsonAsync(response, new { error = "AI 对话未启用（Settings.Ai.Enabled = false）" }, 200, ct).ConfigureAwait(false);
                return;
            }

            var question = await ReadQuestionAsync(context.Request).ConfigureAwait(false);
            if (question is null)
            {
                await WriteJsonAsync(response, new { error = "请求缺少 messages 数组或没有可处理的问题" }, 400, ct).ConfigureAwait(false);
                return;
            }

            // SSE 头：必须 chunked + 逐块 Flush，否则 HttpListener 会攒到结束才发
            response.StatusCode = 200;
            response.ContentType = "text/event-stream; charset=utf-8";
            response.Headers["Cache-Control"] = "no-store";
            response.KeepAlive = true;
            response.SendChunked = true;

            var stream = response.OutputStream;
            async Task SendAsync(string eventName, string payload)
            {
                var bytes = Encoding.UTF8.GetBytes($"data: {payload}\n\n");
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            try
            {
                await _ai.AskStreamingAsync(
                    question,
                    async chunk =>
                    {
                        var json = JsonSerializer.Serialize(new { text = chunk });
                        await SendAsync("message", json).ConfigureAwait(false);
                    },
                    ct).ConfigureAwait(false);

                await SendAsync("done", "[DONE]").ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"AI 对话流式处理失败: {ex}");
                var err = JsonSerializer.Serialize(new { error = $"AI 处理失败: {ex.Message}" });
                await SendAsync("error", err).ConfigureAwait(false);
            }
        }

        /// <summary>取最后一条 role=user 的文本。Deep Chat 的历史可能混有 role=ai 的消息。</summary>
        private static string ExtractLastUserText(JsonElement messages)
        {
            string? last = null;
            foreach (var m in messages.EnumerateArray())
            {
                var role = m.TryGetProperty("role", out var r) ? r.GetString() : null;
                if (!string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = m.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    last = text;
                }
            }

            return last ?? string.Empty;
        }

        /// <summary>
        /// AI 信息面板：GET /ai/info。返回模型加载状态/后端/写工具开关/待确认写入/知识库文档清单（整库语料）。
        /// </summary>
        private static async Task WriteInfoAsync(HttpListenerResponse response, CancellationToken ct)
        {
            var svc = AiChatService.Instance;
            var cfg = MainAPP.Models.Settings.Instance.Ai;
            var docs = KnowledgeBase.ListDocumentSources();
            var info = new
            {
                loaded = svc.IsLoaded,
                pendingWrite = svc.HasPendingWrite,
                model = string.IsNullOrEmpty(cfg.ModelPath) ? "" : Path.GetFileName(cfg.ModelPath),
                backend = cfg.Backend,
                port = cfg.LlamaServerPort,
                contextSize = cfg.ContextSize,
                maxTokens = cfg.MaxTokens,
                allowWriteTools = cfg.AllowWriteTools,
                knowledgeCount = docs.Count,
                knowledge = docs.Take(6).ToArray(),
            };
            await WriteJsonAsync(response, info, 200, ct).ConfigureAwait(false);
        }

        /// <summary>清空 AI 会话：POST /ai/clear（MAF 重建会话，历史上下文丢弃）。</summary>
        private static async Task WriteClearAsync(HttpListenerResponse response, CancellationToken ct)
        {
            try
            {
                await AiChatService.Instance.ResetConversationAsync(ct).ConfigureAwait(false);
                await WriteJsonAsync(response, new { ok = true, msg = "已开启新会话" }, 200, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[AI 对话 Web] 清空会话失败: {ex.Message}");
                await WriteJsonAsync(response, new { ok = false, msg = ex.Message }, 500, ct).ConfigureAwait(false);
            }
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, int status, CancellationToken ct)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            response.StatusCode = status;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.Headers["Cache-Control"] = "no-store";
            await response.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }

        // ─────────────────────────────────────────────────────────────
        // 内嵌前端页面（Deep Chat 负责 UI，这里只做配置与样式）
        // ─────────────────────────────────────────────────────────────

        private const string IndexHtml = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>AI 检测助手</title>
<script type="module" src="/deepchat.js"></script>
<style>
*{box-sizing:border-box}
html,body{margin:0;height:100%;background:#101014;color:#e8e8ec;
  font:14px/1.6 -apple-system,BlinkMacSystemFont,"Microsoft YaHei",sans-serif}
header{padding:10px 14px;border-bottom:1px solid #26262e;display:flex;
  justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap}
h1{font-size:15px;font-weight:600;margin:0}
.hint{color:#8a8a96;font-size:12px}
.panel{display:flex;gap:24px;padding:8px 14px;background:#17171d;
  border-bottom:1px solid #26262e;font-size:12px;overflow-x:auto;align-items:center}
.panel .k{color:#8a8a96;margin-right:6px}
.panel .v{font-variant-numeric:tabular-nums}
.badge{display:inline-block;padding:1px 8px;border-radius:999px;font-size:11px}
.badge.on{background:#173c2c;color:#3ddc84}
.badge.off{background:#3c1a1a;color:#ff6b6b}
.badge.idle{background:#26262e;color:#9a9aa8}
#clearBtn{background:#2a2a66;border:none;color:#fff;border-radius:6px;
  padding:3px 12px;cursor:pointer;font-size:12px}
#clearBtn:hover{background:#33337e}
deep-chat{width:100%;height:calc(100% - 92px);display:block}
@media (max-width:700px){.panel{flex-direction:column;gap:6px;align-items:flex-start}}
</style>
</head>
<body>
<header><h1>AI 检测助手</h1><span class="hint" id="meta">—</span>
<button id="clearBtn">清空会话</button></header>
<div class="panel">
  <span class="item"><span class="k">模型</span><span class="v" id="i-model">-</span></span>
  <span class="item"><span class="k">后端</span><span class="v" id="i-backend">-</span></span>
  <span class="item"><span class="k">上下文</span><span class="v" id="i-ctx">-</span></span>
  <span class="item"><span class="k">加载</span><span id="i-loaded">-</span></span>
  <span class="item"><span class="k">写工具</span><span id="i-write">-</span></span>
  <span class="item"><span class="k">待确认写入</span><span id="i-pending">-</span></span>
  <span class="item"><span class="k">知识库</span><span class="v" id="i-kb">-</span></span>
</div>
<deep-chat
  connect='{"url":"/ai/chat/stream","method":"POST","stream":true}'
  history='[{"text":"你好，我是检测系统的 AI 助手。可以问我：某个条码的检测记录、最近几小时的统计、当前相机参数。","role":"ai"}]'
  textInput='{"placeholder":{"text":"问点什么…（例如：查一下条码 ABC123）"}}'
  chatStyle='{"border":"none","fontFamily":"inherit"}'
  messageStyle='{"default":{"backgroundColor":"transparent","fontSize":"14px"}}'
></deep-chat>
<script>
var $=function(id){return document.getElementById(id);};
function badge(el,on,onTxt,offTxt){el.className=on?'badge on':'badge off';el.textContent=on?onTxt:offTxt;}
async function infoTick(){
  try{
    var r=await fetch('/ai/info',{cache:'no-store'});
    var s=await r.json();
    $('i-model').textContent=s.model||'(未配置)';
    $('i-backend').textContent=s.backend||'-';
    $('i-ctx').textContent=(s.contextSize?Math.round(s.contextSize/1024)+' K':'?')+' / 回复 '+s.maxTokens;
    badge($('i-loaded'),!!s.loaded,'已加载','未加载');
    badge($('i-write'),!!s.allowWriteTools,'开启','关闭');
    badge($('i-pending'),!!s.pendingWrite,'有','无');
    $('i-kb').textContent=(s.knowledgeCount||0)+' 篇'+(s.knowledge&&s.knowledge.length?('：'+s.knowledge.join('、')):'');
    $('meta').textContent='更新于 '+new Date().toLocaleTimeString();
  }catch(e){}
}
$('clearBtn').onclick=function(){
  $('clearBtn').disabled=true;
  fetch('/ai/clear',{method:'POST',cache:'no-store'}).then(function(r){return r.json();}).then(function(j){
    alert(j&&j.ok?'已开启新会话，刷新页面可见新的对话记录。':'清空失败：'+(j&&j.msg||'未知错误'));
    infoTick();
  }).finally(function(){ $('clearBtn').disabled=false; });
};
setInterval(infoTick,5000);
infoTick();
</script>
</body>
</html>
""";
    }
}
