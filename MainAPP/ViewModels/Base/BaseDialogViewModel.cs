using CommunityToolkit.Mvvm.Input;

namespace MainAPP.ViewModels.Base
{
    /// <summary>
    /// 对话框 ViewModel 基类，提供关闭命令和请求关闭事件。
    /// </summary>
    public abstract class BaseDialogViewModel : ObservableDisposable
    {
        private bool? _dialogResult;

        /// <summary>对话框结果（true=确定，false=取消，null=未关闭）</summary>
        public bool? DialogResult
        {
            get => _dialogResult;
            set => SetProperty(ref _dialogResult, value);
        }

        /// <summary>关闭命令</summary>
        public IRelayCommand CloseCommand { get; }

        /// <summary>请求关闭事件</summary>
        public event Action? RequestClose;

        protected BaseDialogViewModel()
        {
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
        }

        /// <summary>触发关闭事件</summary>
        protected void OnRequestClose() => RequestClose?.Invoke();
    }
}
