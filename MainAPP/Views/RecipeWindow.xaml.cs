using MainAPP.Services;
using MainAPP.ViewModels;
using System.ComponentModel;
using System.Windows;

// VSTHRD100/VSTHRD101: Loaded/Closing 是 WPF 生命周期事件处理器，必须保持 async void；
// 已包 try-catch，异常不会导致进程崩溃
#pragma warning disable VSTHRD100
#pragma warning disable VSTHRD101

namespace MainAPP.Views
{
    /// <summary>
    /// RecipeWindow.xaml 的交互逻辑
    /// </summary>
    public partial class RecipeWindow : Window
    {
        public RecipeWindow()
        {
            InitializeComponent();
            // 2026-09-15 分辨率适配：XAML 的 1200×800 在 1024×768 上会超出屏幕，
            // 且原 MinWidth=1024 恰等于屏宽、零余量导致底部按钮不可达。
            // 这里按当前工作区夹取并下调最小尺寸，细节见 WindowSizing。
            WindowSizing.Apply(this, Width, Height);
            Closing += RecipeWindow_Closing;

            // 2026-09-15 画面示教：勾选示教后点击图像，把点击处设为抓取点。
            // 用 PreviewMouseLeftButtonUp（隧道事件）确保不被控件内部处理吞掉。
            imageViewer.PreviewMouseLeftButtonUp += imageViewer_PreviewMouseLeftButtonUp;
        }

        /// <summary>
        /// 画面示教：点击测试推理图像，把点击处反算为长/短轴偏移并写入配方。
        /// 图像像素坐标由 ImageViewer.ImageContainer 提供（缩放/平移由其内部变换吸收）。
        /// </summary>
        private void imageViewer_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is not RecipeViewModel vm || !vm.IsGrabTeachMode)
            {
                return;
            }

            // ImageContainer 的坐标空间即图像像素（缩放/平移变换作用于该元素自身）
            var pt = e.GetPosition(imageViewer.ImageContainer);
            vm.SetGrabPointByImagePosition(pt.X, pt.Y);
            e.Handled = true;
        }

        public RecipeWindow(RecipeViewModel viewModel) : this()
        {
            DataContext = viewModel;
            // M700: 配方页打开时暂停主循环 + 等当前帧完成
            Loaded += async (_, _) =>
            {
                try
                {
                    if (DataContext is RecipeViewModel vm)
                    {
                        await vm.ActivateAsync().ConfigureAwait(true);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"RecipeWindow 加载激活失败: {ex}");
                }
            };
        }

        private async void RecipeWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                Closing -= RecipeWindow_Closing;
                if (DataContext is RecipeViewModel vm)
                {
                    // P0-FIX: 关闭前询问是否保存修改，避免误改参数后被静默保存
                    var result = NotificationService.Ask(
                        "是否保存配方的修改？",
                        "确认保存",
                        MessageBoxButton.YesNoCancel);
                    if (result == MessageBoxResult.Cancel)
                    {
                        e.Cancel = true;
                        Closing += RecipeWindow_Closing;
                        return;
                    }
                    if (result == MessageBoxResult.Yes)
                    {
                        try { vm.SaveSilently(); }
                        catch (Exception ex) { LogService.Instance.Error($"保存配方失败: {ex}"); }
                    }
                    // 关闭窗口仅停用配方会话（恢复硬触发/主循环 + 清理会话资源），
                    // 不 Dispose VM——RecipeViewModel 由配方列表长期持有并可再次打开，
                    // 过早销毁会导致二次打开后推理/取图抛 ObjectDisposedException。
                    await vm.DeactivateAndClearSessionAsync(skipSave: true).ConfigureAwait(true);
                    imageViewer?.Dispose();
                }
                else
                {
                    imageViewer?.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"RecipeWindow 关闭失败: {ex}");
                try { imageViewer?.Dispose(); } catch { /* 已尽力清理 */ }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
