using MainAPP.Models;
using System.Collections.ObjectModel;

namespace MainAPP.Services
{
    /// <summary>
    /// 日志服务抽象。当前实现为单例 <see cref="LogService"/>，
    /// 为后续 ViewModel 改造为构造函数注入做准备。
    /// 保留 <see cref="LogService.Instance"/> 静态属性以向后兼容现有调用。
    /// </summary>
    public interface ILogService
    {
        /// <summary>日志记录集合（用于 UI 绑定）</summary>
        ObservableCollection<LogEntry> Logs { get; }

        /// <summary>记录信息级日志（同时写入 UI 集合与 Serilog 持久化）</summary>
        void Info(string message);

        /// <summary>记录警告级日志</summary>
        void Warning(string message);

        /// <summary>记录错误级日志</summary>
        void Error(string message);

        /// <summary>记录调试级日志</summary>
        void Debug(string message);

        /// <summary>清空 UI 日志集合（不影响 Serilog 持久化日志）</summary>
        void ClearLogs();
    }
}
