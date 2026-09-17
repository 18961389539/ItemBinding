using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MainAPP.Models;

namespace MainAPP.Services;

/// <summary>
/// 自定义通讯协议模板渲染器（2026-09-13）。
/// <para>把一条 <see cref="DbModel"/> 按 <see cref="ProtocolTemplateConfig"/> 渲染成待发送文本：
/// 段按顺序拼接，段满足 <see cref="ProtocolSegment.Condition"/> 才输出；占位符
/// <c>$X $Y $RZ $BC $ENC</c>（可选后缀 <c>:小数位</c>）在渲染时替换。
/// 拒发策略：<see cref="ProtocolTemplateConfig.RejectWhenAngleUnknown"/>（角度未知 -9999）
/// 与 <see cref="ProtocolTemplateConfig.NoBarcodeBehavior"/> == RejectMessage（无码）任一成立 → 返回 null。</para>
/// <para>纯静态函数，无 IO，供业务与单测共用。未知条件/未知占位符采取保守策略：
/// 未知条件段不输出、未知占位符原样保留（利于模板排错），不抛异常。</para>
/// </summary>
public static class ProtocolTemplateRenderer
{
    private static readonly Regex PlaceholderRe = new(@"\$([A-Za-z]+)(?::(\d+))?", RegexOptions.Compiled);

    /// <summary>是否读到有效二维码（非空且非 "noread" 哨兵）。</summary>
    public static bool HasBarcode(DbModel m)
        => !string.IsNullOrEmpty(m.Barcode)
           && !string.Equals(m.Barcode, "noread", StringComparison.OrdinalIgnoreCase);

    /// <summary>段条件是否满足（未知条件按未满足处理，配置错误时只缺段不崩帧）。</summary>
    public static bool IsSegmentSatisfied(ProtocolSegment seg, DbModel m)
        => seg.Condition switch
        {
            "Always" => true,
            "BarcodeNonEmpty" => HasBarcode(m),
            "EncoderValid" => m.Encode != 0,
            "AngleKnown" => m.Angle != AngleTracker.UnknownAngle,
            _ => false,
        };

    /// <summary>
    /// 渲染单条消息。返回 null = 整条拒发（角度未知或无码 RejectMessage）。
    /// </summary>
    /// <param name="m">待发送的记录。★ 本方法**不修改**它——它同时是 EF 跟踪的实体，
    /// 改写会污染落库数据与画面显示。</param>
    /// <param name="cfg">协议模板配置。</param>
    /// <param name="angle">本轮生效的发送角（已按目标角度域换算）；null = 用 <c>m.Angle</c> 原值。
    /// 2026-09-17 新增：折叠域下发送值与落库值不同，必须由调用方传入实际要发的值。</param>
    public static string? Render(DbModel m, ProtocolTemplateConfig cfg, double? angle = null)
    {
        if (cfg.RejectWhenAngleUnknown && m.Angle == AngleTracker.UnknownAngle)
        {
            return null;
        }

        if (string.Equals(cfg.NoBarcodeBehavior, "RejectMessage", StringComparison.OrdinalIgnoreCase)
            && !HasBarcode(m))
        {
            return null;
        }

        var sb = new StringBuilder();
        if (cfg.Segments is not null)
        {
            foreach (var seg in cfg.Segments)
            {
                if (!IsSegmentSatisfied(seg, m))
                {
                    continue;
                }

                sb.Append(ReplacePlaceholders(seg.Text, m, cfg, angle));
            }
        }

        sb.Append(ToLineEnding(cfg.LineEnding));
        return sb.ToString();
    }

    /// <summary>多消息拼接时使用的行结束符（帧内多条产品逐行；默认 CRLF）。</summary>
    public static string FrameLineEnding(ProtocolTemplateConfig cfg)
        => string.Equals(cfg.LineEnding, "LF", StringComparison.OrdinalIgnoreCase) ? "\n" : "\r\n";

    private static string ReplacePlaceholders(string text, DbModel m, ProtocolTemplateConfig cfg, double? angle)
        => PlaceholderRe.Replace(text, mt =>
        {
            var name = mt.Groups[1].Value;
            int decimals = mt.Groups[2].Success ? int.Parse(mt.Groups[2].Value, CultureInfo.InvariantCulture) : 2;
            switch (name)
            {
                case "X":
                    return FormatDouble(m.WorldX, decimals);
                case "Y":
                    return FormatDouble(m.WorldY, decimals);
                case "RZ":
                    // 2026-09-17: 发送角可能已被换算到其它角度域（如折叠到 (-90,90]），
                    // 未传时退回落库值（与历史行为一致）
                    return FormatDouble(angle ?? m.Angle, decimals);
                case "BC":
                    if (HasBarcode(m))
                    {
                        return m.Barcode;
                    }

                    return string.Equals(cfg.NoBarcodeBehavior, "Placeholder", StringComparison.OrdinalIgnoreCase)
                        ? cfg.NoBarcodePlaceholder
                        : string.Empty;
                case "ENC":
                    return m.Encode.ToString(CultureInfo.InvariantCulture);
                default:
                    return mt.Value; // 未知占位符原样保留（利于模板排错）
            }
        });

    private static string FormatDouble(double v, int decimals)
    {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
            return "0".ToString(CultureInfo.InvariantCulture);
        }

        // 2026-09-15: 改为"十进制四舍五入"（AwayFromZero），与协议说明及单元测试一致。
        // 原先直接 Math.Round(v, decimals) 有两重问题：
        //   1) 默认是银行家舍入（中点取偶），与文档写的"四舍五入"不符；
        //   2) 即便改成 AwayFromZero 也修不好 —— double 的二进制表示会让 12.345 这类值
        //      略小于十进制中点（≈12.34499999999999975），12.345 仍然输出 12.34。
        // 做法：先取 double 的最短往返十进制表示（.NET Core 3.0+ 的 "R"），在 decimal 上舍入，
        // 使输出符合现场对"四舍五入"的常规定义。
        if (decimal.TryParse(v.ToString("R", CultureInfo.InvariantCulture),
                             NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
        {
            return Math.Round(dec, decimals, MidpointRounding.AwayFromZero)
                       .ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        // decimal 表示范围（±7.9e28 量级）装不下时的兜底：退回 double 舍入，避免因格式化失败丢报文
        return Math.Round(v, decimals).ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static string ToLineEnding(string le)
        => string.Equals(le, "CRLF", StringComparison.OrdinalIgnoreCase) ? "\r\n"
         : string.Equals(le, "LF", StringComparison.OrdinalIgnoreCase) ? "\n"
         : string.Empty;
}