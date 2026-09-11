using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using LicenseCore;
using Microsoft.Win32;

namespace LicenseManager;

/// <summary>
/// 极简激活码生成器：输入机器码 → 生成绑定该机器的激活码。
/// 私钥自动加载（记住上次路径 / 程序目录 keys / 向上查找 LicenseSystem/keys），可手动更换。
/// </summary>
public partial class MainWindow : Window
{
    private string? _privateKeyPath;

    public MainWindow()
    {
        InitializeComponent();
        DaysRadio.Checked += (_, _) => DaysBox.IsEnabled = true;
        PermRadio.Checked += (_, _) => DaysBox.IsEnabled = false;
        DateRadio.Checked += (_, _) => DateBox.IsEnabled = true;
        PermRadio.Checked += (_, _) => DateBox.IsEnabled = false;
        DaysRadio.Checked += (_, _) => DateBox.IsEnabled = false;
        DateRadio.Checked += (_, _) => DaysBox.IsEnabled = false;

        // 私钥加载顺序：上次记忆路径 → 程序目录 keys/ → 向上查找源码目录 LicenseSystem/keys/
        if (!TryLoadRememberedKey())
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "keys", "license_private.pem"),
                FindUpwardKey(AppContext.BaseDirectory),
            };
            foreach (var path in candidates)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    LoadPrivateKey(path);
                    break;
                }
            }
        }

        if (_privateKeyPath is null)
            KeyStatus.Text = "未加载私钥（点击\"选择私钥\"加载 license_private.pem）";
    }

    private void OnCopyLocalMachine(object sender, RoutedEventArgs e)
    {
        try
        {
            MachineCodeBox.Text = MachineCode.Create();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"采集本机机器码失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnGenerate(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_privateKeyPath))
            {
                MessageBox.Show("请先加载私钥（点击下方\"选择私钥\"，选 license_private.pem）", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var machine = MachineCodeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(machine))
            {
                MessageBox.Show("请填写机器码（或点\"采集本机\"）", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fp = MachineCode.TryParse(machine)
                     ?? throw new FormatException("机器码格式非法（应为 4 组 Base32 字符，如 XXXXX-XXXXX-XXXXX-XXXXX）");

            uint expireUnix;
            if (DaysRadio.IsChecked == true)
                expireUnix = int.TryParse(DaysBox.Text, out var days) && days > 0
                    ? (uint)DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds() : 0;
            else if (DateRadio.IsChecked == true && DateBox.SelectedDate is DateTime date)
                expireUnix = (uint)new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.Zero).ToUnixTimeSeconds();
            else
                expireUnix = 0; // 永久

            // 单机绑定：激活码内嵌机器哈希，仅该机器可激活
            var payload = ActivationPayload.Create(
                ActivationPayload.TypeSingleMachine, productId: 1,
                MachineCode.ComputeHash(fp), expireUnix, maxMachines: 1);

            using var privateKey = LicenseKeys.LoadPrivateKey(File.ReadAllText(_privateKeyPath));
            var code = ActivationCode.Generate(privateKey, payload);

            ResultBox.Text = code;
            Clipboard.SetText(code);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"生成失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopyResult(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ResultBox.Text)) return;
        Clipboard.SetText(ResultBox.Text);
    }

    private void OnBrowsePrivateKey(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PEM 私钥|*.pem|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true)
            LoadPrivateKey(dialog.FileName);
    }

    private void LoadPrivateKey(string path)
    {
        try
        {
            LicenseKeys.LoadPrivateKey(File.ReadAllText(path)).Dispose(); // 校验可加载
            _privateKeyPath = path;
            // 记住路径，下次启动自动加载
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "manager.config"), path);
            }
            catch { /* 记忆失败不影响使用 */ }
            KeyStatus.Text = $"私钥: {path}";
            KeyStatus.Foreground = Brushes.SeaGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"私钥加载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryLoadRememberedKey()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "manager.config");
            if (!File.Exists(configPath)) return false;
            var path = File.ReadAllText(configPath).Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            LoadPrivateKey(path);
            return _privateKeyPath is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>从当前目录逐级向上查找 LicenseSystem/keys/license_private.pem（开发/源码场景直接可用）。</summary>
    private static string? FindUpwardKey(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            if (!dir.Name.Equals("LicenseSystem", StringComparison.OrdinalIgnoreCase))
                continue;
            var key = Path.Combine(dir.FullName, "keys", "license_private.pem");
            if (File.Exists(key)) return key;
        }
        return null;
    }
}
