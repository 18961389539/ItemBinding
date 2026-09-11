using System.IO;
using System.Windows;
using System.Windows.Media;
using LicenseCore;

namespace DemoApp;

public partial class MainWindow : Window
{
    private const ushort ProductId = 1;
    private readonly byte[] _machineFingerprint = HardwareFingerprint.ComputeFingerprintBytes();
    private string? _publicKeyPem;

    public MainWindow()
    {
        InitializeComponent();
        MachineBox.Text = MachineCode.Encode(_machineFingerprint);
        TryLoadPublicKey();
    }

    private void TryLoadPublicKey()
    {
        // 演示加载程序目录 keys/license_public.pem；真实产品应把公钥文本编译进程序集
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "keys", "license_public.pem"),
            Path.Combine(Directory.GetCurrentDirectory(), "license_public.pem"),
        };
        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                _publicKeyPem = File.ReadAllText(path);
                PublicKeyBox.Text = _publicKeyPem;
                return;
            }
        }
        PublicKeyBox.Text = "（未找到公钥文件 — 请把 license_public.pem 放到程序目录 keys/ 下，或改用 LicenseManager 生成密钥对）";
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        MachineBox.Text = MachineCode.Encode(HardwareFingerprint.ComputeFingerprintBytes());
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_publicKeyPem))
        {
            ShowResult("公钥未加载，无法校验", Brushes.DarkOrange);
            return;
        }

        var result = LicenseValidator.Validate(CodeBox.Text, _machineFingerprint, _publicKeyPem, ProductId);
        var (text, color) = result.Status switch
        {
            LicenseStatus.Valid => ($"✓ 激活成功 — {result.Message}", Brushes.SeaGreen),
            LicenseStatus.Expired => ($"✗ {result.Message}", Brushes.IndianRed),
            LicenseStatus.InvalidSignature => ("✗ 签名无效：激活码被篡改或非本厂商签发", Brushes.IndianRed),
            LicenseStatus.MachineMismatch => ("✗ 机器不匹配：激活码绑定的是其他机器", Brushes.IndianRed),
            LicenseStatus.WrongProduct => ($"✗ 产品不匹配：{result.Message}", Brushes.IndianRed),
            _ => ("✗ 激活码格式非法", Brushes.IndianRed),
        };
        ShowResult(text, color);
    }

    private void OnSimulateForgery(object sender, RoutedEventArgs e)
    {
        // 演示：把用户输入的激活码改一个字符，展示签名校验如何拦截伪造
        if (string.IsNullOrWhiteSpace(CodeBox.Text))
        {
            ShowResult("先粘贴一个真实激活码，再点此按钮模拟篡改", Brushes.DarkOrange);
            return;
        }
        var chars = CodeBox.Text.ToCharArray();
        chars[^1] = chars[^1] == 'A' ? 'B' : 'A';
        CodeBox.Text = new string(chars);
        ShowResult("已把激活码末位改掉一个字符，现在校验看结果 →", Brushes.DarkOrange);
    }

    private void ShowResult(string text, Brush color)
    {
        ResultText.Text = text;
        ResultText.Foreground = color;
        ResultCard.BorderBrush = color;
    }
}
