using MainAPP.Services;
using MainAPP.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MainAPP.Views
{
    /// <summary>
    /// SettingsView.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
            Unloaded += SettingsView_Unloaded;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SettingsView_Loaded;
            if (DataContext is SettingsViewModel vm)
            {
                PasswordBox.Password = vm.Password;
            }
        }

        // S7: 离开设置页时若有未保存修改（IsDirty=true），弹窗提醒用户
        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel vm) return;
            if (!vm.IsDirty) return;

            var result = NotificationService.Ask(
                "设置有未保存的修改，确定要离开吗？未保存的修改将丢失。",
                "未保存的修改",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            // 用户选 No 想留下，但 Unloaded 已经发生无法取消，这里仅作为信息提示；
            // 实际防止丢失的方式是保存按钮统一通过 SaveCommand 显式提交。
            // 注：WPF UserControl 的 Unloaded 不能取消，此提示仅提醒用户下次注意。
            _ = result;
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.Password = PasswordBox.Password;
            }
        }

        /// <summary>
        /// int 类型 TextBox 输入验证，过滤非数字字符
        /// </summary>
        private void NumberValidation_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>
        /// int 类型 TextBox 粘贴验证，过滤非数字粘贴内容
        /// </summary>
        private void NumberValidation_Pasting(object sender, System.Windows.DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(System.Windows.DataFormats.Text))
            {
                var text = e.DataObject.GetData(System.Windows.DataFormats.Text) as string;
                if (text != null)
                {
                    foreach (char c in text)
                    {
                        if (!char.IsDigit(c))
                        {
                            e.CancelCommand();
                            return;
                        }
                    }
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
    }
}
