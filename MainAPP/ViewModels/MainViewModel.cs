using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MainAPP.Messages;
using MainAPP.Models;
using MainAPP.Services;
using MainAPP;
using Microsoft.Extensions.DependencyInjection;

// VSTHRD001: 使用 Dispatcher.BeginInvoke/InvokeAsync 切换到 UI 线程是 WPF 标准模式，无需 JoinableTaskFactory
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：承载数据绑定属性、条码去重计数、FPS/内存/推理耗时状态更新、
    /// 登录会话管理、DispatcherTimer 轮询及 WeakReferenceMessenger 消息接收。
    /// 从 MainWindow.xaml.cs 提取，View 仅保留 UI 事件转发与窗口生命周期处理。
    /// </summary>
    public class MainViewModel : ObservableObject, IDisposable
    {
        // === 常量 ===
        // L373a: 限制去重集合上限。
        private const int MaxBarcodes = 100_000;
        // L367a: 条码集合大小日志记录间隔，提取为常量避免魔法数字
        private const int BarcodeLogInterval = 10_000;
        // L409b: 自动最小化时间间隔（分钟），提取为常量避免魔法数字
        private const int AutoMinimizeIntervalMinutes = 10;
        // M342c: 用户活动事件节流阈值（秒），仅当距上次 Stop+Start 超过此阈值时才重置定时器
        private const int ActivityResetThrottleSec = 5;
        // 编码器状态轮询间隔（秒）
        private const int EncoderStatusPollIntervalSec = 2;
        // 判定编码器仍在接收的最近接收时间阈值（秒），超过此时间未收到视为未接收
        private const int EncoderReceivingThresholdSec = 10;
        // P1-12: 系统状态指标轮询间隔（秒）
        private const int SystemStatusPollIntervalSec = 2;

        // === 条码去重字段 ===
        // _count 写操作在锁内执行，锁提供内存屏障；Count 读取为 32 位 int 原子读取，无需 volatile
        private int _count;
        private int _aiCount;
        private readonly ConcurrentQueue<string> _barcodeQueue = new();
        private readonly HashSet<string> _barcodeSet = new(StringComparer.Ordinal);

        // === 登录状态字段 ===
        private AppUser? _currentUser;
        private readonly DispatcherTimer _loginTimeoutTimer = new();
        private readonly DispatcherTimer _autoMinimizeTimer = new();
        // L368b: 使用单调时钟 Stopwatch 替代 DateTime.Now 计算登录超时，避免系统时间回调导致误判
        private readonly Stopwatch _loginStopwatch = new();
        // M342c: 上次执行用户活动定时器重置的时刻（UTC），用于节流判断
        private DateTime _lastActivityResetTime = DateTime.MinValue;

        // === 编码器状态字段 ===
        // 编码器状态轮询定时器：周期性查询 ToVGT.LastEncoderReceiveTime 判断是否在接收数据
        private readonly DispatcherTimer _encoderStatusTimer = new();
        private string _encoderStatusText = "未接收";

        // === 系统状态字段 ===
        // P1-12: 系统状态指标（FPS/内存/当前配方/推理耗时/丢帧数）字段与定时器
        private readonly DispatcherTimer _systemStatusTimer = new();
        private string _fpsText = "0.0";
        private string _memoryText = "-";
        private string _currentRecipeText = "-";
        // FPS 计算：基于 HomeViewModel.ProcessedFrameCount 的增量
        private long _lastProcessedFrameCount;
        private DateTime _lastFpsUpdateTime = DateTime.UtcNow;
        // 推理耗时（最近一帧，来自 TimingLogger 暴露的静态字段）
        private string _inferenceTimeText = "-";
        // L: 累计丢帧数文本（来自 HomeViewModel.DropFrameCount）
        private string _dropFrameText = "丢帧: 0";

        // === Dispatcher（捕获 UI 线程调度器） ===
        private readonly Dispatcher _dispatcher;

        // === 导航服务（由 App 启动时注入，替代原 Action/Func 反向回调） ===
        /// <summary>
        /// 导航服务静态属性，由 App.xaml.cs 在启动时注入。
        /// 使用静态属性便于 MainViewModel 无参构造函数访问，避免破坏现有 DI 兼容性。
        /// </summary>
        public static INavigationService? NavigationService { get; set; }

        // VGT 通信服务（构造函数注入；若为 null 则回退到 DI 容器解析）
        private readonly IToVGTService _toVgtService;

        // === 帧计数指标（由 HomeViewModel 通过 messenger 推送，替代原 Func 反向回调） ===
        private long _processedFrameCount;
        private int _dropFrameCount;

        // === 快捷键命令（通过 INavigationService 转发执行实际导航） ===
        /// <summary>F1: 跳转到关于页</summary>
        public RelayCommand NavigateToAboutCommand { get; }
        /// <summary>Ctrl+S: 保存当前页面设置</summary>
        public RelayCommand SaveCurrentPageCommand { get; }
        /// <summary>Ctrl+Tab: 切换到下一个页面</summary>
        public RelayCommand NavigateToNextPageCommand { get; }
        /// <summary>Ctrl+Shift+Tab: 切换到上一个页面</summary>
        public RelayCommand NavigateToPreviousPageCommand { get; }
        /// <summary>Ctrl+1~5: 按页面名称跳转</summary>
        public RelayCommand<string> NavigateToPageCommand { get; }
        /// <summary>清零读码数量、识别数量和去重跟踪列表（换班/换产品场景）</summary>
        public RelayCommand ResetCountCommand { get; }

        // === 绑定属性 ===
        public int Count
        {
            get => _count;
            private set => SetProperty(ref _count, value);
        }

        // H17: 独立的 AI 识别数量计数，与读码数量分离
        public int AiCount
        {
            get => _aiCount;
            private set => SetProperty(ref _aiCount, value);
        }

        public bool IsLoggedIn => _currentUser is not null;

        public AppUser? CurrentUser
        {
            get => _currentUser;
            private set
            {
                if (_currentUser == value)
                {
                    return;
                }

                if (_currentUser is not null)
                {
                    _currentUser.PropertyChanged -= CurrentUser_PropertyChanged;
                }

                _currentUser = value;
                if (_currentUser is not null)
                {
                    _currentUser.PropertyChanged += CurrentUser_PropertyChanged;
                }

                OnPropertyChanged(nameof(CurrentUser));
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(LoginStatusText));
                OnPropertyChanged(nameof(LoginButtonText));
                OnPropertyChanged(nameof(CanAccessRecipes));
                UpdateRoleBadge();
                UpdateSessionRemainDisplay();
                OnPropertyChanged(nameof(CanAccessDatabase));
                OnPropertyChanged(nameof(CanAccessSettings));
                OnPropertyChanged(nameof(CanAccessLogs));
                OnPropertyChanged(nameof(CanResetCount));

                if (_currentUser is null)
                {
                    // L368b: 登出时 Reset Stopwatch，停止超时计时
                    _loginStopwatch.Reset();
                    _loginTimeoutTimer.Stop();
                    _ = _dispatcher.BeginInvoke(() => NavigationService?.EnsureVisibleSelected());
                }
                else
                {
                    // M156: 登录后无论是否配置超时，都需确保可见标签页被选中
                    if (Settings.Instance.ExistLoginTimeout > 0 && !_isStartupAutoLogin)
                    {
                        // H90b: 登录后基于当前 ExistLoginTimeout 计算定时器间隔
                        _loginTimeoutTimer.Interval = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(30).Ticks, TimeSpan.FromMilliseconds(Settings.Instance.ExistLoginTimeout / 2.0).Ticks));
                        // L368b: 登录时 Restart Stopwatch，开始单调计时
                        _loginStopwatch.Restart();
                        _loginTimeoutTimer.Start();
                    }
                    _ = _dispatcher.BeginInvoke(() => NavigationService?.EnsureVisibleSelected());
                }
            }
        }

        // 精简（2026-09-12）：登录态由图标颜色表达（橙=已登录角色/灰=未登录），
        // "已登录："前缀冗余——文本只留身份，为主页标题栏省约 60px
        public string LoginStatusText => _currentUser is null ? "未登录" : _currentUser.DisplayName;

        /// <summary>
        /// 编码器接收状态文本，供顶部状态栏展示。
        /// 基于 ToVGT.LastEncoderReceiveTime 与当前时间差判断：阈值内为"接收中"，否则为"未接收"。
        /// </summary>
        public string EncoderStatusText
        {
            get => _encoderStatusText;
            private set => SetProperty(ref _encoderStatusText, value);
        }

        /// <summary>
        /// 编码器是否正在接收报文，用于驱动状态栏指示灯颜色。
        /// </summary>
        public bool IsEncoderReceiving
        {
            get => _isEncoderReceiving;
            private set => SetProperty(ref _isEncoderReceiving, value);
        }
        private bool _isEncoderReceiving;

        // P1-12: 系统状态指标（FPS/内存/当前配方/推理耗时）
        /// <summary>每秒处理帧数文本</summary>
        public string FpsText
        {
            get => _fpsText;
            private set => SetProperty(ref _fpsText, value);
        }

        /// <summary>进程工作集内存文本</summary>
        public string MemoryText
        {
            get => _memoryText;
            private set => SetProperty(ref _memoryText, value);
        }

        /// <summary>当前配方名文本</summary>
        public string CurrentRecipeText
        {
            get => _currentRecipeText;
            private set => SetProperty(ref _currentRecipeText, value);
        }

        /// <summary>最近一帧推理耗时文本</summary>
        public string InferenceTimeText
        {
            get => _inferenceTimeText;
            private set => SetProperty(ref _inferenceTimeText, value);
        }

        /// <summary>最近一次接收到的编码器原始值（counts），尚未收到时为 "-" </summary>
        public string EncoderValueText
        {
            get => _encoderValueText;
            private set => SetProperty(ref _encoderValueText, value);
        }
        private string _encoderValueText = "-";

        /// <summary>累计丢帧数文本（用于状态栏显示）</summary>
        public string DropFrameText
        {
            get => _dropFrameText;
            private set => SetProperty(ref _dropFrameText, value);
        }

        public string LoginButtonText => _currentUser is null ? "登录" : "注销";

        /// <summary>角色徽章文本（管理员/操作员/访客），未登录为空（徽章背景透明即不可见）。</summary>
        public string RoleBadgeText
        {
            get => _roleBadgeText;
            private set => SetProperty(ref _roleBadgeText, value);
        }
        private string _roleBadgeText = string.Empty;

        /// <summary>角色徽章颜色：管理员橙（可切配方/改参数）、操作员青、访客灰。</summary>
        public System.Windows.Media.Brush RoleBadgeBrush
        {
            get => _roleBadgeBrush;
            private set => SetProperty(ref _roleBadgeBrush, value);
        }
        private System.Windows.Media.Brush _roleBadgeBrush = System.Windows.Media.Brushes.Transparent;

        /// <summary>
        /// 会话剩余时间文本。仅手动登录且 ExistLoginTimeout &gt; 0 时显示——
        /// 产线自动登录会话不限时（_isStartupAutoLogin 不启动超时定时器），显示"剩余"反而误导。
        /// </summary>
        public string SessionRemainDisplay
        {
            get => _sessionRemainDisplay;
            private set => SetProperty(ref _sessionRemainDisplay, value);
        }
        private string _sessionRemainDisplay = string.Empty;

        /// <summary>
        /// 会话剩余时间的显示颜色：充足时半透明灰（融入标题栏），
        /// 最后 5 分钟转橙提醒——避免操作员在配料/调参中途被超时登出打断。
        /// </summary>
        public System.Windows.Media.Brush SessionRemainBrush
        {
            get => _sessionRemainBrush;
            private set => SetProperty(ref _sessionRemainBrush, value);
        }
        private System.Windows.Media.Brush _sessionRemainBrush = System.Windows.Media.Brushes.Transparent;

        /// <summary>会话剩余时间的更新节流由 _loginTimeoutTimer.Tick 驱动（间隔 min(30s, 超时/2)）。</summary>
        private void UpdateSessionRemainDisplay()
        {
            if (_currentUser is null || !_loginStopwatch.IsRunning
                || Settings.Instance.ExistLoginTimeout <= 0 || _isStartupAutoLogin)
            {
                SessionRemainDisplay = string.Empty;
                SessionRemainBrush = System.Windows.Media.Brushes.Transparent;
                return;
            }

            var remain = TimeSpan.FromMilliseconds(Settings.Instance.ExistLoginTimeout) - _loginStopwatch.Elapsed;
            if (remain < TimeSpan.Zero) remain = TimeSpan.Zero;
            SessionRemainDisplay = remain.TotalMinutes >= 1
                ? $"会话剩余 {remain.Minutes} 分 {remain.Seconds:00} 秒"
                : $"会话剩余 {remain.Seconds} 秒";
            // 最后 5 分钟转橙提醒（贴合并入会话的黄色告警系）；
            // 充足时半透明灰——融入标题栏底色，不与角色徽章争抢视觉
            SessionRemainBrush = remain.TotalMinutes < 5
                ? MakeFrozenBrush(0xFF, 0xFF, 0xB3, 0x47)
                : MakeFrozenBrush(0x88, 0xB4, 0xB2, 0xA9);
        }

        /// <summary>角色徽章：按 Role 映射文本与颜色（冻结画刷，线程安全）。</summary>
        private void UpdateRoleBadge()
        {
            if (_currentUser is null)
            {
                // 未登录：文字徽章清空（图标改灰色仍可见——当前无登录身份）
                RoleBadgeText = string.Empty;
                RoleBadgeBrush = MakeFrozenBrush(0xFF, 0xB4, 0xB2, 0xA9);
                return;
            }

            (RoleBadgeText, RoleBadgeBrush) = _currentUser.Role switch
            {
                UserRole.Admin => ("管理员", MakeFrozenBrush(0xFF, 0xFF, 0xB3, 0x47)),
                UserRole.Operator => ("操作员", MakeFrozenBrush(0xFF, 0x5D, 0xCA, 0xA5)),
                _ => ("访客", MakeFrozenBrush(0xFF, 0xB4, 0xB2, 0xA9)),
            };
        }

        private static System.Windows.Media.SolidColorBrush MakeFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        public bool CanAccessHome => true;

        // 全员同权(2026-09-05)：权限不再按角色区分，任何已登录用户均可访问
        // 配方/设置并清零计数；角色仅作展示与管理语义保留。启动即自动登录默认管理员。
        public bool CanAccessRecipes => IsLoggedIn;

        public bool CanAccessDatabase => _currentUser is not null;

        public bool CanAccessSettings => IsLoggedIn;

        public bool CanAccessLogs => _currentUser is not null;

        /// <summary>
        /// 是否允许清零计数（登录后即可用；角色门禁已移除）
        /// </summary>
        public bool CanResetCount => IsLoggedIn;

        // 启动自动登录标记：为 true 时建立会话不启动"登录超时强制登出"定时器，
        // 避免默认 5 分钟(ExistLoginTimeout=300000)把产线自动登录踢下线。手动登录仍按原超时策略。
        private bool _isStartupAutoLogin;

        // === 构造函数 ===
        public MainViewModel(IToVGTService? toVgtService = null)
        {
            _toVgtService = toVgtService ?? App.Services.GetRequiredService<IToVGTService>();

            // 捕获 UI 线程 Dispatcher（ViewModel 在 UI 线程上构造）
            _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            // 初始化快捷键命令（通过 INavigationService 转发到 View 执行实际导航）
            NavigateToAboutCommand = new RelayCommand(() => NavigationService?.NavigateTo("About"));
            SaveCurrentPageCommand = new RelayCommand(() => NavigationService?.SaveCurrentPage());
            NavigateToNextPageCommand = new RelayCommand(() => NavigationService?.NavigateToNext());
            NavigateToPreviousPageCommand = new RelayCommand(() => NavigationService?.NavigateToPrevious());
            NavigateToPageCommand = new RelayCommand<string>(p => { if (!string.IsNullOrEmpty(p)) NavigationService?.NavigateTo(p); });
            ResetCountCommand = new RelayCommand(ResetCount);

            // L98/H90b: 登录超时定时器间隔取 30 秒与 ExistLoginTimeout/2 的较小值
            // 当 ExistLoginTimeout 较短时（如 5 秒），原 30 秒粒度会导致超时检测严重滞后
            _loginTimeoutTimer.Interval = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(30).Ticks, TimeSpan.FromMilliseconds(Settings.Instance.ExistLoginTimeout / 2.0).Ticks));
            _loginTimeoutTimer.Tick += LoginTimeoutTimer_Tick;

            // L368a: 构造时立即启动，10 分钟后强制最小化窗口（无操作时自动收起，避免长时间无人值守遮挡桌面）
            _autoMinimizeTimer.Interval = TimeSpan.FromMinutes(AutoMinimizeIntervalMinutes);
            _autoMinimizeTimer.Tick += AutoMinimizeTimer_Tick;
            _autoMinimizeTimer.Start();

            // 编码器状态轮询定时器：周期性查询 ToVGT 最近接收时间并更新 UI 状态
            _encoderStatusTimer.Interval = TimeSpan.FromSeconds(EncoderStatusPollIntervalSec);
            _encoderStatusTimer.Tick += EncoderStatusTimer_Tick;
            _encoderStatusTimer.Start();

            // P1-12: 系统状态定时器，周期性更新 FPS/内存/当前配方显示
            _systemStatusTimer.Interval = TimeSpan.FromSeconds(SystemStatusPollIntervalSec);
            _systemStatusTimer.Tick += SystemStatusTimer_Tick;
            _systemStatusTimer.Start();

            // 订阅设置变更与会话变更事件
            Settings.Instance.Changed += Settings_Changed;
            AuthService.Instance.SessionChanged += AuthService_SessionChanged;

            // 注册接收来自 ViewModel 的添加条码消息，使用弱引用 messenger 避免内存泄漏
            WeakReferenceMessenger.Default.Register<MainViewModel, AddBarcodeMessage>(this, (r, m) =>
            {
                r.AddBarcode(m.Value);
            });
            // H17: 注册 AI 识别计数消息
            WeakReferenceMessenger.Default.Register<MainViewModel, AddDetectionMessage>(this, (r, m) =>
            {
                r.AddAiCount(m.Value);
            });
            // 注册帧计数指标消息：由 HomeViewModel 推送 ProcessedFrameCount / DropFrameCount，
            // 替代原 GetProcessedFrameCount / GetDropFrameCount Func 反向回调
            // 使用 Interlocked 保证跨线程（messenger 在后台线程触发，UI 线程读取）可见性与原子性
            WeakReferenceMessenger.Default.Register<MainViewModel, FrameMetricsMessage>(this, (r, m) =>
            {
                Interlocked.Exchange(ref r._processedFrameCount, m.ProcessedFrameCount);
                Interlocked.Exchange(ref r._dropFrameCount, m.DropFrameCount);
            });

            // 启动自动登录(2026-09-05)：应用启动即默认管理员登录 = 全部权限，免登录弹窗。
            // 手动同步 CurrentUser（AuthService 的 SessionChanged 会被异步派发，这里先直接置位，
            // 保证状态栏/权限门控在首个 UI 帧即正确）。登出后顶部按钮仍可手动登录其它账号。
            _isStartupAutoLogin = true;
            try
            {
                if (AuthService.Instance.AutoLoginDefaultAdmin())
                {
                    CurrentUser = AuthService.Instance.CurrentUser;
                    LogService.Instance.Info($"[MainViewModel] 已自动登录默认管理员: {_currentUser?.Username}");
                }
                else
                {
                    LogService.Instance.Warning("[MainViewModel] 自动登录失败：系统不存在管理员账户");
                }
            }
            finally
            {
                _isStartupAutoLogin = false;
            }
        }

        // === 条码去重与计数 ===
        public void AddBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
            {
                return;
            }

            if (!_dispatcher.CheckAccess())
            {
                // M127: Dispatcher 可能正在关闭，检查并捕获异常避免 fire-and-forget 静默失败
                if (_dispatcher.HasShutdownStarted) return;
                try { _ = _dispatcher.InvokeAsync(() => AddBarcode(barcode)); }
                catch (Exception ex) { LogService.Instance.Error($"派发 AddBarcode 失败: {ex}"); }
                return;
            }

            // M237: 捕获 Dispatcher.InvokeAsync 异步执行中的异常，避免未观察异常
            try
            {
                int queueCount = 0;
                int setCount = 0;
                bool shouldLog = false;

                if (!_barcodeSet.Add(barcode))
                {
                    return;
                }

                _barcodeQueue.Enqueue(barcode);
                int newCount = Interlocked.Increment(ref _count);

                while (_barcodeQueue.TryDequeue(out var removedBarcode) && _barcodeQueue.Count > MaxBarcodes)
                {
                    _barcodeSet.Remove(removedBarcode);
                }

                if (newCount % BarcodeLogInterval == 0)
                {
                    queueCount = _barcodeQueue.Count;
                    setCount = _barcodeSet.Count;
                    shouldLog = true;
                }

                // M310a: 锁外触发 OnPropertyChanged，避免在锁内执行可能阻塞的回调
                OnPropertyChanged(nameof(Count));

                if (shouldLog)
                {
                    // M309a: 锁外调用 LogCollectionSize，减少锁持有时间
                    MemoryDiagnostics.LogCollectionSize("MainViewModel._barcodeQueue", queueCount);
                    MemoryDiagnostics.LogCollectionSize("MainViewModel._barcodeSet", setCount);
                }
            }
            catch (Exception ex) { LogService.Instance.Error($"AddBarcode 执行失败: {ex}"); }
        }

        // H17: AI 识别计数增加，由 AddDetectionMessage 触发
        private void AddAiCount(int count)
        {
            if (!_dispatcher.CheckAccess())
            {
                // M127: Dispatcher 可能正在关闭，检查并捕获异常避免 fire-and-forget 静默失败
                if (_dispatcher.HasShutdownStarted) return;
                try { _ = _dispatcher.InvokeAsync(() => AddAiCount(count)); }
                catch (Exception ex) { LogService.Instance.Error($"派发 AddAiCount 失败: {ex}"); }
                return;
            }
            // M237: 捕获 Dispatcher.InvokeAsync 异步执行中的异常，避免未观察异常
            try
            {
                AiCount += count;
            }
            catch (Exception ex) { LogService.Instance.Error($"AddAiCount 执行失败: {ex}"); }
        }

        // === 计数清零（换班/换产品场景） ===
        /// <summary>
        /// 清零读码数量、识别数量和去重跟踪列表。
        /// 同时清空条码去重集合与队列，确保计数从零开始重新统计。
        /// </summary>
        public void ResetCount()
        {
            try
            {
                _count = 0;
                _aiCount = 0;
                _barcodeSet.Clear();
                _barcodeQueue.Clear();
                OnPropertyChanged(nameof(Count));
                OnPropertyChanged(nameof(AiCount));
                // 清零 HomeViewModel 的 ProductTracker 跟踪列表
                NavigationService?.ResetCount();
                LogService.Instance.Info("[MainViewModel] 计数已清零（读码数量/识别数量/去重跟踪列表）");
                NotificationService.Success("计数已清零");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"清零计数失败: {ex}");
                NotificationService.Error($"清零计数失败: {ex.Message}");
            }
        }

        // === 登录/登出 ===
        /// <summary>
        /// 登录：调用 AuthService 认证并设置当前用户。
        /// </summary>
        public void Login(string username, string password)
        {
            if (AuthService.Instance.Authenticate(username, password, out var user))
            {
                CurrentUser = user;
                NotificationService.Success("登录成功");
                return;
            }
            NotificationService.Error("账号或密码错误");
        }

        /// <summary>
        /// 登出：清除当前会话状态。
        /// H87c: 包 try-finally，确保 AuthService.Logout 抛出异常时 CurrentUser = null 仍执行
        /// </summary>
        public void Logout()
        {
            try
            {
                AuthService.Instance.Logout();
            }
            finally
            {
                CurrentUser = null;
            }
            NotificationService.Info("已退出登录。");
        }

        // === 用户活动：重置自动最小化定时器 ===
        /// <summary>
        /// H88c: 用户交互时重置自动最小化定时器，避免活跃时窗口被反复最小化。
        /// M342c: 节流，PreviewMouseMove/PreviewKeyDown 高频触发，避免每次事件都 Stop+Start 造成抖动
        /// </summary>
        public void ResetAutoMinimizeTimer()
        {
            var now = DateTime.UtcNow;
            if (now - _lastActivityResetTime < TimeSpan.FromSeconds(ActivityResetThrottleSec))
            {
                return;
            }
            _lastActivityResetTime = now;
            // Stop + Start 重置定时器计时，相当于重新开始倒计时
            _autoMinimizeTimer.Stop();
            _autoMinimizeTimer.Start();
        }

        // === 定时器 Tick ===
        private void LoginTimeoutTimer_Tick(object? sender, EventArgs e)
        {
            // L368b: 使用 Stopwatch.IsRunning 判断是否处于登录计时状态（单调时钟，不受系统时间回调影响）
            if (_currentUser is null || !_loginStopwatch.IsRunning)
            {
                return;
            }

            if (Settings.Instance.ExistLoginTimeout <= 0)
            {
                _loginTimeoutTimer.Stop();
                return;
            }

            var timeout = TimeSpan.FromMilliseconds(Settings.Instance.ExistLoginTimeout);
            // L368b: 使用 Stopwatch.Elapsed 计算经过时间（单调时钟）
            if (_loginStopwatch.Elapsed >= timeout)
            {
                Logout();
            }

            UpdateSessionRemainDisplay();
        }

        private void AutoMinimizeTimer_Tick(object? sender, EventArgs e)
        {
            NavigationService?.EnsureWindowMinimized();
        }

        // 编码器状态轮询：基于最近接收时间判断是否在接收数据
        private void EncoderStatusTimer_Tick(object? sender, EventArgs e)
        {
            var last = _toVgtService.LastEncoderReceiveTime;
            bool isReceiving = last.HasValue
                && (DateTime.Now - last.Value).TotalSeconds < EncoderReceivingThresholdSec;
            EncoderStatusText = isReceiving ? "接收中" : "未接收";
            IsEncoderReceiving = isReceiving;
            // 同步刷新编码器数值文本
            var enc = _toVgtService.LastEncoderValue;
            EncoderValueText = enc.HasValue ? enc.Value.ToString() : "-";
        }

        // P1-12: 系统状态轮询：更新 FPS / 内存 / 当前配方 / 推理耗时 / 丢帧数
        private void SystemStatusTimer_Tick(object? sender, EventArgs e)
        {
            // FPS：基于 HomeViewModel.ProcessedFrameCount 增量 / 经过秒数
            // 统计的是实际处理的帧数（含跳过帧），不依赖检测结果
            try
            {
                var now = DateTime.UtcNow;
                // 帧计数由 HomeViewModel 通过 FrameMetricsMessage 推送，使用 Interlocked.Read 保证跨线程原子读取
                var currentFrames = Interlocked.Read(ref _processedFrameCount);
                var lastFrames = _lastProcessedFrameCount;
                var elapsedSec = (now - _lastFpsUpdateTime).TotalSeconds;
                if (elapsedSec < 0.5) elapsedSec = 0.5; // 防除零
                double fps = (currentFrames - lastFrames) / elapsedSec;
                if (fps < 0) fps = 0;
                _lastProcessedFrameCount = currentFrames;
                _lastFpsUpdateTime = now;
                FpsText = fps.ToString("F1");
            }
            catch (Exception ex) { LogService.Instance.Error($"SystemStatus FPS 更新失败: {ex}"); }

            // 内存：进程工作集
            try
            {
                using var proc = Process.GetCurrentProcess();
                proc.Refresh();
                long mb = proc.WorkingSet64 / (1024 * 1024);
                MemoryText = $"{mb} MB";
            }
            catch (Exception ex) { LogService.Instance.Error($"SystemStatus 内存更新失败: {ex}"); }

            // 当前配方名
            try
            {
                var name = RecipesManage.Instance.CurrentRecipe?.Name;
                CurrentRecipeText = string.IsNullOrEmpty(name) ? "未加载" : name!;
            }
            catch (Exception ex) { LogService.Instance.Error($"SystemStatus 配方更新失败: {ex}"); }

            // 最近一帧推理耗时（纯推理段）+ 历史 均/最大 + 设备
            try
            {
                long infMs = TimingLogger.LastInferenceMs;
                long avgMs = TimingLogger.AvgInferenceMs;
                long maxMs = TimingLogger.MaxInferenceMs;
                string dev = TimingLogger.LastDevice;
                InferenceTimeText = (infMs > 0)
                    ? $"{infMs}ms / 均 {avgMs}ms / 最大 {maxMs}ms ({dev})"
                    : "-";
            }
            catch (Exception ex) { LogService.Instance.Error($"SystemStatus 推理耗时更新失败: {ex}"); }

            // L: 累计丢帧数（来自 HomeViewModel 通过 messenger 推送的 DropFrameCount）
            try
            {
                int dropCount = _dropFrameCount;
                DropFrameText = $"丢帧: {dropCount}";
            }
            catch (Exception ex) { LogService.Instance.Error($"SystemStatus 丢帧数更新失败: {ex}"); }
        }

        // === 事件处理 ===
        private void Settings_Changed(object? sender, EventArgs e)
        {
            // M87: 异步派发避免死锁
            _ = _dispatcher.BeginInvoke(() =>
            {
                NavigationService?.ApplyWindowSettings();
                // H51a: 设置变更后重启登录超时定时器，使新的 ExistLoginTimeout 立即生效
                if (_currentUser is not null && Settings.Instance.ExistLoginTimeout > 0)
                {
                    // H90b: 设置变更后重新计算定时器间隔
                    _loginTimeoutTimer.Interval = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(30).Ticks, TimeSpan.FromMilliseconds(Settings.Instance.ExistLoginTimeout / 2.0).Ticks));
                    // L368b: 使用 Stopwatch.Restart 重新开始单调计时
                    _loginStopwatch.Restart();
                    _loginTimeoutTimer.Start();
                }
                else
                {
                    _loginTimeoutTimer.Stop();
                }
            });
        }

        private void AuthService_SessionChanged(object? sender, EventArgs e)
        {
            // M88: 异步派发避免死锁
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(_currentUser, AuthService.Instance.CurrentUser))
                {
                    CurrentUser = AuthService.Instance.CurrentUser;
                }
            });
        }

        private void CurrentUser_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // H52b: PropertyChanged 可能由非 UI 线程触发，后续涉及 UI 线程操作（如 EnsureVisibleTabSelected），需切回 UI 线程
            if (!_dispatcher.CheckAccess())
            {
                _ = _dispatcher.BeginInvoke(() => CurrentUser_PropertyChanged(sender, e));
                return;
            }

            if (e.PropertyName is nameof(AppUser.Username) or nameof(AppUser.DisplayName) or nameof(AppUser.Role) or nameof(AppUser.Enabled))
            {
                OnPropertyChanged(nameof(LoginStatusText));
                OnPropertyChanged(nameof(LoginButtonText));
                OnPropertyChanged(nameof(CanAccessRecipes));
                OnPropertyChanged(nameof(CanAccessDatabase));
                OnPropertyChanged(nameof(CanAccessSettings));
                OnPropertyChanged(nameof(CanAccessLogs));
                OnPropertyChanged(nameof(CanResetCount));

                if (_currentUser is not null && !_currentUser.Enabled)
                {
                    Logout();
                    return;
                }

                _ = _dispatcher.BeginInvoke(() => NavigationService?.EnsureVisibleSelected());
            }
        }

        // === 资源释放 ===
        public void Dispose()
        {
            // M322b: 前段操作（事件取消订阅、定时器停止）分段 try-catch，避免单个抛出跳过后续清理
            try { Settings.Instance.Changed -= Settings_Changed; }
            catch (Exception ex) { LogService.Instance.Error($"取消订阅 Settings.Changed 失败: {ex}"); }
            try { AuthService.Instance.SessionChanged -= AuthService_SessionChanged; }
            catch (Exception ex) { LogService.Instance.Error($"取消订阅 SessionChanged 失败: {ex}"); }
            // H66: 取消订阅 CurrentUser.PropertyChanged，避免用户对象生命周期长于窗口时内存泄漏
            if (_currentUser is not null)
            {
                try { _currentUser.PropertyChanged -= CurrentUser_PropertyChanged; }
                catch (Exception ex) { LogService.Instance.Error($"取消订阅 CurrentUser.PropertyChanged 失败: {ex}"); }
            }
            try
            {
                _autoMinimizeTimer.Stop();
                _autoMinimizeTimer.Tick -= AutoMinimizeTimer_Tick;
            }
            catch (Exception ex) { LogService.Instance.Error($"停止自动最小化定时器失败: {ex}"); }
            // M54: 显式停止并取消订阅 _loginTimeoutTimer
            try
            {
                _loginTimeoutTimer.Stop();
                _loginTimeoutTimer.Tick -= LoginTimeoutTimer_Tick;
            }
            catch (Exception ex) { LogService.Instance.Error($"停止登录超时定时器失败: {ex}"); }
            // 停止编码器状态轮询定时器
            try
            {
                _encoderStatusTimer.Stop();
                _encoderStatusTimer.Tick -= EncoderStatusTimer_Tick;
            }
            catch (Exception ex) { LogService.Instance.Error($"停止编码器状态定时器失败: {ex}"); }
            // P1-12: 停止系统状态轮询定时器
            try
            {
                _systemStatusTimer.Stop();
                _systemStatusTimer.Tick -= SystemStatusTimer_Tick;
            }
            catch (Exception ex) { LogService.Instance.Error($"停止系统状态定时器失败: {ex}"); }

            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }
}
