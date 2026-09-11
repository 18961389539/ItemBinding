using System.Windows;

namespace MainAPP.Services
{
    /// <summary>
    /// 通知服务抽象。当前实现为静态 NotificationService，
    /// 为后续 ViewModel 改造为构造函数注入做准备。
    /// 实际注册到 DI 容器的实现为 <see cref="NotificationServiceImpl"/>，
    /// 内部委托给静态 <see cref="NotificationService"/> 方法，保持向后兼容。
    /// </summary>
    public interface INotificationService
    {
        /// <summary>显示信息提示（蓝色，Info 图标，4 秒后自动关闭）</summary>
        void Info(string message);

        /// <summary>显示成功提示（绿色，Success 图标，3 秒后自动关闭）</summary>
        void Success(string message);

        /// <summary>显示警告提示（橙色，Warning 图标，5 秒后自动关闭，带时间戳）</summary>
        void Warning(string message);

        /// <summary>显示错误提示（红色，Error 图标，不自动关闭需手动关闭，带时间戳）</summary>
        void Error(string message);

        /// <summary>
        /// 显示模态确认对话框，居中于主窗口。必须在 UI 线程调用。
        /// </summary>
        MessageBoxResult Ask(
            string message,
            string caption,
            MessageBoxButton buttons = MessageBoxButton.YesNo,
            MessageBoxImage image = MessageBoxImage.Question);
    }
}
