using MainAPP.Models;
using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// 硬触发丢帧场景的编码器差分匹配回归测试（2026-09-12）。
///
/// <para>背景：硬触发按约 200mm 距离触发，但产线无法严格等距（间隔在 200mm 附近波动），
/// 且中间可能丢帧/跳帧（IsFrameLoss / NeedDrop）→ 相邻两次处理的编码器差为
/// <c>k×Δ̂</c>（k=1 正常，k≥2 丢帧）形态。</para>
///
/// <para>本组测试锁定多假设匹配（k=1..MaxEncoderFrameGap）的行为：
/// ① 触发波动（±2%）在容差内正常命中；② 丢 1~2 帧（fwd=2Δ̂/3Δ̂）经 k=2/k=3 假设命中；
/// ③ Δ̂ 按命中间隔归一化——丢帧观测（fwd=2Δ̂）不参与平滑，Δ̂ 保持单帧语义，
/// 恢复正常帧后 k=1 立即命中（修复前 fwd 直接平滑会把 Δ̂ 拉向 2Δ̂，形成隔帧失配怪圈）。</para>
///
/// <para>场景约束：匹配轴用默认 "Y"（横向）。横向坐标跨帧稳定是位置匹配兜底的前提，
/// 也是 Δ̂ 能经位置过渡建立的前提——与生产配置一致。</para>
///
/// <para>Δ̂ 标称值 200 counts（假设脉冲当量 1 count/mm）。</para>
/// </summary>
public class EncoderGapTrackingTests : IDisposable
{
    private readonly ProductTracker _tracker = new();

    public void Dispose()
    {
        _tracker.Clear();
        Settings.Instance.DedupEnabled = true;
        Settings.Instance.DedupByEncoder = false;
        Settings.Instance.DedupTrackAxis = "Y";
        Settings.Instance.DedupPositionThreshold = 3.0;
        Settings.Instance.DedupAngleThreshold = 2.0;
    }

    private static DbModel Frame(uint encoder, double worldY) =>
        new() { Barcode = "noread", Encode = encoder, WorldX = 5.0, WorldY = worldY, Angle = 0 };

