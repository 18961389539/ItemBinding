using System;
using System.Windows.Controls;

namespace MainAPP.Views
{
    /// <summary>
    /// RecipesManageView.xaml 的交互逻辑
    /// </summary>
    public partial class RecipesManageView : UserControl, IDisposable
    {
        public RecipesManageView()
        {
            InitializeComponent();
            // H76: 不在 Unloaded 中 Dispose ViewModel（与 HomeView 策略一致，
            // 避免 TabControl 切换标签时 ViewModel 被释放导致功能永久失效）。
            // 改为由 MainWindow.OnClosed 在窗口关闭时显式调用 Dispose。
        }

        /// <summary>
        /// 释放 ViewModel 资源。由 MainWindow.OnClosed 在窗口关闭时调用。
        /// </summary>
        public void Dispose()
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
