using System;
using System.IO;
using System.Text;

namespace MainAPP.Services
{
    /// <summary>
    /// 统一数据根目录解析。
    ///
    /// 背景（2026-09-15）：此前所有数据都写在可执行文件目录下
    /// （<c>AppContext.BaseDirectory</c>），带来三个问题——装到 Program Files 会因 UAC 失败；
    /// 重新发布若清理目录会连数据一起丢；授权文件却单独放在 <c>%LOCALAPPDATA%</c>
    /// （按用户隔离）而业务数据按机器共享，导致"换 Windows 账户就要求重新激活"。
    ///
    /// 解析顺序（一旦选定即固定，不做运行期切换）：
    /// <list type="number">
    ///   <item><c>D:\ProgramData\ItemBinding\</c>——需为就绪的本地固定磁盘且可写；</item>
    ///   <item><c>C:\ProgramData\ItemBinding\</c>——同上；</item>
    ///   <item>可执行文件目录——兜底，保证任何环境都能启动。</item>
    /// </list>
    ///
    /// 约束：<b>不修改系统 <c>%ProgramData%</c></b>，不写注册表，不改任何系统配置。
    /// 这里只是把 <c>D:\ProgramData\ItemBinding</c> 当作一个普通目录使用——
    /// 目录名沿用 ProgramData 只是为了符合既有命名习惯，不代表任何系统语义。
    ///
    /// 注意：<c>Models\best.onnx</c> 与 <c>cuda_runtime_dlls\</c> 属于随包分发的部署资产，
    /// 仍走可执行文件目录，<b>不</b>纳入数据根。
    /// </summary>
    public static class DataPaths
    {
        /// <summary>数据根下的应用子目录名。</summary>
        private const string AppFolderName = "ItemBinding";

        /// <summary>优先使用的盘符（无该盘时回退 C:）。</summary>
        private const string PreferredDrive = "D";

        /// <summary>
        /// 显式指定数据根的环境变量。部署脚本可用它把数据指到任意目录；
        /// 集成测试用它把数据根隔离到测试目录，避免误操作真实数据。
        /// 优先级最高。
        /// </summary>
        public const string DataRootEnvironmentVariable = "ITEMBINDING_DATA_DIR";

        private static readonly object Sync = new();
        private static string? _root;

        /// <summary>数据根解析过程说明，供日志初始化后回填输出。</summary>
        public static string ResolutionNote { get; private set; } = string.Empty;

        /// <summary>
        /// 数据根目录（绝对路径）。首次访问时解析并确保根目录存在且可写。
        /// </summary>
        public static string Root
        {
            get
            {
                if (_root is not null)
                {
                    return _root;
                }

                lock (Sync)
                {
                    _root ??= Resolve();
                    return _root;
                }
            }
        }

        // ───────────────────────── 数据子路径 ─────────────────────────
        // 均为计算属性（非静态字段），保证在 Root 解析完成后才取值，避免静态初始化顺序问题。

        /// <summary><c>Saves</c> 根目录。</summary>
        public static string Saves => Path.Combine(Root, "Saves");

        /// <summary>
        /// 数据库目录（<c>barcode_data.db</c> 与 <c>Logs.db</c> 同目录）。
        /// 2026-09-15 布局统一：业务库原先单独放在数据根的 <c>DataBase\</c>，
        /// 与日志库所在的 <c>Saves\DataBase\</c> 分层不一致，排查和备份时容易找错；现统一到 <c>Saves\DataBase\</c>。
        /// </summary>
        public static string DatabaseDir => Path.Combine(Saves, "DataBase");

        /// <summary>主业务库文件（条码/检测记录）。</summary>
        public static string BarcodeDatabase => Path.Combine(DatabaseDir, "barcode_data.db");

        /// <summary>日志库文件（Serilog SQLite Sink）。</summary>
        public static string LogDatabase => Path.Combine(DatabaseDir, "Logs.db");

        /// <summary>
        /// 旧布局下的业务库目录（数据根下直接放 <c>DataBase\</c>）。
        /// 仅供 <see cref="DataRootMigrator"/> 做一次性布局归一，业务代码不要使用。
        /// </summary>
        public static string ObsoleteDatabaseDir => Path.Combine(Root, "DataBase");

        /// <summary>配方目录。</summary>
        public static string RecipesDir => Path.Combine(Saves, "Recipes");

        /// <summary>账号目录（<c>users.json</c>）。</summary>
        public static string SecurityDir => Path.Combine(Saves, "Security");

        /// <summary>设置目录。</summary>
        public static string SettingsDir => Path.Combine(Saves, "Settings");

        /// <summary>设置文件。</summary>
        public static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

        /// <summary>检测图片默认保存目录（可被设置项覆盖）。</summary>
        public static string PicturesDir => Path.Combine(Saves, "Pictures");

        /// <summary>日报输出目录。</summary>
        public static string ReportsDir => Path.Combine(Saves, "Reports");

