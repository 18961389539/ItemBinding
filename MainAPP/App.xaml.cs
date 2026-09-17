using Extensions;
using MainAPP.Application;
using MainAPP.Models;
using MainAPP.Services;
using MainAPP.Services.AI;
using MainAPP.Views;
using MainAPP.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SixLabors.ImageSharp.Memory;
using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

// VSTHRD100: OnStartup/OnExit 是 WPF 生命周期事件处理器，必须保持 async void；已包 try-catch
#pragma warning disable VSTHRD100

namespace MainAPP
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private Task? _scannerInitializeTask;
        private CancellationTokenSource? _scannerInitializeCts;
        private Task? _memorySnapshotTask;
        private readonly CancellationTokenSource _memorySnapshotCts = new();
        // L97: 内存快照记录间隔（秒），提取为常量避免硬编码
        private const int MemorySnapshotIntervalSec = 60;
        // 数据/日志清理后台任务
        private Task? _dataCleanupTask;
        private readonly CancellationTokenSource _dataCleanupCts = new();
        // RTC(2026-08-06): 配方切换 TCP 服务（上位机指令切换配方，监听 5000 端口）
        // 2026-09-16: 以下四个对外宿主改为由 DI 容器创建并持有（原先在此处直接 new，
        // 与容器里注册的其它服务形成"一部分走容器、一部分手工 new"的双轨）。
        // 字段在 ConfigureServices 中（容器建好后）赋值，容器负责 Dispose；
        // OnExit 里仍显式 Stop()，且必须早于容器释放，保持原有关闭时序。
        private RecipeTcpServerService? _recipeTcpServer;
        // 2026-09-11: 相机调试 Web 服务（手机实时看图 + 只读参数，监听 5188 端口）。
        // 由原 WebLiveView 独立进程的能力移植而来，改为进程内托管以复用 Devices.Scanners 单例，
        // 避免两个进程争抢同一台 GigE 读码器；使用 HttpListener 而非 Kestrel，零新增框架依赖。
        private CameraWebHost? _cameraWebHost;
        // 2026-09-12: AI 对话 Web 服务（浏览器级对话 UI，监听 5190 端口）。
        // 内嵌页面跑 Deep Chat（MIT），C# 侧只提供 /ai/chat 端点桥接到 AiChatService。
        // 同一份页面也暴露给手机浏览器，与 CameraWebHost（5188）同一模式、错开端口。
        private AiWebHost? _aiWebHost;
        // 2026-09-13: 统一远程运维门户（5188/5190 错开端口 5191）
        private OpsWebHost? _opsWebHost;
        // L407b: OnExit 数据清理任务等待超时（秒）
        private const int OnExitDataCleanupWaitSec = 5;
        private const int OnExitDataCleanupFinalWaitSec = 1;
        // M342b: 设置加载标志，仅在设置成功加载后才允许 OnExit 保存，避免启动失败时用默认值覆盖
        private static bool _settingsLoaded;
        // L407b: OnExit 各阶段超时值（秒），提取为常量避免魔法数字
        private const int OnExitMemorySnapshotWaitSec = 2;
        private const int OnExitMemorySnapshotFinalWaitSec = 1;
        private const int OnExitSaveCtsTimeoutSec = 2;
        private const int OnExitSaveTimeoutSec = 3;
        private const int OnExitSaveFinalWaitSec = 1;
        private const int OnExitToVGTStopSec = 3;
        private const int OnExitScannerInitWaitSec = 5;
        private const int OnExitScannerInitFinalWaitSec = 1;
        // L408b: InitializeLogging SQLite Sink 参数，提取为常量避免魔法数字
        private const int LogRetentionDays = 10;
        private const int LogBatchSize = 10;
        private const int LogMaxDbSizeMb = 2;

        // DI 容器：组合根，在 ConfigureServices（App.OnStartup 内）中建立。
        // 2026-09-16: 由 `public static IServiceProvider Services { get; private set; } = default!;`
        // 改为"未初始化即抛明确异常"——原先的 default! 在容器建立前被访问会得到 null，
        // 报出的是某个下游的 NullReferenceException，排查时看不出真正原因。
        private static IServiceProvider? s_services;

        /// <summary>
        /// DI 容器。仅在 <see cref="ConfigureServices"/> 之后可用；过早访问会抛出可识别的异常。
        /// </summary>
        public static IServiceProvider Services =>
            s_services ?? throw new InvalidOperationException(
                "DI 容器尚未初始化：App.Services 只能在 App.OnStartup 的 ConfigureServices 之后访问。" +
                "若当前处于设计时（VS 设计器）或启动早期，请改用 App.ServicesOrNull。");

        /// <summary>
        /// DI 容器；未初始化时返回 <c>null</c>。仅用于"可能尚未初始化"的场景
        /// （设计时视图、启动早期探询），业务流程请用 <see cref="Services"/>，以免静默拿到 null。
        /// </summary>
        public static IServiceProvider? ServicesOrNull => s_services;

        protected override async void OnStartup(StartupEventArgs e)
        {
            // H52d: async void 顶层 try-catch，防止未处理异常静默终止进程
            try
            {
                // H50d: 日志初始化前调用，需独立异常保护（此时日志未初始化，用 Debug 输出）
                try
                {
                    PCPowerHelper.DisablePowerThrottling();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"禁用电源节流失败: {ex}");
                }
                RenderOptions.ProcessRenderMode = RenderMode.Default;
                // 2026-09-15: 数据根解析与历史数据搬运必须在日志初始化之前完成——
                // 日志库本身也要落到新数据根，且 SQLite 文件不能在句柄打开后再搬运。
                var migrationNote = RunDataRootMigration();
                ConfigureImageSharpMemory();
                InitializeLogging();
                // 数据根结论在日志可用后回填输出，方便现场核对数据到底落在哪
                Log.Information($"[数据根] {DataPaths.Root} | {DataPaths.ResolutionNote}");
                if (!string.IsNullOrEmpty(migrationNote))
                {
                    Log.Warning($"[数据根] {migrationNote}");
                }
                // DI 容器初始化：注册现有单例服务，为后续渐进式迁移到构造函数注入打基础
                // 不改变现有 .Instance 调用方式，DI 容器与新基础设施共存
                ConfigureServices();
                // L419a: ReloadSettingsAndStartCleanup 必须在 base.OnStartup 之前调用，
                // 保持同步执行以便 MainWindow 构造时能使用最新设置（ApplyWindowSettings 等）
                ReloadSettingsAndStartCleanup();
                base.OnStartup(e);
                // H20: 全局异常处理器必须在任何 await 之前注册，
                // 否则 await 期间的异常会直接终止进程无日志
                RegisterGlobalExceptionHandlers();
                // LIC(2026-08-06): 离线授权门槛——未激活/过期/换机则弹出激活窗口，拒绝则退出
                // （StartupUri 已移除，主窗口改由代码创建，见下方）
                if (!EnsureActivated())
                {
                    Shutdown(-1);
                    return;
                }
                await StartApplicationServices();
                // M61: 此处后续代码需在 UI 线程执行（InitializeRecipes 等访问 UI），不加 ConfigureAwait(false)
                await InitializeDatabaseAsync();
                InitializeRecipes();
                StartMemoryDiagnostics();
                // 启动数据/日志清理后台任务（数据库就绪后）
                StartDataCleanup();
                // LIC(2026-08-06): StartupUri 已移除，初始化完成后手动创建主窗口
                new MainWindow().Show();
            }
            catch (Exception ex)
            {
                // 启动失败时提示用户并退出（日志可能已初始化，尝试记录）
                try { LogService.Instance.Error($"应用程序启动失败: {ex}"); } catch { }
                // H80f: 用嵌套 try-catch 保护 NotificationService，UI 不可用时跳过提示
                try { NotificationService.Error($"应用程序启动失败: {ex}"); }
                catch { /* UI 不可用，跳过提示 */ }
                Shutdown(-1);
            }
        }

        /// <summary>
        /// 2026-09-16: 授权门禁改为**运行期**解析，不再使用编译期常量。
        /// <para>历史：此处原为 <c>private static readonly bool ActivationRequired = false;</c>。
        /// 现场为规避「换 Windows 账户后要求重新激活」（机器指纹含 UserName + 授权文件按用户存放，
        /// 双重失配）而把它置 false。但常量形态使"临时停用"实为永久停用——恢复必须改源码重编译。
        /// 该作用域错配已由 2026-09-15 的数据根改造消除（授权文件已随数据根按机器存放），
        /// 指纹中的 UserName 也已移除，故门禁恢复默认启用。</para>
        /// <para>切换方式（均为运行期，无需重编译）：
        /// ① 环境变量 <see cref="LicenseService.BypassEnvironmentVariable"/>=1 临时旁路（调试）；
        /// ② 配置项 <c>Security.LicenseRequired</c>（settings.json）设为 false。</para>
        /// </summary>
        private bool EnsureActivated()
        {
            if (!LicenseService.IsGateEnabled)
            {
                LogService.Instance.Warning(
                    $"[LIC] 授权门禁已关闭（{LicenseService.GateDecisionNote}）——不校验激活码、不弹激活窗口。" +
                    $"恢复方式：删除环境变量 {LicenseService.BypassEnvironmentVariable}，" +
                    "并把设置项 Security.LicenseRequired 置为 true。");
                return true;
            }

            try
            {
                if (LicenseService.ValidateAtStartup())
                {
                    return true;
                }

                var activationWindow = new ActivationWindow();
                return activationWindow.ShowDialog() == true;
            }
            catch (Exception ex)
            {
                try { LogService.Instance.Error($"授权检查失败: {ex}"); } catch { }
                return false;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // ★ 2026-09-15 修复：由 async void 改为同步方法 + 有界阻塞等待。
            //
            // 原因：WPF 调用 OnExit 时**不会 await 它**。原实现是 `async void`，命中第一个真正
            // 让出线程的 await 后，WPF 继续走完关闭流程并结束进程；该 await 的续体被投递到已在
            // 关闭的 Dispatcher 上，永远不会被调度 —— 后面的代码等于不存在。
            //
            // 修复前的实际后果（全部被跳过）：设置落盘、待处理条码落盘、ToVGT/配方TCP/相机/AI/
            // 运维门户/llama-server 各服务停止、扫码枪关闭（含 AutoReconnect 禁用与 SDK 清理窗口）、
            // DI 容器释放、日志刷盘。
            //
            // 为何同步阻塞是安全的：等待对象都是后台 Task，且 MemoryDiagnostics.RunPeriodicSnapshotAsync
            // 与 DataRetentionService.RunPeriodicCleanupAsync 内部均已 ConfigureAwait(false)，
            // 不会回调 Dispatcher；所有等待都有超时上限，即使个别任务卡住也不会永久挂起。
            // 退出期间界面本就不需要响应，阻塞是可接受的。
#pragma warning disable VSTHRD002 // 退出流程必须同步等待，异步等待在此处等价于不等待
            SaveCriticalStateBeforeShutdown();

            // M322a: 分段包 try-catch，避免单个清理操作抛出跳过后续清理
            try { _memorySnapshotCts.Cancel(); }
            catch (Exception ex) { LogService.Instance.Error($"取消内存快照任务失败: {ex}"); }
            try { (_memorySnapshotTask?.WaitAsync(TimeSpan.FromSeconds(OnExitMemorySnapshotWaitSec)) ?? Task.CompletedTask).GetAwaiter().GetResult(); }
            catch (Exception ex) { LogService.Instance.Error($"等待内存快照任务退出失败: {ex}"); }
            // H63: 等待任务完全退出后再 Dispose CTS，避免任务访问已释放的 token 造成 use-after-dispose
            if (_memorySnapshotTask is not null && !_memorySnapshotTask.IsCompleted)
            {
                try { _memorySnapshotTask.WaitAsync(TimeSpan.FromSeconds(OnExitMemorySnapshotFinalWaitSec)).GetAwaiter().GetResult(); }
                catch (Exception ex) { LogService.Instance.Error($"等待内存快照任务最终退出失败: {ex}"); }
            }
            try { _memorySnapshotCts.Dispose(); }
            catch (Exception ex) { LogService.Instance.Error($"释放内存快照 CTS 失败: {ex}"); }
            // 数据/日志清理任务取消与等待
            try { _dataCleanupCts.Cancel(); }
            catch (Exception ex) { LogService.Instance.Error($"取消数据清理任务失败: {ex}"); }
            try { (_dataCleanupTask?.WaitAsync(TimeSpan.FromSeconds(OnExitDataCleanupWaitSec)) ?? Task.CompletedTask).GetAwaiter().GetResult(); }
            catch (Exception ex) { LogService.Instance.Error($"等待数据清理任务退出失败: {ex}"); }
            if (_dataCleanupTask is not null && !_dataCleanupTask.IsCompleted)
            {
                try { _dataCleanupTask.WaitAsync(TimeSpan.FromSeconds(OnExitDataCleanupFinalWaitSec)).GetAwaiter().GetResult(); }
                catch (Exception ex) { LogService.Instance.Error($"等待数据清理任务最终退出失败: {ex}"); }
            }
            try { _dataCleanupCts.Dispose(); }
            catch (Exception ex) { LogService.Instance.Error($"释放数据清理 CTS 失败: {ex}"); }
            try { RecipesManage.Instance.CurrentRecipeChanged -= RecipesManage_CurrentRecipeChanged; }
            catch (Exception ex) { LogService.Instance.Error($"取消订阅 CurrentRecipeChanged 失败: {ex}"); }
            // L380a: TemporaryLog.CloseAndFlush 移到末尾 finally 块中与 Log.CloseAndFlush 相邻，
            // 避免在 OnExit 清理过程中过早关闭导致后续日志丢失
            // M311a: 显式 try-finally，确保 saveTask 完成后才 Dispose CTS，避免 use-after-dispose
            CancellationTokenSource? saveCts = null;
            try
            {
                saveCts = new CancellationTokenSource(TimeSpan.FromSeconds(OnExitSaveCtsTimeoutSec));
                // M326b: 将 Task.Run 调用包入 try-catch，避免其本身抛出异常（如 OOM）跳过后续 OnExit 清理
                Task? saveTask;
                try
                {
                    saveTask = Task.Run(() => BarcodeDataService.Instance.SavePendingAsync(saveCts.Token));
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"启动保存待处理条码任务失败: {ex}");
                    saveTask = null;
                }
                if (saveTask is not null)
                {
                    try
                    {
                        // M293a: 显式超时，避免无超时阻塞退出流程
                        saveTask.WaitAsync(TimeSpan.FromSeconds(OnExitSaveTimeoutSec)).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error($"保存待处理条码失败: {ex}");
                    }
                    // H63: 确保任务已退出后再 Dispose，避免 use-after-dispose
                    if (!saveTask.IsCompleted)
                    {
                        try { saveTask.WaitAsync(TimeSpan.FromSeconds(OnExitSaveFinalWaitSec)).GetAwaiter().GetResult(); }
                        catch (Exception ex) { LogService.Instance.Error($"等待保存任务最终退出失败: {ex}"); }
                    }
                }
            }
            finally
            {
                saveCts?.Dispose();
            }
            // H52a: Dispose 单独 try-catch，避免抛出跳过后续清理
            try { BarcodeDataService.Instance.Dispose(); }
            catch (Exception ex) { LogService.Instance.Error($"释放 BarcodeDataService 失败: {ex}"); }

            try
            {
                // 通过 DI 获取 IToVGTService 并释放（替代原 ToVGT.StopAsync() 静态调用）
                var toVgt = Services.GetRequiredService<IToVGTService>();
                toVgt.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(OnExitToVGTStopSec)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"停止 ToVGT 失败: {ex}");
            }

            // RTC(2026-08-06): 停止配方切换 TCP 服务
            try { _recipeTcpServer?.Stop(); }
            catch (Exception ex) { LogService.Instance.Error($"停止配方切换 TCP 服务失败: {ex}"); }

            // 2026-09-11: 停止相机调试 Web 服务。必须在扫码枪关闭之前完成，
            // 否则可能遗留"主循环暂停 + 软触发"状态阻止进程正常退出。
            try { _cameraWebHost?.Stop(); }
            catch (Exception ex) { LogService.Instance.Error($"停止相机调试 Web 服务失败: {ex}"); }

            // 2026-09-12: 停止 AI 对话 Web 服务（与相机调试服务同一时序，须在扫码枪关闭之前）
            try { _aiWebHost?.Stop(); }
            catch (Exception ex) { LogService.Instance.Error($"停止 AI 对话 Web 服务失败: {ex}"); }

            // 2026-09-13: 停止统一运维门户（同一时序）
            try { _opsWebHost?.Stop(); }
            catch (Exception ex) { LogService.Instance.Error($"停止运维门户 Web 服务失败: {ex}"); }

            // 2026-09-12: 停止 llama-server 侧车（进程内推理已退役，显存由侧车持有）
            try { LlamaServerHost.Instance.Stop(); }
            catch (Exception ex) { LogService.Instance.Error($"停止 llama-server 侧车失败: {ex}"); }

            // H64: 超时后 Cancel 并等待任务退出（带较短二次等待），再调用 Close()
            try { _scannerInitializeCts?.Cancel(); }
            catch (Exception ex) { LogService.Instance.Error($"取消扫码枪初始化 CTS 失败: {ex}"); }
            if (_scannerInitializeTask is not null)
            {
                try { _scannerInitializeTask.WaitAsync(TimeSpan.FromSeconds(OnExitScannerInitWaitSec)).GetAwaiter().GetResult(); }
                catch (Exception ex) { LogService.Instance.Error($"等待扫码枪初始化任务退出失败: {ex}"); }
                // H64: 即使超时也等待任务最终退出（较短二次等待），确保不再访问设备后再 Close
                if (!_scannerInitializeTask.IsCompleted)
                {
                    try { _scannerInitializeTask.WaitAsync(TimeSpan.FromSeconds(OnExitScannerInitFinalWaitSec)).GetAwaiter().GetResult(); }
                    catch (Exception ex) { LogService.Instance.Error($"等待扫码枪初始化任务最终退出失败: {ex}"); }
                }
            }
            try { _scannerInitializeCts?.Dispose(); }
            catch (Exception ex) { LogService.Instance.Error($"释放扫码枪初始化 CTS 失败: {ex}"); }

            try
            {
                // P0-FIX: 程序退出时彻底关闭扫码枪，避免后台重连循环阻止进程退出
                // 仅调用 Close()/Disconnect() 不会停止 AutoReconnect 后台任务，
                // 必须先禁用 AutoReconnect 再 Dispose 才能彻底释放设备与后台循环
                var scanner = Devices.Scanners.HikScaner;
                if (scanner is not null)
                {
                    scanner.AutoReconnect = false;
                    scanner.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"关闭扫码枪失败: {ex}");
            }

            // FIX(2026-08-13): 给海康 SDK 短暂清理窗口。CloseDevice/DestroyHandle 已同步调用，
            // 但 SDK 内部非托管线程完成网络会话拆除需要时间；若进程立刻退出会截断该流程，
            // 设备会被驱动标记为占用（MVS 等打不开），需等 SDK 会话超时才恢复。
            try { Thread.Sleep(500); }
            catch (Exception ex) { LogService.Instance.Error($"等待 SDK 清理失败: {ex}"); }

            Log.Information("应用程序退出");
            // H79e: 释放内存诊断资源，需在 Log.CloseAndFlush 之前调用（内部可能记录日志）
            try { MemoryDiagnostics.Shutdown(); }
            catch (Exception ex) { try { Log.Error(ex, "MemoryDiagnostics.Shutdown 失败"); } catch { } }
            // 清理 DI 容器（含其上注册的四个对外宿主与 VM）。
            // 2026-09-16: 用 ServicesOrNull —— Services 现已改为"未初始化即抛异常"，
            // 若启动早期失败（容器尚未建立）本行会抛出并逃出 OnExit 的清理流程。
            // 语义上"容器不存在"时就是无事可清理。
            if (ServicesOrNull is IDisposable disposableProvider)
            {
                try { disposableProvider.Dispose(); }
                catch (Exception ex) { LogService.Instance.Error($"释放 DI 容器失败: {ex}"); }
            }
            // L120: base.OnExit 移到 CloseAndFlush 之前调用，确保基类退出逻辑在日志系统仍可用时执行
            base.OnExit(e);
            // 2026-09-15: 原先在此处的 Settings.Save() 已上移到 OnExit 开头同步执行
            // （此处位于多个 await 之后，实际从未被执行到）。日志落盘另由 ProcessExit 兜底，
            // 因此这一段即使跑不到也不会丢失用户配置。
            // H78b/L380a: TemporaryLog.CloseAndFlush 与 Log.CloseAndFlush 相邻，
            // 确保 OnExit 清理过程中的日志已落盘后再关闭
            try { TemporaryLog.CloseAndFlush(); }
            catch (Exception ex) { try { Log.Error(ex, "TemporaryLog.CloseAndFlush 失败"); } catch { } }
            try { Log.CloseAndFlush(); }
            catch { /* 日志系统已关闭，失败无上报途径 */ }
