using MainAPP.Models;
using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// 自定义通讯协议模板渲染器测试（2026-09-13）。
/// <para>覆盖：占位符替换与小数位、段条件（Always/无码/编码器/角度已知）、
/// 拒发策略（角度未知 / 无码 RejectMessage）、$BC 无码 Blank/Placeholder 两态。</para>
/// </summary>
public class ProtocolTemplateRendererTests
{
    private static DbModel Model(double x, double y, double angle, string barcode, uint encode)
        => new DbModel
        {
            WorldX = x,
            WorldY = y,
            Angle = angle,
            Barcode = barcode,
            Encode = encode,
        };

    private static ProtocolTemplateConfig Cfg(Action<ProtocolTemplateConfig>? setup = null)
    {
        var c = new ProtocolTemplateConfig { Name = "测试", EndPoint = "127.0.0.1:9001" };
        setup?.Invoke(c);
        return c;
    }

    [Fact]
    public void Render_BasicPlaceholders_FormatsNumbers()
    {
        // 两位小数 + 四舍五入（12.345→12.35）
        var cfg = Cfg(c => c.Segments.Add(new ProtocolSegment
        {
            Condition = "Always",
            Text = "[X]$X[Y]$Y[RZ]$RZ",
        }));

        var s = ProtocolTemplateRenderer.Render(Model(12.345, 56.789, 90.1234, "B2C3D4", 0), cfg);

        Assert.Equal("[X]12.35[Y]56.79[RZ]90.12", s);
    }

    [Fact]
    public void Render_CustomDecimalPlaces_UsesSuffix()
    {
        var cfg = Cfg(c => c.Segments.Add(new ProtocolSegment
        {
            Condition = "Always",
            Text = "$X:3,$Y:0",
        }));

        var s = ProtocolTemplateRenderer.Render(Model(1.2345, 2.5, 0, "", 0), cfg);

        Assert.Equal("1.235,3", s);
    }

    [Fact]
    public void Render_BarcodeSegment_OmittedWhenNoBarcode()
    {
        var cfg = Cfg(c => c.Segments.AddRange(new[]
        {
            new ProtocolSegment { Condition = "Always", Text = "[X]$X" },
            new ProtocolSegment { Condition = "BarcodeNonEmpty", Text = "[ID]$BC" },
        }));

        // noread 哨兵视为无码 → [ID] 段省略
        var noCode = ProtocolTemplateRenderer.Render(Model(1, 2, 3, "noread", 0), cfg);
        Assert.Equal("[X]1.00", noCode);

        var withCode = ProtocolTemplateRenderer.Render(Model(1, 2, 3, "B2C3D4", 0), cfg);
        Assert.Equal("[X]1.00[ID]B2C3D4", withCode);
    }

    [Fact]
    public void Render_EncoderSegment_OmittedWhenEncoderZero()
    {
        var cfg = Cfg(c => c.Segments.AddRange(new[]
        {
            new ProtocolSegment { Condition = "Always", Text = "[X]$X" },
            new ProtocolSegment { Condition = "EncoderValid", Text = "[E]$ENC" },
        }));

        Assert.Equal("[X]1.00", ProtocolTemplateRenderer.Render(Model(1, 2, 3, "A", 0), cfg));
        Assert.Equal("[X]1.00[E]425", ProtocolTemplateRenderer.Render(Model(1, 2, 3, "A", 425), cfg));
    }

    [Fact]
    public void Render_AngleUnknown_RejectedByDefault()
    {
        var cfg = Cfg(c => c.Segments.Add(new ProtocolSegment { Condition = "Always", Text = "[X]$X" }));

        // 默认 RejectWhenAngleUnknown=true → -9999 整条拒发（返回 null）
        Assert.Null(ProtocolTemplateRenderer.Render(Model(1, 2, AngleTracker.UnknownAngle, "A", 0), cfg));

        cfg.RejectWhenAngleUnknown = false;
        Assert.Equal("[X]1.00", ProtocolTemplateRenderer.Render(Model(1, 2, AngleTracker.UnknownAngle, "A", 0), cfg));
    }

    [Fact]
    public void Render_NoBarcodeBehavior_BlankPlaceholderReject()
    {
        // Blank：无码 $BC（在 Always 段内）→ 空串
        var blank = Cfg(c => c.Segments.Add(new ProtocolSegment { Condition = "Always", Text = "[B]$BC" }));
        Assert.Equal("[B]", ProtocolTemplateRenderer.Render(Model(1, 2, 3, "noread", 0), blank));

        // Placeholder：无码 $BC → 配置占位
        var ph = Cfg(c =>
        {
            c.NoBarcodeBehavior = "Placeholder";
            c.NoBarcodePlaceholder = "NONE";
            c.Segments.Add(new ProtocolSegment { Condition = "Always", Text = "[ID]$BC" });
        });
        Assert.Equal("[ID]NONE", ProtocolTemplateRenderer.Render(Model(1, 2, 3, "noread", 0), ph));

        // RejectMessage：无码整条拒发
        var reject = Cfg(c =>
        {
            c.NoBarcodeBehavior = "RejectMessage";
            c.Segments.Add(new ProtocolSegment { Condition = "Always", Text = "[X]$X" });
        });
        Assert.Null(ProtocolTemplateRenderer.Render(Model(1, 2, 3, "noread", 0), reject));
        Assert.Equal("[X]1.00", ProtocolTemplateRenderer.Render(Model(1, 2, 3, "OK", 0), reject));
    }

    [Fact]
    public void Render_AngleKnownSegment_OmittedWhenUnknown()
    {
        var cfg = Cfg(c => c.Segments.AddRange(new[]
        {
            new ProtocolSegment { Condition = "Always", Text = "[X]$X" },
            new ProtocolSegment { Condition = "AngleKnown", Text = "[RZ]$RZ" },
        }));

        // 角度未知但 RejectWhenAngleUnknown=false → 位置照发、[RZ] 段省略
        cfg.RejectWhenAngleUnknown = false;
        Assert.Equal("[X]1.00", ProtocolTemplateRenderer.Render(Model(1, 2, AngleTracker.UnknownAngle, "A", 0), cfg));
        Assert.Equal("[X]1.00[RZ]90.00", ProtocolTemplateRenderer.Render(Model(1, 2, 90, "A", 0), cfg));
    }

    [Fact]
    public void Render_UnknownPlaceholder_Preserved()
    {
        var cfg = Cfg(c => c.Segments.Add(new ProtocolSegment { Condition = "Always", Text = "$UNKNOWN" }));

        Assert.Equal("$UNKNOWN", ProtocolTemplateRenderer.Render(Model(1, 2, 3, "A", 0), cfg));
    }

    [Fact]
    public void Render_LineEnding_AppliedPerMessage()
    {
        var cfg = Cfg(c =>
        {
            c.LineEnding = "CRLF";
            c.Segments.Add(new ProtocolSegment { Condition = "Always", Text = "[X]$X" });
        });

        Assert.Equal("[X]1.00\r\n", ProtocolTemplateRenderer.Render(Model(1, 2, 3, "A", 0), cfg));
        Assert.Equal("\r\n", ProtocolTemplateRenderer.FrameLineEnding(cfg));
    }
}