using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using MainAPP.Models;

namespace MainAPP.Services;

/// <summary>
/// TCP Server 配方切换服务：上位机/机械手可通过 TCP 连接发送指令切换配方。
/// 行分隔协议（支持 \n 或 \r\n）：
///   SWITCH_RECIPE:&lt;配方名&gt;  切换配方，应答 OK 或 NOT_FOUND:&lt;原因&gt;
///   GET_RECIPE             查询当前配方，应答 CURRENT:&lt;配方名&gt;（无配方时 CURRENT:NONE）
///   LIST_RECIPES           列出全部配方，逐行 RECIPE:&lt;配方名&gt;，结尾 OK
///   其他                    应答 ERROR:UNKNOWN_CMD
/// 配方切换复用 RecipesManage.SetAndSaveCurrentRecipe（与界面操作同一条链路），
/// 并切到 UI 线程执行，与界面切换行为一致。
/// </summary>
public sealed class RecipeTcpServerService : IDisposable
{
    /// <summary>默认监听端口（后续如需可配置化，从 Settings 读取）。</summary>
    public const int DefaultPort = 5000;

    private readonly int _port;
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public RecipeTcpServerService(int port = DefaultPort)
    {
        _port = port;
    }

    public bool IsRunning => _listener is not null;

    /// <summary>启动监听（非阻塞）。端口被占用或启动失败会记录日志并置空监听。</summary>
    public void Start()
    {
        if (_listener is not null) return;
        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _acceptLoop = AcceptLoopAsync(_cts.Token);
            LogService.Instance.Info($"配方切换 TCP 服务已启动，监听端口 {_port}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"配方切换 TCP 服务启动失败: {ex}");
            _listener = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>停止监听并关闭所有客户端连接。</summary>
    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        foreach (var client in _clients.Keys)
        {
            try { client.Dispose(); } catch { }
        }
        _clients.Clear();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        _acceptLoop = null;
        LogService.Instance.Info("配方切换 TCP 服务已停止");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                if (!_clients.TryAdd(client, 0))
                {
                    client.Dispose();
                    continue;
                }
                _ = Task.Run(() => HandleClientAsync(client, ct));
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"配方切换 TCP 接收循环异常: {ex}");
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true,
            };

            while (!ct.IsCancellationRequested)
            {
                var line = await ReadLineWithTimeoutAsync(stream, ct).ConfigureAwait(false);
                if (line is null) break; // 客户端关闭

                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                var response = ProcessCommand(trimmed);
                await writer.WriteLineAsync(response.AsMemory(), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 服务停止
        }
        catch (Exception ex)
        {
            LogService.Instance.Warning($"配方切换 TCP 客户端处理异常: {ex.Message}");
        }
        finally
        {
            _clients.TryRemove(client, out _);
            try { client.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// 读取一行指令（\n 或 \r\n 结尾）。容错：客户端未发送换行符时，
    /// 数据累积超过 800ms 无新数据也按整行处理，避免手动/调试工具发送不带换行的指令被永久挂起。
    /// </summary>
    private static async Task<string?> ReadLineWithTimeoutAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        var deadline = DateTime.UtcNow.AddSeconds(0.8);

        while (!ct.IsCancellationRequested)
        {
            if (stream.DataAvailable)
            {
                var n = await stream.ReadAsync(buf, ct).ConfigureAwait(false);
                if (n == 0) return sb.Length > 0 ? sb.ToString() : null; // 连接关闭
                if (buf[0] == (byte)'\n')
                {
                    if (sb.Length > 0 && sb[^1] == '\r') sb.Length--; // 去掉 \r\n 的 \r
                    return sb.ToString();
                }
                sb.Append((char)buf[0]);
                deadline = DateTime.UtcNow.AddSeconds(0.8); // 有数据则刷新等待窗口
            }
            else if (sb.Length > 0 && DateTime.UtcNow > deadline)
            {
                return sb.ToString(); // 超时无换行 → 容错按整行处理
            }
            else
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    private static string ProcessCommand(string line)
    {
        try
        {
            if (line.StartsWith("SWITCH_RECIPE:", StringComparison.OrdinalIgnoreCase))
            {
                var name = line["SWITCH_RECIPE:".Length..].Trim();
                if (name.Length == 0)
                    return "ERROR:EMPTY_RECIPE_NAME";

                var recipe = RecipesManage.Instance.FindByName(name);
                if (recipe is null)
                {
                    // RTC(2026-08-06): 配方不存在也弹 Toast 提示操作员（橙色 Warning）
#pragma warning disable VSTHRD001 // WPF 标准 UI 线程调度,与项目内 ImageViewer/LogService 一致
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        NotificationService.Warning($"配方不存在: {name}"));
#pragma warning restore VSTHRD001
                    return $"NOT_FOUND:{name}";
                }

                // 切到 UI 线程执行，与界面切换配方行为一致（订阅者可能访问 UI）
#pragma warning disable VSTHRD001 // WPF 标准 UI 线程调度,与项目内 ImageViewer/LogService 一致
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    RecipesManage.Instance.SetAndSaveCurrentRecipe(recipe);
                    // RTC(2026-08-06): 切换成功弹出 Toast 提示操作员
                    NotificationService.Success($"已切换配方: {name}");
                });
#pragma warning restore VSTHRD001
                LogService.Instance.Info($"TCP 指令切换配方: {name}");
                return "OK";
            }

            if (line.Equals("GET_RECIPE", StringComparison.OrdinalIgnoreCase))
            {
                var current = RecipesManage.Instance.CurrentRecipe?.Name;
                return $"CURRENT:{current ?? "NONE"}";
            }

            if (line.Equals("LIST_RECIPES", StringComparison.OrdinalIgnoreCase))
            {
                var names = RecipesManage.Instance.Recipes.Select(r => r.Name).ToArray();
                var sb = new StringBuilder();
                foreach (var n in names)
                    sb.Append("RECIPE:").Append(n).Append("\r\n");
                sb.Append("OK");
                return sb.ToString();
            }

            return "ERROR:UNKNOWN_CMD";
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"配方切换 TCP 指令处理异常: {line} => {ex}");
            return $"ERROR:{ex.Message}";
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
