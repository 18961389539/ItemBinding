using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MainAPP.Models;
using Microsoft.EntityFrameworkCore;

namespace MainAPP.Services.AI
{
    /// <summary>单条文档检索命中。</summary>
    public sealed record KnowledgeHit(string Source, string Text, double Score);

    /// <summary>
    /// 知识库检索（RAG 的 R，2026-09-12）。
    ///
    /// <para>语料：文档（md/txt/docx，递归扫描、按扩展名与目录白名单过滤）+ 历史日志（Logs.db）。</para>
    ///
    /// <para>检索用<b>简化 BM25</b>（倒排索引 + idf 加权的 tf 饱和），不引入嵌入模型——
    /// 语料只有个位数文档，关键词检索已足够且零新依赖；中文用 2-gram 分词
    /// （"曝光" 命中 "曝光时间"）。将来语义检索可让侧车加载嵌入模型升级。</para>
    ///
    /// <para>docx 解析用轻量 zip + XML 提取（word/document.xml 的 w:t 文本），
    /// 不引入 OpenXML SDK 重依赖。索引懒构建，30 分钟 TTL 自动重建（文档可能更新）。</para>
    /// </summary>
    public static class KnowledgeBase
    {
        private sealed record Chunk(string Source, string Text);

        private sealed record IndexedChunk(Chunk Chunk, Dictionary<string, int> TermFreq);

        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static List<Chunk>? _chunks;
        private static List<IndexedChunk>? _blocks;
        private static Dictionary<string, List<int>>? _inverted;
        private static DateTime _builtAtUtc = DateTime.MinValue;

        /// <summary>索引有效期。文档可能被更新，超时后自动重建。</summary>
        private static readonly TimeSpan IndexTtl = TimeSpan.FromMinutes(30);

        /// <summary>
        /// 强制下次检索重建索引（AI 写入新案例后调用——索引 TTL 30 分钟太长，
        /// 新案例必须立即可检索）。线程安全：仅置空引用，重建由 Gate 串行化。
        /// </summary>
        public static void InvalidateIndex()
        {
            _chunks = null;
            _blocks = null;
            _inverted = null;
            _builtAtUtc = DateTime.MinValue;
        }

        /// <summary>检索文档语料，返回最相关的 topK 个片段。</summary>
        public static async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
            string query, int topK = 4, CancellationToken cancellationToken = default)
        {
            await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
            if (_blocks is null || _inverted is null || _blocks.Count == 0)
            {
                return Array.Empty<KnowledgeHit>();
            }

            var terms = Tokenize(query).Distinct().ToList();
            if (terms.Count == 0)
            {
                return Array.Empty<KnowledgeHit>();
            }

            var total = _blocks.Count;
            var scores = new Dictionary<int, double>();

            foreach (var term in terms)
            {
                if (!_inverted.TryGetValue(term, out var hits))
                {
                    continue;
                }

                // idf：越稀有的词权重越高；tf 饱和防止长块刷分
                var idf = Math.Log(1.0 + (double)total / hits.Count);
                foreach (var index in hits)
                {
                    var tf = _blocks[index].TermFreq[term];
                    var contribution = idf * (double)tf / (tf + 1.2);
                    scores[index] = scores.GetValueOrDefault(index) + contribution;
                }
            }

            return scores
                .OrderByDescending(kv => kv.Value)
                .Take(topK)
                .Select(kv => new KnowledgeHit(_chunks![kv.Key].Source, _chunks[kv.Key].Text, kv.Value))
                .Where(h => h.Score >= 0.4) // 阈值：分数过低说明只是零星词碰撞，宁可如实说没找到
                .ToList();
        }

        /// <summary>
        /// 检索历史日志（Logs.db）：按关键词匹配 RenderedMessage/Exception，取最近 limit 条。
        /// </summary>
        public static async Task<IReadOnlyList<string>> SearchLogsAsync(
            string keyword, int hours, int limit, CancellationToken cancellationToken = default)
        {
            var since = DateTime.Now.AddHours(-Math.Max(1, hours));
            var kw = (keyword ?? string.Empty).Trim();

            await using var db = new LogDbContext();
            var query = db.Logs.AsNoTracking().Where(l => l.Timestamp >= since);

            if (kw.Length > 0)
            {
                // 关键词为空时不过滤，等价于「最近有哪些日志」
                query = query.Where(l =>
                    l.RenderedMessage.Contains(kw) ||
                    (l.Exception != null && l.Exception.Contains(kw)) ||
                    l.Level.Contains(kw));
            }

            var rows = await query
                .OrderByDescending(l => l.Id)
                .Take(Math.Clamp(limit <= 0 ? 15 : limit, 1, 60))
                .Select(l => new { l.Timestamp, l.Level, l.RenderedMessage })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows
                .Select(r => $"[{r.Timestamp:MM-dd HH:mm:ss}] [{r.Level}] {r.RenderedMessage}")
                .ToList();
        }

        // ─────────────────────────────────────────────────────────────
        // 索引构建
        // ─────────────────────────────────────────────────────────────

        private static async Task EnsureIndexAsync(CancellationToken cancellationToken)
        {
            if (_blocks is not null && DateTime.UtcNow - _builtAtUtc < IndexTtl)
            {
                return;
            }

            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_blocks is not null && DateTime.UtcNow - _builtAtUtc < IndexTtl)
                {
                    return;
                }

