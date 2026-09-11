using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MainAPP.Messages
{
    /// <summary>
    /// H17: AI 识别计数消息，当一次推理完成后通知 MainWindow 增加识别数量。
    /// </summary>
    public sealed class AddDetectionMessage : ValueChangedMessage<int>
    {
        /// <summary>
        /// 初始化 <see cref="AddDetectionMessage"/> 实例。
        /// </summary>
        /// <param name="count">本次推理新增的识别数量。</param>
        // L410c: 非负校验移至 base() 调用，避免 base 先存储无效值后再抛异常
        public AddDetectionMessage(int count) : base(count < 0 ? throw new ArgumentOutOfRangeException(nameof(count)) : count)
        {
        }
    }
}
