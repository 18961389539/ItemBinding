using System.Diagnostics;
using System.IO;
using System.Net.Http;
using MainAPP.Models;

namespace MainAPP.Services.AI
{
    /// <summary>
    /// llama-server 侧车管理器（2026-09-12）。
    ///
    /// <para>背景：LLamaSharp 0.27 进程内推理有两个无法在库层绕过的问题——
    /// ① 语法采样链（Grammar/SamplingPipeline）触发原生访问违例（c0000005，已实测）；
    /// ② ChatClient 适配不支持 ChatOptions.Tools。侧车（llama.cpp 官方预编译 llama-server，
    /// OpenAI 兼容 HTTP）一并解决：语法约束走 response_format，思考模式走
    /// <c>--chat-template-kwargs</c>，且推理进程与主程序隔离，崩了也不带垮主程序。</para>
    ///
    /// <para>生命周期：随 AI 首次使用懒启动（<see cref="EnsureStartedAsync"/>），
    /// 应用退出时 <see cref="Stop"/>。进程用 <c>Process.Start</c> 直启并重定向 stderr 到日志，
    /// 便于现场排查。</para>
    /// </summary>
    public sealed class LlamaServerHost : IDisposable
    {
        public static LlamaServerHost Instance { get; } = new();

        private readonly SemaphoreSlim _gate = new(1, 1);
        private Process? _process;
        private bool _disposed;

        /// <summary>侧车进程是否存活。</summary>
        public bool IsRunning => _process is { HasExited: false };

        /// <summary>OpenAI 兼容端点（供 OpenAI 适配器使用）。</summary>
        public string BaseUrl
        {
            get
            {
                var port = MainAPP.Models.Settings.Instance.Ai.LlamaServerPort;
                return $"http://127.0.0.1:{port}/v1";
            }
        }

        /// <summary>
        /// 确保侧车已启动并就绪（/health 返回 ok）。幂等；就绪判定最多等 120 秒
        /// （首次要加载 2.5 GiB 模型，实测约 35~45 秒）。
        /// </summary>
        public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
        {
            // 复用判定：已有健康的侧车（本轮进程或上一轮遗留，甚至外部手动启动）且模型匹配 → 直接用。
            // 否则会在同一端口上反复拉起新实例，新实例绑不上端口即退出，形成请求黑洞。
            if (await IsReadyForModelAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (await IsReadyForModelAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                var settings = MainAPP.Models.Settings.Instance.Ai;
                var exe = settings.LlamaServerExe;
                if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                {
                    throw new FileNotFoundException($"llama-server.exe 不存在或未配置: {exe}");
                }
                if (string.IsNullOrWhiteSpace(settings.ModelPath) || !File.Exists(settings.ModelPath))
                {
                    throw new FileNotFoundException($"GGUF 模型文件不存在或未配置: {settings.ModelPath}");
                }

                // ★ 模型路径必须是纯 ASCII（llama.cpp 原生层限制，已实测）
                var arguments =
                    $"-m {settings.ModelPath}" +
                    $" --host 127.0.0.1 --port {settings.LlamaServerPort}" +
                    $" -c {settings.ContextSize} -ngl {settings.GpuLayers}" +
                    " --jinja" +
                    " --chat-template-kwargs \"{\\\"enable_thinking\\\":false}\"";

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };

                _process = Process.Start(psi)
                    ?? throw new InvalidOperationException("llama-server 进程启动失败");
                _process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        LogService.Instance.Debug($"[llama-server] {e.Data}");
                    }
                };
                _process.BeginErrorReadLine();

                LogService.Instance.Info($"AI 对话：llama-server 侧车已启动（PID {_process.Id}，端口 {settings.LlamaServerPort}）");
            }
            finally
            {
                _gate.Release();
            }

            await WaitReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>轮询 /health 直到就绪或超时（120 秒）。</summary>
        private static async Task WaitReadyAsync(CancellationToken cancellationToken)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var deadline = DateTime.UtcNow.AddSeconds(120);
            Exception? last = null;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var resp = await http.GetAsync(
                        $"http://127.0.0.1:{MainAPP.Models.Settings.Instance.Ai.LlamaServerPort}/health",
                        cancellationToken).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        LogService.Instance.Info("AI 对话：llama-server 就绪");
                        return;
                    }
                    // 200 之外（503 = 模型还在加载）继续等
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    last = ex; // 连接被拒 = 还没监听，继续等
                }

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"llama-server 120 秒内未就绪（最后一次错误: {last?.Message}）");
        }

        /// <summary>
        /// 侧车是否已就绪且加载了期望的模型（/props 会返回 -m 的模型路径）。
        /// 端口被「模型不匹配」的侧车占用时抛出可读异常，而不是静默用错模型。
        /// </summary>
        private static async Task<bool> IsReadyForModelAsync(CancellationToken cancellationToken)
        {
            if (!await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            try
            {
                var settings = MainAPP.Models.Settings.Instance.Ai;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var props = await http.GetStringAsync(
                    $"http://127.0.0.1:{settings.LlamaServerPort}/props", cancellationToken).ConfigureAwait(false);
                var expected = Path.GetFileName(settings.ModelPath ?? string.Empty);
                var matched = string.IsNullOrEmpty(expected) || props.Contains(expected, StringComparison.OrdinalIgnoreCase);
                if (!matched)
                {
                    LogService.Instance.Warning(
                        $"[llama-server] 端口 {settings.LlamaServerPort} 上已有侧车但模型不匹配（期望 {expected}）");
                }
                return matched;
            }
            catch
            {
                return false; // /props 读取失败按未就绪处理，走重启路径
            }
        }

        private static async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                using var resp = await http.GetAsync(
                    $"http://127.0.0.1:{MainAPP.Models.Settings.Instance.Ai.LlamaServerPort}/health",
                    cancellationToken).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>停止侧车（连同子进程树）。</summary>
        public void Stop()
        {
            if (_process is null)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }
                LogService.Instance.Info("AI 对话：llama-server 侧车已停止");
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"停止 llama-server 失败: {ex.Message}");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            _gate.Dispose();
        }
    }
}
