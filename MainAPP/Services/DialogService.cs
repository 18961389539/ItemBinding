using MainAPP.Models;
using MainAPP.ViewModels;
using MainAPP.Views;
using System.Windows;

namespace MainAPP.Services
{
    /// <summary>
    /// IDialogService 的默认实现，使用 WPF Window 显示对话框。
    /// 封装原本散落在 ViewModel 中的 new Window 调用，让 VM 不再直接依赖 View。
    /// </summary>
    public class DialogService : IDialogService
    {
        /// <summary>
        /// 显示配方编辑对话框（模态）。
        /// 原代码：RecipeWindow recipesView = new RecipeWindow(SelectedRecipe); recipesView.ShowDialog();
        /// </summary>
        public Task<bool> ShowRecipeEditorAsync(RecipeViewModel recipeVm)
        {
            var window = new RecipeWindow(recipeVm)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            // ShowDialog 是阻塞的，用 Task.FromResult 包装以符合 async 签名
            return Task.FromResult(window.ShowDialog() ?? false);
        }

        /// <summary>
        /// 显示图表分析窗口（非模态）。
        /// 原代码：var chartsWindow = new ChartsView(items); chartsWindow.Show();
        /// </summary>
        public void ShowCharts(IReadOnlyList<DbModel> items)
        {
            var window = new ChartsView(items)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.Show();
        }

        /// <summary>
        /// 显示文本输入对话框（模态）。
        /// 替代 SettingsViewModel 中原本在代码中拼接 TextBlock/TextBox/Button 的反模式，
        /// 改用 InputDialog.xaml 定义 UI。
        /// </summary>
        public Task<string?> ShowInputDialogAsync(string title, string message, string defaultValue = "")
        {
            var dialog = new InputDialog(title, message, defaultValue)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            bool? result = dialog.ShowDialog();
            return Task.FromResult(result == true ? dialog.InputValue : null);
        }

        /// <summary>
        /// 显示登录窗口（模态）。
        /// 原代码（MainWindow.xaml.cs）：var loginWindow = new LoginWindow { Owner = this }; loginWindow.ShowDialog();
        /// </summary>
        public Task<bool> ShowLoginAsync()
        {
            var loginWindow = new LoginWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            return Task.FromResult(loginWindow.ShowDialog() ?? false);
        }

        /// <summary>
        /// 显示确认对话框（模态）。直接委托给现有 NotificationService.Ask。
        /// </summary>
        public bool Confirm(string message, string caption = "", MessageBoxButton buttons = MessageBoxButton.YesNo, MessageBoxImage icon = MessageBoxImage.Question)
        {
            return NotificationService.Ask(message, caption, buttons, icon) == MessageBoxResult.Yes;
        }
    }
}
