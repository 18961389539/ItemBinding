using MainAPP.Models;
using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// ProductTracker 产品去重跟踪器单元测试。
/// 覆盖有条码/无条码匹配、过期清理、DedupEnabled 开关与 Clear 行为。
/// 注意：依赖 Settings.Instance 单例，使用共享上下文；测试串行执行避免状态污染。
/// </summary>
public class ProductTrackerTests : IDisposable
{
    private readonly ProductTracker _tracker = new();

    public void Dispose()
    {
        _tracker.Clear();
        // 恢复 DedupEnabled 默认值，避免污染后续测试
        Settings.Instance.DedupEnabled = true;
        Settings.Instance.DedupByEncoder = false;
        Settings.Instance.DedupTrackAxis = "Y";
        Settings.Instance.DedupPositionThreshold = 3.0;
        Settings.Instance.DedupAngleThreshold = 2.0;
    }

    [Fact]
    public void FilterNewAndCount_WhenDedupDisabled_ReturnsInputCount()
    {
        Settings.Instance.DedupEnabled = false;
        var models = new List<DbModel>
        {
            new() { Barcode = "A" },
            new() { Barcode = "B" }
        };

        var count = _tracker.FilterNewAndCount(models);

        Assert.Equal(2, count);
    }

    [Fact]
    public void FilterNewAndCount_NewBarcodes_ReturnsAllAsNew()
    {
        var models = new List<DbModel>
        {
            new() { Barcode = "A" },
            new() { Barcode = "B" },
            new() { Barcode = "C" }
        };

        var count = _tracker.FilterNewAndCount(models);

        Assert.Equal(3, count);
    }

    [Fact]
    public void FilterNewAndCount_DuplicateBarcode_ReturnsZero()
    {
        var models = new List<DbModel> { new() { Barcode = "A" } };
        _tracker.FilterNewAndCount(models);

        var duplicates = new List<DbModel> { new() { Barcode = "A" } };
        var count = _tracker.FilterNewAndCount(duplicates);

        Assert.Equal(0, count);
    }

    [Fact]
    public void FilterNewAndCount_BarcodeCaseSensitive_MatchesAsEqual()
    {
        // ProductTracker 用 StringComparison.Ordinal 比较条码
        _tracker.FilterNewAndCount(new List<DbModel> { new() { Barcode = "ABC" } });

        var count = _tracker.FilterNewAndCount(new List<DbModel> { new() { Barcode = "ABC" } });

        Assert.Equal(0, count);
    }

