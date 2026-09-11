using MainAPP.Models;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// StorageSettings 子配置类单元测试。
/// 验证默认值、属性读写与边界条件。
/// </summary>
public class StorageSettingsTests
{
    [Fact]
    public void Defaults_AreSetCorrectly()
    {
        var settings = new StorageSettings();

        Assert.True(settings.IsSaveDraw);
        Assert.False(settings.IsSaveSource);
        Assert.Equal(10, settings.MinRecentDays);
        Assert.NotNull(settings.PicturesSaveFolder);
        Assert.Contains("Saves", settings.PicturesSaveFolder);
        Assert.Contains("Pictures", settings.PicturesSaveFolder);
    }

    [Fact]
    public void Properties_CanBeAssigned()
    {
        var settings = new StorageSettings
        {
            IsSaveDraw = false,
            IsSaveSource = true,
            MinRecentDays = 30,
            PicturesSaveFolder = @"D:\Images"
        };

        Assert.False(settings.IsSaveDraw);
        Assert.True(settings.IsSaveSource);
        Assert.Equal(30, settings.MinRecentDays);
        Assert.Equal(@"D:\Images", settings.PicturesSaveFolder);
    }

    [Fact]
    public void MinRecentDays_AcceptsZeroAndNegativeValues()
    {
        // 默认值不强制范围约束，仅验证读写一致性
        var settings = new StorageSettings();

        settings.MinRecentDays = 0;
        Assert.Equal(0, settings.MinRecentDays);

        settings.MinRecentDays = -1;
        Assert.Equal(-1, settings.MinRecentDays);
    }

    [Fact]
    public void PicturesSaveFolder_CanBeNullOrEmpty()
    {
        var settings = new StorageSettings();

        settings.PicturesSaveFolder = "";
        Assert.Equal("", settings.PicturesSaveFolder);

        settings.PicturesSaveFolder = null!;
        Assert.Null(settings.PicturesSaveFolder);
    }

    [Fact]
    public void IsSaveDraw_AndIsSaveSource_AreIndependent()
    {
        var settings = new StorageSettings();

        settings.IsSaveDraw = false;
        Assert.False(settings.IsSaveDraw);
        Assert.False(settings.IsSaveSource);

        settings.IsSaveSource = true;
        Assert.False(settings.IsSaveDraw);
        Assert.True(settings.IsSaveSource);
    }
}
