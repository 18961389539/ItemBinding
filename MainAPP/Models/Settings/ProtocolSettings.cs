using System.Collections.Generic;

namespace MainAPP.Models;

/// <summary>
/// 自定义通讯协议配置（2026-09-13 新增）。
/// <para>以模板段（<see cref="ProtocolTemplateConfig.Segments"/>）定义输出格式：
/// 段按顺序拼接，段满足 <see cref="ProtocolSegment.Condition"/> 条件才输出；
/// 占位符在渲染时替换（$X $Y $RZ $BC $ENC）。见 <c>MainAPP.Services.ProtocolTemplateRenderer</c>。</para>
/// <para>通过 <see cref="MainAPP.Models.Settings.ProtocolTemplates"/> 扁平转发参与 settings.json 序列化；
/// 全局默认配置；配方级覆盖暂未启用（预留）。</para>
/// </summary>
public sealed class ProtocolSettings
{
    /// <summary>全部自定义协议模板（含停用项）。</summary>
    public List<ProtocolTemplateConfig> Protocols { get; set; } = [];

    /// <summary>
    /// 默认发送角度域（2026-09-17 新增）：<c>Signed180</c> = (-180,180]，<c>Folded90</c> = (-90,90]。
    /// <para>作用于**内置 VGT/LL 报文**以及未单独覆盖的自定义协议项；
    /// 自定义协议可用 <see cref="ProtocolTemplateConfig.AngleDomain"/> 单独覆盖。</para>
    /// <para>默认 <c>Signed180</c> = 与历史行为完全一致（零行为变化）。
    /// 仅在机械手腕关节只能转 ±90、且产品/夹爪 180° 对称时才改为 <c>Folded90</c>。</para>
    /// </summary>
    public string DefaultAngleDomain { get; set; } = "Signed180";
}