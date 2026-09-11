using Extensions;
using MainAPP.Application;
using MainAPP.Models;
using MainAPP.Services;
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
        private readonly RecipeTcpServerService _recipeTcpServer = new();
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

        // DI 容器：作为新基础设施与现有 .Instance 单例共存，为后续渐进式迁移到构造函数注入打基础
        public static IServiceProvider Services { get; private set; } = default!;

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
                ConfigureImageSharpMemory();
                InitializeLogging();
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
                StartApplicationServices();
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
        /// REVIEW(2026-09-05): 激活码门禁临时停用开关。
        /// 现场出现「激活成功后过一段时间又要求重新激活」（机器指纹成分漂移导致 MachineMismatch，
        /// 或换 Windows 账户运行导致授权文件与指纹双重失配，详见 HardwareFingerprint 与 LicenseService）。
        /// 在指纹策略与授权文件位置整改完成前，置 false 直接放行：
        /// 不校验激活码、不弹激活窗口、不要求 license.txt。
        /// 恢复授权门禁：将本字段改回 true 即可，其余代码未做任何删除。
        /// 注：用 static readonly 而非 const——const=false 时 if(!ActivationRequired) 恒真，
        /// 编译器把下方授权分支判为不可达代码（CS0162）。
        /// </summary>
        private static readonly bool ActivationRequired = false;

        /// <summary>
        /// LIC(2026-08-06): 离线授权检查。已激活且有效直接放行；否则弹出激活窗口，
        /// 用户激活成功返回 true，取消/退出返回 false（调用方 Shutdown）。
        /// 注意：<see cref="ActivationRequired"/> 为 false 时恒放行。
        /// </summary>
        private bool EnsureActivated()
        {
            if (!ActivationRequired)
            {
                LogService.Instance.Warning("[LIC] 激活码门禁已停用（ActivationRequired=false），跳过授权检查");
                return true;
            }

            try
            {
                if (LicenseService.ValidateAtStartup())
                    return true;

                var activationWindow = new ActivationWindow();
                return activationWindow.ShowDialog() == true;
            }
            catch (Exception ex)
            {
                try { LogService.Instance.Error($"授权检查失败: {ex}"); } catch { }
                return false;
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            // M322a: 分段包 try-catch，避免单个清理操作抛出跳过后续清理
            try { _memorySnapshotCts.Cancel(); }
            catch (Exception ex) { LogService.Instance.Error($"取消内存快照任务失败: {ex}"); }
            try { await (_memorySnapshotTask?.WaitAsync(TimeSpan.FromSeconds(OnExitMemorySnapshotWaitSec)) ?? Task.CompletedTask); }
            catch (Exception ex) { LogService.Instance.Error($"等待内存快照任务退出失败: {ex}"); }
            // H63: 等待任务完全退出后再 Dispose CTS，避免任务访问已释放的 token 造成 use-after-dispose
            if (_memorySnapshotTask is not null && !_memorySnapshotTask.IsCompleted)
            {
                try { await _memorySnapshotTask.WaitAsync(TimeSpan.FromSeconds(OnExitMemorySnapshotFinalWaitSec)); }
                catch (Exception ex) { LogService.Instance.Error($"等待内存快照任务最终退出失败: {ex}"); }
            }
            try { _memorySnapshotCts.Dispose(); }
            catch (Exception ex) { LogService.Instance.Error($"释放内存快照 CTS 失败: {ex}"); }
            // 数据/日志清理任务取消与等待
            try { _dataCleanupCts.Cancel(); }
            catch (Exception ex) { LogService.Instance.Error($"取消数据清理任务失败: {ex}"); }
            try { await (_dataCleanupTask?.WaitAsync(TimeSpan.FromSeconds(OnExitDataCleanupWaitSec)) ?? Task.CompletedTask); }
            catch (Exception ex) { LogService.Instance.Error($"等待数据清理任务退出失败: {ex}"); }
            if (_dataCleanupTask is not null && !_dataCleanupTask.IsCompleted)
            {
                try { await _dataCleanupTask.WaitAsync(TimeSpan.FromSeconds(OnExitDataCleanupFinalWaitSec)); }
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
                        await saveTask.WaitAsync(TimeSpan.FromSeconds(OnExitSaveTimeoutSec));
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Error($"保存待处理条码失败: {ex}");
                    }
                    // H63: 确保任务已退出后再 Dispose，避免 use-after-dispose
                    if (!saveTask.IsCompleted)
                    {
                        try { await saveTask.WaitAsync(TimeSpan.FromSeconds(OnExitSaveFinalWaitSec)); }
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
                await toVgt.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(OnExitToVGTStopSec));
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"停止 ToVGT 失败: {ex}");
            }

            // RTC(2026-08-06): 停止配方切换 TCP 服务
            try { _recipeTcpServer.Stop(); }
            catch (Exception ex) { LogService.Instance.Error($"停止配方切换 TCP 服务失败: {ex}"); }

            // H64: 超时后 Cancel 并等待任务退出（带较短二次等待），再调用 Close()
            try { _scannerInitializeCts?.Cancel(); }
            catch (Exception ex) { LogService.Instance.Error($"取消扫码枪初始化 CTS 失败: {ex}"); }
            if (_scannerInitializeTask is not null)
            {
                try { await _scannerInitializeTask.WaitAsync(TimeSpan.FromSeconds(OnExitScannerInitWaitSec)); }
                catch (Exception ex) { LogService.Instance.Error($"等待扫码枪初始化任务退出失败: {ex}"); }
                // H64: 即使超时也等待任务最终退出（较短二次等待），确保不再访问设备后再 Close
                if (!_scannerInitializeTask.IsCompleted)
                {
                    try { await _scannerInitializeTask.WaitAsync(TimeSpan.FromSeconds(OnExitScannerInitFinalWaitSec)); }
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
            try { await Task.Delay(500); }
            catch (Exception ex) { LogService.Instance.Error($"等待 SDK 清理失败: {ex}"); }

            Log.Information("应用程序退出");
            // H79e: 释放内存诊断资源，需在 Log.CloseAndFlush 之前调用（内部可能记录日志）
            try { MemoryDiagnostics.Shutdown(); }
            catch (Exception ex) { try { Log.Error(ex, "MemoryDiagnostics.Shutdown 失败"); } catch { } }
            // 清理 DI 容器
            if (Services is IDisposable disposableProvider)
            {
                try { disposableProvider.Dispose(); }
                catch (Exception ex) { LogService.Instance.Error($"释放 DI 容器失败: {ex}"); }
            }
            // L120: base.OnExit 移到 CloseAndFlush 之前调用，确保基类退出逻辑在日志系统仍可用时执行
            base.OnExit(e);
            // M18: Settings.Save() 可能抛 IOException（磁盘满/文件锁定），需保护以确保 Log.CloseAndFlush 执行
            // M342b: 仅在设置成功加载后才保存，避免启动失败时用默认值覆盖用户配置
            try
            {
                if (_settingsLoaded)
                {
                    Settings.Instance.Save();
                }
                else
                {
                    Log.Warning("设置未成功加载，跳过 OnExit 保存以避免覆盖用户配置");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "退出时保存设置失败");
            }
            finally
            {
                // H78b/L380a: TemporaryLog.CloseAndFlush 移至此处并包 try-catch，
                // 与 Log.CloseAndFlush 相邻，确保 OnExit 清理过程中的日志已落盘后再关闭
                try { TemporaryLog.CloseAndFlush(); }
                catch (Exception ex) { try { Log.Error(ex, "TemporaryLog.CloseAndFlush 失败"); } catch { } }
                Log.CloseAndFlush();
            }
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

        private static void InitializeLogging()
        {
            var databaseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves", "DataBase");
            Directory.CreateDirectory(databaseDirectory);
            var logDbPath = Path.Combine(databaseDirectory, "Logs.db");
            var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .WriteTo.File(Path.Combine(logDirectory, "log-.txt"), rollingInterval: RollingInterval.Day)
                .WriteTo.SQLite(sqliteDbPath: logDbPath,
                                    tableName: "Logs",
                                    storeTimestampInUtc: false,
                                    retentionPeriod: TimeSpan.FromDays(LogRetentionDays),
                                    batchSize: LogBatchSize,
                                    maxDatabaseSize: LogMaxDbSizeMb)
                .CreateLogger();
        }

        /// <summary>
        /// 配置 DI 容器并注册现有单例服务。
        /// 注册时直接传入 .Instance 单例实例，确保 DI 解析的对象与现有静态访问的是同一实例，
        /// 实现 DI 容器与现有 .Instance 调用共存，为后续渐进式迁移到构造函数注入打基础。
        /// </summary>
        private static void ConfigureServices()
        {
            var services = new ServiceCollection();

            // 基础设施服务：直接注册现有单例实例
            services.AddSingleton<Settings>(Settings.Instance);
            services.AddSingleton<RecipesManage>(RecipesManage.Instance);
            services.AddSingleton<LogService>(LogService.Instance);
            services.AddSingleton<BarcodeDataService>(BarcodeDataService.Instance);
            // DetectionRecordService：通过构造函数注入 IBarcodeDataService 和 AngleTracker
            services.AddSingleton<DetectionRecordService>(sp => new DetectionRecordService(
                sp.GetRequiredService<IBarcodeDataService>(),
                sp.GetRequiredService<AngleTracker>()));
            services.AddSingleton<AuthService>(AuthService.Instance);
            services.AddSingleton<LogDatabaseService>(LogDatabaseService.Instance);

            // IToVGTService：通过 INetworkSettings 接口注入网络参数，替代原 ToVGT 静态类
            var toVgtService = new ToVGTService(Settings.Instance);
            services.AddSingleton<IToVGTService>(toVgtService);
            // 桥接：让旧代码中的 ToVGT.Xxx 静态调用继续工作
            ToVGT.SetService(toVgtService);

            // ModelLoaderService：负责 YOLO 模型加载（三级回退策略）
            services.AddSingleton<ModelLoaderService>();

            // AngleTracker：跨帧角度锁定（单例，配方切换时 Clear）
            services.AddSingleton<AngleTracker>(AngleTracker.Instance);

            // 接口抽象注册：为后续 ViewModel 改造为构造函数注入打基础。
            // - LogService/AuthService/BarcodeDataService 均为已有 Instance 的单例类，
            //   此处复用同一单例实例注册为接口类型，保证 DI 解析的对象与现有 .Instance 调用是同一实例。
            // - NotificationService 为静态类无法实现接口，使用 NotificationServiceImpl 包装类委托给静态方法。
            // 现有 ViewModel 中的 XxxService.Instance 静态调用方式保持不变（向后兼容）。
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

            // ViewModel：当前保持无参构造函数，将来迁移到构造函数注入时由 DI 解析依赖
            services.AddSingleton<HomeViewModel>();

            Services = services.BuildServiceProvider();

            // 为 ViewModel 设置静态 DialogService（向后兼容：DI 与现有 .Instance 调用共存）
            // VM 仍通过静态属性访问，但实例来自 DI 容器，便于未来替换实现或单元测试注入 mock
            var dialogService = Services.GetRequiredService<IDialogService>();
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

        private void StartApplicationServices()
        {
            // 通过 DI 获取 IToVGTService 启动（替代原 ToVGT.Start() 静态调用）
            var toVgt = Services.GetRequiredService<IToVGTService>();
            try
            {
                // StartAsync 内部同步创建 UDP 客户端并启动后台任务（仅 Task.Run 部分是异步），
                // 返回值是已完成任务，GetResult() 不阻塞
#pragma warning disable VSTHRD002
                toVgt.StartAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动 ToVGT 服务失败: {ex}");
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
                _recipeTcpServer.Start();
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"启动配方切换 TCP 服务失败: {ex}");
            }
        }

        private static async Task InitializeDatabaseAsync()
        {
            using var db = new AppDbContext();
            await db.Database.EnsureCreatedAsync().ConfigureAwait(false);
            await db.EnsureIndexesAsync().ConfigureAwait(false);
            // 2026-09-08: 灰度判向统计列升级（既有库补列，幂等；EnsureCreated 不会给已存在库加列）
            await db.EnsureBrightnessColumnsAsync().ConfigureAwait(false);
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
            var recipesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves", "Recipes");
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