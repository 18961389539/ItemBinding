using MainAPP.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// 主循环暂停闸门测试（2026-09-16）。
///
/// <para><b>回归背景</b>：暂停原是一个静态布尔，三方使用者各自手写
/// "读 IsLoopPaused → 若未暂停则 PauseLoop → finally 若本次没暂停过则 ResumeLoop"。
/// 那套写法有读-改-写竞态（两方可能同时认为"不是我暂停的"→ 都暂停、都恢复），
/// 而且无法回答"是谁把主循环停住了"。本文件把新语义钉住：**按持有者记账、只能释放自己那份**。</para>
///
/// <para>用例统一使用自建实例（<c>new MainLoopGate()</c>），不复用 <see cref="MainLoopGate.Default"/>，
/// 避免静态状态在用例之间互相污染。</para>
/// </summary>
public class MainLoopGateTests
{
    [Fact]
    public void Pause_ThenIsPaused_AndDescribeNamesTheOwner()
    {
        var gate = new MainLoopGate();

        Assert.False(gate.IsPaused);
        Assert.Equal("未暂停", gate.Describe());

        var becamePaused = gate.Pause(MainLoopGate.RecipePage);

        Assert.True(becamePaused);
        Assert.True(gate.IsPaused);
        Assert.Contains(MainLoopGate.RecipePage, gate.CurrentHolders);
        Assert.Contains(MainLoopGate.RecipePage, gate.Describe());
    }

    [Fact]
    public void Pause_IsIdempotentPerOwner()
    {
        var gate = new MainLoopGate();

        Assert.True(gate.Pause(MainLoopGate.CameraDebug));   // 首次：状态变了
        Assert.False(gate.Pause(MainLoopGate.CameraDebug));  // 重入：状态未变
        Assert.Single(gate.CurrentHolders);

        // 重入两次只需释放一次
        Assert.True(gate.Resume(MainLoopGate.CameraDebug));
        Assert.False(gate.IsPaused);
    }

    /// <summary>
    /// 核心修复点：**非持有者不能释放别人的暂停**。
    /// 旧实现里 `ResumeLoop()` 是个无条件的全局置 false，任何一方恢复都会把别人的暂停一起放掉。
    /// </summary>
    [Fact]
    public void Resume_ByNonHolder_DoesNotReleaseOtherHolders()
    {
        var gate = new MainLoopGate();
        gate.Pause(MainLoopGate.RecipePage);

        var releasedByOther = gate.Resume(MainLoopGate.CameraDebug);

        Assert.False(releasedByOther);      // 相机调试并未持有，释放无效
        Assert.True(gate.IsPaused);         // 配方页的持有不受影响
        Assert.Contains(MainLoopGate.RecipePage, gate.CurrentHolders);
    }

    [Fact]
    public void MultipleHolders_StayPausedUntilEveryOneReleases()
    {
        var gate = new MainLoopGate();
        gate.Pause(MainLoopGate.RecipePage);
        gate.Pause(MainLoopGate.CameraDebug);
        gate.Pause(MainLoopGate.AiChat);

        Assert.Equal(3, gate.CurrentHolders.Count);

        // 注意 Resume 的返回值语义：true = "确实释放了自己那份"，**不是**"循环已恢复"。
        // 这里相机调试确实持有过，所以返回 true；但仍有其它持有者，主循环必须保持暂停。
        Assert.True(gate.Resume(MainLoopGate.CameraDebug));
        Assert.True(gate.IsPaused);

        Assert.True(gate.Resume(MainLoopGate.RecipePage));
        Assert.True(gate.IsPaused);

        Assert.True(gate.Resume(MainLoopGate.AiChat));
        Assert.False(gate.IsPaused);                         // 全部释放后才恢复
        Assert.Empty(gate.CurrentHolders);
    }

    [Fact]
    public void Resume_WhenNotHeldAtAll_ReturnsFalse()
    {
        var gate = new MainLoopGate();

        Assert.False(gate.Resume(MainLoopGate.AiChat));
        Assert.False(gate.IsPaused);
    }

    [Fact]
    public void Describe_ListsEveryHolder()
    {
        var gate = new MainLoopGate();
        gate.Pause(MainLoopGate.RecipePage);
        gate.Pause(MainLoopGate.AiChat);

        var text = gate.Describe();

        Assert.Contains(MainLoopGate.RecipePage, text);
        Assert.Contains(MainLoopGate.AiChat, text);
    }

    [Fact]
    public void Pause_RejectsEmptyOwner()
    {
        var gate = new MainLoopGate();

        Assert.Throws<ArgumentException>(() => gate.Pause(" "));
        Assert.Throws<ArgumentException>(() => gate.Resume(string.Empty));
    }

    /// <summary>
    /// 并发下的收敛性：多个持有者各自 Pause/Resume 配对完成后，最终必须是"未暂停"。
    /// 旧实现（全局布尔）在并发下会出现"最后一次写的值"由调度决定 —— 可能停在"暂停"上。
    /// </summary>
    [Fact]
    public async Task ConcurrentPauseResumePairs_ConvergeToUnpaused()
    {
        var gate = new MainLoopGate();
        const int Owners = 32;
        const int Rounds = 200;

        await Task.WhenAll(Enumerable.Range(0, Owners).Select(i => Task.Run(() =>
        {
            var owner = $"owner-{i}";
            for (var r = 0; r < Rounds; r++)
            {
                gate.Pause(owner);
                gate.Resume(owner);
            }
        })));

        Assert.False(gate.IsPaused);
        Assert.Empty(gate.CurrentHolders);
    }
}