    [Fact]
    public void FilterNewAndCount_NoreadTreatedAsNoBarcode()
    {
        // "noread" 视为无条码产品，使用位置+角度匹配
        var models = new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100, Angle = 0 }
        };

        var count = _tracker.FilterNewAndCount(models);

        Assert.Equal(1, count);
    }

    [Fact]
    public void FilterNewAndCount_WhitespaceBarcodeTreatedAsNoBarcode()
    {
        var models = new List<DbModel>
        {
            new() { Barcode = "   ", WorldY = 100, Angle = 0 }
        };

        var count = _tracker.FilterNewAndCount(models);

        Assert.Equal(1, count);
    }

    [Fact]
    public void FilterNewAndCount_NoBarcodeMatchesByPositionAndAngle()
    {
        // 第一次添加一个无条码产品
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.0, Angle = 10.0 }
        });

        // 第二次相同位置+角度应视为重复
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.5, Angle = 10.5 }
        });

        Assert.Equal(0, count);
    }

    [Fact]
    public void FilterNewAndCount_NoBarcodePositionExceedsThreshold_TreatedAsNew()
    {
        Settings.Instance.DedupPositionThreshold = 3.0;
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.0, Angle = 10.0 }
        });

        // 位置差 10 > 阈值 3，应视为新产品
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 110.0, Angle = 10.0 }
        });

        Assert.Equal(1, count);
    }

    [Fact]
    public void FilterNewAndCount_NoBarcodeAngleExceedsThreshold_TreatedAsNew()
    {
        Settings.Instance.DedupAngleThreshold = 2.0;
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.0, Angle = 10.0 }
        });

        // 角度差 10 > 阈值 2，应视为新产品
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.0, Angle = 20.0 }
        });

        Assert.Equal(1, count);
    }

    [Fact]
    public void FilterNewAndCount_TrackByX_UsesWorldX()
    {
        Settings.Instance.DedupTrackAxis = "X";
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldX = 50.0, Angle = 0.0 }
        });

        // Y 不同但 X 接近，应视为重复
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldX = 50.5, WorldY = 999.0, Angle = 0.5 }
        });

        Assert.Equal(0, count);
    }

    [Fact]
    public void FilterNewAndCount_AngleWraps180Degrees()
    {
        // AngleDifference 考虑 180 度周期：0 与 179 差 1 度
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.0, Angle = 0.0 }
        });

        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.0, Angle = 179.0 }
        });

        Assert.Equal(0, count); // 差 1 度 < 阈值 2，视为重复
    }

    [Fact]
    public void Clear_RemovesAllTrackedItems()
    {
        _tracker.FilterNewAndCount(new List<DbModel> { new() { Barcode = "A" } });
        _tracker.Clear();

        // 清空后再次添加相同条码应视为新
        var count = _tracker.FilterNewAndCount(new List<DbModel> { new() { Barcode = "A" } });

        Assert.Equal(1, count);
    }

    [Fact]
    public void FilterNewAndCount_EmptyList_ReturnsZero()
    {
        var count = _tracker.FilterNewAndCount(new List<DbModel>());

        Assert.Equal(0, count);
    }

    [Fact]
    public void FilterNewAndCount_MixedBarcodeAndNoBarcode_OnlyMatchesWithinCategory()
    {
        // 有条码 A 与无条码 noread 同位置
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "A", WorldY = 100.0, Angle = 0.0 },
            new() { Barcode = "noread", WorldY = 100.0, Angle = 0.0 }
        });

        // 再次添加无条码 noread 同位置，应匹配到第一个无条码项
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.0, Angle = 0.0 }
        });

        Assert.Equal(0, count);
    }

    // ---------- 编码器差分匹配场景（DedupByEncoder=true） ----------

    [Fact]
    public void EncoderMode_SameProductAcrossFrames_NotCountedTwice()
    {
        Settings.Instance.DedupByEncoder = true;

        // 第 1 帧：两个产品，编码器 1000/2000（间距 1000 counts）
        var count1 = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100, Angle = 0, Encode = 1000 },
            new() { Barcode = "noread", WorldY = 200, Angle = 0, Encode = 2000 },
        });
        Assert.Equal(2, count1);

        // 第 2 帧：同一批产品前移 30 counts（Δ̂ 建立：位置匹配过渡）
        var count2 = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.5, Angle = 0, Encode = 1030 },
            new() { Barcode = "noread", WorldY = 200.5, Angle = 0, Encode = 2030 },
        });
        Assert.Equal(0, count2);

        // 第 3 帧：编码器差分匹配生效，世界坐标可抖动（不参与匹配）
        var count3 = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 150, Angle = 0, Encode = 1060 },
            new() { Barcode = "noread", WorldY = 250, Angle = 0, Encode = 2060 },
        });
        Assert.Equal(0, count3);
    }

    [Fact]
    public void EncoderMode_EncoderWrapsAround_StillDeduplicated()
    {
        Settings.Instance.DedupByEncoder = true;

        // 首帧在回绕边界前
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100, Angle = 0, Encode = uint.MaxValue - 5 }
        });

        // 第二帧回绕到 10：无符号前进距离 = 15 counts，位置过渡匹配
        var count2 = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.5, Angle = 0, Encode = 10 }
        });
        Assert.Equal(0, count2);

        // 第三帧继续前移 15 counts：差分匹配
        var count3 = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 200, Angle = 0, Encode = 25 }
        });
        Assert.Equal(0, count3);
    }

    [Fact]
    public void EncoderMode_LargeGap_TreatedAsNewProduct()
    {
        Settings.Instance.DedupByEncoder = true;

        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100, Angle = 0, Encode = 1000 }
        });
        // 建立 Δ̂ = 30
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.5, Angle = 0, Encode = 1030 }
        });

        // 大跳变（8970 counts）且位置远离 → 新产品
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 900, Angle = 0, Encode = 10000 }
        });
        Assert.Equal(1, count);
    }

    [Fact]
    public void EncoderMode_EncoderZero_FallsBackToPositionMatching()
    {
        Settings.Instance.DedupByEncoder = true;

        // Encode=0（编码器无效）→ 走位置匹配
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100, Angle = 10, Encode = 0 }
        });
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.5, Angle = 10.5, Encode = 0 }
        });
        Assert.Equal(0, count);
    }

    [Fact]
    public void EncoderMode_StartupPhase_UnknownAngle_StillDeduplicated()
    {
        Settings.Instance.DedupByEncoder = true;

        // 首帧：无角度特征（Angle = -9999 未知哨兵），编码器 1000
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100, Angle = AngleTracker.UnknownAngle, Encode = 1000 }
        });

        // 第二帧：仍无特征，编码器前移 30 counts，位置接近。
        // 编码器模式启动阶段（Δ̂ 未建立）过渡匹配必须忽略角度，否则 -9999 会永远匹配不上导致重复计数。
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.5, Angle = AngleTracker.UnknownAngle, Encode = 1030 }
        });
        Assert.Equal(0, count);
    }

    [Fact]
    public void PositionMode_UnknownAngle_RelaxedToPositionOnly()
    {
        Settings.Instance.DedupByEncoder = false;

        // 首帧：无特征（Angle = -9999）
        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100, Angle = AngleTracker.UnknownAngle }
        });

        // 第二帧：仍无特征，位置接近 → 应视为同一产品（角度未知放宽为纯位置匹配）
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100.5, Angle = AngleTracker.UnknownAngle }
        });
        Assert.Equal(0, count);
    }

    [Fact]
    public void PositionMode_UnknownAngleAndFarPosition_TreatedAsNew()
    {
        Settings.Instance.DedupByEncoder = false;

        _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 100, Angle = AngleTracker.UnknownAngle }
        });

        // 位置差超阈值 → 新产品（即使角度都未知）
        var count = _tracker.FilterNewAndCount(new List<DbModel>
        {
            new() { Barcode = "noread", WorldY = 200, Angle = AngleTracker.UnknownAngle }
        });
        Assert.Equal(1, count);
    }
}
