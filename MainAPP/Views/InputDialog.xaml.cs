using System.Windows;

namespace MainAPP.Views
{
    /// <summary>
    /// 输入对话框。替代 SettingsViewModel 中原本在代码中拼接 TextBlock/TextBox/Button 的反模式。
    /// 通过 DialogService.ShowInputDialogAsync 调用，VM 不再直接构造 UI 元素。
    /// </summary>
    public partial class InputDialog : Window
    {
        /// <summary>提示消息（绑定到 XAML 中的 TextBlock）。</summary>
        public string Message { get; }

        /// <summary>用户输入的文本（双向绑定到 XAML 中的 TextBox）。</summary>
        public string InputValue { get; set; }

        public InputDialog(string title, string message, string defaultValue = "")
        {
            InitializeComponent();
            Title = title;
            Message = message;
            InputValue = defaultValue;
            DataContext = this;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