        /// <summary>AI 知识库目录。</summary>
        public static string KnowledgeDir => Path.Combine(Saves, "Knowledge");

        /// <summary>AI 写入的异常案例库目录。</summary>
        public static string KnowledgeCasesDir => Path.Combine(KnowledgeDir, "Cases");

        /// <summary>临时日志目录。</summary>
        public static string TempLogsDir => Path.Combine(Saves, "temp-logs");

        /// <summary>运行日志（txt）目录。</summary>
        public static string LogsDir => Path.Combine(Root, "logs");

        /// <summary>授权文件（与业务数据同根，消除作用域错配）。</summary>
        public static string LicenseFile => Path.Combine(Root, "license.txt");

        /// <summary>
        /// 旧版授权文件位置（<c>%LOCALAPPDATA%\ItemBinding\license.txt</c>）。
        /// 仅作为读取兼容回退：老机器上已激活的文件仍能被识别，不做写入。
        /// </summary>
        public static string LegacyLicenseFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ItemBinding",
            "license.txt");

        /// <summary>旧版数据根（可执行文件目录），供迁移逻辑使用。</summary>
        public static string LegacyRoot => AppContext.BaseDirectory;

        // ───────────────────────── 解析 ─────────────────────────

        private static string Resolve()
        {
            var note = new StringBuilder();

            // 0) 显式覆盖：部署脚本指定目录，或集成测试隔离数据根（优先级最高）
            var overridden = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                var candidate = overridden.Trim();
                if (TryEnsureWritable(candidate, out var overrideReason))
                {
                    _root = Path.GetFullPath(candidate);
                    note.Append($"数据根 = {_root}（由 {DataRootEnvironmentVariable} 指定）");
                    ResolutionNote = note.ToString();
                    return _root;
                }

                note.Append($"{DataRootEnvironmentVariable} 指定的路径不可写 → {overrideReason}；");
            }

            // 1) 优先盘符（D:\ProgramData\ItemBinding）——要求本地固定磁盘
            if (TryPrepareProgramDataRoot(PreferredDrive, out var note1))
            {
                note.Append($"数据根 = {_root}（{PreferredDrive}: 可用）");
                ResolutionNote = note.ToString();
                return _root!;
            }

            note.Append($"{PreferredDrive}: 不可用 → {note1}；");

            // 2) 回退系统盘 ProgramData（同样只是普通目录，不依赖 %ProgramData% 环境变量）
            if (TryPrepareProgramDataRoot("C", out var note2))
            {
                note.Append($"已回退 {_root}");
                ResolutionNote = note.ToString();
                return _root!;
            }

            note.Append($"C: 不可用 → {note2}；");

            // 3) 兜底：可执行文件目录（不要求固定磁盘，网络盘/只读环境也能给出结论）
            var fallback = AppContext.BaseDirectory;
            if (TryEnsureWritable(fallback, out var note3))
            {
                _root = fallback;
                note.Append($"已兜底到可执行文件目录 {fallback}");
            }
            else
            {
                // 连程序目录都写不了，仍然返回它——上层会在写文件时抛出可读的异常，
                // 好过在这里抛 TypeInitializationException 让整个类型永久不可用。
                _root = fallback;
                note.Append($"可执行文件目录也不可写（{note3}），仍返回 {fallback}");
            }

            ResolutionNote = note.ToString();
            return _root!;
        }

        /// <summary>尝试准备的 <c>&lt;盘符&gt;:\ProgramData\ItemBinding</c> 数据根。</summary>
        private static bool TryPrepareProgramDataRoot(string driveLetter, out string reason)
        {
            var candidate = Path.Combine($"{driveLetter}:\\", "ProgramData", AppFolderName);

            var pathRoot = Path.GetPathRoot(Path.GetFullPath(candidate));
            if (string.IsNullOrEmpty(pathRoot))
            {
                reason = "无法解析盘符";
                return false;
            }

            try
            {
                var drive = new DriveInfo(pathRoot);
                if (!drive.IsReady)
                {
                    reason = "卷未就绪";
                    return false;
                }

                if (drive.DriveType != DriveType.Fixed)
                {
                    reason = $"非本地固定磁盘（{drive.DriveType}）";
                    return false;
                }
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }

            if (!TryEnsureWritable(candidate, out reason))
            {
                return false;
            }

            _root = candidate;
            return true;
        }

        /// <summary>确保目录存在且可写（用一次探针写入验证真实写权限，而非只看属性）。</summary>
        private static bool TryEnsureWritable(string directory, out string reason)
        {
            try
            {
                Directory.CreateDirectory(directory);

                // 目录已存在不足以说明可写（ProgramData 的属主陷阱会导致只读），实际写一次
                var probe = Path.Combine(directory, ".write-probe");
                File.WriteAllText(probe, "probe");
                File.Delete(probe);

                reason = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }
}
