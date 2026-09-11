using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace AIAgent.Mcp;

internal static class VisionMcpServer
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static WebApplication? _app;

    // REVIEW-FIX: MCP 访问 token。优先读取环境变量 AIAGENT_MCP_TOKEN，
    // 未设置时生成随机 token 并在启动时输出到日志/控制台。
    private static readonly string AccessToken = ResolveAccessToken();

    public static bool IsRunning => _app is not null;

    private static string ResolveAccessToken()
    {
        var configured = Environment.GetEnvironmentVariable("AIAGENT_MCP_TOKEN");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    public static async Task StartAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_app is not null)
            {
                return;
            }

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls(VisionWorkspace.McpUrl);
            builder.Services
                .AddMcpServer()
                .WithHttpTransport(options =>
                {
                    options.Stateless = true;
                })
                .WithToolsFromAssembly();

            var app = builder.Build();

            // REVIEW-FIX: MCP HTTP 端点要求 Authorization: Bearer <token> 头或 ?token= 参数，否则 401
            app.Use(async (context, next) =>
            {
                if (IsAuthorized(context))
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized").ConfigureAwait(false);
            });

            app.MapMcp();
            await app.StartAsync().ConfigureAwait(false);

            _app = app;
            // REVIEW-FIX: 启动时输出访问 token，便于调用方配置 MCP 客户端
            Console.WriteLine($"[VisionMcpServer] MCP 服务已启动: {VisionWorkspace.McpUrl}/mcp");
            Console.WriteLine($"[VisionMcpServer] MCP Access Token: {AccessToken}");
            System.Diagnostics.Debug.WriteLine($"[VisionMcpServer] MCP Access Token: {AccessToken}");
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task StopAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_app is null)
            {
                return;
            }

            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>REVIEW-FIX: 校验 Authorization: Bearer &lt;token&gt; 头或 ?token= 查询参数</summary>
    private static bool IsAuthorized(HttpContext context)
    {
        if (string.IsNullOrEmpty(AccessToken))
        {
            return false;
        }

        string? provided = null;
        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            provided = authorization.Substring("Bearer ".Length).Trim();
        }

        if (string.IsNullOrEmpty(provided))
        {
            provided = context.Request.Query["token"].ToString();
        }

        return !string.IsNullOrEmpty(provided) && FixedTimeEquals(provided, AccessToken);
    }

    /// <summary>REVIEW-FIX: 常量时间比较，避免通过响应时间差探测 token</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }
}