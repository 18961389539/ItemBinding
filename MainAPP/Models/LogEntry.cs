using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Media;

namespace MainAPP.Models
{
    /// <summary>
    /// 日志条目模型，用于 UI 日志列表展示和 Serilog SQLite 日志读取。
    /// 包含时间戳、日志级别、消息内容，以及根据级别自动着色的显示颜色。
    /// </summary>
    public class LogEntry
    {
        // L275: 默认 Color 画刷冻结，提高 WPF 渲染性能
        // M333b: 缓存级别颜色为静态冻结画刷，避免每次调用 GetColorByLevel 都创建新画刷
        // 色盲友好配色（Wong 2011）：Verbose=黑, Debug=蓝, Info=绿, Warning=橙, Error=朱红, Fatal=紫红
        private static readonly SolidColorBrush s_defaultColor;
        private static readonly SolidColorBrush s_errorColor;
        private static readonly SolidColorBrush s_warningColor;
        private static readonly SolidColorBrush s_infoColor;
        private static readonly SolidColorBrush s_debugColor;
        private static readonly SolidColorBrush s_fatalColor;

        static LogEntry()
        {
            // 色盲友好配色（Wong 2011）。注意：需用完全限定名 System.Windows.Media.Color
            // 避免与本类实例属性 Color（SolidColorBrush 类型）冲突导致 CS0120。
            s_defaultColor = CreateFrozenBrush(Colors.Black);                                          // Verbose/其他 #000000
            s_debugColor = CreateFrozenBrush(System.Windows.Media.Color.FromRgb(0x00, 0x72, 0xB2));    // Debug #0072B2
            s_infoColor = CreateFrozenBrush(System.Windows.Media.Color.FromRgb(0x00, 0x9E, 0x73));     // Info #009E73
            s_warningColor = CreateFrozenBrush(System.Windows.Media.Color.FromRgb(0xE6, 0x9F, 0x00));  // Warning #E69F00
            s_errorColor = CreateFrozenBrush(System.Windows.Media.Color.FromRgb(0xD5, 0x5E, 0x00));    // Error #D55E00
            s_fatalColor = CreateFrozenBrush(System.Windows.Media.Color.FromRgb(0xCC, 0x79, 0xA7));     // Fatal #CC79A7
        }

        /// <summary>
        /// M333b: 创建冻结的 SolidColorBrush，提高 WPF 渲染性能
        /// </summary>
        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>日志ID（来自数据库自增主键）</summary>
        public long Id { get; set; }

        /// <summary>日志时间戳</summary>
        public DateTime Timestamp { get; set; }

        // L282: 预缓存时间格式化文本，避免 FilterLog 每次重新计算
        private string? _timestampText;
        /// <summary>时间格式化文本（yyyy-MM-dd HH:mm:ss.fff），用于过滤和显示
        /// M331b: 使用 Interlocked.CompareExchange 保护懒加载，确保多线程下不会重复计算
        /// </summary>
        public string TimestampText
        {
            get
            {
                if (_timestampText is null)
                {
                    var value = Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    Interlocked.CompareExchange(ref _timestampText, value, null);
                }
                return _timestampText;
            }
        }

        /// <summary>日志级别（INFO/WARNING/ERROR/DEBUG）</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>日志消息内容</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 异常信息字符串
        /// M288b: Serilog SQLite Sink 表中 Exception 列允许 NULL，故属性为 nullable；
        /// 由 NormalizeLogEntry 在加载后 ??= string.Empty 兜底；UI 直接创建的 LogEntry 默认为 string.Empty
        /// </summary>
        public string? Exception { get; set; } = string.Empty;

