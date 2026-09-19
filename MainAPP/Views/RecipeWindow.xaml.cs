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

            // 2026-09-15 画面示教：点击图像把点击处设为抓取点。
            // 2026-09-19 改为以可拖拽十字 ROI 为主：Press 记录起点，Up 判断位移——
            // 位移小 = 单击取点（隧道事件不被控件吞掉）；位移大 = 拖动十字 ROI，不干预
            // （ROI 拖动期间 Position 已被实时反算，松手无需再取点，且不 Handled 让控件正常收尾）。
            imageViewer.PreviewMouseLeftButtonDown += imageViewer_PreviewMouseLeftButtonDown;
            imageViewer.PreviewMouseLeftButtonUp += imageViewer_PreviewMouseLeftButtonUp;
        }

        private System.Windows.Point _grabPressScreenPos;

        /// <summary>记录按下位置，用于区分单击取点与拖动十字 ROI。</summary>
        private void imageViewer_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is RecipeViewModel { IsGrabTeachMode: true })
            {
                _grabPressScreenPos = e.GetPosition(imageViewer);
            }
        }

        /// <summary>
        /// 画面示教：单击图像，把点击处反算为长/短轴偏移并写入配方。
        /// 图像像素坐标由 ImageViewer.ImageContainer 提供（缩放/平移由其内部变换吸收）。
        /// </summary>
        private void imageViewer_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is not RecipeViewModel vm || !vm.IsGrabTeachMode)
            {
                return;
            }

            // 位移超过阈值视为拖拽十字 ROI：不取点、不 Handled，交给控件完成拖拽收尾（CompleteEdit）
            var upScreenPos = e.GetPosition(imageViewer);
            if ((upScreenPos - _grabPressScreenPos).Length > 4)
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
            // 2026-09-19: 新增推理结果后自动切到「推理结果」页，避免用户找不到结果在哪
            viewModel.InferenceResultAdded += OnInferenceResultAdded;
            // 2026-09-19: 注册 ImageViewer，供示教十字 ROI（GrabPointRoi）加入/移除（ViewerState）
            RecipeWindowGrabRoiHost.Register(imageViewer);
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
            Closed += (_, _) =>
            {
                if (DataContext is RecipeViewModel vm)
                {
                    vm.InferenceResultAdded -= OnInferenceResultAdded;
                }
                RecipeWindowGrabRoiHost.Unregister(imageViewer);
            };
        }

        /// <summary>「推理结果」Tab 索引（与 XAML TabControl 顺序一致：0采集/1坐标系/2AI/3推理结果）</summary>
        private const int ResultTabIndex = 3;

        private void OnInferenceResultAdded()
        {
            // 新结果出现时切到结果页；已在结果页则保持原位置不动
            if (MainTabControl.SelectedIndex != ResultTabIndex)
            {
                MainTabControl.SelectedIndex = ResultTabIndex;
            }
        }

        private void ShortcutsButton_Click(object sender, RoutedEventArgs e)
        {
            shortcutsPopup.IsOpen = !shortcutsPopup.IsOpen;
        }

        private void SyncParamsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not RecipeViewModel vm)
            {
                return;
            }

            new RecipeSyncWindow(vm.Recipe) { Owner = this }.ShowDialog();
        }

        private void HealthBadge_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            healthPopup.PlacementTarget = (System.Windows.UIElement)sender;
            healthPopup.IsOpen = !healthPopup.IsOpen;
        }

        private async void RecipeWindow_Closing(object? sender, CancelEventArgs e)
        {
            try
            {
                Closing -= RecipeWindow_Closing;
                if (DataContext is RecipeViewModel vm)
                {
                    // P0-FIX: 关闭前询问是否保存修改，避免误改参数后被静默保存；
                    // 无未保存修改（IsDirty）时直接关闭，不做无意义打扰
                    if (vm.IsDirty)
                    {
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
