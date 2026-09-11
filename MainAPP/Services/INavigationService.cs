namespace MainAPP.Services
{
    /// <summary>
    /// 导航服务抽象。用于页面切换和保存当前页。
    /// 替代 MainViewModel 中的 Action/Func 回调，避免 VM 反向依赖 View。
    /// </summary>
    public interface INavigationService
    {
        /// <summary>切换到指定页面（通过 pageKey）</summary>
        void NavigateTo(string pageKey);

        /// <summary>切换到下一个标签页</summary>
        void NavigateToNext();

        /// <summary>切换到上一个标签页</summary>
        void NavigateToPrevious();

        /// <summary>确保当前 Tab 可见（展开折叠状态）</summary>
        void EnsureVisibleSelected();

        /// <summary>保存当前页面（触发当前 VM 的保存命令）</summary>
        void SaveCurrentPage();

        /// <summary>应用窗口设置（从 Settings 读取）</summary>
        void ApplyWindowSettings();

        /// <summary>确保窗口最小化（如登录后）</summary>
        void EnsureWindowMinimized();

        /// <summary>重置计数（清零计数器）</summary>
        void ResetCount();

        /// <summary>切换到指定页面（异步）</summary>
        Task NavigateToAsync(string pageKey);

        /// <summary>切换到下一个标签页（异步）</summary>
        Task NavigateToNextAsync();

        /// <summary>切换到上一个标签页（异步）</summary>
        Task NavigateToPreviousAsync();

        /// <summary>确保当前 Tab 可见（异步）</summary>
        Task EnsureVisibleSelectedAsync();

        /// <summary>保存当前页面（异步）</summary>
        Task SaveCurrentPageAsync();

        /// <summary>应用窗口设置（异步）</summary>
        Task ApplyWindowSettingsAsync();

        /// <summary>确保窗口最小化（异步）</summary>
        Task EnsureWindowMinimizedAsync();

        /// <summary>重置计数（异步）</summary>
        Task ResetCountAsync();
    }
}
