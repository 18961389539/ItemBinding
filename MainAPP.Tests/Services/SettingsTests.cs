using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// Settings 拆分后子配置类属性转发单元测试。
/// 验证 Settings 顶层转发属性与子配置类（Storage/Ui/Network/Database/Algorithm/Security）
/// 之间的双向读写一致性。使用单例并在 finally 中恢复原值避免状态污染。
/// </summary>
public class SettingsTests
{
    [Fact]
    public void Instance_SubConfigs_AreInitialized()
    {
        var s = Settings.Instance;

        Assert.NotNull(s.Storage);
        Assert.NotNull(s.Ui);
        Assert.NotNull(s.Network);
        Assert.NotNull(s.Database);
        Assert.NotNull(s.Algorithm);
        Assert.NotNull(s.Security);

        Assert.IsType<StorageSettings>(s.Storage);
        Assert.IsType<AlgorithmSettings>(s.Algorithm);
    }

    [Fact]
    public void Forwarding_Storage_IsSaveDraw_BidirectionalSync()
    {
        var s = Settings.Instance;
        var original = s.IsSaveDraw;
        try
        {
            s.IsSaveDraw = true;
            Assert.True(s.Storage.IsSaveDraw);

            s.Storage.IsSaveDraw = false;
            Assert.False(s.IsSaveDraw);
        }
        finally
        {
            s.IsSaveDraw = original;
        }
    }

    [Fact]
    public void Forwarding_Algorithm_DedupEnabled_BidirectionalSync()
    {
        var s = Settings.Instance;
        var original = s.DedupEnabled;
        try
        {
            s.DedupEnabled = false;
            Assert.False(s.Algorithm.DedupEnabled);

            s.Algorithm.DedupEnabled = true;
            Assert.True(s.DedupEnabled);
        }
        finally
        {
            s.DedupEnabled = original;
        }
    }

    [Fact]
    public void Forwarding_Algorithm_DedupPositionThreshold_BidirectionalSync()
    {
        var s = Settings.Instance;
        var original = s.DedupPositionThreshold;
        try
        {
            s.DedupPositionThreshold = 7.5;
            Assert.Equal(7.5, s.Algorithm.DedupPositionThreshold);

            s.Algorithm.DedupPositionThreshold = 12.3;
            Assert.Equal(12.3, s.DedupPositionThreshold);
        }
        finally
        {
            s.DedupPositionThreshold = original;
        }
    }

    [Fact]
    public void Forwarding_Storage_MinRecentDays_BidirectionalSync()
    {
        var s = Settings.Instance;
        var original = s.MinRecentDays;
        try
        {
            s.MinRecentDays = 30;
            Assert.Equal(30, s.Storage.MinRecentDays);

            s.Storage.MinRecentDays = 5;
            Assert.Equal(5, s.MinRecentDays);
        }
        finally
        {
            s.MinRecentDays = original;
        }
    }
}
