using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MainAPP.Messages
{
    /// <summary>
    /// 添加条码消息，用于通知 MainWindow 将新读取的条码加入去重计数集合。
    /// </summary>
    /// <param name="value">条码内容字符串。</param>
    public sealed class AddBarcodeMessage : ValueChangedMessage<string>
    {
        // L410c: null 校验移至 base() 调用，避免 base 先存储无效值后再抛异常
        // L424b: 空白校验也移至 base() 调用前，与 null 校验策略保持一致；
        // string.IsNullOrWhiteSpace 同时覆盖 null/空字符串/空白字符串三种情况，
        // 统一抛 ArgumentException（注意：原 null 抛 ArgumentNullException 改为 ArgumentException，
        // 若调用方按 ArgumentNullException 精确捕获需相应调整）
        public AddBarcodeMessage(string value)
            : base(string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("value 不能为 null 或空白字符串", nameof(value))
                : value)
        {
        }
    }
}