        /// <summary>
        /// 格式化后的异常堆栈文本：第一行原样保留，后续行缩进 4 空格，便于阅读。
        /// 为空时返回空字符串。
        /// </summary>
        public string FormattedException
        {
            get
            {
                if (string.IsNullOrEmpty(Exception)) return string.Empty;
                // 按换行符分割，每行前加空格缩进，让堆栈更易读；?? 兜底以消除 nullable 警告
                var lines = (Exception ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                return string.Join(Environment.NewLine, lines.Select((line, i) =>
                    i == 0 ? line : "    " + line));  // 第一行不缩进，后续行缩进
            }
        }

        /// <summary>Serilog 渲染后的消息模板</summary>
        public string RenderedMessage { get; set; } = string.Empty;

        /// <summary>
        /// 日志附加属性 JSON
        /// M288b: Serilog SQLite Sink 表中 Properties 列允许 NULL，故属性为 nullable；
        /// 由 NormalizeLogEntry 在加载后 ??= string.Empty 兜底
        /// </summary>
        public string? Properties { get; set; } = string.Empty;

        /// <summary>根据日志级别自动设置的显示颜色（用于 UI 列表着色）</summary>
        // L334: setter 改为 internal，仅允许同程序集内通过 GetColorByLevel 设置
        public SolidColorBrush Color { get; internal set; } = s_defaultColor;

        /// <summary>
        /// 级别单字母显示（用于色盲友好的徽章标签）：V/D/I/W/E/F
        /// 兼容大小写与常见缩写，默认取首字母大写。
        /// </summary>
        public string LevelDisplay
        {
            get
            {
                var n = Level.ToUpperInvariant().Trim();
                return n switch
                {
                    "VERBOSE" or "VRB" => "V",
                    "DEBUG" or "DBG" => "D",
                    "INFORMATION" or "INFO" or "INF" => "I",
                    "WARNING" or "WARN" or "WRN" => "W",
                    "ERROR" or "ERR" => "E",
                    "FATAL" or "FTL" => "F",
                    _ => n.Length > 0 ? n[..1] : "?"
                };
            }
        }

        /// <summary>
        /// 级别完整名称（含中文说明），用于徽章 ToolTip：
        /// Verbose 详细 / Debug 调试 / Information 信息 / Warning 警告 / Error 错误 / Fatal 致命
        /// </summary>
        public string LevelFullName
        {
            get
            {
                var n = Level.ToUpperInvariant().Trim();
                return n switch
                {
                    "VERBOSE" or "VRB" => "Verbose 详细",
                    "DEBUG" or "DBG" => "Debug 调试",
                    "INFORMATION" or "INFO" or "INF" => "Information 信息",
                    "WARNING" or "WARN" or "WRN" => "Warning 警告",
                    "ERROR" or "ERR" => "Error 错误",
                    "FATAL" or "FTL" => "Fatal 致命",
                    _ => Level
                };
            }
        }

        /// <summary>默认构造函数，时间戳为当前本地时间</summary>
        public LogEntry()
        {
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// 带级别和消息的构造函数，自动根据级别设置显示颜色
        /// </summary>
        /// <param name="level">日志级别</param>
        /// <param name="message">日志消息</param>
        public LogEntry(string level, string message) : this()
        {
            Level = level;
            Message = message;
            RenderedMessage = message;
            Color = GetColorByLevel(level); // L41: 调用静态方法
        }

        /// <summary>
        /// 根据日志级别返回对应的显示颜色（色盲友好配色 Wong 2011）：
        /// Verbose=黑色, DEBUG=蓝色, INFO=绿色, WARNING=橙色, ERROR=朱红色, FATAL=紫红色, 其他=黑色
        /// L41: 改为静态方法，避免仅为取颜色而创建完整 LogEntry 实例
        /// M333b: 返回缓存的静态冻结画刷，避免每次调用都创建新画刷
        /// </summary>
        public static SolidColorBrush GetColorByLevel(string level)
        {
            // M25: 使用 ToUpperInvariant 避免土耳其语等文化下 "i" → "İ" 导致匹配失败
            return level.ToUpperInvariant() switch
            {
                "VERBOSE" or "VRB" => s_defaultColor,
                "DEBUG" or "DBG" => s_debugColor,
                "INFORMATION" or "INFO" or "INF" => s_infoColor,
                "WARNING" or "WARN" or "WRN" => s_warningColor,
                "ERROR" or "ERR" => s_errorColor,
                "FATAL" or "FTL" => s_fatalColor,
                _ => s_defaultColor
            };
        }
    }
}