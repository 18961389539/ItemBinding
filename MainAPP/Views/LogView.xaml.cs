using MainAPP.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MainAPP.Views
{
    /// <summary>
    /// LogView.xaml 的交互逻辑
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
        }

        private void LogView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= LogView_Loaded;
            _viewModel.ReloadLogsCommand.Execute(null);
        }

        private void LogView_Unloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= LogView_Unloaded;
            _viewModel.Dispose();
        }

        private void LogDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _viewModel.CopySelectedCommand.Execute(null);
        }
    }
}
