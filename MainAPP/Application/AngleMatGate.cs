namespace MainAPP.Application;

/// <summary>
/// 决定是否为本帧创建 Mat 源图（2026-09-13 从 HomeViewModel 的内联条件抽出，便于回归测试）。
/// <para><b>为什么要独立成函数</b>：这个 gate 曾因只看 <c>angleEnabled || grayDirectionEnabled</c>
/// 而漏掉特征池，造成「开特征池 + 关亮度判向」时 <c>angleMat = null</c> →
/// <see cref="HeadTailFeaturePool.Evaluate"/> 因 <c>bgrImage is null</c> 静默返回 null →
/// 7 个特征全不算、头尾退化为掩码主轴角且无日志。抽出来配测试锁住三个消费者的契约，
/// 防止将来再漏。<b>HomeViewModel 必须经由本函数做判断，不要内联展开</b>。</para>
/// <para><b>三个消费者的语义分层</b>：本函数只决定"要不要创建 Mat"；
/// <list type="bullet">
/// <item>角度模型路径由 <paramref name="angleEnabled"/> 把守</item>
/// <item>特征池由 <paramref name="featurePoolEnabled"/> 把守（整个级联，含全部 7 个特征）</item>
/// <item>亮度特征 ④ 由 <paramref name="grayDirectionEnabled"/> 单独把守
/// （DetectionRecordService 传参 brightnessEnabled）——关亮度判向只关那一个特征，不杀特征池</item>
/// </list></para>
/// </summary>
public static class AngleMatGate
{
    /// <summary>任一消费者需要 Mat 源图时即为 true。三个参数互相独立、缺一即缺一个能力。</summary>
    public static bool ShouldCreate(bool angleEnabled, bool grayDirectionEnabled, bool featurePoolEnabled)
        => angleEnabled || grayDirectionEnabled || featurePoolEnabled;
}
