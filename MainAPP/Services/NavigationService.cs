using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MainAPP.Models;
using MainAPP.ViewModels;
using MainAPP.Views;

namespace MainAPP.Services
{
    /// <summary>
    /// INavigationService 的默认实现，通过引用 MainWindow 的 TabControl 切换页面。
    /// 使用 Func{MainWindow} 工厂避免循环依赖（NavigationService 不持有 MainWindow 引用，
    /// 每次调用时通过工厂获取当前主窗口实例）。
    /// </summary>
    public class NavigationService : INavigationService
    {
        private readonly Func<MainWindow?> _mainWindowFactory;

        // 页面名称到 TabItem 索引的映射（与 MainWindow.xaml 中 TabItem 顺序保持一致）
        // 注意：索引 4 是日志页，关于页索引为 5
        private static readonly Dictionary<string, int> PageNameToIndex = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Home", 0 },
            { "Recipe", 1 },
            { "Recipes", 1 },
            { "Database", 2 },
            { "Settings", 3 },
            { "Logs", 4 },
            { "About", 5 },
        };

        public NavigationService(Func<MainWindow?> mainWindowFactory)
        {
            _mainWindowFactory = mainWindowFactory;
        }

        private MainWindow? MainWindow => _mainWindowFactory();
        private TabControl? MainTabControl => MainWindow?.MainTabControl;

