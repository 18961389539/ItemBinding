using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using MainAPP.Services;

namespace MainAPP.ViewModels
{
    /// <summary>
    /// 关于页面的只读 ViewModel，展示应用版本、构建日期等信息。
    /// M52: 所有属性为只读，移除不必要的 setter（AboutViewModel 数据在构造时确定，无需外部修改）。
    /// P2-25: 补充依赖许可信息、复制信息、检查更新命令
    /// </summary>
    public class AboutViewModel
    {
        public string AppTitle { get; } = "物码绑定系统";
        public string Version { get; } = GetAssemblyVersion();
        // L66: 版权年份动态使用当前年份，避免跨年后信息过期
        public string Copyright { get; } = $"Copyright © {DateTime.Now.Year}";
        public string Description { get; } = "用于物码绑定的自动化系统，实现二维码识别、数据记录和配方管理。";
        public string Author { get; } = "技术团队";
        public string Company { get; } = "—";
        public string BuildDate { get; } = GetBuildDate();
        /// <summary>用于数据可视化卡片的短构建日期（仅日期部分）</summary>
        public string BuildDateShort => DateTime.TryParse(BuildDate, out var d) ? d.ToString("yyyy-MM-dd") : BuildDate;

        /// <summary>
        /// 主要开源依赖列表（显示用）。版本号在运行时动态获取，失败时仅显示库名。
        /// </summary>
        public string Dependencies { get; } = GetDependencies();

        // 系统信息
        public string OsVersion { get; } = Environment.OSVersion.VersionString;
        public string RuntimeVersion { get; } = RuntimeInformation.FrameworkDescription;
        public string MachineName { get; } = Environment.MachineName;
        public int ProcessorCount { get; } = Environment.ProcessorCount;
        public string AppStartTime { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        /// <summary>当前托管内存使用量（展示启动时快照，不实时刷新）</summary>
        public string MemorySize => $"{GC.GetTotalMemory(false) / 1024 / 1024} MB";

        // 许可证信息
        public string LicenseInfo { get; } = "本软件为授权用户使用，未经许可不得复制、传播或修改。";
        public string LicenseType { get; } = "工业版授权";
        public string LicenseExpiry { get; } = "永久授权";

        // 技术支持联系信息（统一显示）
        public string SupportPhone { get; } = "18961389539";
        public string SupportEmail { get; } = "support@example.com";
        public string SupportHours { get; } = "工作日 9:00-18:00";

        /// <summary>复制全部信息到剪贴板</summary>
        public IRelayCommand CopyInfoCommand { get; }

        /// <summary>检查更新（提示用户联系技术支持）</summary>
        public IRelayCommand CheckUpdateCommand { get; }

        public AboutViewModel()
        {
            CopyInfoCommand = new RelayCommand(CopyInfo);
            CheckUpdateCommand = new RelayCommand(CheckUpdate);
        }

        private void CopyInfo()
        {
            // P2-25: 将全部关于信息格式化为文本并复制到剪贴板，便于用户提交技术支持
            var sb = new StringBuilder();
            sb.AppendLine($"应用: {AppTitle}");
            sb.AppendLine($"版本: {Version}");
            sb.AppendLine($"构建日期: {BuildDate}");
            sb.AppendLine($"版权: {Copyright}");
            sb.AppendLine($"作者: {Author}");
            sb.AppendLine($"公司: {Company}");
            sb.AppendLine($"描述: {Description}");
            sb.AppendLine("主要依赖:");
            sb.AppendLine(Dependencies);
            sb.AppendLine("系统信息:");
            sb.AppendLine($"  操作系统: {OsVersion}");
            sb.AppendLine($"  运行时: {RuntimeVersion}");
            sb.AppendLine($"  计算机名: {MachineName}");
            sb.AppendLine($"  CPU 核心数: {ProcessorCount}");
            sb.AppendLine($"  启动时间: {AppStartTime}");
            sb.AppendLine($"  内存使用: {MemorySize}");
            sb.AppendLine($"授权类型: {LicenseType}");
            sb.AppendLine($"授权期限: {LicenseExpiry}");
            sb.AppendLine($"服务热线: {SupportPhone}");
            sb.AppendLine($"服务邮箱: {SupportEmail}");
            sb.AppendLine($"服务时间: {SupportHours}");
            try
            {
                Clipboard.SetText(sb.ToString());
                NotificationService.Success("信息已复制到剪贴板");
            }
            catch (Exception ex)
            {
                NotificationService.Error($"复制失败: {ex.Message}");
            }
        }

        private void CheckUpdate()
        {
            // 本系统暂不支持在线自动更新，提示用户联系技术支持获取最新版本
            NotificationService.Info(
                $"当前版本: {Version}\n构建日期: {BuildDate}\n\n" +
                $"本系统暂不支持在线自动更新，请联系技术支持获取最新版本。\n电话: {SupportPhone}");
        }

        private static string GetAssemblyVersion()
        {
            var assembly = Assembly.GetEntryAssembly();
            var version = assembly?.GetName().Version;
            return version?.ToString() ?? "1.0.0";
        }

        private static string GetBuildDate()
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly == null) return "未知";
            // L38: assembly.Location 在某些单文件发布(.NET single-file)场景下可能为空字符串
            var location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !System.IO.File.Exists(location))
            {
                return "未知";
            }
            // L72: 直接使用 File.GetLastWriteTime，避免创建多余的 FileInfo 对象
            return System.IO.File.GetLastWriteTime(location).ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 枚举本应用引用的主要 OSS 库及版本号。
        /// 仅展示在 UI 上，版本号取自已加载程序集；未加载的库不会出现。
        /// </summary>
        private static string GetDependencies()
        {
            var sb = new StringBuilder();
            // 关键 OSS 依赖的 SimpleName 列表
            var interesting = new[]
            {
                "CommunityToolkit.Mvvm",
                "HandyControl",
                "MahApps.Metro",
                "ScottPlot",
                "SixLabors.ImageSharp",
                "Microsoft.ML.OnnxRuntime",
                "OpenCvSharp",
                "Serilog",
                "HikScanner",
                "JinlongYolo",
            };

            // 一次性获取已加载程序集，避免多次 AppDomain.GetAssemblies 调用
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name is not null)
                .GroupBy(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var name in interesting)
            {
                if (loaded.TryGetValue(name, out var asm))
                {
                    var ver = asm.GetName().Version;
                    sb.AppendLine($"  • {name} v{ver}");
                }
                else
                {
                    sb.AppendLine($"  • {name} (运行时未加载)");
                }
            }
            return sb.ToString();
        }
    }
}
