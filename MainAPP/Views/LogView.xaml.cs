using MainAPP.Services;
using MainAPP.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MainAPP.Views
{
    /// <summary>
    /// LogView.xaml 的交互逻辑
    /// 2026-09-12: 接线实时跟随滚动与「问 AI 助手」标签跳转事件。
    /// </summary>
    public partial class LogView : UserControl
    {
        private readonly LogViewModel _viewModel;

        public LogView()
        {
            InitializeComponent();

            _viewModel = new LogViewModel();
            DataContext = _viewModel;
            Loaded += LogView_Loaded;
            // H85a: Unloaded 时释放 DispatcherTimer，避免泄漏
            Unloaded += LogView_Unloaded;
            // 实时跟随：每轮静默刷新完成后滚动到最新一条（日志按 id 倒序 = 第一行）
            _viewModel.ScrollToNewestRequested += LogViewModel_ScrollToNewestRequested;
            // 问 AI 助手：跳转到主窗口 AI 助手标签页
            _viewModel.AiAssistRequested += LogViewModel_AiAssistRequested;
        }

        private void LogView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= LogView_Loaded;
            _viewModel.ReloadLogsCommand.Execute(null);
        }

        private void LogView_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= LogView_Unloaded;
            _viewModel.ScrollToNewestRequested -= LogViewModel_ScrollToNewestRequested;
            _viewModel.AiAssistRequested -= LogViewModel_AiAssistRequested;
            _viewModel.Dispose();
        }

        private void LogViewModel_ScrollToNewestRequested()
        {
            if (LogDataGrid.Items.Count > 0)
            {
                LogDataGrid.ScrollIntoView(LogDataGrid.Items[0]);
            }
        }

        private void LogViewModel_AiAssistRequested()
        {
            if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ActivateAiChatTab();
                NotificationService.Info("日志全文已复制到剪贴板，可在 AI 助手中粘贴提问。");
            }
        }

        private void LogDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CopySelectedCommand.Execute(null);
        }
    }
}