    [Fact]
    public void ProductTracker_EncoderMode_TriggerJitterWithinTolerance_Matches()
    {
        // 触发波动 ±2%（fwd=204 vs Δ̂=200）：在 0.5Δ̂ 容差内，k=1 直接命中
        Settings.Instance.DedupEnabled = true;
        Settings.Instance.DedupByEncoder = true;
        Settings.Instance.DedupTrackAxis = "Y";
        var tracker = new ProductTracker();
        var y = 10.0;

        Assert.Equal(1, tracker.FilterNewAndCount(new[] { Frame(1000, y) }));
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1200, y + 0.1) }));   // Δ̂ 建立 = 200
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1404, y + 0.2) }));   // fwd=204（+2%）
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1596, y + 0.3) }));   // fwd=192（-2%）
    }

    [Fact]
    public void ProductTracker_EncoderMode_DroppedFrame_MatchedViaSecondAssumption()
    {
        // 丢 1 帧：fwd=400=2Δ̂ → k=2 假设命中（修复前无 k 假设，编码器失配，
        // 依赖位置匹配兜底——横向漂移大时兜底失效即重复计数）
        Settings.Instance.DedupEnabled = true;
        Settings.Instance.DedupByEncoder = true;
        Settings.Instance.DedupTrackAxis = "Y";
        var tracker = new ProductTracker();
        var y = 10.0;

        Assert.Equal(1, tracker.FilterNewAndCount(new[] { Frame(1000, y) }));
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1200, y + 0.1) }));   // Δ̂ = 200
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1600, y + 0.2) }));   // ★ fwd=400=2Δ̂ → k=2
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1800, y + 0.3) }));   // 恢复：fwd=200 → k=1
    }

    [Fact]
    public void ProductTracker_EncoderMode_DoubleDroppedFrame_MatchedViaThirdAssumption()
    {
        // 连续丢 2 帧：fwd=600=3Δ̂ → k=3 假设命中
        Settings.Instance.DedupEnabled = true;
        Settings.Instance.DedupByEncoder = true;
        Settings.Instance.DedupTrackAxis = "Y";
        var tracker = new ProductTracker();
        var y = 10.0;

        Assert.Equal(1, tracker.FilterNewAndCount(new[] { Frame(1000, y) }));
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1200, y + 0.1) }));   // Δ̂ = 200
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1800, y + 0.2) }));   // ★ fwd=600=3Δ̂ → k=3
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(2000, y + 0.3) }));   // 恢复：k=1
    }

    [Fact]
    public void ProductTracker_EncoderMode_DeltaNotPollutedByDroppedFrameObservation()
    {
        // ★ 核心回归：丢帧观测（fwd=2Δ̂）不得参与 Δ̂ 平滑——
        //   若被平滑，Δ̂ → 2Δ̂，随后正常帧（fwd=Δ̂）与 2Δ̂ 偏差 = Δ̂ > 容差 0.5·Δ̂，
        //   所有正常帧失配（隔帧匹配怪圈）。本测试验证恢复帧 k=1 立即命中。
        Settings.Instance.DedupEnabled = true;
        Settings.Instance.DedupByEncoder = true;
        Settings.Instance.DedupTrackAxis = "Y";
        var tracker = new ProductTracker();
        var y = 10.0;

        Assert.Equal(1, tracker.FilterNewAndCount(new[] { Frame(1000, y) }));
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1200, y + 0.1) }));   // Δ̂ = 200
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1600, y + 0.2) }));   // 丢 1 帧（k=2）
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1400, y + 0.3) }));   // ★ 恢复正常（k=1，fwd=200）
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1600, y + 0.4) }));   // 再次丢帧仍命中（k=2）
    }

    [Fact]
    public void ProductTracker_EncoderMode_LargeGapBeyondAssumptions_PositionFallbackCatches()
    {
        // 编码器差超出全部假设（fwd=2000=10Δ̂ ≫ k≤4）：编码器匹配失败，
        // 但横向坐标稳定（Y 差 0.2mm ≪ 3mm）→ 位置匹配兜底正确接住，不误判新品。
        // ★ 这验证了纵深防御的第二道防线：编码器假设空间故意不放大（k≤4），
        //   超出部分交给位置证据裁决——横向稳定是比"编码器差恰好为 k×Δ̂"更强的同品证据。
        Settings.Instance.DedupEnabled = true;
        Settings.Instance.DedupByEncoder = true;
        Settings.Instance.DedupTrackAxis = "Y";
        Settings.Instance.DedupPositionThreshold = 3.0;
        var tracker = new ProductTracker();
        var y = 10.0;

        Assert.Equal(1, tracker.FilterNewAndCount(new[] { Frame(1000, y) }));
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1200, y + 0.1) }));   // Δ̂ = 200
        // fwd = 2000 = 10Δ̂：超出全部假设 → 位置兜底命中（横向稳定是强同品证据）→ count=0
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(3200, y + 0.2) }));
    }

    [Fact]
    public void ProductTracker_EncoderMode_TriggerIntervalChange_KeepsDedupWorking()
    {
        // 换产线/换触发间隔（200 → 1200，超出多假设空间 k≤4）：
        // 编码器匹配连续失败 → 第 5 次失败重置 Δ̂ → 位置过渡（横向稳定）重建 Δ̂=1200 →
        // 编码器匹配恢复。断言目标：整个切换过程不产生重复计数（count 全 0）。
        Settings.Instance.DedupEnabled = true;
        Settings.Instance.DedupByEncoder = true;
        Settings.Instance.DedupTrackAxis = "Y";
        var tracker = new ProductTracker();
        var y = 10.0;

        Assert.Equal(1, tracker.FilterNewAndCount(new[] { Frame(1000, y) }));      // 首帧
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1200, y + 0.1) })); // Δ̂ = 200
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(1400, y + 0.2) }));
        // ── 触发间隔变为 1200 ──
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(2600, y + 0.3) }));  // 失配→位置兜底
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(3800, y + 0.4) }));  // 连续失败累计
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(5000, y + 0.5) }));
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(6200, y + 0.6) }));
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(7400, y + 0.7) }));  // 第 5 次失败 → Δ̂ 重置
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(8600, y + 0.8) }));  // 位置过渡重建 Δ̂=1200
        Assert.Equal(0, tracker.FilterNewAndCount(new[] { Frame(9800, y + 0.9) }));  // 编码器匹配恢复
    }

    [Fact]
    public void AngleTracker_TriggerIntervalChange_AngleLockSurvives()
    {
        // 触发间隔变化后角度锁定不得中断（中断会输出 -9999，整条不发 VGT）
        var tracker = new AngleTracker();

        Assert.Equal(45, tracker.Resolve(encoder: 1000, worldX: 10, worldY: 10, modelAngle: 45.0));
        Assert.Equal(45, tracker.Resolve(encoder: 1200, worldX: 10, worldY: 10, modelAngle: null));
        // ── 触发间隔变为 1200 ──（编码器失配 → 位置兜底 → 失败累计 → Δ̂ 重置 → 重建）
        Assert.Equal(45, tracker.Resolve(encoder: 2600, worldX: 10, worldY: 10, modelAngle: null));
        Assert.Equal(45, tracker.Resolve(encoder: 3800, worldX: 10, worldY: 10, modelAngle: null));
        Assert.Equal(45, tracker.Resolve(encoder: 5000, worldX: 10, worldY: 10, modelAngle: null));
        Assert.Equal(45, tracker.Resolve(encoder: 6200, worldX: 10, worldY: 10, modelAngle: null));
        Assert.Equal(45, tracker.Resolve(encoder: 7400, worldX: 10, worldY: 10, modelAngle: null));
        Assert.Equal(45, tracker.Resolve(encoder: 8600, worldX: 10, worldY: 10, modelAngle: null));
        Assert.Equal(45, tracker.Resolve(encoder: 9800, worldX: 10, worldY: 10, modelAngle: null));
        }
    }