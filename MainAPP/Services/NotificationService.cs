using System;
using System.Windows;
using System.Windows.Threading;
using HandyControl.Controls;
using HandyControl.Data;
using MainAPP;

// VSTHRD001: 使用 Dispatcher.BeginInvoke 切换到 UI 线程是 WPF 标准模式，无需 JoinableTaskFactory
#pragma warning disable VSTHRD001

namespace MainAPP.Services
{
    /// <summary>
    /// P2-17: 轻量级通知服务，封装 HandyControl Growl 调用。
    /// 用于替代 OK 类 MessageBox.Show，避免阻塞 UI 线程，提供更现代的提示体验。
    /// 注意：使用前必须在 MainWindow.xaml 中放置 hc:Growl 控件（x:Name=GrowlRootPanel）并注册。
    /// YesNo 确认对话框通过 Ask 方法，自动传入 MainWindow 作为 owner 使其居中于父窗口。
    /// 语义层级：
    /// - Info（蓝/Info 图标）：操作反馈，4 秒关闭
    /// - Success（绿/Success 图标）：关键成功操作（如保存成功），3 秒关闭
    /// - Warning（橙/Warning 图标）：非阻断异常，可继续运行，5 秒关闭，带时间戳
    /// - Error（红/Error 图标，StaysOpen）：严重错误需人工介入，不自动关闭，带时间戳
    /// REVIEW(2026-08-05): 自动关闭不再依赖 HandyControl 内置计时（鼠标悬停会暂停计时），
    /// 改为每个通知分配唯一 Token 注册到主窗口容器，由 DispatcherTimer 到期强制 Growl.Clear(token)，
    /// 确保 3/4/5 秒必关；计时到期同时 Unregister 释放字典。
    /// </summary>
    public static class NotificationService
    {
        // 持续时间（毫秒）
        private const int SuccessDurationMs = 3000;
        private const int InfoDurationMs = 4000;
        private const int WarningDurationMs = 5000;

        // 通知 Token 前缀（唯一，用于定向关闭）
        private const string TokenPrefix = "nof_";

        /// <summary>显示信息提示（蓝色，Info 图标，4 秒后强制关闭）</summary>
        public static void Info(string message) =>
            Show(new GrowlInfo { Message = message, ShowDateTime = false }, InfoDurationMs, Growl.Info, useToken: true);

        /// <summary>显示成功提示（绿色，Success 图标，3 秒后强制关闭）</summary>
        public static void Success(string message) =>
            Show(new GrowlInfo { Message = message, ShowDateTime = false }, SuccessDurationMs, Growl.Success, useToken: true);

        /// <summary>显示警告提示（橙色，Warning 图标，5 秒后强制关闭，带时间戳）</summary>
        public static void Warning(string message) =>
            Show(new GrowlInfo { Message = message, ShowDateTime = true }, WarningDurationMs, Growl.Warning, useToken: true);

        /// <summary>
        /// 显示错误提示（红色，Error 图标，不自动关闭需手动关闭，带时间戳）。
        /// 严重错误必须人工确认，避免被忽略。不分配 Token（默认容器），手动关闭即可。
        /// </summary>
        public static void Error(string message) =>
            Show(new GrowlInfo { Message = message, ShowDateTime = true, StaysOpen = true }, null, Growl.Error, useToken: false);

        /// <summary>
        /// 显示模态确认对话框，居中于主窗口。
        /// 必须在 UI 线程调用。
        /// </summary>
        public static System.Windows.MessageBoxResult Ask(
            string message,
            string caption,
            System.Windows.MessageBoxButton buttons = System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage image = System.Windows.MessageBoxImage.Question)
        {
            var owner = System.Windows.Application.Current?.MainWindow;
            if (owner is null)
            {
                return System.Windows.MessageBox.Show(message, caption, buttons, image);
            }
            return System.Windows.MessageBox.Show(owner, message, caption, buttons, image);
        }

        /// <summary>
        /// 统一显示逻辑：
        /// 1. useToken=true 时分配唯一 Token 并 Register 到主窗口 Growl 容器（与默认容器同一面板）；
        /// 2. 调用 Growl 显示；
        /// 3. 若 autoCloseMs 有值，启动 DispatcherTimer，到期强制 Growl.Clear(token) 并 Unregister，
        ///    不受鼠标悬停暂停内置计时的影响。
        /// </summary>
        private static void Show(GrowlInfo info, int? autoCloseMs, Action<GrowlInfo> showAction, bool useToken)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            void execute()
            {
                try
                {
                    var panel = MainWindow.GrowlRoot;
                    if (panel is null)
                    {
                        // Growl 容器尚未就绪（窗口未初始化），静默跳过
                        return;
                    }

                    string? token = null;
                    if (useToken)
                    {
                        token = TokenPrefix + Guid.NewGuid().ToString("N");
                        Growl.Register(token, panel);
                        info.Token = token;
                    }

                    showAction(info);

                    if (token is not null && autoCloseMs is int ms && ms > 0)
                    {
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                        timer.Tick += (_, _) =>
                        {
                            timer.Stop();
                            try
                            {
                                Growl.Clear(token);
                                Growl.Unregister(token, panel);
                            }
                            catch { /* 通知可能已被手动关闭，忽略 */ }
                        };
                        timer.Start();
                    }
                }
                catch (Exception ex)
                {
                    // REVIEW(2026-08-05): 通知失败不再静默吞掉——记录日志便于排查
                    // （Growl 未注册全局父容器、容器未就绪、UI 不可用等场景）。
                    try { LogService.Instance.Warning($"通知显示失败: {ex.Message}"); }
                    catch { /* 日志也不可用，忽略 */ }
                }
            }

            if (dispatcher.CheckAccess())
            {
                execute();
            }
            else
            {
                _ = dispatcher.BeginInvoke(execute);
            }
        }
    }
}
