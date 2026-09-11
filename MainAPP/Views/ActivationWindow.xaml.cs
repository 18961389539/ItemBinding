using System.Windows;
using System.Windows.Media;
using MainAPP.Services;

namespace MainAPP.Views;

/// <summary>
/// 激活窗口：展示本机机器码，接收厂商签发的激活码并本地校验。
/// 校验通过返回 true（DialogResult），由 App.OnStartup 决定是否放行进入主界面。
/// </summary>
public partial class ActivationWindow : Window
{
    public ActivationWindow()
    {
        InitializeComponent();
        try
        {
            MachineCodeBox.Text = LicenseService.MachineCodeText;
        }
        catch (Exception ex)
        {
            MachineCodeBox.Text = "（机器码采集失败）";
            ShowMessage($"机器码采集失败: {ex.Message}", Brushes.IndianRed);
        }
    }

    private void OnCopyMachine(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(MachineCodeBox.Text))
        {
            Clipboard.SetText(MachineCodeBox.Text);
            ShowMessage("机器码已复制", Brushes.SeaGreen);
        }
    }

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        if (LicenseService.TryActivate(code, out var message))
        {
            ShowMessage($"✓ {message}", Brushes.SeaGreen);
            DialogResult = true;
        }
        else
        {
            ShowMessage($"✗ {message}", Brushes.IndianRed);
        }
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowMessage(string text, Brush color)
    {
        ResultText.Text = text;
        ResultText.Foreground = color;
        ResultCard.BorderBrush = color;
    }
}
