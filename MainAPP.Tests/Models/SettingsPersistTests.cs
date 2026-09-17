using MainAPP.Models;
using MainAPP.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MainAPP.Tests.Models;

/// <summary>
/// Settings 单例线程安全 + 定期落盘测试（2026-09-16）。
///
/// <para><b>回归背景</b>：① <c>Instance</c> 原为无锁双重检查
/// （<c>if (_instance == null) _instance = Load();</c>），并发首访会各自 Load 出不同实例，
/// 后写者覆盖先写者，先拿到的调用方握着被丢弃的实例；② 配置原先只在设置页/切配方/退出三处显式
/// <c>Save()</c>，其他路径改了属性在崩溃时会静默丢失。</para>
///
/// <para><b>隔离方式</b>：本文件会改动真实单例与磁盘文件，故用例统一
/// "保存原文件内容 → 改动 → 断言 → finally 还原属性并写回原文件 → Reload"。
/// 测试工程已禁用并行执行，且数据根被隔离到测试 bin 下的 TestDataRoot。</para>
/// </summary>
public class SettingsPersistTests
{
    [Fact]
    public void Instance_IsSingleInstanceUnderConcurrentAccess()
    {
        // 无锁双重检查的经典症状：并发首访拿到不同实例。Lazy 之后必须全部同一引用。
        var refs = new Settings[64];

        Parallel.For(0, refs.Length, i => refs[i] = Settings.Instance);

        Assert.All(refs, r => Assert.Same(refs[0], r));
    }

    [Fact]
    public void TryFlushToDiskIfChanged_WritesOnChange_AndSkipsWhenUnchanged()
    {
        var settings = Settings.Instance;
        var originalLanguage = settings.Language;
        var originalFile = ReadSettingsFileOrNull();
        try
        {
            // 用一个必然不同的值，保证"内容有变化"这一前提成立（与执行顺序无关）
            settings.Language = "test-" + Guid.NewGuid().ToString("N");

            var wrote = settings.TryFlushToDiskIfChanged();
            Assert.True(wrote, "内容已变化时应落盘");

            // 立即再刷一次：内容未变 → 不应重复写盘
            var wroteAgain = settings.TryFlushToDiskIfChanged();
            Assert.False(wroteAgain, "内容未变化时不应重复落盘");

            // 落盘内容确实包含刚写入的值
            var json = ReadSettingsFileOrNull();
            Assert.NotNull(json);
            Assert.Contains(settings.Language, json);
        }
        finally
        {
            settings.Language = originalLanguage;
            RestoreSettingsFile(originalFile);
            // 让内存态与文件重新对齐，避免影响其它用例
            settings.Reload();
        }
    }

    [Fact]
    public void Save_WritesFileAndRaisesChanged()
    {
        var settings = Settings.Instance;
        var originalLanguage = settings.Language;
        var originalFile = ReadSettingsFileOrNull();
        var raised = 0;
        void OnChanged(object? sender, EventArgs e) => raised++;

        try
        {
            settings.Changed += OnChanged;
            settings.Language = "test-" + Guid.NewGuid().ToString("N");

            settings.Save();

            Assert.True(raised > 0, "Save 应触发 Changed（订阅方据此刷新）");
            Assert.NotNull(ReadSettingsFileOrNull());
        }
        finally
        {
            settings.Changed -= OnChanged;
            settings.Language = originalLanguage;
            RestoreSettingsFile(originalFile);
            settings.Reload();
        }
    }

    [Fact]
    public void StartPeriodicPersist_IsIdempotent_AndStoppable()
    {
        // 只验证"可重复启动、可停止"不抛异常（真正的周期性行为由定时器驱动，不适合在单测里等 60s）
        var settings = Settings.Instance;

        settings.StartPeriodicPersist();
        settings.StartPeriodicPersist();   // 幂等

        settings.StopPeriodicPersist();
        settings.StopPeriodicPersist();    // 幂等
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static string? ReadSettingsFileOrNull()
    {
        var path = DataPaths.SettingsFile;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static void RestoreSettingsFile(string? originalContent)
    {
        var path = DataPaths.SettingsFile;
        if (originalContent is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        File.WriteAllText(path, originalContent);
    }
}
