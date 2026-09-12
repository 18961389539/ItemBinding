using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MainAPP.Models;
using MainAPP.Services;
using MainAPP.ViewModels;
using MainAPP.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MainAPP
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// 主窗口 View 层：仅保留 UI 事件转发与窗口生命周期处理。
    /// 业务逻辑（条码计数、FPS/内存/推理耗时更新、登录会话、定时器）已迁移至 <see cref="MainViewModel"/>。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        /// <summary>
        /// REVIEW(2026-08-05): 供 NotificationService 按 token 注册 Growl 容器，
        /// 实现定时强制关闭通知（不受 HandyControl 鼠标悬停暂停计时的影响）。
        /// </summary>
        public static StackPanel? GrowlRoot { get; private set; }

        // 页面名称到 TabItem 索引的映射（Ctrl+1~5 直达）
        private static readonly Dictionary<string, int> PageNameToIndex = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Home", 0 },
            { "Recipe", 1 },
            { "Database", 2 },
            { "Settings", 3 },
            { "About", 5 },
        };

        /// <summary>
        /// 跳转到 AI 助手标签页（按命名元素定位，不依赖 TabItem 顺序索引）。
        /// 调用方：日志页「问 AI 助手」按钮等场景。
        /// </summary>
        public void ActivateAiChatTab()
        {
            MainTabControl.SelectedItem = AiChatTabItem;
        }

        public MainWindow()
        {
            // M128: 构造函数顶层 try-catch，防止初始化异常导致窗口处于不一致状态
            try
            {
                InitializeComponent();
                GrowlRoot = GrowlRootPanel;
                // 从 DI 容器获取 VGT 服务，传递给 MainViewModel
                var toVgtService = App.Services.GetRequiredService<IToVGTService>();
                // 创建 MainViewModel 并设为 DataContext，使 XAML 中的 {Binding ...} 解析到 ViewModel
                _viewModel = new MainViewModel(toVgtService);
                DataContext = _viewModel;
                // 注入导航服务：替代原 9 个 Action/Func 反向回调，VM 通过 INavigationService 访问 UI 元素
                // 使用 Func 工厂返回当前 MainWindow 实例，避免 NavigationService 持有强引用导致循环依赖
                MainViewModel.NavigationService = new NavigationService(() => this);
                // 应用初始窗口设置
                ApplyWindowSettings();
                // H88c: 监听用户交互事件，活跃时重置自动最小化定时器，避免操作中窗口被反复最小化
                PreviewMouseMove += MainWindow_UserActivity;
                PreviewKeyDown += MainWindow_UserActivity;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"MainWindow 初始化失败: {ex}");
                throw;
            }
        }

        // === 登录按钮点击：UI 交互（LoginWindow / 确认框）后转发到 ViewModel ===
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            // M313a: 包 try-catch，分别处理异常，避免未处理异常导致 UI 线程崩溃
            try
            {
                if (_viewModel.IsLoggedIn)
                {
                    // P0-FIX: 登出前加二次确认，避免误触导致操作中断
                    var confirm = NotificationService.Ask(
                        "是否确认退出登录？退出后部分功能将不可用。",
                        "确认登出");
                    if (confirm != MessageBoxResult.Yes)
                        return;
                    _viewModel.Logout();
                    return;
                }

                var loginWindow = new LoginWindow
                {
                    Owner = this
                };

                if (loginWindow.ShowDialog() != true)
                {
                    return;
                }

                _viewModel.Login(loginWindow.EnteredUserName, loginWindow.EnteredPassword);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"LoginButton_Click 执行失败: {ex}");
                try { NotificationService.Error($"登录操作失败: {ex}"); }
                catch { /* UI 不可用，跳过提示 */ }
            }
        }

        // H88c: 用户交互时转发到 ViewModel 重置自动最小化定时器
        private void MainWindow_UserActivity(object sender, InputEventArgs e)
        {
            _viewModel.ResetAutoMinimizeTimer();
        }

        // === 窗口设置（由 ViewModel 通过回调调用） ===
        private void ApplyWindowSettings()
        {
            var settings = Settings.Instance;
            // L368c: 验证窗口尺寸合法性，避免异常设置导致窗口过小无法显示
            // 与 XAML MinWidth/MinHeight 保持一致
            Width = Math.Max(1024, settings.WindowWidth);
            Height = Math.Max(700, settings.WindowHeight);
            Title = settings.WindowTitle;
        }

        private void EnsureWindowMinimized()
        {
            if (WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void EnsureVisibleTabSelected()
        {
            // M268b: 当前选中标签可见时无需调整
            if (MainTabControl.SelectedItem is TabItem selectedTab && selectedTab.Visibility == Visibility.Visible)
            {
                return;
            }

            // 遍历找到第一个可见的标签页，而非假设索引 0 可见
            for (int i = 0; i < MainTabControl.Items.Count; i++)
            {
                if (MainTabControl.Items[i] is TabItem tab && tab.Visibility == Visibility.Visible)
                {
                    MainTabControl.SelectedIndex = i;
                    return;
                }
            }
        }

        // === 快捷键导航（由 ViewModel 命令通过回调调用） ===
        // F1: 跳转到关于页
        private void NavigateToAbout() => NavigateToPage("About");

        // Ctrl+1~5: 按页面名称跳转
        private void NavigateToPage(string? pageName)
        {
            if (string.IsNullOrEmpty(pageName)) return;
            if (PageNameToIndex.TryGetValue(pageName, out int index))
            {
                SelectTabIfVisible(index);
            }
        }

        // Ctrl+Tab: 循环切换到下一个可见页面
        private void NavigateToNextPage()
        {
            int count = MainTabControl.Items.Count;
            if (count == 0) return;
            int current = MainTabControl.SelectedIndex;
            for (int i = 1; i <= count; i++)
            {
                int next = (current + i) % count;
                if (MainTabControl.Items[next] is TabItem tab && tab.Visibility == Visibility.Visible)
                {
                    MainTabControl.SelectedIndex = next;
                    return;
                }
            }
        }

        // Ctrl+Shift+Tab: 循环切换到上一个可见页面
        private void NavigateToPreviousPage()
        {
            int count = MainTabControl.Items.Count;
            if (count == 0) return;
            int current = MainTabControl.SelectedIndex;
            for (int i = 1; i <= count; i++)
            {
                int prev = (current - i + count) % count;
                if (MainTabControl.Items[prev] is TabItem tab && tab.Visibility == Visibility.Visible)
                {
                    MainTabControl.SelectedIndex = prev;
                    return;
                }
            }
        }

        // 选择指定索引的标签页（仅当可见时）
        private void SelectTabIfVisible(int index)
        {
            if (index < 0 || index >= MainTabControl.Items.Count) return;
            if (MainTabControl.Items[index] is TabItem tab && tab.Visibility == Visibility.Visible)
            {
                MainTabControl.SelectedIndex = index;
            }
        }

        // Ctrl+S: 保存当前页面设置（仅当当前页面支持保存时）
        // 实际逻辑由 INavigationService.SaveCurrentPage 实现，避免 View 向下转型访问 SettingsViewModel
        private void SaveCurrentPage()
        {
            MainViewModel.NavigationService?.SaveCurrentPage();
        }

        // === 窗口生命周期 ===
        protected override void OnClosing(CancelEventArgs e)
        {
            // P0-FIX: 主窗口关闭前加二次确认，避免生产环境误关导致采图/绑定流程中断
            var msg = "确定要退出应用程序吗？\n如有正在进行的采图/检测流程将被中断。";
            var result = NotificationService.Ask(msg, "确认退出");
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // H88c: 取消订阅用户活动事件
            try
            {
                PreviewMouseMove -= MainWindow_UserActivity;
                PreviewKeyDown -= MainWindow_UserActivity;
            }
            catch (Exception ex) { LogService.Instance.Error($"取消订阅用户活动事件失败: {ex}"); }

            // M19/M144: 直接使用 x:Name 生成的字段，避免 FindName 魔法字符串
            if (HomeViewControl is not null)
            {
                // M150: ViewModel 和 HomeViewControl 的 Dispose 需独立 try-catch，避免一方抛出跳过另一方释放
                try { HomeViewControl.ViewModel?.Dispose(); }
                catch (Exception ex) { LogService.Instance.Error($"释放 HomeViewModel 失败: {ex}"); }
                // H50c: 不在 Unloaded 中 Dispose（避免 TabControl 切换标签导致主页失效），改为窗口关闭时释放 ImageViewer
                try { HomeViewControl.Dispose(); }
                catch (Exception ex) { LogService.Instance.Error($"释放 HomeViewControl 失败: {ex}"); }
            }
            else
            {
                LogService.Instance.Warning("OnClosed: HomeViewControl 未初始化，跳过释放 HomeViewModel");
            }

            // H76: 在窗口关闭时释放 RecipesManageView 的 ViewModel 资源
            if (RecipesManageViewControl is not null)
            {
                try { RecipesManageViewControl.Dispose(); }
                catch (Exception ex) { LogService.Instance.Error($"释放 RecipesManageViewControl 失败: {ex}"); }
            }
            else
            {
                // M319a: 补充 else 分支记录警告，与 HomeViewControl 保持一致
                LogService.Instance.Warning("OnClosed: RecipesManageViewControl 未初始化，跳过释放");
            }

            // 释放 MainViewModel（停止定时器、取消事件订阅、注销 Messenger）
            try { _viewModel.Dispose(); }
            catch (Exception ex) { LogService.Instance.Error($"释放 MainViewModel 失败: {ex}"); }

            base.OnClosed(e);
        }
    }
}
