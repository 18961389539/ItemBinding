using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace AIAgent.Mcp;

internal static class VisionMcpStdioServer
{
    public static async Task RunAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        using var app = builder.Build();
        await app.RunAsync().ConfigureAwait(false);
    }
}
