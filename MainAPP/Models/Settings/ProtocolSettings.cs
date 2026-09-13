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
}