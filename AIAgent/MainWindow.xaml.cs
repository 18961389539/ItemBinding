using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AIAgent.Mcp;

namespace AIAgent
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _statusTimer;
        private bool _isRefreshing;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;

            _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _statusTimer.Tick += StatusTimer_Tick;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await VisionWorkspace.InitializeAsync(ScannerImageViewer);
                await VisionMcpServer.StartAsync();
                Debug.WriteLine("MCP server started on http://127.0.0.1:32145/mcp");

                await RefreshScannerStatusAsync();
                _statusTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动 MCP 或扫码工作区失败: {ex}");
                ScannerConnectionValue.Text = "启动失败";
                ScannerConnectionValue.Foreground = Brushes.IndianRed;
                ScannerStatusMessageValue.Text = ex.Message;
            }
        }

        private async void StatusTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshScannerStatusAsync();
        }

        private async Task RefreshScannerStatusAsync()
        {
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            try
            {
                var deviceInfo = await VisionWorkspace.GetScannerDeviceInfoAsync();
                if (!deviceInfo.Success)
                {
                    SetScannerStatus("未连接", Brushes.IndianRed, deviceInfo.Message);
                    SetScannerDeviceInfo(null);
                    return;
                }

                SetScannerStatus("已连接", Brushes.SeaGreen, deviceInfo.Message);
                SetScannerDeviceInfo(deviceInfo);
            }
            catch (Exception ex)
            {
                SetScannerStatus("刷新失败", Brushes.DarkOrange, ex.Message);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void SetScannerStatus(string statusText, Brush foreground, string message)
        {
            ScannerConnectionValue.Text = statusText;
            ScannerConnectionValue.Foreground = foreground;
            ScannerStatusMessageValue.Text = message;
        }

        private void SetScannerDeviceInfo(ScannerDeviceInfoDto? deviceInfo)
        {
            if (deviceInfo is null)
            {
                ScannerIndexValue.Text = "-";
                ScannerDeviceNameValue.Text = "-";
                ScannerModelValue.Text = "-";
                ScannerSerialValue.Text = "-";
                ScannerInterfaceValue.Text = "-";
                ScannerIpValue.Text = "-";
                ScannerMacValue.Text = "-";
                ScannerVersionValue.Text = "-";
                return;
            }

            ScannerIndexValue.Text = deviceInfo.Index.ToString();
            ScannerDeviceNameValue.Text = TextOrDash(deviceInfo.UserDefinedName) == "-"
                ? TextOrDash(deviceInfo.ModelName)
                : deviceInfo.UserDefinedName;
            ScannerModelValue.Text = TextOrDash(deviceInfo.ModelName);
            ScannerSerialValue.Text = TextOrDash(deviceInfo.SerialNumber);
            ScannerInterfaceValue.Text = TextOrDash(deviceInfo.InterfaceType);
            ScannerIpValue.Text = TextOrDash(deviceInfo.IpAddress);
            ScannerMacValue.Text = TextOrDash(deviceInfo.MacAddress);
            ScannerVersionValue.Text = TextOrDash(deviceInfo.DeviceVersion);
        }

        private static string TextOrDash(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private async void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                _statusTimer.Stop();
                await VisionMcpServer.StopAsync();
                await VisionWorkspace.ShutdownAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"关闭 MCP 或扫码工作区失败: {ex}");
            }
        }
    }
}