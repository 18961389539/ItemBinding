using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using AIAgent.Mcp;

namespace AIAgent
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (HasArgument(e.Args, "--stdio"))
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                try
                {
                    await VisionWorkspace.InitializeAsync().ConfigureAwait(false);
                    await VisionMcpStdioServer.RunAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"stdio MCP server failed: {ex}");
                    Debug.WriteLine($"stdio MCP server failed: {ex}");
                    Shutdown(1);
                    return;
                }
                finally
                {
                    await VisionWorkspace.ShutdownAsync().ConfigureAwait(false);
                }

                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        private static bool HasArgument(string[] args, string expected)
        {
            return args.Any(arg => string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase));
        }
    }

}
