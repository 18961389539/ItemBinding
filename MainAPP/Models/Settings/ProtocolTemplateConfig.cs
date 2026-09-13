using System.Collections.Generic;

namespace MainAPP.Models;

/// <summary>
/// 单条自定义通讯协议（2026-09-13）。
/// <para>发送触发：<see cref="MainAPP.Services.ToVGTService.SendTo"/> 的 receiver 参数
/// 非内置 "VGT"/"LL" 时，按 <see cref="Name"/> 匹配到启用的协议即走模板渲染发送。</para>
/// </summary>
public sealed class ProtocolTemplateConfig
{
    /// <summary>协议名（= MessageReceiver 输入值，需唯一）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>是否启用（停用项不发送）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>传输方式：UDP（预留 TCP，当前仅 UDP 生效）。</summary>
    public string Transport { get; set; } = "UDP";

    /// <summary>目标端点 "ip:port"，如 "192.168.1.50:9001"。</summary>
    public string EndPoint { get; set; } = string.Empty;

    /// <summary>模板段（按顺序拼接；段条件不满足则该段省略）。</summary>
    public List<ProtocolSegment> Segments { get; set; } = [];

    /// <summary>
    /// 二维码未读取到时的行为（Barcode 为空或 "noread"）：
    /// Blank=省略条件段且 $BC 置空；Placeholder=$BC 输出 <see cref="NoBarcodePlaceholder"/>；
    /// RejectMessage=整条拒发（与角度未知拒发相互独立）。
    /// </summary>
    public string NoBarcodeBehavior { get; set; } = "Blank";

    /// <summary>NoBarcodeBehavior=Placeholder 时 $BC 的输出（默认空串）。</summary>
    public string NoBarcodePlaceholder { get; set; } = string.Empty;

    /// <summary>角度未知（-9999）时整条拒发（与现有 VGT/LL 行为一致，默认 true）。</summary>
    public bool RejectWhenAngleUnknown { get; set; } = true;

    /// <summary>行结束符："" 无 / CRLF / LF。</summary>
    public string LineEnding { get; set; } = string.Empty;
}

/// <summary>模板段：条件 + 模板文本。</summary>
public sealed class ProtocolSegment
{
    /// <summary>
    /// 段生效条件：Always（恒输出）| BarcodeNonEmpty（读到码）| EncoderValid（编码器非 0）|
    /// AngleKnown（角度已知，非 -9999）。
    /// </summary>
    public string Condition { get; set; } = "Always";

    /// <summary>模板文本，支持占位符 $X $Y $RZ $BC $ENC，可选 :小数位（如 $X:3）。</summary>
    public string Text { get; set; } = string.Empty;
}