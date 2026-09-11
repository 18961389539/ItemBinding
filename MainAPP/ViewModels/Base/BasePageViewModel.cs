using CommunityToolkit.Mvvm.ComponentModel;
using MainAPP.ViewModels.Common;

namespace MainAPP.ViewModels.Base
{
    /// <summary>
    /// 页面 ViewModel 基类，统一加载状态/忙碌状态/错误信息模式。
    /// 子类继承后无需重复定义 IsBusy/BusyMessage/LoadState/ErrorMessage。
    /// </summary>
    public abstract class BasePageViewModel : ObservableDisposable
    {
        private bool _isBusy;
        /// <summary>全局忙碌状态，用于 Loading 指示与按钮禁用</summary>
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private string _busyMessage = "正在处理...";
        /// <summary>操作类型相关的 Loading 文案</summary>
        public string BusyMessage
        {
            get => _busyMessage;
            set => SetProperty(ref _busyMessage, value);
        }

        private LoadState _loadState = LoadState.Initial;
        /// <summary>加载状态枚举，区分空数据与加载失败</summary>
        public LoadState LoadState
        {
            get => _loadState;
            set => SetProperty(ref _loadState, value);
        }

        private string _errorMessage = string.Empty;
        /// <summary>错误信息（LoadState=Error 时显示）</summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }
    }
}
