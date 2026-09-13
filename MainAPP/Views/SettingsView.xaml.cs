using MainAPP.Services;
using MainAPP.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace MainAPP.Views
{
    /// <summary>
    /// SettingsView.xaml 的交互逻辑
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
            Loaded += SettingsView_Loaded;
            Unloaded += SettingsView_Unloaded;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SettingsView_Loaded;
            if (DataContext is SettingsViewModel vm)
            {
                PasswordBox.Password = vm.Password;
            }
        }

        // S7: 离开设置页时若有未保存修改（IsDirty=true），弹窗提醒用户
        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsViewModel vm) return;
            if (!vm.IsDirty) return;

            var result = NotificationService.Ask(
                "设置有未保存的修改，确定要离开吗？未保存的修改将丢失。",
                "未保存的修改",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            // 用户选 No 想留下，但 Unloaded 已经发生无法取消，这里仅作为信息提示；
            // 实际防止丢失的方式是保存按钮统一通过 SaveCommand 显式提交。
            // 注：WPF UserControl 的 Unloaded 不能取消，此提示仅提醒用户下次注意。
            _ = result;
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.Password = PasswordBox.Password;
            }
        }

        // 2026-09-13: 通讯协议 Tab 帮助——弹窗给出占位符/条件/策略/用法说明
        private void ProtocolTabHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(ProtocolHelpText, "自定义通讯协议说明", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private const string ProtocolHelpText =
            "自定义通讯协议：把检测结果按模板文本发送到指定 UDP 端点（2026-09-13）。\n" +
            "◆ 同一时刻只有一个接收方生效：主页「接收方协议」填什么就发什么（VGT / LL / 或你的协议名）。\n\n" +
            "【字段占位符】（模板里按需放置，默认两位小数，四舍五入且固定补零）\n" +
            "  $X / $Y   产品世界坐标（mm）\n" +
            "  $RZ       角度（度，范围 -180~180；-9999 表示角度未知）\n" +
            "  $BC       二维码内容（无码时输出见「无码策略」）\n" +
            "  $ENC      帧级编码器值（0 = 本帧未收到编码器）\n" +
            "  格式后缀   $X:3 保留 3 位小数，$X:0 整数（不写默认 2 位）\n" +
            "  未知占位符会原样保留在输出里（便于发现拼写错误）\n" +
            "  数值异常（NaN/Infinity）按 0 输出，不会使发送崩溃\n\n" +
            "【段条件】段只有满足条件才整体输出，逐段按顺序拼接\n" +
            "  Always          恒输出\n" +
            "  BarcodeNonEmpty 本产品读到有效二维码（非空且非 noread）才输出\n" +
            "  EncoderValid    本帧编码器非 0 才输出\n" +
            "  AngleKnown      角度已知（非 -9999）才输出\n" +
            "  未知的条件名按“不满足”处理（该段静默跳过，不报错）\n\n" +
            "【二维码未读到时的行为（无码策略，字段层）】\n" +
            "  Blank          $BC 置空；配合 BarcodeNonEmpty 段可整段省略（默认）\n" +
            "  Placeholder    $BC 输出下方「无码占位」填写的文本\n" +
            "  RejectMessage  无码整条拒发（见拒发规则）\n" +
            "  注意：段条件（发不发 [ID] 段）与无码策略（$BC 输出啥）是两层，可独立配置\n\n" +
            "【拒发规则】以下任一命中，该产品整条不发送（不影响同帧其他产品）\n" +
            "  ① 勾选「角度未知拒发」且角度 = -9999（默认勾选，建议保持）\n" +
            "  ② 无码策略 = RejectMessage 且未读到码\n" +
            "  本帧全部被拒发时，不发送任何 UDP 数据（日志记 Debug 提示）\n\n" +
            "【发送行为】\n" +
            "  传输：UDP，无连接——目标不可达不会报错，请以日志确认\n" +
            "  端点：仅支持 IP:端口（如 192.168.1.50:9001），不支持域名；解析失败会告警并不发送\n" +
            "  多产品：每条产品一行（按「行尾」分隔），整帧拼成一条 UDP 报文发出\n" +
            "  日志：发送成功记 Info（前缀 [自定义协议] 协议名: 帧=… 条目数=… 内容），失败记 Warning\n\n" +
            "【命名约束】\n" +
            "  协议名必填且建议唯一；不能与内置 VGT、LL 同名（大小写不敏感）；\n" +
            "  主页接收方填了不存在的协议名时，日志会提示“未知的接收方”且不发送\n\n" +
            "【启用步骤】\n" +
            "  1. 本页新增/修改协议后点「保存并生效」（写入 settings.json，重启不丢）；\n" +
            "  2. 把主页「接收方协议」文本框填成协议名（如：机器人C）；\n" +
            "  3. 下一帧起按此模板发送，见日志 [自定义协议] 机器人C: …\n\n" +
            "【排错速查】\n" +
            "  画面/表格有值但没发出去 → 看日志：协议名没匹配上？角度全是 -9999？无码被拒？\n" +
            "  输出里出现 $X 原样 → 占位符拼写错误（区分大小写）\n" +
            "  端点没反应 → 先本机试 127.0.0.1:端口，再查防火墙与 urlacl";

        /// <summary>
        /// int 类型 TextBox 输入验证，过滤非数字字符
        /// </summary>
        private void NumberValidation_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>
        /// int 类型 TextBox 粘贴验证，过滤非数字粘贴内容
        /// </summary>
        private void NumberValidation_Pasting(object sender, System.Windows.DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(System.Windows.DataFormats.Text))
            {
                var text = e.DataObject.GetData(System.Windows.DataFormats.Text) as string;
                if (text != null)
                {
                    foreach (char c in text)
                    {
                        if (!char.IsDigit(c))
                        {
                            e.CancelCommand();
                            return;
                        }
                    }
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
    }
}
