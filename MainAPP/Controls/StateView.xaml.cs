using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MainAPP.ViewModels.Common;

namespace MainAPP.Controls
{
    /// <summary>
    /// 空状态子类型。配合 <see cref="StateView.State"/> 为 Empty 时使用，
    /// 用于在"暂无数据 / 未配置 / 无结果 / 网络异常"等场景间切换图标与文案。
    /// </summary>
    public enum EmptyStateType
    {
        /// <summary>默认空状态：暂无数据。</summary>
        Default,

        /// <summary>未配置：未完成必要配置。</summary>
        NoConfig,

        /// <summary>搜索无结果：未找到匹配记录。</summary>
        NoResult,

        /// <summary>网络异常：无法连接到服务。</summary>
        NetworkError
    }

    /// <summary>
    /// 状态视图。根据 LoadState 显示对应 UI（加载中/空数据/错误）。
    /// </summary>
    public partial class StateView : UserControl
    {
        public static readonly DependencyProperty StateProperty =
            DependencyProperty.Register(nameof(State), typeof(LoadState), typeof(StateView),
                new PropertyMetadata(LoadState.Initial));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(StateView),
                new PropertyMetadata(null));

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(StateView),
                new PropertyMetadata(null));

        public static readonly DependencyProperty HintProperty =
            DependencyProperty.Register(nameof(Hint), typeof(string), typeof(StateView),
                new PropertyMetadata(null));

        public static readonly DependencyProperty RetryCommandProperty =
            DependencyProperty.Register(nameof(RetryCommand), typeof(IRelayCommand), typeof(StateView),
                new PropertyMetadata(null));

        /// <summary>
        /// 空状态子类型依赖属性。仅当 <see cref="State"/> 为 Empty 时生效，
        /// 配合 XAML 中的 DataTrigger 切换图标 Kind / 主标题 / 副标题。
        /// </summary>
        public static readonly DependencyProperty EmptyStateTypeProperty =
            DependencyProperty.Register(
                nameof(EmptyStateType),
                typeof(EmptyStateType),
                typeof(StateView),
                new PropertyMetadata(EmptyStateType.Default, OnEmptyStateTypeChanged));

        /// <summary>
        /// CTA 按钮文案。非空时显示 CTA 按钮（默认 null 以配合 NullToVisibilityConverter 隐藏按钮）。
        /// </summary>
        public static readonly DependencyProperty CtaTextProperty =
            DependencyProperty.Register(nameof(CtaText), typeof(string), typeof(StateView),
                new PropertyMetadata(null));

        /// <summary>
        /// CTA 按钮命令。
        /// </summary>
        public static readonly DependencyProperty CtaCommandProperty =
            DependencyProperty.Register(nameof(CtaCommand), typeof(ICommand), typeof(StateView),
                new PropertyMetadata(null));

        public LoadState State { get => (LoadState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
        public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
        public IRelayCommand RetryCommand { get => (IRelayCommand)GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }

        /// <summary>空状态子类型。</summary>
        public EmptyStateType EmptyStateType
        {
            get => (EmptyStateType)GetValue(EmptyStateTypeProperty);
            set => SetValue(EmptyStateTypeProperty, value);
        }

        /// <summary>CTA 按钮文案。</summary>
        public string CtaText { get => (string)GetValue(CtaTextProperty); set => SetValue(CtaTextProperty, value); }

        /// <summary>CTA 按钮命令。</summary>
        public ICommand CtaCommand { get => (ICommand)GetValue(CtaCommandProperty); set => SetValue(CtaCommandProperty, value); }

        public StateView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// EmptyStateType 变更回调。
        /// 图标 / 主标题 / 副标题的切换由 StateView.xaml 中的 DataTrigger 处理，
        /// 此处保留钩子以便后续扩展（如需在代码侧感知变更可在此添加逻辑）。
        /// 注意：不在此处设置 Title/Hint 依赖属性，避免与 Error 状态共享 Title 时产生串扰。
        /// </summary>
        private static void OnEmptyStateTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 当前切换逻辑全部由 XAML DataTrigger 实现，无需在此重复 SetValue。
        }
    }
}
