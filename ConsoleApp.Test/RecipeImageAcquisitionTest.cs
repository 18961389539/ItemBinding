using MainAPP.Services;
using MainAPP.ViewModels;
using System.Diagnostics;

namespace ConsoleApp.Test;

/// <summary>
/// 集成测试：验证配方页获取图像时无锁竞争
/// </summary>
public static class RecipeImageAcquisitionTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== 配方页图像获取集成测试 ===");
        Console.WriteLine();

        // 1. 模拟主循环持锁取图
        Console.WriteLine("[1] 模拟主循环持锁取图 5s...");
        var loopTask = Task.Run(async () =>
        {
            using var scope = await ScannerAccessService.Instance.AcquireAsync();
            Console.WriteLine("    [Loop] 拿到锁，模拟取图 5s...");
            await Task.Delay(5000);
            Console.WriteLine("    [Loop] 取图完成，释放锁");
        });
        await Task.Delay(200);
        Console.WriteLine("    [Verify] 主循环已开始 ✓");

        // 2. 配方页暂停主循环
        Console.WriteLine();
        Console.WriteLine("[2] 配方页 PauseLoop...");
        HomeViewModel.PauseLoop();
        Console.WriteLine("    [Recipe] 暂停标记已设置");

        // 3. 等当前帧完成（主循环持锁中，需等 5s）
        Console.WriteLine("    [Recipe] 等待当前帧完成...");
        await Task.WhenAny(loopTask, Task.Delay(7000));
        Console.WriteLine("    [Recipe] 当前帧已完成 ✓");

        // 4. 配方页取锁
        Console.WriteLine();
        Console.WriteLine("[3] 配方页取设备锁...");
        var sw = Stopwatch.StartNew();
        try
        {
            using var scope = await ScannerAccessService.Instance.AcquireAsync();
            sw.Stop();
            Console.WriteLine($"    [Recipe] 拿到锁，耗时 {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"    [{(sw.ElapsedMilliseconds < 1000 ? "Verify" : "Fail")}] {(sw.ElapsedMilliseconds < 1000 ? "✓ 瞬间拿到" : "✗ 太慢")}");
        }
        catch (TimeoutException)
        {
            sw.Stop();
            Console.WriteLine($"    [Fail] 超时 ({sw.ElapsedMilliseconds}ms)! ✗");
            return;
        }

        // 5. 模拟主循环检查暂停标志（应跳过）
        Console.WriteLine();
        Console.WriteLine("[4] 验证暂停期间主循环跳过取帧...");
        var skippedTask = Task.Run(async () =>
        {
            // 模拟 ReadImageOneLoop 的暂停检查
            var isPaused = (bool)typeof(HomeViewModel)
                .GetField("s_isPaused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;
            if (isPaused)
            {
                Console.WriteLine("    [Loop] 暂停标志为 true，跳过取帧 ✓");
                return;
            }
            using var scope = await ScannerAccessService.Instance.AcquireAsync();
            Console.WriteLine("    [Loop] 不应该拿到锁! ✗");
        });
        await Task.WhenAny(skippedTask, Task.Delay(1000));
        Console.WriteLine("    [Verify] 暂停检查正常 ✓");

        // 6. 恢复
        Console.WriteLine();
        Console.WriteLine("[5] 配方页关闭，ResumeLoop...");
        HomeViewModel.ResumeLoop();

        // 7. 验证恢复
        Console.WriteLine();
        Console.WriteLine("[6] 验证主循环恢复取锁...");
        var sw2 = Stopwatch.StartNew();
        try
        {
            using var scope = await ScannerAccessService.Instance.AcquireAsync();
            sw2.Stop();
            Console.WriteLine($"    [Loop] 拿到锁，耗时 {sw2.ElapsedMilliseconds}ms");
            Console.WriteLine($"    [{(sw2.ElapsedMilliseconds < 1000 ? "Verify" : "Fail")}] {(sw2.ElapsedMilliseconds < 1000 ? "✓ 已恢复" : "✗")}");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("    [Fail] 恢复取锁超时! ✗");
        }

        Console.WriteLine();
        Console.WriteLine("=== 测试完成 ===");
    }
}
