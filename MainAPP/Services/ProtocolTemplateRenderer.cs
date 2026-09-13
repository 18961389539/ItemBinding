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
    public static string? Render(DbModel m, ProtocolTemplateConfig cfg)
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

                sb.Append(ReplacePlaceholders(seg.Text, m, cfg));
            }
        }

        sb.Append(ToLineEnding(cfg.LineEnding));
        return sb.ToString();
    }

    /// <summary>多消息拼接时使用的行结束符（帧内多条产品逐行；默认 CRLF）。</summary>
    public static string FrameLineEnding(ProtocolTemplateConfig cfg)
        => string.Equals(cfg.LineEnding, "LF", StringComparison.OrdinalIgnoreCase) ? "\n" : "\r\n";

    private static string ReplacePlaceholders(string text, DbModel m, ProtocolTemplateConfig cfg)
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
                    return FormatDouble(m.Angle, decimals);
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

        return Math.Round(v, decimals).ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static string ToLineEnding(string le)
        => string.Equals(le, "CRLF", StringComparison.OrdinalIgnoreCase) ? "\r\n"
         : string.Equals(le, "LF", StringComparison.OrdinalIgnoreCase) ? "\n"
         : string.Empty;
}