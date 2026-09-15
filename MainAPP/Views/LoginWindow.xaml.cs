using MainAPP.Models;
using MainAPP.Services;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MainAPP.Views
{
    public partial class LoginWindow : Window
    {
        /// <summary>UI-FIX(2026-08-13): 校验失败时的红色边框画刷（Material Red 700）</summary>
        private static readonly Brush ErrorBorderBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F));
        /// <summary>UI-FIX(2026-08-13): 提交中标志，防止重复点击登录触发多次校验</summary>
        private bool _isSubmitting;

        public LoginWindow()
        {
            InitializeComponent();
            // 2026-09-15 分辨率适配：920×600 在 1024×768 @100% 下可放，
            // 但目标机若为 125% 缩放（有效宽 819 DIP）就会超出屏幕。
            // 仅收敛尺寸、不改动本窗口自定义的最小尺寸策略。
            WindowSizing.ApplySizeOnly(this, Width, Height);
            // L260: Loaded lambda 改为命名方法
            Loaded += LoginWindow_Loaded;
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UserNameTextBox.Text = Settings.Instance.UserName;
            // UI-FIX(2026-08-13): 页脚显示程序集版本号
            VersionText.Text = $"物码绑定系统 v{GetAppVersion()}";
            PasswordTextBox.Focus();
        }

        private static string GetAppVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                return "1.0.0";
            }
        }

        public string EnteredUserName => UserNameTextBox.Text.Trim();

        public string EnteredPassword => PasswordTextBox.Password;

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // UI-FIX(2026-08-13): 登录改为窗口内校验：空值/账号密码错误不再弹系统框，
            // 在表单内联提示并红边高亮，校验通过才关闭窗口返回 true。
            if (_isSubmitting)
            {
                return;
            }

            var userName = EnteredUserName;
            var password = EnteredPassword;

            if (string.IsNullOrEmpty(userName))
            {
                ShowError("请输入账号", UserNameTextBox, UserNameErrorBorder);
                return;
            }

            // 允许空密码：系统支持免密账户（如默认 Operator 密码为空字符串），
            // 空密码直接进入认证，由 Authenticate 按存储密码精确匹配判定成败。

            _isSubmitting = true;
            try
            {
                if (AuthService.Instance.Authenticate(userName, password, out _))
                {
                    DialogResult = true;
                    Close();
                    return;
                }

                // 失败：清空密码并聚焦，内联提示（不区分账号/密码错误，避免泄露哪个字段存在）
                ShowError("账号或密码错误，请重新输入", PasswordTextBox, PasswordErrorBorder);
                PasswordTextBox.Clear();
                PasswordTextBox.Focus();
            }
            finally
            {
                _isSubmitting = false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// UI-FIX(2026-08-13): 显示内联错误：红字提示 + 输入框外层 Border 红边 + 聚焦目标输入框。
        /// </summary>
        private void ShowError(string message, Control focusTarget, Border errorBorder)
        {
            ErrorTextBlock.Text = message;
            ErrorTextBlock.Visibility = Visibility.Visible;
            if (errorBorder is not null)
            {
                errorBorder.BorderBrush = ErrorBorderBrush;
            }
            focusTarget?.Focus();
        }

        /// <summary>
        /// UI-FIX(2026-08-13): 清除错误提示与红边（输入内容变化时自动调用）。
        /// </summary>
        private void ClearError()
        {
            ErrorTextBlock.Visibility = Visibility.Collapsed;
            UserNameErrorBorder.BorderBrush = Brushes.Transparent;
            PasswordErrorBorder.BorderBrush = Brushes.Transparent;
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ErrorTextBlock.Visibility == Visibility.Visible)
            {
                ClearError();
            }
        }

        private void PasswordTextBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ErrorTextBlock.Visibility == Visibility.Visible)
            {
                ClearError();
            }
        }
    }
}
