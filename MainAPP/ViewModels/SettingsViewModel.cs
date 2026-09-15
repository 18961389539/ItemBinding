using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Models;
using MainAPP.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;


namespace MainAPP.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        /// <summary>
        /// 对话框服务抽象。VM 通过此属性调用弹窗，避免直接依赖 View。
        /// 默认实例在 App.xaml.cs 的 DI 配置中被替换为 DI 解析的实例。
        /// </summary>
        public static IDialogService DialogService { get; set; } = new Services.DialogService();

        // ResetToDefaults 使用的默认值常量
        private const int DefaultMinRecentDays = 10;
        private const int DefaultBarcodeRemoveCount = 5;
        private const int DefaultExistLoginTimeout = 300_000;
        private const int DefaultBackupExpireDays = 14;
        private const int DefaultWindowWidth = 1200;
        private const int DefaultWindowHeight = 800;
        private const string DefaultWindowTitle = "ABB机器人";
        private const string DefaultUserName = "abb";
        private const string DefaultConnectivityCheckIP = "127.0.0.1";
        private const string DefaultDetectionResultSendIP = "127.0.0.1";
        private const int DefaultEncoderReceiverPort = 11301;
        private const int DefaultDetectionResultSendPort = 2611;
        private const string DefaultMessageReceiver = "LL";
        private const bool DefaultDedupEnabled = true;
        private const string DefaultDedupTrackAxis = "Y";
        private const double DefaultDedupPositionThreshold = 3.0;
        private const double DefaultDedupAngleThreshold = 2.0;
        // 2026-09-05: 四边独立边缘最小间距默认值（各 10px，防止抓到半个产品）
        private const double DefaultEdgeMarginPixels = 10;

        private readonly Settings _settings = Settings.Instance;
        private readonly AuthService _authService = AuthService.Instance;

        private bool _isSaveDraw;
        private bool _isSaveSource;
        private int _minRecentDays;
        private int _barcodeRemoveCount;
        private int _existLoginTimeout;
        private int _backupExpireDays;
        private string _picturesSaveFolder = string.Empty;
        private int _windowWidth;
        private int _windowHeight;
        private string _windowTitle = string.Empty;
        private string _password = string.Empty;
        private string _userName = string.Empty;
        private string _connectivityCheckIP = string.Empty;
        private string _detectionResultSendIP = string.Empty;
        private int _encoderReceiverPort;
        private int _detectionResultSendPort;
        private string _messageReceiver = string.Empty;
        private bool _dedupEnabled;
        private string _dedupTrackAxis = string.Empty;
        private double _dedupPositionThreshold;
        private double _dedupAngleThreshold;
        private double _edgeMarginLeftPixels;
        private double _edgeMarginTopPixels;
        private double _edgeMarginRightPixels;
        private double _edgeMarginBottomPixels;
        // 产品掩码面积上下限（原图像素，0=禁用），2026-09-07
        private double _minMaskAreaPixels;
        private double _maxMaskAreaPixels;
        private AppUser? _selectedUser;
        private string _selectedUserName = string.Empty;
        private string _selectedUserDisplayName = string.Empty;
        private string _selectedUserPassword = string.Empty;
        private string _selectedUserConfirmPassword = string.Empty;
        private UserRole _selectedUserRole = UserRole.Operator;
        private bool _selectedUserEnabled = true;
        private string _newAccountUserName = string.Empty;
        private string _newAccountDisplayName = string.Empty;
        private string _newAccountPassword = string.Empty;
        private UserRole _newAccountRole = UserRole.Operator;
        private bool _newAccountEnabled = true;

        // 测试连接相关字段
        private bool _isTestingConnection;
        private string _testConnectionResult = string.Empty;
        private string _testConnectionStatusColor = "#78909C";  // 灰色默认

        // L: dirty 标记与加载标志。_isLoading 用于构造函数及程序化加载期间禁止 setter 触发 dirty
        private bool _isDirty;
        private bool _isLoading;

        public SettingsViewModel()
        {
            // 加载期间禁止 setter 触发 dirty（构造函数直接赋值字段，此处为防御性保护）
            _isLoading = true;
            // 从设置加载初始值
            _isSaveDraw = _settings.IsSaveDraw;
            _isSaveSource = _settings.IsSaveSource;
            _minRecentDays = _settings.MinRecentDays;
            _barcodeRemoveCount = _settings.BarcodeRemoveCount;
            _existLoginTimeout = _settings.ExistLoginTimeout;
            _backupExpireDays = _settings.BackupExpireDays;
            _picturesSaveFolder = _settings.PicturesSaveFolder;
            _windowWidth = _settings.WindowWidth;
            _windowHeight = _settings.WindowHeight;
            _windowTitle = _settings.WindowTitle;
            _password = _settings.Password;
            _userName = _settings.UserName;
            _connectivityCheckIP = _settings.ConnectivityCheckIP;
            _detectionResultSendIP = _settings.DetectionResultSendIP;
            _encoderReceiverPort = _settings.EncoderReceiverPort;
            _detectionResultSendPort = _settings.DetectionResultSendPort;
            _messageReceiver = _settings.MessageReceiver;
            _dedupEnabled = _settings.DedupEnabled;
            _dedupTrackAxis = _settings.DedupTrackAxis;
            _dedupPositionThreshold = _settings.DedupPositionThreshold;
            _dedupAngleThreshold = _settings.DedupAngleThreshold;
            _edgeMarginLeftPixels = _settings.EdgeMarginLeftPixels;
            _edgeMarginTopPixels = _settings.EdgeMarginTopPixels;
            _edgeMarginRightPixels = _settings.EdgeMarginRightPixels;
            _edgeMarginBottomPixels = _settings.EdgeMarginBottomPixels;
            _minMaskAreaPixels = _settings.MinMaskAreaPixels;
            _maxMaskAreaPixels = _settings.MaxMaskAreaPixels;

            SaveCommand = new RelayCommand(Save);
            BrowsePicturesFolderCommand = new RelayCommand(BrowsePicturesFolder);
            ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);
            AddAccountCommand = new RelayCommand(AddAccount);
            ApplySelectedAccountCommand = new RelayCommand(ApplySelectedAccount, CanApplySelectedAccount);
            DeleteAccountCommand = new RelayCommand(DeleteAccount);
            RefreshSelectedAccountCommand = new RelayCommand(RefreshSelectedAccount, CanApplySelectedAccount);
            ResetAccountsCommand = new RelayCommand(ResetAccounts);
            // 测试连接命令：默认 AsyncRelayCommandOptions.None 禁止并发执行，防止测试期间重复点击
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
            _isLoading = false;
        }

        /// <summary>自定义通讯协议配置（2026-09-13）。</summary>
        public ProtocolSettingsViewModel ProtocolVM { get; } = new();

        #region Properties

        /// <summary>设置是否有未保存的修改，用于保存按钮 dirty 提示。</summary>
        public bool IsDirty
        {
            get => _isDirty;
            private set => SetProperty(ref _isDirty, value);
        }

        // L: 统一在可写属性变化时标记 dirty；_isLoading 期间（构造、加载账号编辑器等程序化赋值）不触发
        private bool SetPropertyAndMarkDirty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            bool changed = SetProperty(ref field, value, propertyName);
            if (changed && !_isLoading)
            {
                IsDirty = true;
            }
            return changed;
        }

        public bool IsSaveDraw
        {
            get => _isSaveDraw;
            set => SetPropertyAndMarkDirty(ref _isSaveDraw, value);
        }

        public bool IsSaveSource
        {
            get => _isSaveSource;
            set => SetPropertyAndMarkDirty(ref _isSaveSource, value);
        }

        public int MinRecentDays
        {
            get => _minRecentDays;
            set => SetPropertyAndMarkDirty(ref _minRecentDays, value);
        }

        public int BarcodeRemoveCount
        {
            get => _barcodeRemoveCount;
            set => SetPropertyAndMarkDirty(ref _barcodeRemoveCount, value);
        }

        public int ExistLoginTimeout
        {
            get => _existLoginTimeout;
            set => SetPropertyAndMarkDirty(ref _existLoginTimeout, value);
        }

        public int BackupExpireDays
        {
            get => _backupExpireDays;
            set => SetPropertyAndMarkDirty(ref _backupExpireDays, value);
        }

        public string PicturesSaveFolder
        {
            get => _picturesSaveFolder;
            set => SetPropertyAndMarkDirty(ref _picturesSaveFolder, value);
        }

        public int WindowWidth
        {
            get => _windowWidth;
            set => SetPropertyAndMarkDirty(ref _windowWidth, value);
        }

        public int WindowHeight
        {
            get => _windowHeight;
            set => SetPropertyAndMarkDirty(ref _windowHeight, value);
        }

        public string WindowTitle
        {
            get => _windowTitle;
            set => SetPropertyAndMarkDirty(ref _windowTitle, value);
        }

        public string Password
        {
            get => _password;
            set => SetPropertyAndMarkDirty(ref _password, value);
        }

        public string UserName
        {
            get => _userName;
            set => SetPropertyAndMarkDirty(ref _userName, value);
        }

        public string ConnectivityCheckIP
        {
            get => _connectivityCheckIP;
            set => SetPropertyAndMarkDirty(ref _connectivityCheckIP, value);
        }

        public string DetectionResultSendIP
        {
            get => _detectionResultSendIP;
            set => SetPropertyAndMarkDirty(ref _detectionResultSendIP, value);
        }

        public int EncoderReceiverPort
        {
            get => _encoderReceiverPort;
            set => SetPropertyAndMarkDirty(ref _encoderReceiverPort, value);
        }

        public int DetectionResultSendPort
        {
            get => _detectionResultSendPort;
            set => SetPropertyAndMarkDirty(ref _detectionResultSendPort, value);
        }

        public string MessageReceiver
        {
            get => _messageReceiver;
            set => SetPropertyAndMarkDirty(ref _messageReceiver, value);
        }

        // UI 下拉框可选项
        public IReadOnlyList<string> AvailableReceivers { get; } = ["LL", "VGT"];

        public bool DedupEnabled
        {
            get => _dedupEnabled;
            set => SetPropertyAndMarkDirty(ref _dedupEnabled, value);
        }

        public string DedupTrackAxis
        {
            get => _dedupTrackAxis;
            set => SetPropertyAndMarkDirty(ref _dedupTrackAxis, value);
        }

        public double DedupPositionThreshold
        {
            get => _dedupPositionThreshold;
            set => SetPropertyAndMarkDirty(ref _dedupPositionThreshold, value);
        }

        public double DedupAngleThreshold
        {
            get => _dedupAngleThreshold;
            set => SetPropertyAndMarkDirty(ref _dedupAngleThreshold, value);
        }

        /// <summary>
        /// 检测框距图像左边缘的最小间距（像素）：不足该值判定无效并过滤（不发机器人）。
        /// 2026-09-05: 由单一 EdgeMinMarginPixels 拆分为四边独立，默认各 10。
        /// </summary>
        public double EdgeMarginLeftPixels
        {
            get => _edgeMarginLeftPixels;
            set => SetPropertyAndMarkDirty(ref _edgeMarginLeftPixels, value);
        }

        /// <summary>
        /// 检测框距图像上边缘的最小间距（像素）：不足该值判定无效并过滤（不发机器人）。
        /// </summary>
        public double EdgeMarginTopPixels
        {
            get => _edgeMarginTopPixels;
            set => SetPropertyAndMarkDirty(ref _edgeMarginTopPixels, value);
        }

        /// <summary>
        /// 检测框距图像右边缘的最小间距（像素）：不足该值判定无效并过滤（不发机器人）。
        /// </summary>
        public double EdgeMarginRightPixels
        {
            get => _edgeMarginRightPixels;
            set => SetPropertyAndMarkDirty(ref _edgeMarginRightPixels, value);
        }

        /// <summary>
        /// 检测框距图像下边缘的最小间距（像素）：不足该值判定无效并过滤（不发机器人）。
        /// </summary>
        public double EdgeMarginBottomPixels
        {
            get => _edgeMarginBottomPixels;
            set => SetPropertyAndMarkDirty(ref _edgeMarginBottomPixels, value);
        }

        /// <summary>
        /// 产品掩码面积下限（原图像素，0=不启用）：掩码面积低于该值的目标过滤（不发机器人）。
        /// 用于剔除误检小目标/噪声。
        /// </summary>
        public double MinMaskAreaPixels
        {
            get => _minMaskAreaPixels;
            set => SetPropertyAndMarkDirty(ref _minMaskAreaPixels, value);
        }

        /// <summary>
        /// 产品掩码面积上限（原图像素，0=不启用）：掩码面积高于该值的目标过滤（不发机器人）。
        /// 用于剔除超大异常目标。
        /// </summary>
        public double MaxMaskAreaPixels
        {
            get => _maxMaskAreaPixels;
            set => SetPropertyAndMarkDirty(ref _maxMaskAreaPixels, value);
        }

        public IReadOnlyList<string> AvailableAxes { get; } = ["X", "Y"];

        public ObservableCollection<AppUser> Users => _authService.Users;

        // L364a: 使用 IReadOnlyList<UserRole> 替代 Array，提供更强的类型信息便于 XAML 绑定
        public IReadOnlyList<UserRole> AvailableRoles { get; } = Enum.GetValues<UserRole>();

        public AppUser? SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (SetProperty(ref _selectedUser, value))
                {
                    LoadSelectedAccountEditor();
                    ApplySelectedAccountCommand.NotifyCanExecuteChanged();
                    DeleteAccountCommand.NotifyCanExecuteChanged();
                    RefreshSelectedAccountCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public string SelectedUserName
        {
            get => _selectedUserName;
            set => SetPropertyAndMarkDirty(ref _selectedUserName, value);
        }

        public string SelectedUserDisplayName
        {
            get => _selectedUserDisplayName;
            set => SetPropertyAndMarkDirty(ref _selectedUserDisplayName, value);
        }

        public string SelectedUserPassword
        {
            get => _selectedUserPassword;
            set => SetPropertyAndMarkDirty(ref _selectedUserPassword, value);
        }

        public string SelectedUserConfirmPassword
        {
            get => _selectedUserConfirmPassword;
            set => SetPropertyAndMarkDirty(ref _selectedUserConfirmPassword, value);
        }

        public UserRole SelectedUserRole
        {
            get => _selectedUserRole;
            set => SetPropertyAndMarkDirty(ref _selectedUserRole, value);
        }

        public bool SelectedUserEnabled
        {
            get => _selectedUserEnabled;
            set => SetPropertyAndMarkDirty(ref _selectedUserEnabled, value);
        }

        public string NewAccountUserName
        {
            get => _newAccountUserName;
            set => SetPropertyAndMarkDirty(ref _newAccountUserName, value);
        }

        public string NewAccountDisplayName
        {
            get => _newAccountDisplayName;
            set => SetPropertyAndMarkDirty(ref _newAccountDisplayName, value);
        }

        public string NewAccountPassword
        {
            get => _newAccountPassword;
            set => SetPropertyAndMarkDirty(ref _newAccountPassword, value);
        }

        public UserRole NewAccountRole
        {
            get => _newAccountRole;
            set => SetPropertyAndMarkDirty(ref _newAccountRole, value);
        }

        public bool NewAccountEnabled
        {
            get => _newAccountEnabled;
            set => SetPropertyAndMarkDirty(ref _newAccountEnabled, value);
        }

        /// <summary>测试连接进行中标志，用于禁用按钮与显示加载提示。</summary>
        public bool IsTestingConnection
        {
            get => _isTestingConnection;
            set => SetProperty(ref _isTestingConnection, value);
        }

        /// <summary>测试连接结果文本（多行，含每项检测的 ✓/✗ 详情）。</summary>
        public string TestConnectionResult
        {
            get => _testConnectionResult;
            set => SetProperty(ref _testConnectionResult, value);
        }

        /// <summary>测试连接状态颜色（绿色#4CAF50成功 / 红色#F44336失败 / 灰色#78909C默认 / 橙色#FF9800进行中）。</summary>
        public string TestConnectionStatusColor
        {
            get => _testConnectionStatusColor;
            set => SetProperty(ref _testConnectionStatusColor, value);
        }

        #endregion

        #region Commands

        // L399a: 本类使用手动 RelayCommand 而非 [RelayCommand] 特性，暂不迁移（改动较大，需同步调整 XAML 绑定）

        public RelayCommand SaveCommand { get; }
        public RelayCommand BrowsePicturesFolderCommand { get; }
        public RelayCommand ResetToDefaultsCommand { get; }
        public RelayCommand AddAccountCommand { get; }
        public RelayCommand ApplySelectedAccountCommand { get; }
        public RelayCommand DeleteAccountCommand { get; }
        public RelayCommand RefreshSelectedAccountCommand { get; }
        public RelayCommand ResetAccountsCommand { get; }
        // 测试连接命令（异步，禁止并发执行）
        public AsyncRelayCommand TestConnectionCommand { get; }

        #endregion

        #region Command Implementations

        private void Save()
        {
            // M74: try-catch 保证失败时提示错误
            try
            {
                AuditLogService.Instance.Record("保存", "全局设置");
                // L371a: 保存前校验数值字段有效性
            if (MinRecentDays <= 0)
                {
                    NotificationService.Warning("最近天数必须大于 0。");
                    return;
                }
                if (BarcodeRemoveCount < 0)
                {
                    NotificationService.Warning("条码移除数量不能为负数。");
                    return;
                }
                if (ExistLoginTimeout <= 0)
                {
                    NotificationService.Warning("登录超时时间必须大于 0。");
                    return;
                }
                if (BackupExpireDays < 0)
                {
                    NotificationService.Warning("备份过期天数不能为负数。");
                    return;
                }
                if (WindowWidth <= 0)
                {
                    NotificationService.Warning("窗口宽度必须大于 0。");
                    return;
                }
                if (WindowHeight <= 0)
                {
                    NotificationService.Warning("窗口高度必须大于 0。");
                    return;
                }

                // L42: 仅在用户点击保存时，才将本地编辑值写回单例
                _settings.IsSaveDraw = _isSaveDraw;
                _settings.IsSaveSource = _isSaveSource;
                _settings.MinRecentDays = _minRecentDays;
                _settings.BarcodeRemoveCount = _barcodeRemoveCount;
                _settings.ExistLoginTimeout = _existLoginTimeout;
                _settings.BackupExpireDays = _backupExpireDays;
                _settings.PicturesSaveFolder = _picturesSaveFolder;
                _settings.WindowWidth = _windowWidth;
                _settings.WindowHeight = _windowHeight;
                _settings.WindowTitle = _windowTitle;
                _settings.Password = _password;
                _settings.UserName = _userName;
                _settings.ConnectivityCheckIP = _connectivityCheckIP;
                _settings.DetectionResultSendIP = _detectionResultSendIP;
                _settings.EncoderReceiverPort = _encoderReceiverPort;
                _settings.DetectionResultSendPort = _detectionResultSendPort;
                _settings.MessageReceiver = _messageReceiver;
                _settings.DedupEnabled = _dedupEnabled;
                _settings.DedupTrackAxis = _dedupTrackAxis;
                _settings.DedupPositionThreshold = _dedupPositionThreshold;
                _settings.DedupAngleThreshold = _dedupAngleThreshold;
                _settings.EdgeMarginLeftPixels = _edgeMarginLeftPixels;
                _settings.EdgeMarginTopPixels = _edgeMarginTopPixels;
                _settings.EdgeMarginRightPixels = _edgeMarginRightPixels;
                _settings.EdgeMarginBottomPixels = _edgeMarginBottomPixels;
                _settings.MinMaskAreaPixels = _minMaskAreaPixels;
                _settings.MaxMaskAreaPixels = _maxMaskAreaPixels;

                _settings.Save();
                _authService.EnsureAdminAccount(UserName, Password);
                _authService.Save();
                NotificationService.Success("设置已保存。");
                // L: 保存成功后清除 dirty 标记
                IsDirty = false;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"保存设置失败: {ex}");
                NotificationService.Error(ex.Message);
            }
        }

        private void BrowsePicturesFolder()
        {
            var dialog = new OpenFolderDialog();
            dialog.Multiselect = false;
            if (dialog.ShowDialog() == true)
            {
                PicturesSaveFolder = dialog.FolderName;
            }
        }

        private void ResetToDefaults()
        {
            // P0-FIX: 重置为默认前加二次确认，避免误触丢失所有自定义配置
            var confirm = NotificationService.Ask(
                "确定要将所有设置重置为默认值吗？\n该操作不可撤销，自定义的网络配置、阈值等将全部丢失。",
                "确认重置默认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            IsSaveDraw = true;
            IsSaveSource = false;
            // L108: 使用默认值常量
            MinRecentDays = DefaultMinRecentDays;
            BarcodeRemoveCount = DefaultBarcodeRemoveCount;
            ExistLoginTimeout = DefaultExistLoginTimeout;
            BackupExpireDays = DefaultBackupExpireDays;
            // L109: Path.Combine 改为多参数形式，避免路径分隔符硬编码
            // 2026-09-15: 恢复默认值改为跟随统一数据根，不再写死在 exe 目录
            PicturesSaveFolder = DataPaths.PicturesDir;
            WindowWidth = DefaultWindowWidth;
            WindowHeight = DefaultWindowHeight;
            WindowTitle = DefaultWindowTitle;
            UserName = DefaultUserName;
            Password = string.Empty;
            ConnectivityCheckIP = DefaultConnectivityCheckIP;
            DetectionResultSendIP = DefaultDetectionResultSendIP;
            EncoderReceiverPort = DefaultEncoderReceiverPort;
            DetectionResultSendPort = DefaultDetectionResultSendPort;
            MessageReceiver = DefaultMessageReceiver;
            DedupEnabled = DefaultDedupEnabled;
            DedupTrackAxis = DefaultDedupTrackAxis;
            DedupPositionThreshold = DefaultDedupPositionThreshold;
            DedupAngleThreshold = DefaultDedupAngleThreshold;
            ResetAccounts();
            // L: 重置为默认后值已改但未保存，标记 dirty
            IsDirty = true;
        }

        private void AddAccount()
        {
            // 允许空密码：与免密登录模型一致（默认 Operator 即空密码账户），
            // 仅强制账号名非空。留空创建的账户在登录窗口密码留空即可登录。
            if (string.IsNullOrWhiteSpace(NewAccountUserName))
            {
                NotificationService.Info("请输入账号。");
                return;
            }

            // L396a: 提取 Trim() 为局部变量
            var trimmedUserName = NewAccountUserName.Trim();

            if (Users.Any(x => string.Equals(x.Username, trimmedUserName, StringComparison.OrdinalIgnoreCase)))
            {
                NotificationService.Info("该账号已存在。");
                return;
            }

            if (NewAccountRole == UserRole.Admin && Users.Any(x => x.Role == UserRole.Admin))
            {
                NotificationService.Info("系统仅保留一个管理员账号。");
                return;
            }

            // 空密码提示：告知管理员该账户将免密登录，避免误以为已设置密码。
            // 注意：仅"真正为空"才是免密；纯空格会被当作字面密码原样存储。
            if (string.IsNullOrEmpty(NewAccountPassword))
            {
                NotificationService.Info("未设置密码，登录时密码留空即可。");
            }

            // M75: 统一 Trim 用户名
            var previousSelectedUser = SelectedUser;
            var createdUser = AppUser.Create(trimmedUserName, NewAccountDisplayName, NewAccountPassword, NewAccountRole, NewAccountEnabled);
            Users.Add(createdUser);
            AuditLogService.Instance.Record("新增", "账号", null, trimmedUserName);
            SelectedUser = createdUser;
            try
            {
                _authService.Save();
            }
            catch (Exception ex)
            {
                // L362a: Save 失败时回滚 Users 集合和 SelectedUser，保留输入便于重试
                Users.Remove(createdUser);
                SelectedUser = previousSelectedUser;
                LogService.Instance.Error($"添加账号后保存失败: {ex}");
                NotificationService.Error(ex.Message);
                return;
            }
            // Save 成功后才清空输入（程序化清空，期间禁止触发 dirty）
            _isLoading = true;
            NewAccountUserName = string.Empty;
            NewAccountDisplayName = string.Empty;
            NewAccountPassword = string.Empty;
            NewAccountRole = UserRole.Operator;
            NewAccountEnabled = true;
            _isLoading = false;
        }

        private bool CanApplySelectedAccount()
        {
            return SelectedUser is not null;
        }

        private void ApplySelectedAccount()
        {
            try
            {
                if (SelectedUser is null)
                {
                    return;
                }

                var trimmedUserName = SelectedUserName.Trim();
                var trimmedDisplayName = SelectedUserDisplayName.Trim();

                if (string.IsNullOrWhiteSpace(trimmedUserName))
                {
                    NotificationService.Info("账号不能为空。");
                    return;
                }

                if (Users.Any(x => !ReferenceEquals(x, SelectedUser) && string.Equals(x.Username, trimmedUserName, StringComparison.OrdinalIgnoreCase)))
                {
                    NotificationService.Info("该账号名已存在。");
                    return;
                }

                if (SelectedUser.Role == UserRole.Admin && SelectedUserRole != UserRole.Admin && Users.Count(x => x.Role == UserRole.Admin) <= 1)
                {
                    NotificationService.Info("不能取消最后一个管理员账号。");
                    return;
                }

                // H83: null 检查，避免无管理员时 FirstOrDefault 返回 null
                var existingAdmin = Users.FirstOrDefault(x => x.Role == UserRole.Admin);
                if (SelectedUserRole == UserRole.Admin && existingAdmin is not null && !ReferenceEquals(SelectedUser, existingAdmin))
                {
                    NotificationService.Info("系统仅允许一个管理员账号。");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(SelectedUserPassword) || !string.IsNullOrWhiteSpace(SelectedUserConfirmPassword))
                {
                    if (!string.Equals(SelectedUserPassword, SelectedUserConfirmPassword, StringComparison.Ordinal))
                    {
                        NotificationService.Info("两次输入的新密码不一致。");
                        return;
                    }
                }

                SelectedUser.Username = trimmedUserName;
                SelectedUser.DisplayName = string.IsNullOrWhiteSpace(trimmedDisplayName) ? trimmedUserName : trimmedDisplayName;
                SelectedUser.Role = SelectedUserRole;
                SelectedUser.Enabled = SelectedUserEnabled;

                if (!string.IsNullOrWhiteSpace(SelectedUserPassword))
                {
                    SelectedUser.SetPassword(SelectedUserPassword);
                }

                _authService.Save();

                if (ReferenceEquals(_authService.CurrentUser, SelectedUser) && !SelectedUser.Enabled)
                {
                    _authService.Logout();
                }

                NotificationService.Success("账号信息已更新。");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"应用账号信息失败: {ex}");
                NotificationService.Error(ex.Message);
            }
        }

        private void DeleteAccount()
        {
            try
            {
                if (SelectedUser is null)
                {
                    return;
                }

                if (ReferenceEquals(_authService.CurrentUser, SelectedUser))
                {
                    NotificationService.Info("当前登录账号不能直接删除，请先退出登录。");
                    return;
                }

                if (SelectedUser.Role == UserRole.Admin && Users.Count(x => x.Role == UserRole.Admin) <= 1)
                {
                    NotificationService.Info("不能删除最后一个管理员账号。");
                    return;
                }

                // M339: Save 失败时回滚 Users 和 SelectedUser
                var userToDelete = SelectedUser;
                Users.Remove(userToDelete);
                AuditLogService.Instance.Record("删除", "账号", userToDelete.Username, null);
                SelectedUser = null;
                try
                {
                    _authService.Save();
                }
                catch
                {
                    Users.Add(userToDelete);
                    SelectedUser = userToDelete;
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"删除账号失败: {ex}");
                NotificationService.Error(ex.Message);
            }
        }

        private void RefreshSelectedAccount()
        {
            LoadSelectedAccountEditor();
        }

        private void LoadSelectedAccountEditor()
        {
            // L: 加载账号编辑器期间禁止触发 dirty
            _isLoading = true;
            try
            {
                if (SelectedUser is null)
                {
                    SelectedUserName = string.Empty;
                    SelectedUserDisplayName = string.Empty;
                    SelectedUserPassword = string.Empty;
                    SelectedUserConfirmPassword = string.Empty;
                    SelectedUserRole = UserRole.Operator;
                    SelectedUserEnabled = true;
                    return;
                }

                SelectedUserName = SelectedUser.Username;
                SelectedUserDisplayName = SelectedUser.DisplayName;
                SelectedUserPassword = string.Empty;
                SelectedUserConfirmPassword = string.Empty;
                SelectedUserRole = SelectedUser.Role;
                SelectedUserEnabled = SelectedUser.Enabled;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void ResetAccounts()
        {
            try
            {
                _authService.ResetDefaults(UserName, Password);
                SelectedUser = null;
                NewAccountUserName = string.Empty;
                NewAccountDisplayName = string.Empty;
                NewAccountPassword = string.Empty;
                NewAccountRole = UserRole.Operator;
                NewAccountEnabled = true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"重置账号失败: {ex}");
                NotificationService.Error(ex.Message);
            }
        }

        /// <summary>
        /// 测试通信设置是否可用：Ping 连通性检测IP、绑定接收端口、TCP 连通发送目标。
        /// 各项检测超时 3 秒，避免 UI 长时间卡顿。
        /// </summary>
        private async Task TestConnectionAsync()
        {
            IsTestingConnection = true;
            TestConnectionStatusColor = "#FF9800";
            TestConnectionResult = "正在测试...";

            var results = new System.Text.StringBuilder();
            var allSuccess = true;

            // 1. Ping 测试 ConnectivityCheckIP（连通性检测IP）
            try
            {
                var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(ConnectivityCheckIP, 3000);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    results.AppendLine($"✓ Ping {ConnectivityCheckIP} 成功 ({reply.RoundtripTime}ms)");
                }
                else
                {
                    results.AppendLine($"✗ Ping {ConnectivityCheckIP} 失败: {reply.Status}");
                    allSuccess = false;
                }
            }
            catch (Exception ex)
            {
                results.AppendLine($"✗ Ping {ConnectivityCheckIP} 异常: {ex.Message}");
                allSuccess = false;
            }

            // 2. 检测 EncoderReceiverPort 是否可绑定（接收编码器报文端口）
            try
            {
                using var testSocket = new System.Net.Sockets.Socket(
                    System.Net.Sockets.AddressFamily.InterNetwork,
                    System.Net.Sockets.SocketType.Dgram,
                    System.Net.Sockets.ProtocolType.Udp);
                testSocket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, EncoderReceiverPort));
                results.AppendLine($"✓ 接收端口 {EncoderReceiverPort} 可用");
                testSocket.Close();
            }
            catch (Exception ex)
            {
                results.AppendLine($"✗ 接收端口 {EncoderReceiverPort} 不可用: {ex.Message}");
                allSuccess = false;
            }

            // 3. 检测 DetectionResultSendPort 目标是否可达（TCP 连通性，仅尝试连接不发送数据）
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync(DetectionResultSendIP, DetectionResultSendPort);
                await Task.WhenAny(connectTask, Task.Delay(3000));
                if (connectTask.IsCompletedSuccessfully)
                {
                    results.AppendLine($"✓ 发送目标 {DetectionResultSendIP}:{DetectionResultSendPort} 可达");
                }
                else
                {
                    results.AppendLine($"✗ 发送目标 {DetectionResultSendIP}:{DetectionResultSendPort} 连接超时或失败");
                    allSuccess = false;
                }
            }
            catch (Exception ex)
            {
                results.AppendLine($"✗ 发送目标 {DetectionResultSendIP}:{DetectionResultSendPort} 不可达: {ex.Message}");
                allSuccess = false;
            }

            TestConnectionResult = results.ToString().TrimEnd();
            TestConnectionStatusColor = allSuccess ? "#4CAF50" : "#F44336";
            IsTestingConnection = false;

            // 使用 NotificationService 提示用户
            if (allSuccess)
            {
                NotificationService.Success("通信测试全部通过");
            }
            else
            {
                NotificationService.Warning("通信测试存在问题，请查看详情");
            }
        }

        #endregion
    }
}