#pragma warning restore VSTHRD002
        }

        private void RecipesManage_CurrentRecipeChanged(Recipe? recipe)
        {
            // L422a: 包 try-catch，避免事件处理器抛出异常影响事件发布方
            try
            {
                ApplyRecipeScannerSettings(recipe);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"RecipesManage_CurrentRecipeChanged 处理失败: {ex}");
            }
        }

        /// <summary>
        /// 2026-09-15: 解析统一数据根并把 exe 目录下的历史数据搬运过去。
        /// 任何异常都不阻断启动——最坏情况是"不迁移"，历史数据仍完整留在原位。
        /// </summary>
        private static string? RunDataRootMigration()
        {
            try
            {
                return DataRootMigrator.MigrateIfNeeded();
            }
            catch (Exception ex)
            {
                return $"数据根迁移检查失败（不影响启动）：{ex}";
            }
        }

        /// <summary>
        /// 2026-09-15: 退出时"必须落盘"的动作，必须在 <see cref="OnExit"/> 的任何 await 之前同步执行
        /// （原因见 OnExit 顶部注释：WPF 不 await OnExit，首个 await 之后的代码不会执行）。
        /// 本方法自身绝不抛异常，避免影响后续清理。
        /// </summary>
        private static void SaveCriticalStateBeforeShutdown()
        {
            // 2026-09-16: 先停掉定期落盘定时器（避免退出过程中与下面的 Save 并发写同一文件），
            // 再由本方法做最后一次显式落盘。
            try { Settings.Instance.StopPeriodicPersist(); }
            catch (Exception ex) { try { Log.Warning(ex, "[退出] 停止设置定期落盘失败"); } catch { } }

            try
            {
                // M342b: 仅在设置成功加载后才保存，避免启动失败时用默认值覆盖用户配置
                if (_settingsLoaded)
                {
                    Settings.Instance.Save();
                    Log.Information("[退出] 设置已保存");
                }
                else
                {
                    Log.Warning("[退出] 设置未成功加载，跳过保存以避免覆盖用户配置");
                }
            }
            catch (Exception ex)
            {
                try { Log.Error(ex, "[退出] 保存设置失败"); } catch { /* 日志不可用则放弃 */ }
            }
        }

        private static void InitializeLogging()
        {
            // 2026-09-15: 日志路径跟随统一数据根（DataPaths），不再写死在 exe 目录
            var databaseDirectory = DataPaths.DatabaseDir;
            Directory.CreateDirectory(databaseDirectory);
            var logDbPath = DataPaths.LogDatabase;
            var logDirectory = DataPaths.LogsDir;
            Directory.CreateDirectory(logDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                // retainedFileCountLimit:32（约 1 个月）：3 帧/s 连续运行时 txt 日志约 150-170MB/天，
                // 无上限会无限累积——SQLite 侧已有 retentionPeriod/maxDatabaseSize 双保留，txt 侧补齐。
                .WriteTo.File(Path.Combine(logDirectory, "log-.txt"), rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 32)
                .WriteTo.SQLite(sqliteDbPath: logDbPath,
                                    tableName: "Logs",
                                    storeTimestampInUtc: false,
                                    retentionPeriod: TimeSpan.FromDays(LogRetentionDays),
                                    batchSize: LogBatchSize,
                                    maxDatabaseSize: LogMaxDbSizeMb)
                .CreateLogger();

            // 2026-09-15 兜底：OnExit 里的 Log.CloseAndFlush() 位于多个 await 之后，
            // 而 WPF 不 await async void OnExit —— 那段代码可能来不及执行，导致最后一段日志丢失。
            // ProcessExit 在进程真正结束前触发，此处再刷一次；CloseAndFlush 可重复调用。
            AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
            {
                try { Log.CloseAndFlush(); }
                catch { /* 进程已进入退出流程，失败也不再有上报途径 */ }
            };
        }

        /// <summary>
        /// 配置 DI 容器并注册现有单例服务。
        /// 注册时直接传入 .Instance 单例实例，确保 DI 解析的对象与现有静态访问的是同一实例，
        /// 实现 DI 容器与现有 .Instance 调用共存，为后续渐进式迁移到构造函数注入打基础。
        /// <para>2026-09-16: 对外宿主（配方 TCP / 相机 Web / AI Web / 运维门户）纳入容器，
        /// 不再在字段初始化时手工 new —— 消除"同一批服务一半由容器管、一半手工管"的双轨。</para>
        /// </summary>
        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // 基础设施服务：直接注册现有单例实例
            services.AddSingleton<Settings>(Settings.Instance);
            services.AddSingleton<RecipesManage>(RecipesManage.Instance);
            services.AddSingleton<LogService>(LogService.Instance);
            services.AddSingleton<BarcodeDataService>(BarcodeDataService.Instance);
            services.AddSingleton<AuthService>(AuthService.Instance);
            services.AddSingleton<LogDatabaseService>(LogDatabaseService.Instance);

            // 检测运行期配置：领域层只认 IDetectionRuntimeConfig 这个端口，"去哪个单例取值"由组合根注入。
            // ★ 必须用委托而不是直接注册 Settings.Instance.Algorithm —— Settings.Reload() 会用反射把
            // 新实例的所有可写公共属性拷回（其中包含 Algorithm 这类子对象），也就是重载后
            // Settings.Instance.Algorithm 指向的是**另一个对象**；若在此缓存子对象引用，
            // 设置页每次重载都会让检测逻辑读到过期配置（静默错误）。委托每次调用都现取，永远是最新值。
            services.AddSingleton<IDetectionRuntimeConfig>(sp =>
            {
                var recipes = sp.GetRequiredService<RecipesManage>();
                return new DetectionRuntimeConfig(
                    algorithm: () => Settings.Instance.Algorithm,
                    currentRecipeName: () => recipes.CurrentRecipe?.Name,
                    stationCode: () => StationCodeProvider.Current);
            });

            // DetectionRecordService：依赖全部通过构造函数注入（原先内部直读全局单例）
            services.AddSingleton<DetectionRecordService>(sp => new DetectionRecordService(
                sp.GetRequiredService<IBarcodeDataService>(),
                sp.GetRequiredService<AngleTracker>(),
                sp.GetRequiredService<IDetectionRuntimeConfig>()));

            // IToVGTService：通过 Settings 注入网络参数（2026-09-13：ToVGT 静态桥接已移除，仅此一处入口）
            var toVgtService = new ToVGTService(Settings.Instance);
            services.AddSingleton<IToVGTService>(toVgtService);

            // ModelLoaderService：负责 YOLO 模型加载（三级回退策略）
            services.AddSingleton<ModelLoaderService>();

            // AngleTracker：跨帧角度锁定（单例，配方切换时 Clear）
            services.AddSingleton<AngleTracker>(AngleTracker.Instance);

            // 对外宿主：由容器创建并持有（容器负责 Dispose），App 在下方取回引用用于启停时序控制。
            // OpsWebHost 需要 IToVGTService（原先在请求处理里通过 App.Services 反查，现改为构造函数注入）。
            services.AddSingleton<RecipeTcpServerService>();
            services.AddSingleton<CameraWebHost>();
            services.AddSingleton<AiWebHost>();
            services.AddSingleton<OpsWebHost>();

            // 接口抽象注册：为后续 ViewModel 改造为构造函数注入打基础。
            // - LogService/AuthService/BarcodeDataService 均为已有 Instance 的单例类，
            //   此处复用同一单例实例注册为接口类型，保证 DI 解析的对象与现有 .Instance 调用是同一实例。
            // - NotificationService 为静态类无法实现接口，使用 NotificationServiceImpl 包装类委托给静态方法。
            services.AddSingleton<ILogService>(LogService.Instance);
            services.AddSingleton<IAuthService>(AuthService.Instance);
            services.AddSingleton<IBarcodeDataService>(BarcodeDataService.Instance);
            services.AddSingleton<INotificationService, NotificationServiceImpl>();
            services.AddSingleton<IDialogService, DialogService>();

            // 导航服务注册：替代 MainViewModel 中原 9 个 Action/Func 反向回调。
            // 注意：实际 MainViewModel.NavigationService 静态属性由 MainWindow 构造函数注入
            // （使用 () => this 工厂，确保 MainWindow 实例已创建）。
            // 此 DI 注册供将来 ViewModel 迁移到构造函数注入时使用，工厂通过 Application.Current.MainWindow
            // 获取当前主窗口实例（解析时 MainWindow 必已创建）。
            services.AddSingleton<INavigationService>(provider =>
                new NavigationService(() => System.Windows.Application.Current.MainWindow as MainWindow));

            // ViewModel：依赖全部由构造函数注入（原先用 App.Services 兜底解析，属 Service Locator）
            services.AddSingleton<HomeViewModel>();

            var provider = services.BuildServiceProvider();
            s_services = provider;

            // 取回对外宿主实例（启停时序由 App 控制，实例归容器所有）
            _recipeTcpServer = provider.GetRequiredService<RecipeTcpServerService>();
            _cameraWebHost = provider.GetRequiredService<CameraWebHost>();
            _aiWebHost = provider.GetRequiredService<AiWebHost>();
            _opsWebHost = provider.GetRequiredService<OpsWebHost>();

            // 为 ViewModel 设置静态 DialogService（向后兼容：DI 与现有 .Instance 调用共存）
            // VM 仍通过静态属性访问，但实例来自 DI 容器，便于未来替换实现或单元测试注入 mock
            var dialogService = provider.GetRequiredService<IDialogService>();
            RecipeManageViewModel.DialogService = dialogService;
            DatabaseViewModel.DialogService = dialogService;
            SettingsViewModel.DialogService = dialogService;
        }

        /// <summary>
        /// 配置 ImageSharp 使用托管内存分配器，避免非托管内存池无限膨胀导致 OOM。
        /// 必须在任何 Image.Load 调用之前执行。
        /// </summary>
        private static void ConfigureImageSharpMemory()
        {
            try
            {
                SixLabors.ImageSharp.Configuration.Default.MemoryAllocator = SimpleGcMemoryAllocator.Default;
            }
            catch (Exception ex)
            {
                // M117: 配置失败不应阻止应用启动，使用 Trace 输出便于诊断
                System.Diagnostics.Trace.WriteLine($"ImageSharp 内存配置失败: {ex}");
            }
        }

        private static void ReloadSettingsAndStartCleanup()
        {
            // M262: Settings.Reload() 在 UI 线程同步执行
            // 保持同步：MainWindow 构造时会立即使用 Settings（ApplyWindowSettings），异步 Reload 会导致使用旧设置
            Settings.Instance.Reload();
            // M342b: 标记设置已成功加载，OnExit 中据此判断是否允许 Save()
            _settingsLoaded = true;
            // 2026-09-16: 启动定期落盘（60s，内容有变化才写）。
            // 原先配置只在设置页/切配方/退出三处显式 Save()，任何"改了属性但没调 Save"的路径
            // 在崩溃或断电时会静默丢失；现在最长丢失窗口被收敛到 60 秒。
            Settings.Instance.StartPeriodicPersist();
            _ = Task.Run(() =>
            {
                // M48: fire-and-forget 任务必须有异常处理，否则变成 UnobservedTaskException
                try
                {
                    FileHelper.DeleteOldFiles(Settings.Instance.MinRecentDays, Settings.Instance.PicturesSaveFolder);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"清理旧文件失败: {ex}");
                }
            });
        }

        /// <summary>
        /// 启动常驻服务（ToVGT / 日报 / 扫码枪初始化 / 四个对外宿主）。
        /// 2026-09-16: 由同步方法改为 async——原先对 <c>toVgt.StartAsync()</c> 用
        /// <c>GetAwaiter().GetResult()</c> 同步等待，依据是"返回值是已完成任务、不阻塞"。
        /// 该前提一旦哪天不再成立（StartAsync 内部新增了真正的异步 I/O），
        /// 就会在 UI 线程上静默阻塞启动流程——而这类前提没人会记得复核。
        /// 现改为直接 await：成立时零开销，不成立时也不会阻塞 UI。
        /// 注意不追加 ConfigureAwait(false)：本方法体内后续动作（宿主启动、扫码枪任务编排）
        /// 与原实现一样在 UI 线程执行。
        /// </summary>
        private async Task StartApplicationServices()
        {
            // 通过 DI 获取 IToVGTService 启动（替代原 ToVGT.Start() 静态调用）
            var toVgt = Services.GetRequiredService<IToVGTService>();
            try
            {
                await toVgt.StartAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动 ToVGT 服务失败: {ex}");
            }
            // 2026-09-13: 启动班报/日报自动产出（跨天自动生成昨日日报 + 图表页可手动）
            try
            {
                ReportService.Instance.Start();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动日报服务失败: {ex}");
            }
            // L367a: 添加 60 秒超时，避免扫码枪初始化任务无限期挂起
            _scannerInitializeCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var token = _scannerInitializeCts.Token;
            _scannerInitializeTask = Task.Run(async () =>
            {
                try
                {
                    await Devices.Scanners.Initialize(token);
                    ApplyRecipeScannerSettings(RecipesManage.Instance.CurrentRecipe);
                }
                catch (OperationCanceledException) { LogService.Instance.Warning("扫码枪初始化超时"); }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"扫码枪初始化失败: {ex}");
                }
            });

            // RTC(2026-08-06): 启动配方切换 TCP 服务（上位机指令切换配方）
            try
            {
                _recipeTcpServer?.Start();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动配方切换 TCP 服务失败: {ex}");
            }

            // 2026-09-11: 启动相机调试 Web 服务（手机实时看图）。此处仅起监听，
            // 真正接管相机发生在首个客户端接入时，故可与其他服务并列启动。
            try
            {
                _cameraWebHost?.Start();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动相机调试 Web 服务失败: {ex}");
            }

            // 2026-09-12: 启动 AI 对话 Web 服务（浏览器级对话 UI）。此处仅起监听，
            // 真正加载模型发生在首个提问时（AiChatService 按需加载）。
            try
            {
                _aiWebHost?.Start();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动 AI 对话 Web 服务失败: {ex}");
            }

            // 2026-09-13: 启动统一运维门户（5191）
            try
            {
                _opsWebHost?.Start();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动运维门户 Web 服务失败: {ex}");
            }
        }

        private static async Task InitializeDatabaseAsync()
        {
            using var db = new AppDbContext();
            // 2026-09-16: 原先这里串着 EnsureCreated + EnsureIndexesAsync + EnsureBrightnessColumnsAsync
            // + EnsureTraceColumnsAsync 四个手写步骤，新增字段必须记得同步改它们。
            // 现统一为一次"模型 → 库"的增量对齐（补缺失列 + 补缺失索引），
            // 新增列/索引只需改 DbModel 与 AppDbContext.OnModelCreating，不需要再动本方法。
            var sync = await db.ApplySchemaSyncAsync().ConfigureAwait(false);
            if (sync.HasChanges)
            {
                // 结构变更必须可见：现场排查"为什么老机器行为不同"时，这两行是直接线索
                LogService.Instance.Warning(
                    $"[DB] 库结构已同步：新增列 [{string.Join(", ", sync.AddedColumns)}]、" +
                    $"新增索引 [{string.Join(", ", sync.AddedIndexes)}]");
            }

            if (sync.SkippedColumns.Count > 0)
            {
                // 不静默跳过：这些列在 SQLite 上无法安全自动补，必须人工给显式迁移步骤
                LogService.Instance.Error(
                    $"[DB] 以下模型列无法自动补（非空且无默认值，或类型无法映射）：{string.Join(", ", sync.SkippedColumns)}；" +
                    "请为其提供显式迁移步骤，否则该列写入会报 no such column。");
            }

            await MigrateLegacyAngleDomainAsync(db).ConfigureAwait(false);
            LogService.Instance.Info("应用程序启动");
        }

        /// <summary>
        /// 2026-09-05 角度规范域迁移：[0,360) → (-180,180]（与机器人 RZ 发送域统一）。
        /// 幂等：仅把 Angle &gt; 180 的历史记录减 360；已在新域（≤180）或未知哨兵(-9999)的记录不受影响，
        /// 重复执行结果不变。旧库首次升级时把历史 328° 等转换为 -32°，保证查询/图表/导出/统计口径一致。
        /// </summary>
        private static async Task MigrateLegacyAngleDomainAsync(AppDbContext db)
        {
            try
            {
                var legacyCount = await db.BarcodeData.CountAsync(m => m.Angle > 180).ConfigureAwait(false);
                if (legacyCount > 0)
                {
                    await db.Database.ExecuteSqlRawAsync("UPDATE BarcodeData SET Angle = Angle - 360 WHERE Angle > 180").ConfigureAwait(false);
                    LogService.Instance.Info($"角度域迁移完成：[0,360)→(-180,180]，共换算历史记录 {legacyCount} 条");
                }
            }
            catch (Exception ex)
            {
                // 迁移失败不阻断启动：新写入记录仍为新域，历史旧域记录下次启动会重试迁移
                LogService.Instance.Error($"角度域历史数据迁移失败（不影响本次启动，将在下次启动重试）: {ex.Message}");
            }
        }

        private void InitializeRecipes()
        {
            // 2026-09-15: 跟随统一数据根，不再写死在 exe 目录
            var recipesPath = DataPaths.RecipesDir;
            // M267: 先订阅事件再 Initialize，避免 Initialize 设置默认配方时事件丢失，无需手动补偿调用
            RecipesManage.Instance.CurrentRecipeChanged += RecipesManage_CurrentRecipeChanged;
            RecipesManage.Instance.Initialize(recipesPath);
            LogService.Instance.Info($"配方管理器初始化完成，路径: {recipesPath}");
        }

        private void StartMemoryDiagnostics()
        {
            MemoryDiagnostics.LogSnapshot("Startup");
            // 每 MemorySnapshotIntervalSec 秒记录一次内存快照
            _memorySnapshotTask = MemoryDiagnostics.RunPeriodicSnapshotAsync(MemorySnapshotIntervalSec, _memorySnapshotCts.Token);
        }

        /// <summary>
        /// 启动数据/日志保留期清理后台任务。
        /// 启动时立即执行一次清理，然后按 Settings.DataCleanupIntervalHours 间隔周期执行。
        /// 设计参考 StartMemoryDiagnostics。
        /// </summary>
        private void StartDataCleanup()
        {
            var intervalHours = Settings.Instance.DataCleanupIntervalHours;
            _dataCleanupTask = DataRetentionService.RunPeriodicCleanupAsync(intervalHours, _dataCleanupCts.Token);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                // H80b: 用 try-catch 包裹 Log.Error，确保 SetObserved 一定执行
                try { LogService.Instance.Error($"UnobservedTaskException: {e.Exception}"); }
                catch { /* logging failed, swallow to ensure SetObserved */ }
                e.SetObserved();
            };
            DispatcherUnhandledException += (sender, args) =>
            {
                // M335a: OperationCanceledException 通常是取消操作（如 CTS.Cancel）触发，非错误，
                // 不需弹窗打扰用户，直接标记已处理
                if (args.Exception is OperationCanceledException)
                {
                    args.Handled = true;
                    return;
                }
                // M312b: 关键日志和 UI 调用均包 try-catch，确保至少能设置 args.Handled = true
                try
                {
                    // 记录完整异常信息（含堆栈），便于诊断
                    LogService.Instance.Error($"未处理的UI线程异常: {args.Exception}");
                }
                catch { /* 日志记录失败，忽略 */ }
                // 对可能导致数据损坏的严重异常，不吞掉，让应用崩溃以保护数据
                var ex = args.Exception;
                if (ex is StackOverflowException ||
                    ex is OutOfMemoryException ||
                    ex is AccessViolationException ||
                    ex is System.Runtime.InteropServices.SEHException)
                {
                    args.Handled = false;
                    return;
                }
                // M129: 非致命异常需向用户提示，避免静默吞掉
                // L432b: 仅显示异常 Message，完整堆栈已通过 LogService 记录，避免向用户暴露技术细节
                try { NotificationService.Error($"发生未处理的异常: {args.Exception.Message}"); }
                catch { /* UI 不可用，跳过提示 */ }
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                // H80c: LogService.Instance.Error 未保护，用 try-catch 包裹
                try { LogService.Instance.Error($"UnhandledException: {e.ExceptionObject}"); }
                catch { /* ignore */ }
                // M236: 确保日志刷新到磁盘
                try { Log.CloseAndFlush(); } catch { }
                // REVIEW(2026-08-05): 后台线程（线程池/Task.Run）异常会走此通道且进程必然终止，
                // 弹窗告知用户原因，避免"无声闪退"（UI 线程异常走 DispatcherUnhandledException 可 Handle）。
                try
                {
                    var msg = e.ExceptionObject is Exception ex ? ex.Message : e.ExceptionObject?.ToString() ?? "未知异常";
                    System.Windows.MessageBox.Show(
                        $"程序发生未处理的异常（后台线程），即将退出。\n\n异常信息：{msg}\n\n详细堆栈已写入日志。",
                        "程序异常",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
                catch { /* 弹窗失败（如无 UI 会话），忽略 */ }
            };
        }

        private static void ApplyRecipeScannerSettings(Recipe? recipe)
        {
            if (recipe?.ImageTool is null)
            {
                return;
            }

            try
            {
                var scanner = Devices.Scanners.HikScaner;
                if (scanner is null || !scanner.IsOpen)
                {
                    return;
                }

                scanner.SetExposureTime(recipe.ImageTool.ExposureTime);
                scanner.SetGain(recipe.ImageTool.Gain);
                LogService.Instance.Info($"已应用当前配方[{recipe.Name}]的扫码枪参数，曝光: {recipe.ImageTool.ExposureTime}，增益: {recipe.ImageTool.Gain}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"应用当前配方[{recipe.Name}]的扫码枪参数失败: {ex}");
            }
        }
    }
}