                var chunks = new List<Chunk>();
                foreach (var file in EnumerateDocuments())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var text = await ReadTextAsync(file, cancellationToken).ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        var source = Path.GetFileName(file);
                        foreach (var piece in ChunkText(text))
                        {
                            chunks.Add(new Chunk(source, piece));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning($"[RAG] 语料读取失败 {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                var blocks = new List<IndexedChunk>(chunks.Count);
                var inverted = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < chunks.Count; i++)
                {
                    var tf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var term in Tokenize(chunks[i].Text))
                    {
                        tf[term] = tf.GetValueOrDefault(term) + 1;
                    }

                    blocks.Add(new IndexedChunk(chunks[i], tf));
                    foreach (var term in tf.Keys)
                    {
                        if (!inverted.TryGetValue(term, out var list))
                        {
                            inverted[term] = list = new List<int>();
                        }
                        list.Add(i);
                    }
                }

                _chunks = chunks;
                _blocks = blocks;
                _inverted = inverted;
                _builtAtUtc = DateTime.UtcNow;
                LogService.Instance.Info($"[RAG] 知识索引已构建：{chunks.Count} 个片段（{blocks.Count} 块）");
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// 枚举语料文件：根目录默认取 AiSettings.KnowledgeDocsFolder（工作区根，覆盖 docs/ 两份
        /// 手册与根目录的多份说明文档），递归但按目录黑名单剪枝、按扩展名白名单过滤。
        /// </summary>
        private static IEnumerable<string> EnumerateDocuments()
        {
            // 多根扫描：文档目录（配置）+ 应用数据案例目录（AI 写入的异常案例库）
            var roots = new List<string>
            {
                MainAPP.Models.Settings.Instance.Ai.KnowledgeDocsFolder,
                Path.Combine(AppContext.BaseDirectory, "Saves", "Knowledge"),
            };

            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bin", "obj", ".git", ".workbuddy", "node_modules",
                "BenchmarkDotNet.Artifacts", "Models", "ScottPlot5", "ImageViewerControl",
            };
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".txt", ".docx" };

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Walk(root, excluded))
                {
                    if (extensions.Contains(Path.GetExtension(file)) &&
                        new FileInfo(file).Length is > 0 and < 5 * 1024 * 1024)
                    {
                        yield return file;
                    }
                }
            }
        }

        private static IEnumerable<string> Walk(string dir, HashSet<string> excluded)
        {
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                string[] subDirs;
                string[] files;
                try
                {
                    subDirs = Directory.GetDirectories(current);
                    files = Directory.GetFiles(current);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var f in files)
                {
                    yield return f;
                }

                foreach (var d in subDirs)
                {
                    if (!excluded.Contains(Path.GetFileName(d)))
                    {
                        stack.Push(d);
                    }
                }
            }
        }

        /// <summary>
        /// docx 是 zip 容器，正文在 word/document.xml：按 w:p 分段、提取 w:t 文本。
        /// 轻量实现，不引入 OpenXML SDK（本场景只需要正文纯文本）。
        /// </summary>
        private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".docx")
            {
                return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            }

            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null)
            {
                return string.Empty;
            }

            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var xml = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var sb = new StringBuilder();
            foreach (Match para in Regex.Matches(xml, "<w:p[ >].*?</w:p>", RegexOptions.Singleline))
            {
                var line = string.Concat(
                    Regex.Matches(para.Value, "<w:t[^>]*>(.*?)</w:t>", RegexOptions.Singleline)
                        .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)));
                if (line.Trim().Length > 0)
                {
                    sb.AppendLine(line.Trim());
                }
            }

            return sb.ToString();
        }

        /// <summary>按段落聚合切块：目标 500 字/块，标题行（# 开头）强制起新块，保证语义完整。</summary>
        private static IEnumerable<string> ChunkText(string text)
        {
            var buffer = new StringBuilder();
            foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var isHeading = line.StartsWith('#');
                if (buffer.Length > 0 && (isHeading || buffer.Length >= 500))
                {
                    yield return buffer.ToString().Trim();
                    buffer.Clear();
                }

                buffer.AppendLine(line);
            }

            if (buffer.Length > 0)
            {
                yield return buffer.ToString().Trim();
            }
        }

        /// <summary>
        /// 分词：ASCII 词按 [a-z0-9_]+；中文连续段做 2-gram
        /// （"曝光时间" → 曝光/光时 —— 查询词"曝光"可命中）。
        /// </summary>
        internal static IEnumerable<string> Tokenize(string text)
        {
            foreach (Match m in Regex.Matches(text.ToLowerInvariant(), "[a-z0-9_]+"))
            {
                if (m.Value.Length > 1)
                {
                    yield return m.Value;
                }
            }

            foreach (Match m in Regex.Matches(text, "[\u4e00-\u9fff]+"))
            {
                var run = m.Value;
                if (run.Length == 1)
                {
                    yield return run;
                    continue;
                }

                for (var i = 0; i + 1 < run.Length; i++)
                {
                    yield return run.Substring(i, 2);
                }
            }
        }
    }
}
