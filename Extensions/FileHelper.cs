using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace Extensions
{
    /// <summary>
    /// 文件操作辅助类。提供文件清理、安全删除、批量重命名等实用方法。
    /// </summary>
    public static class FileHelper
    {
        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="days"></param>
        /// <param name="folder"></param>
        public static void DeleteOldFiles(int days, string folder)
        {
            if (days <= 0)
            {
                return;
            }

            // 如果文件夹路径不是绝对路径，则基于应用程序目录组合
            string fullFolderPath = Path.IsPathRooted(folder)
                ? folder
                : Path.Combine(AppContext.BaseDirectory, folder);

            // 3. 检查文件夹是否存在
            if (!Directory.Exists(fullFolderPath))
            {
                return;
            }

            // L105: 使用 Now 与 LastWriteTime，保持时间一致
            DateTime cutoffDate = DateTime.Now.AddDays(-days);
            int deletedCount = 0;
            long deletedSize = 0;
            var directoryInfo = new DirectoryInfo(fullFolderPath);
            // M176: 使用 EnumerationOptions.IgnoreInaccessible 避免无权限子目录抛出异常中止整个清理
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
            };
            // M137: 使用 EnumerateFiles 延迟枚举，避免一次性加载所有文件信息
            // L395b: 使用 "*" 匹配所有文件，"*.*" 在某些系统上仅匹配含扩展名的文件
            // M348a: 包裹 try-catch，避免 EnumerateFiles 在枚举过程中遇到不可访问的子目录抛出
            // 异常中止整个清理，已删除的数量仍返回，最大化清理效果
            try
            {
                foreach (var file in directoryInfo.EnumerateFiles("*", enumerationOptions))
                {
                    // L385a: 使用 LastWriteTime（最后修改时间）替代 CreationTime，更准确反映文件是否为旧文件
                    if (file.LastWriteTime < cutoffDate)
                    {
                        // M130: 单文件删除失败不应中止整个清理
                        try
                        {
                            // H85c: file.Length 可能抛出异常（如文件被并发删除），移入 try 块内
                            long fileSize = file.Length;
                            file.Delete();
                            deletedCount++;
                            deletedSize += fileSize;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine($"删除文件失败: {file.FullName}, {ex}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // M348a: 枚举过程中异常（如子目录访问被拒），记录 Trace 并返回已删除的数量
                System.Diagnostics.Trace.WriteLine($"EnumerateFiles 枚举失败，已删除 {deletedCount} 个文件: {ex}");
            }
            // L104: 记录清理结果（Extensions 项目无法访问 LogService，使用 Trace 输出）
            // L418b: 同时输出 MB 单位，便于直观查看清理量
            System.Diagnostics.Trace.WriteLine($"清理旧文件完成: 已删除 {deletedCount} 个文件，释放 {deletedSize} 字节（{deletedSize / (1024.0 * 1024.0):F2} MB），目录: {fullFolderPath}");
        }

        /// <summary>
        /// 安全删除文件，支持重试机制
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="maxRetries">最大重试次数（默认3次）</param>
        /// <param name="retryDelayMs">重试延迟毫秒数（默认100ms）</param>
        /// <returns>是否删除成功</returns>
        /// <remarks>
        /// L374a: 重试时使用 Thread.Sleep 阻塞调用线程，不应在 UI 线程调用，否则会导致界面卡顿。
        /// 如需在 UI 线程使用，应通过 Task.Run 等方式在线程池中执行。
        /// </remarks>
        public static bool SafeDelete(string filePath, int maxRetries = 3, int retryDelayMs = 100)
        {
            // M328d: 校验 retryDelayMs 非负，避免 Thread.Sleep 传入负值抛 ArgumentOutOfRangeException
            if (retryDelayMs < 0) throw new ArgumentOutOfRangeException(nameof(retryDelayMs));
            // M349a: 校验 maxRetries 最小为 1，避免调用方传 0 或负值时完全不尝试删除直接返回 false
            if (maxRetries <= 0) maxRetries = 1;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    File.Delete(filePath);
                    return true;
                }
                // H53: 最后一次重试时也需捕获异常并返回 false，而非向上抛出（方法契约要求返回 bool）
                catch (IOException)
                {
                    if (i >= maxRetries - 1) return false;
                    Thread.Sleep(retryDelayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    if (i >= maxRetries - 1) return false;
                    Thread.Sleep(retryDelayMs);
                }
                // M270: 兜底捕获所有其它异常类型，避免未处理异常中止重试逻辑
                // M320b: 记录异常信息到 Trace，避免 catch-all 掩盖严重异常
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"SafeDelete 第 {i+1} 次重试失败: {filePath}, {ex}");
                    if (i >= maxRetries - 1) return false;
                    Thread.Sleep(retryDelayMs);
                }
            }
            return false;
        }

        /// <summary>
        /// 批量重命名文件
        /// </summary>
        /// <param name="directory">目录路径</param>
        /// <param name="searchPattern">搜索模式（默认 "*"）</param>
        /// <param name="newNamePattern">新文件名模式，可使用 {0} 表示原始文件名（不含扩展名），{1} 表示扩展名，{2} 表示序号</param>
        /// <param name="startNumber">起始序号（默认1）</param>
        /// <param name="recursive">是否递归子目录（默认false）</param>
        /// <returns>重命名的文件数量</returns>
        public static int BatchRename(string directory, string searchPattern = "*", string newNamePattern = "{0}_{2}{1}", int startNumber = 1, bool recursive = false)
        {
            // M336b: 校验 searchPattern 非空，避免 EnumerateFiles 内部抛 ArgumentNullException 难以定位
            ArgumentNullException.ThrowIfNull(searchPattern);
            // L396c: 校验 newNamePattern 非空，避免后续 string.Format 抛 ArgumentNullException 难以定位
            ArgumentNullException.ThrowIfNull(newNamePattern);
            if (!Directory.Exists(directory))
                return 0;

            // L275: 使用 EnumerateFiles 延迟枚举，配合 ToList 在重命名前快照文件列表，避免枚举期间修改目录导致跳过文件
            // M327a: 递归模式改用 EnumerationOptions 重载，设置 IgnoreInaccessible = true，避免无权限子目录抛出异常中止整个批处理
            var enumerationOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = recursive,
            };
            var files = Directory.EnumerateFiles(directory, searchPattern, enumerationOptions).ToList();
            int count = 0;
            for (int i = 0; i < files.Count; i++)
            {
                string originalFile = files[i];
                string originalName = Path.GetFileNameWithoutExtension(originalFile);
                string extension = Path.GetExtension(originalFile);
                int sequence = startNumber + i;
                string newFileName = string.Format(newNamePattern, originalName, extension, sequence);
                string? directoryName = Path.GetDirectoryName(originalFile);
                if (directoryName == null)
                    continue; // 跳过根目录文件（理论上不会发生）
                string newFilePath = Path.Combine(directoryName, newFileName);
                
                try
                {
                    // L354: 使用 overwrite:true 避免目标文件已存在时抛出 IOException
                    // M350a: 已知设计限制 - overwrite:true 会静默覆盖已存在的目标文件，
                    // 当 newNamePattern 计算结果与现有文件冲突时不会发出警告。
                    // 暂不改为 false（需保持现有调用方行为兼容），如需检测冲突需改为
                    // File.Exists 预检查并抛出或返回失败计数，改动较大需评估所有调用方影响。
                    File.Move(originalFile, newFilePath, overwrite: true);
                    count++;
                }
                catch (Exception ex)
                {
                    // L107: 捕获所有异常类型，避免其它异常（如 ArgumentException、FileNotFoundException）中止整个批处理
                    System.Diagnostics.Trace.WriteLine($"重命名文件失败: {originalFile} -> {newFilePath}, {ex}");
                }
            }
            return count;
        }

    }
}
