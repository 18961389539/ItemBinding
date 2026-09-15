using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MainAPP.Services
{
    /// <summary>
    /// 数据根从"可执行文件目录"迁移到可配置数据根（见 <see cref="DataPaths"/>）时的一次性搬运。
    ///
    /// 为什么必须迁移：老部署的业务数据（数据库、配方、账号、日志）都在 exe 目录下。
    /// 若切换数据根后不做搬运，现场升级后打开程序会看到一个空库——历史记录凭空消失。
    ///
    /// 安全策略（宁慢勿丢）：
    /// <list type="number">
    ///   <item>只<b>复制</b>到新根，绝不删除原数据；</item>
    ///   <item>复制后校验文件数量一致，不一致视为失败；</item>
    ///   <item>校验通过才把原目录改名为 <c>&lt;名字&gt;.migrated</c>（同卷改名，不跨卷移动），
    ///         使旧位置不再被使用、同时抢救得回来；</item>
    ///   <item>任一步失败都保留原数据并记录原因，<b>不影响程序启动</b>。</item>
    /// </list>
    ///
    /// 幂等性：目标已存在同名目录且有文件时直接跳过；原目录改名后下次自然不再命中。
    /// </summary>
    public static class DataRootMigrator
    {
        /// <summary>
        /// 旧数据根下的一级目录 → 当前数据根下的目标子路径。
        /// <para>注意 <c>DataBase</c> 的映射不是同名的：2026-09-15 布局统一后，
        /// 业务库与日志库都放在 <c>Saves\DataBase\</c>，而旧布局中业务库在数据根下的 <c>DataBase\</c>。</para>
        /// </summary>
        private static readonly (string Name, string DestinationSubPath)[] ManagedEntries =
        {
            ("DataBase", Path.Combine("Saves", "DataBase")),
            ("Saves", "Saves"),
            ("logs", "logs"),
        };

        /// <summary>
        /// 按需把 exe 目录下的历史数据搬运到当前数据根。
        /// 必须在任何数据库/日志句柄打开之前调用（否则会拷到不一致的 SQLite 文件）。
        /// </summary>
        /// <returns>搬运结果说明；无需搬运时返回 <c>null</c>。</returns>
        public static string? MigrateIfNeeded() => MigrateIfNeeded(DataPaths.Root, DataPaths.LegacyRoot);

        /// <summary>
        /// 可注入路径的实现（便于单元测试用临时目录驱动，不触碰真实数据根）。
        /// </summary>
        /// <param name="target">目标数据根。</param>
        /// <param name="legacy">旧数据根（原可执行文件目录）。</param>
        internal static string? MigrateIfNeeded(string target, string legacy)
        {
            // 数据根就是 exe 目录（兜底分支），无所谓"跨根搬运"；但同根内的旧布局仍需归一
            if (IsSamePath(target, legacy))
            {
                return NormalizeInRootLayout(target);
            }

            var report = new StringBuilder();
            var migrated = 0;

            foreach (var (name, destinationSubPath) in ManagedEntries)
            {
                var source = Path.Combine(legacy, name);
                var destination = Path.Combine(target, destinationSubPath);

                if (!Directory.Exists(source))
                {
                    continue;
                }

                if (!HasAnyFile(source))
                {
                    continue;
                }

                try
                {
                    var sourceCount = CountFiles(source);

                    // 合并复制：只补目标缺失的文件，绝不覆盖目标已有文件。
                    // 不能用"目标已有文件就整目录跳过"——Saves 下同时有 DataBase/Recipes/Security，
                    // 若目标只剩一个残留的 Logs.db 就跳过整个 Saves，配方和账号会被漏搬。
                    var copied = MergeCopy(source, destination);

                    var missing = FindMissingFiles(source, destination);
                    if (missing.Count > 0)
                    {
                        throw new IOException($"复制后仍缺失 {missing.Count} 个文件，例如 {missing[0]}");
                    }

                    var retired = BuildRetiredPath(source);
                    Directory.Move(source, retired);

                    migrated++;
                    var detail = copied >= sourceCount
                        ? $"{sourceCount}个文件"
                        : $"{sourceCount}个文件（新增 {copied}，其余目标已存在故保留目标版本）";
                    report.Append($"{name}({detail}) → {destination}，原目录保留为 {Path.GetFileName(retired)}；");
                }
                catch (Exception ex)
                {
                    report.Append($"{name} 迁移失败：{ex.Message}（历史数据仍完整保留在 {source}）；");
                }
            }

            // 跨根搬运完成后，再统一目标根内可能残留的旧布局（DataBase\ → Saves\DataBase\）
            var layoutNote = NormalizeInRootLayout(target);
            if (!string.IsNullOrEmpty(layoutNote))
            {
                report.Append(layoutNote);
            }

            if (migrated == 0 && report.Length == 0)
            {
                return null;
            }

            var prefix = migrated > 0
                ? $"检测到 exe 目录下的历史数据，已搬运 {migrated} 项到新数据根："
                : "检测到 exe 目录下的历史数据，但未能搬运：";

            return prefix + report;
        }

        /// <summary>
        /// 归一数据根内的旧布局：把数据根下直接放的 <c>DataBase\</c> 合并进 <c>Saves\DataBase\</c>，
        /// 使业务库与日志库同目录（2026-09-15 布局统一）。
        /// 幂等：归一后旧目录被改名为 <c>DataBase.migrated</c>，下次不再命中。
        /// </summary>
        /// <returns>归一结果说明；无需归一或失败时返回 <c>null</c> 或错误说明。</returns>
        private static string? NormalizeInRootLayout(string root)
        {
            var obsolete = Path.Combine(root, "DataBase");
            var current = Path.Combine(root, "Saves", "DataBase");

            if (!Directory.Exists(obsolete) || !HasAnyFile(obsolete))
            {
                return null;
            }

            try
            {
                var copied = MergeCopy(obsolete, current);

                var missing = FindMissingFiles(obsolete, current);
                if (missing.Count > 0)
                {
                    throw new IOException($"合并后仍缺失 {missing.Count} 个文件，例如 {missing[0]}");
                }

                var retired = BuildRetiredPath(obsolete);
                Directory.Move(obsolete, retired);

                return $"旧布局 DataBase\\ 已合并到 Saves\\DataBase\\（新增 {copied} 个文件，原目录保留为 {Path.GetFileName(retired)}）；";
            }
            catch (Exception ex)
            {
                return $"旧布局 DataBase\\ 归一失败：{ex.Message}（数据库仍完整保留在 {obsolete}）；";
            }
        }

        private static bool IsSamePath(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>原目录改名目标；若已存在则追加时间戳，避免二次迁移时冲突。</summary>
        private static string BuildRetiredPath(string source)
        {
            var retired = source + ".migrated";
            if (!Directory.Exists(retired) && !File.Exists(retired))
            {
                return retired;
            }

            return $"{source}.migrated-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";
        }

        private static bool HasAnyFile(string directory)
        {
            try
            {
                return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();
            }
            catch (Exception)
            {
                // 枚举失败（权限/占用）时保守认为"有数据"，宁可不迁移也不误判为空
                return true;
            }
        }

        private static int CountFiles(string directory)
        {
            try
            {
                return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count();
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// 合并复制：递归复制源目录中目标尚不存在的文件，返回实际复制的文件数。
        /// 目标已有的同名文件一律保留（不覆盖），避免用旧数据盖掉新数据。
        /// </summary>
        private static int MergeCopy(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            var copied = 0;

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            {
                var targetFile = Path.Combine(destination, Path.GetFileName(file));
                if (File.Exists(targetFile))
                {
                    continue;
                }

                File.Copy(file, targetFile, overwrite: false);
                copied++;
            }

            foreach (var subDirectory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
            {
                copied += MergeCopy(subDirectory, Path.Combine(destination, Path.GetFileName(subDirectory)));
            }

            return copied;
        }

        /// <summary>校验源目录中每个文件在目标目录都存在，返回缺失文件的相对路径列表。</summary>
        private static IReadOnlyList<string> FindMissingFiles(string source, string destination)
        {
            var missing = new List<string>();

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (!File.Exists(Path.Combine(destination, relative)))
                {
                    missing.Add(relative);
                }
            }

            return missing;
        }
    }
}
