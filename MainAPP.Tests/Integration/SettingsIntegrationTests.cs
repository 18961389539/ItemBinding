using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Integration;

/// <summary>
/// Settings 单例集成测试。
/// 验证默认值、Save/Reload 往返、Changed 事件触发等。
/// 注意：Settings 单例依赖文件系统（Saves/Settings/settings.json），测试串行执行避免状态污染。
/// </summary>
public class SettingsIntegrationTests
{
    [Fact]
    public void Instance_IsSingleton()
    {
        var a = Settings.Instance;
        var b = Settings.Instance;

        Assert.Same(a, b);
    }

    [Fact]
    public void Defaults_AreSetCorrectly()
    {
        var s = Settings.Instance;

        Assert.True(s.IsSaveDraw);
        Assert.False(s.IsSaveSource);
        // 2026-09-12 用户定案图片保留 3 天，默认值由 10 改为 3
        Assert.Equal(3, s.MinRecentDays);
        Assert.Equal(5, s.BarcodeRemoveCount);
        Assert.Equal(300_000, s.ExistLoginTimeout);
        Assert.Equal(1000, s.LogsCount);
        Assert.Equal(14, s.BackupExpireDays);
        Assert.Equal(1200, s.WindowWidth);
        Assert.Equal(800, s.WindowHeight);
        Assert.Equal("zh-CN", s.Language);
        Assert.Equal("Light", s.Theme);
        Assert.Equal("ABB机器人", s.WindowTitle);
        Assert.Equal("abb", s.UserName);
        Assert.Equal(5, s.MaxRetries);
        Assert.Equal("127.0.0.1", s.ConnectivityCheckIP);
        Assert.Equal("127.0.0.1", s.DetectionResultSendIP);
        Assert.Equal(11301, s.EncoderReceiverPort);
        Assert.Equal(2611, s.DetectionResultSendPort);
        Assert.Equal("LL", s.MessageReceiver);
        Assert.True(s.DedupEnabled);
        Assert.Equal("Y", s.DedupTrackAxis);
        Assert.Equal(3.0, s.DedupPositionThreshold);
        Assert.Equal(2.0, s.DedupAngleThreshold);
    }

    [Fact]
    public void Save_PersistsProperties()
    {
        var originalLanguage = Settings.Instance.Language;
        var originalTheme = Settings.Instance.Theme;
        try
        {
            Settings.Instance.Language = "en-US";
            Settings.Instance.Theme = "Dark";
            Settings.Instance.Save();
            Settings.Instance.Reload();

            Assert.Equal("en-US", Settings.Instance.Language);
            Assert.Equal("Dark", Settings.Instance.Theme);
        }
        finally
        {
            Settings.Instance.Language = originalLanguage;
            Settings.Instance.Theme = originalTheme;
            Settings.Instance.Save();
        }
    }

    [Fact]
    public void Save_TriggersChangedEvent()
    {
        var fired = false;
        EventHandler handler = (_, _) => fired = true;
        Settings.Instance.Changed += handler;
        try
        {
            Settings.Instance.Save();
            Assert.True(fired);
        }
        finally
        {
            Settings.Instance.Changed -= handler;
        }
    }

    [Fact]
    public void Reload_TriggersChangedEvent()
    {
        var fired = false;
        EventHandler handler = (_, _) => fired = true;
        Settings.Instance.Changed += handler;
        try
        {
            Settings.Instance.Reload();
            Assert.True(fired);
        }
        finally
        {
            Settings.Instance.Changed -= handler;
        }
    }

    [Fact]
    public void Reload_RestoresPersistedValues()
    {
        var original = Settings.Instance.MaxRetries;
        try
        {
            Settings.Instance.MaxRetries = 99;
            Settings.Instance.Save();
            Settings.Instance.MaxRetries = 1; // 修改内存值
            Settings.Instance.Reload();

            Assert.Equal(99, Settings.Instance.MaxRetries);
        }
        finally
        {
            Settings.Instance.MaxRetries = original;
            Settings.Instance.Save();
        }
    }
}