        /// <summary>
        /// 在 UI 线程执行操作；若当前已在 UI 线程则直接执行，否则通过 Dispatcher.InvokeAsync 异步切换。
        /// WPF Dispatcher 在此场景下不会死锁（无 JoinableTaskFactory 协作环境），抑制 VSTHRD001 警告。
        /// </summary>
        private async Task InvokeOnUIAsync(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
#pragma warning disable VSTHRD001 // WPF Dispatcher.InvokeAsync 在此非 VS 协作环境下安全
                await dispatcher.InvokeAsync(action);
#pragma warning restore VSTHRD001
            }
        }

        /// <inheritdoc/>
        public async Task NavigateToAsync(string pageKey)
        {
            if (string.IsNullOrEmpty(pageKey)) return;
            if (!PageNameToIndex.TryGetValue(pageKey, out int index)) return;

            await InvokeOnUIAsync(() => SelectTabIfVisible(index));
        }

        /// <inheritdoc/>
        public void NavigateTo(string pageKey)
        {
            _ = NavigateToAsync(pageKey);
        }

        /// <inheritdoc/>
        public async Task NavigateToNextAsync()
        {
            await InvokeOnUIAsync(() =>
            {
                var tabControl = MainTabControl;
                if (tabControl is null) return;
                int count = tabControl.Items.Count;
                if (count == 0) return;
                int current = tabControl.SelectedIndex;
                for (int i = 1; i <= count; i++)
                {
                    int next = (current + i) % count;
                    if (tabControl.Items[next] is TabItem tab && tab.Visibility == Visibility.Visible)
                    {
                        tabControl.SelectedIndex = next;
                        return;
                    }
                }
            });
        }

        /// <inheritdoc/>
        public void NavigateToNext()
        {
            _ = NavigateToNextAsync();
        }

        /// <inheritdoc/>
        public async Task NavigateToPreviousAsync()
        {
            await InvokeOnUIAsync(() =>
            {
                var tabControl = MainTabControl;
                if (tabControl is null) return;
                int count = tabControl.Items.Count;
                if (count == 0) return;
                int current = tabControl.SelectedIndex;
                for (int i = 1; i <= count; i++)
                {
                    int prev = (current - i + count) % count;
                    if (tabControl.Items[prev] is TabItem tab && tab.Visibility == Visibility.Visible)
                    {
                        tabControl.SelectedIndex = prev;
                        return;
                    }
                }
            });
        }

        /// <inheritdoc/>
        public void NavigateToPrevious()
        {
            _ = NavigateToPreviousAsync();
        }

        /// <inheritdoc/>
        public async Task EnsureVisibleSelectedAsync()
        {
            await InvokeOnUIAsync(() =>
            {
                var tabControl = MainTabControl;
                if (tabControl is null) return;

                // 当前选中标签可见时无需调整
                if (tabControl.SelectedItem is TabItem selectedTab && selectedTab.Visibility == Visibility.Visible)
                {
                    return;
                }

                // 遍历找到第一个可见的标签页，而非假设索引 0 可见
                for (int i = 0; i < tabControl.Items.Count; i++)
                {
                    if (tabControl.Items[i] is TabItem tab && tab.Visibility == Visibility.Visible)
                    {
                        tabControl.SelectedIndex = i;
                        return;
                    }
                }
            });
        }

        /// <inheritdoc/>
        public void EnsureVisibleSelected()
        {
            _ = EnsureVisibleSelectedAsync();
        }

        /// <inheritdoc/>
        public async Task SaveCurrentPageAsync()
        {
            await InvokeOnUIAsync(() =>
            {
                try
                {
                    var tabControl = MainTabControl;
                    if (tabControl?.SelectedItem is not TabItem selectedTab) return;
                    // 当前 TabItem.Content 是 Border，Border.Child 是实际 View（如 SettingsView）
                    if (selectedTab.Content is not Border border) return;
                    if (border.Child is not FrameworkElement view) return;
                    // 通过 DataContext 找到 SettingsViewModel，避免对 View 类型向下转型
                    if (view.DataContext is SettingsViewModel svm && svm.SaveCommand.CanExecute(null))
                    {
                        svm.SaveCommand.Execute(null);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"SaveCurrentPage 执行失败: {ex}");
                }
            });
        }

        /// <inheritdoc/>
        public void SaveCurrentPage()
        {
            _ = SaveCurrentPageAsync();
        }

        /// <inheritdoc/>
        public async Task ApplyWindowSettingsAsync()
        {
            await InvokeOnUIAsync(() =>
            {
                var window = MainWindow;
                if (window is null) return;
                var settings = Settings.Instance;
                // 2026-09-15 分辨率适配：与 MainWindow.ApplyWindowSettings 共用同一套夹取规则，
                // 避免两处各写一份 Math.Max(1024/700) 下限再次分叉（详见 WindowSizing）。
                WindowSizing.Apply(window, settings.WindowWidth, settings.WindowHeight);
                window.Title = settings.WindowTitle;
            });
        }

        /// <inheritdoc/>
        public void ApplyWindowSettings()
        {
            _ = ApplyWindowSettingsAsync();
        }

        /// <inheritdoc/>
        public async Task EnsureWindowMinimizedAsync()
        {
            await InvokeOnUIAsync(() =>
            {
                var window = MainWindow;
                if (window is null) return;
                if (window.WindowState != WindowState.Minimized)
                {
                    window.WindowState = WindowState.Minimized;
                }
            });
        }

        /// <inheritdoc/>
        public void EnsureWindowMinimized()
        {
            _ = EnsureWindowMinimizedAsync();
        }

        /// <inheritdoc/>
        public async Task ResetCountAsync()
        {
            // 通过 MainWindow 的 HomeViewControl 访问 HomeViewModel 的 ResetProductTracker
            // （HomeView 是 XAML 嵌入控件，由 HomeView 自己 new HomeViewModel，不通过 DI 解析）
            await InvokeOnUIAsync(() =>
            {
                try
                {
                    MainWindow?.HomeViewControl?.ViewModel?.ResetProductTracker();
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"ResetCount 调用 HomeViewModel.ResetProductTracker 失败: {ex}");
                }
            });
        }

        /// <inheritdoc/>
        public void ResetCount()
        {
            _ = ResetCountAsync();
        }

        // 选择指定索引的标签页（仅当可见时）
        private void SelectTabIfVisible(int index)
        {
            var tabControl = MainTabControl;
            if (tabControl is null) return;
            if (index < 0 || index >= tabControl.Items.Count) return;
            if (tabControl.Items[index] is TabItem tab && tab.Visibility == Visibility.Visible)
            {
                tabControl.SelectedIndex = index;
            }
        }
    }
}
