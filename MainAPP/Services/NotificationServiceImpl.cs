using System.Windows;

namespace MainAPP.Services
{
    /// <summary>
    /// <see cref="INotificationService"/> 的实例包装实现。
    /// 委托给静态 <see cref="NotificationService"/> 的静态方法，
    /// 既满足 DI 容器对实例类型的注册要求，又无需修改任何现有静态调用方代码。
    /// </summary>
    public sealed class NotificationServiceImpl : INotificationService
    {
        public void Info(string message) => NotificationService.Info(message);

        public void Success(string message) => NotificationService.Success(message);

        public void Warning(string message) => NotificationService.Warning(message);

        public void Error(string message) => NotificationService.Error(message);

        public MessageBoxResult Ask(
            string message,
            string caption,
            MessageBoxButton buttons = MessageBoxButton.YesNo,
            MessageBoxImage image = MessageBoxImage.Question) =>
            NotificationService.Ask(message, caption, buttons, image);
    }
}
