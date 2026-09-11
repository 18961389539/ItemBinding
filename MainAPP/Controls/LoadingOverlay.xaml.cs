using System.Windows;
using System.Windows.Controls;

namespace MainAPP.Controls
{
    /// <summary>
    /// 加载遮罩控件。显示旋转图标和提示文本，覆盖父容器。
    /// </summary>
    public partial class LoadingOverlay : UserControl
    {
        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(LoadingOverlay),
                new PropertyMetadata("正在处理..."));

        public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }

        public LoadingOverlay()
        {
            InitializeComponent();
        }
    }
}
