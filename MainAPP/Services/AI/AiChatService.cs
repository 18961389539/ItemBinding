using System.IO;
using MainAPP.Application;
using MainAPP.Models;
using Microsoft.Agents.AI;
using OpenAI;
using System.ClientModel;
using Microsoft.EntityFrameworkCore;
using MainAPP.ViewModels;
using Microsoft.Extensions.AI;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MainAPP.Services.AI
{
    /// <summary>
    /// 离线 AI 对话服务（2026-09-12 起迭代）。
    ///
    /// 推理组件全部来自开源生态，不自建轮子：
    ///   <list type="bullet">
    ///     <item><see cref="LlamaServerHost"/>：llama-server 侧车（llama.cpp 官方预编译，
    ///           OpenAI 兼容 HTTP）。进程内推理（LLamaSharp）已退役，原因见该类型类注释</item>
    ///     <item><c>OpenAIClient</c> + <c>ChatClientBuilder.UseFunctionInvocation()</c>：
    ///           把侧车的 OpenAI 协议适配成 <see cref="IChatClient"/> 并提供标准的工具调用循环</item>
    ///     <item><c>Microsoft.Agents.AI</c>（MAF）：Agent 抽象 + <see cref="AgentSession"/> 多轮会话状态 +
    ///           工具调用编排（Semantic Kernel 的继任者，2026-04 GA）</item>
    ///   </list>
    ///
    /// 因此换模型 = 换 ModelPath；换推理引擎 = 换 IChatClient 实现；业务代码完全不动。
    /// </summary>
    public sealed class AiChatService : IDisposable
    {
        /// <summary>
        /// 进程内单例（与 LogService / AuthService / BarcodeDataService 等既有模式一致）。
        /// AiWebHost 的 HTTP 端点与将来的 WPF 面板必须共用同一实例，
        /// 否则会各自加载一份模型、各占一份显存。
        /// </summary>
        public static AiChatService Instance { get; } = new();

        private readonly SemaphoreSlim _gate = new(1, 1);
        private HttpClient? _http;
        private IChatClient? _chatClient;
        private AIAgent? _agent;
        private AgentSession? _session;
        private bool _disposed;

        // 2026-09-12: 待确认的写操作。AI 只能「提议」写操作（工具调用只记录不执行），
        // 真正执行必须由人回复「确认」触发 —— AI 提议、人确认、C# 执行，三者分离。
        private PendingWriteAction? _pendingWrite;
        private readonly object _pendingWriteLock = new();

        /// <summary>待确认写操作的记录（描述 + 实际执行委托）。</summary>
        private sealed record PendingWriteAction(string Description, Func<Task<string>> ExecuteAsync);

        /// <summary>模型是否已加载（按需加载模式下，未打开面板时为 false）。</summary>

        public bool IsLoaded => _agent is not null;

        /// <summary>是否存在等待用户确认的写操作。</summary>
        public bool HasPendingWrite => _pendingWrite is not null;

        private static AiSettings Config => MainAPP.Models.Settings.Instance.Ai;

        private static bool Enabled => Config.Enabled;

        /// <summary>
        /// 确保模型已加载。未启用 AI 或已加载时直接返回。
        /// 失败会记录日志并抛出，由调用方决定是否提示用户。
        /// </summary>
        public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
        {
            if (!Enabled)
            {
                throw new InvalidOperationException("AI 对话未启用（Settings.Ai.Enabled = false）");
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_agent is not null)
                {
                    return;
                }

                var modelPath = Config.ModelPath;
                if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
                {
                    throw new FileNotFoundException($"GGUF 模型文件不存在或未配置: {modelPath}");
                }

                // ★ 模型路径必须是纯 ASCII：中文路径下 llama.cpp 原生层会加载失败（已实测）
                // 侧车：启动/就绪 llama-server（OpenAI 兼容 HTTP），模型加载实测约 35~45 秒
                await LlamaServerHost.Instance.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);

                // OpenAI 协议适配器把侧车包装成 IChatClient —— MAF / 流式 / 工具调用代码零改动
                var openAiOptions = new OpenAIClientOptions
                {
                    Endpoint = new Uri(LlamaServerHost.Instance.BaseUrl),
                };
                var openAiClient = new OpenAIClient(new ApiKeyCredential("local-sidecar"), openAiOptions);

                _chatClient = new ChatClientBuilder(openAiClient.GetChatClient("qwen").AsIChatClient())
                    .UseFunctionInvocation()
                    .Build();

                _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

                // MAF：把 IChatClient 包装成 AIAgent，由它负责工具调用编排与多轮会话状态
                _agent = _chatClient.AsAIAgent(
                    name: "DetectionAssistant",
                    instructions: SystemPrompt,
                    tools: BuildTools());

                // 单会话模型：单操作员场景下多轮上下文共享一份（多客户端并发会串话，见类注释）
                _session = await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);



                LogService.Instance.Info($"AI 对话：侧车就绪，模型 {Path.GetFileName(modelPath)}（端口 {Config.LlamaServerPort}，MAF Agent 就绪）");
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>释放模型，归还显存。按需加载模式下关闭面板时调用。</summary>
        public async Task UnloadAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                ClearPendingWrite("模型已卸载");
                _agent = null;
                _session = null;
                _chatClient?.Dispose();
                _chatClient = null;
                // 侧车持有模型与显存：按需释放 = 停侧车（下次提问会自动重启，约 40 秒）
                LlamaServerHost.Instance.Stop();
                LogService.Instance.Info("AI 对话：侧车已停止，显存已归还");
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// 提问。返回 AI 的自然语言回答（可能经过工具调用）。
        ///
        /// <para>多轮：会话状态由 MAF 的 <see cref="AgentSession"/> 维护，AI 记得前面问过什么。</para>
        /// <para>写操作安全模型：<b>AI 提议 → 人确认 → C# 执行</b>。若存在待确认写操作且本次
        /// 输入是「确认/取消」，则**不调用模型**，由 C# 直接确定性执行——既省一次推理，
        /// 也杜绝小模型在确认环节出现幻觉或被提示词注入绕过。</para>
        /// </summary>
        public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return string.Empty;
            }

            // 写操作确认走确定性路径，不经模型（见类注释的安全模型）：
            // 「确认」→ 执行；「取消」→ 放弃；其他输入 → 待确认动作保留至超时。
            var confirmReply = ResolvePendingWriteReply(question, out var toExecute);
            if (confirmReply is not null)
            {
                return confirmReply;
            }
            if (toExecute is not null)
            {
                return await ExecutePendingAsync(toExecute, cancellationToken).ConfigureAwait(false);
            }

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            // ★ 关键架构约束：LLamaSharp 0.27 的 IChatClient 适配不支持 ChatOptions.Tools
            //   （内部只映射采样参数，已查证），因此 MAF 的自主工具调用在此链路上不可用。
            //   故采用「小模型只翻译、C# 执行」的两段式：
            //   ① 无状态意图调用：中文 → JSON 意图（模型不做计算，杜绝编造数字）
            //   ② C# 执行查询，再把数据喂回 MAF 会话让模型组织成自然语言回答（保留多轮上下文）
            var dataJson = await RunIntentAsync(question, cancellationToken).ConfigureAwait(false);

            if (dataJson is null)
            {
                // 非查询类问题：直接走 MAF 会话闲聊（多轮上下文仍然生效）
                var chatResponse = await _agent!.RunAsync(question, _session, null, cancellationToken)
                                               .ConfigureAwait(false);
                return chatResponse.Text ?? string.Empty;
            }

            var summarizePrompt = BuildSummarizePrompt(question, dataJson);
            var answerResponse = await _agent!.RunAsync(summarizePrompt, _session, null, cancellationToken)
                                              .ConfigureAwait(false);
            return answerResponse.Text ?? string.Empty;
        }

        /// <summary>
        /// 流式提问：回答以增量块回调，供 SSE 推给前端实现「逐字打出」。
        /// 安全模型与 <see cref="AskAsync"/> 相同：待确认写操作先走确定性路径（不调模型）。
        /// </summary>
        public async Task AskStreamingAsync(string question, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return;
            }

            var confirmReply = ResolvePendingWriteReply(question, out var toExecute);
            if (confirmReply is not null)
            {
                await onChunk(confirmReply).ConfigureAwait(false);
                return;
            }
            if (toExecute is not null)
            {
                var executed = await ExecutePendingAsync(toExecute, cancellationToken).ConfigureAwait(false);
                await onChunk(executed).ConfigureAwait(false);
                return;
            }

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var dataJson = await RunIntentAsync(question, cancellationToken).ConfigureAwait(false);
            if (dataJson is null)
            {
                await StreamAgentAsync(question, onChunk, cancellationToken).ConfigureAwait(false);
                return;
            }

            await StreamAgentAsync(BuildSummarizePrompt(question, dataJson), onChunk, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>逐块转发 MAF 的流式输出（跳过空块）。</summary>
        private async Task StreamAgentAsync(string message, Func<string, Task> onChunk, CancellationToken cancellationToken)
        {
            await foreach (var update in _agent!.RunStreamingAsync(message, _session, null, cancellationToken)
                                                     .ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    await onChunk(update.Text).ConfigureAwait(false);
                }
            }
        }

        private static string BuildSummarizePrompt(string question, string dataJson) =>
            "用户的问题：" + question + "\n" +
            "数据库查询结果（JSON）：\n" + dataJson + "\n" +
            "请用简洁中文回答用户的问题。规则：" +
            "① 结果是检测记录/统计时，必须引用 JSON 里的真实数字，不要编造；count 为 0 就直接说该时段没有检测记录，不要计算合格率；" +
            "② 结果是知识片段（hits）时，基于片段内容回答，并在末尾注明来源文件名，片段不足以回答就如实说明；" +
            "③ null 不要念出来；" +
            "④ 结果里带 imageUrl 或 markdown 图片字段时，必须原样保留 markdown 图片语法（![检测图](...)），一个字符都不能改。 /no_think";

        /// <summary>
        /// 意图翻译（走 llama-server 侧车，<c>response_format: json_object + schema</c>）。
        /// 语法约束由侧车在采样期强制 —— 输出<b>必然</b>是符合 schema 的合法 JSON，
        /// think 泄漏 / JSON 后跟中文 / 非法 JSON 三类问题从根上消失（实测 5/5 全合法）。
        /// 无状态：不进 MAF 会话历史，避免污染多轮上下文。
        /// </summary>
        private async Task<string?> RunIntentAsync(string question, CancellationToken cancellationToken)
        {
            if (_http is null)
            {
                return null;
            }

            var body = new
            {
                messages = new object[]
                {
                    new { role = "system", content = IntentSystemPrompt.Replace("/no_think", "").Trim() },
                    new { role = "user", content = question },
                },
                temperature = 0f,
                max_tokens = 200,
                response_format = new
                {
                    type = "json_object",
                    schema = new
                    {
                        type = "object",
                        properties = new
                        {
                            intent = new { type = "string", @enum = new[] { "by_barcode", "by_time_range", "aggregate", "camera_settings", "by_result", "get_image", "by_recipe", "by_station", "compare_periods", "switch_recipe", "daily_report", "search_knowledge", "search_logs", "get_settings", "get_recipes", "headtail_audit", "save_case", "encode_range", "alert_summary", "no_barcode_rate", "unknown" } },
                            barcode = new { type = "string" },
                            hours = new { type = "integer" },
                            period_hours = new { type = "integer" },
                            query = new { type = "string" },
                            group = new { type = "string" },
                            title = new { type = "string" },
                            symptom = new { type = "string" },
                            cause = new { type = "string" },
                            solution = new { type = "string" },
                            keywords = new { type = "string" },
                            result = new { type = new[] { "string", "null" } },
                            recipe = new { type = "string" },
                            station = new { type = new[] { "string", "null" } },
                            min_score = new { type = new[] { "number", "null" } },
                            limit = new { type = "integer" },
                            min_encode = new { type = "integer" },
                            max_encode = new { type = "integer" },
                        },
                        required = new[] { "intent" },
                    },
                },
            };

            try
            {
                using var content = new StringContent(
                    JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(
                    $"{LlamaServerHost.Instance.BaseUrl}/chat/completions", content, cancellationToken)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                                                 .ConfigureAwait(false);
                var output = doc.RootElement
                                .GetProperty("choices")[0]
                                .GetProperty("message")
                                .GetProperty("content")
                                .GetString() ?? string.Empty;

                LogService.Instance.Info($"[AI] 意图(schema)={output}");

                var clean = ExtractJson(output);
                if (clean is null)
                {
                    return null;
                }

                using var intentDoc = JsonDocument.Parse(clean);
                return await ExecuteIntentAsync(intentDoc.RootElement, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"[AI] 意图调用/执行失败: {ex.Message}");
                return null;
            }
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            // 中文不转义成 \uXXXX：日志与 HTTP 响应人类可读（application/json 无 XSS 面）
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>
        /// 知识库检索（RAG）：操作手册/说明文档片段，结果交给总结调用组织成回答。
        /// </summary>
        private static async Task<string> SearchKnowledgeAsync(string query)
        {
            var hits = await KnowledgeBase.SearchAsync(query, 4).ConfigureAwait(false);
            if (hits.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    found = false,
                    message = "手册与文档中没有检索到相关内容",
                }, JsonOpts);
            }

            return JsonSerializer.Serialize(new
            {
                found = true,
                note = "片段来自系统文档，回答时在末尾注明来源文件名",
                hits = hits.Select(h => new { source = h.Source, text = h.Text }).ToList(),
            }, JsonOpts);
        }

        /// <summary>历史日志检索（Logs.db，按关键词匹配最近 N 小时）。</summary>
        private static async Task<string> SearchLogsJsonAsync(string keyword, int hours)
        {
            var rows = await KnowledgeBase.SearchLogsAsync(keyword, hours, 15).ConfigureAwait(false);
            return JsonSerializer.Serialize(new
            {
                found = rows.Count > 0,
                hours,
                keyword,
                logs = rows,
            }, JsonOpts);
        }

        // ─────────────────────────────────────────────────────────────
        // 追溯扩展：NG 品 / 看图 / 配方 / 工位 / 时段对比 / 切配方 / 班报
        // ─────────────────────────────────────────────────────────────

        /// <summary>不良品追溯：按 Result（约定 NG）过滤最近记录。</summary>
        private async Task<string> QueryByResultAsync(string result, int hours, int limit)
        {
            result = string.IsNullOrWhiteSpace(result) ? "NG" : result.Trim().ToUpperInvariant();
            hours = ClampHours(hours);
            limit = ClampLimit(limit);
            var since = DateTime.Now.AddHours(-hours);

            await using var db = new AppDbContext();
            var rows = await db.BarcodeData.AsNoTracking()
                .Where(x => x.DetectTime >= since && x.Result == result)
                .OrderByDescending(x => x.DetectTime)
                .Take(limit)
                .Select(x => new { x.Barcode, x.DetectTime, x.Score, x.CostTime, x.RecipeName, x.Result, x.Station, x.ImageFullName })
                .ToListAsync().ConfigureAwait(false);

            return JsonSerializer.Serialize(new { since, hours, result, count = rows.Count, rows }, JsonOpts);
        }

        /// <summary>
        /// 看图：取最近一条匹配记录的检测图。返回<b>相对 URL</b>（同源，WebView2 与手机端都能加载），
        /// 由 AiWebHost 的 /ai/image 端点带白名单校验地吐出图片。
        /// </summary>
        private async Task<string> GetLatestImageAsync(string result, string barcode, int hours)
        {
            string? resultFilter = string.IsNullOrWhiteSpace(result) ? null : result.Trim().ToUpperInvariant();
            string? barcodeFilter = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
            var since = DateTime.Now.AddHours(-ClampHours(hours));

            await using var db = new AppDbContext();
            var query = db.BarcodeData.AsNoTracking()
                .Where(x => x.DetectTime >= since && x.ImageFullName != null && x.ImageFullName != "");
            if (result is not null)
            {
                query = query.Where(x => x.Result == result);
            }
            if (barcode is not null)
            {
                query = query.Where(x => x.Barcode == barcode);
            }

            var row = await query.OrderByDescending(x => x.DetectTime)
                .Select(x => new { x.Barcode, x.DetectTime, x.Score, x.Result, x.ImageFullName })
                .FirstOrDefaultAsync().ConfigureAwait(false);

            if (row is null || string.IsNullOrEmpty(row.ImageFullName) || !File.Exists(row.ImageFullName))
            {
                return JsonSerializer.Serialize(new { found = false, message = "指定范围内没有带图片的记录" }, JsonOpts);
            }

            var encoded = System.Net.WebUtility.UrlEncode(row.ImageFullName);
            return JsonSerializer.Serialize(new
            {
                found = true,
                imageUrl = "/ai/image?path=" + encoded,
                barcode = row.Barcode,
                detectTime = row.DetectTime,
                score = row.Score,
                result = row.Result,
                markdown = $"![检测图](/ai/image?path={encoded})",
            }, JsonOpts);
        }

        /// <summary>配方维度：按配方过滤记录并给出该配方合格率。</summary>
        private async Task<string> QueryByRecipeAsync(string recipe, int hours, int limit)
        {
            recipe = (recipe ?? string.Empty).Trim();
            hours = ClampHours(hours);
            limit = ClampLimit(limit);
            var since = DateTime.Now.AddHours(-hours);

            await using var db = new AppDbContext();
            var rows = await db.BarcodeData.AsNoTracking()
                .Where(x => x.DetectTime >= since && x.RecipeName == recipe)
                .OrderByDescending(x => x.DetectTime)
                .Take(limit)
                .Select(x => new { x.Barcode, x.DetectTime, x.Score, x.Result, x.CostTime })
                .ToListAsync().ConfigureAwait(false);

            var total = rows.Count;
            var ok = rows.Count(x => string.Equals(x.Result, "OK", StringComparison.OrdinalIgnoreCase));
            return JsonSerializer.Serialize(new
            {
                recipe,
                since,
                hours,
                count = total,
                okCount = ok,
                passRate = total == 0 ? (double?)null : Math.Round(ok * 100.0 / total, 2),
                rows,
            }, JsonOpts);
        }

        /// <summary>
        /// 头尾特征池自检：回答「哪个特征对区分头尾有决定性作用」。
        /// <para>数据源：<c>HeadFeatures</c>（每帧判定轨迹）+ <c>HeadTruthPositive</c>（QR 真值符号）。
        /// 输出三个指标——出死区率（有无区分力）/ 裁决占比（谁在干活）/ QR 一致率（判得对不对），
        /// 以及一句综合诊断。三者缺一不可：只看前两个会把"常出死区但判错"误当主力特征。</para>
        /// </summary>
        /// <param name="hours">回溯小时数。</param>
        /// <param name="recipe">限定配方名；空 = 全部配方混合统计。</param>
        private async Task<string> BuildHeadTailAuditAsync(int hours, string? recipe)
        {
            hours = ClampHours(hours);
            var since = DateTime.Now.AddHours(-hours);
            var recipeFilter = string.IsNullOrWhiteSpace(recipe) ? null : recipe.Trim();

            await using var db = new AppDbContext();
            var query = db.BarcodeData.AsNoTracking()
                .Where(x => x.DetectTime >= since && x.HeadFeatures != null);
            if (recipeFilter is not null)
            {
                query = query.Where(x => x.RecipeName == recipeFilter);
            }

            // 升序取——抖动率依赖"相邻帧"语义，必须按时间序
            var rows = await query
                .OrderBy(x => x.DetectTime)
                .Select(x => new { x.HeadFeatures, x.HeadTruthPositive, x.ImageX, x.ImageY })
                .ToListAsync().ConfigureAwait(false);

            if (rows.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    since, hours, recipe = recipeFilter,
                    totalFrames = 0,
                    note = "该时段内没有特征池判定记录（可能未启用特征池开关，或本时段无检测）",
                }, JsonOpts);
            }

            var audit = Application.HeadTailFeatureAudit.Build(
                rows.Select(r => r.HeadFeatures).ToList(),
                rows.Select(r => r.HeadTruthPositive).ToList(),
                rows.Select(r => r.ImageX).ToList(),
                rows.Select(r => r.ImageY).ToList());

            var stats = audit.Stats.Select(s => new
            {
                name = s.Name,
                samples = s.TotalSamples,
                decisiveRate = Math.Round(s.DecisiveRate * 100, 1),
                adjudicatedRate = Math.Round(s.AdjudicatedRate * 100, 1),
                truthSamples = s.TruthComparedCount,
                agreeRate = s.TruthComparedCount > 0 ? Math.Round(s.AgreeRate * 100, 1) : (double?)null,
                takeOver = s.TakeOverCount,
                yielded = s.YieldedCount,
                diagnosis = s.Diagnosis(),
            }).ToList();

            // 按裁决占比降序——直接回答"谁在干活"；一致率与样本量作为可信度依据一并给出
            var ranked = stats.OrderByDescending(s => s.adjudicatedRate).ToList();

            return JsonSerializer.Serialize(new
            {
                since,
                hours,
                recipe = recipeFilter,
                totalFrames = audit.TotalFrames,
                framesWithQrTruth = audit.FramesWithTruth,
                allDeadbandRate = Math.Round(audit.AllDeadbandRate * 100, 1),
                malformedFrames = audit.MalformedFrames,
                takeOverFrames = audit.TakeOverFrames,
                takeOverRate = Math.Round(audit.TakeOverRate * 100, 1),
                consistency = new
                {
                    divergenceRate = Math.Round(audit.Consistency.DivergenceRate * 100, 1),
                    singleDecisiveRate = Math.Round(audit.Consistency.SingleDecisiveRate * 100, 1),
                    flipRate = Math.Round(audit.Consistency.FlipRate * 100, 1),
                    positionUsable = audit.Consistency.PositionUsable,
                    positionStdX = Math.Round(audit.Consistency.PositionStdX, 2),
                    positionStdY = Math.Round(audit.Consistency.PositionStdY, 2),
                    positionCorrelation = audit.Consistency.PositionUsable
                        ? (double?)Math.Round(Math.Max(
                            Math.Abs(audit.Consistency.PositionCorrelationX),
                            Math.Abs(audit.Consistency.PositionCorrelationY)), 3)
                        : null,
                    diagnosis = audit.Consistency.Diagnosis(),
                },
                byAdjudication = ranked,
                note = "agreeRate 的分母是该特征出死区且有 QR 真值的帧数；" +
                       "truthSamples 低于 30 时一致率仅供参考。decidableRate 低 = 该特征对本产品无区分力。" +
                       "takeOverFrames = 弱信号让位机制生效的帧数（最终裁决者不是首个出死区特征）；" +
                       "takeOver/yielded 是逐特征的抢来/让出次数，长期观察可判断该机制是常态修正还是偶发兜底。" +
                       "consistency 三项【不依赖真值】，是无码场景唯一的反馈信号：divergenceRate 低 = 特征互相印证；" +
                       "flipRate 仅在【产品同向摆放】时才有抖动语义（混向时翻转是真实行为）；" +
                       "positionCorrelation 需 positionUsable=true 才有效（产品位置需有足够变化）。",
            }, JsonOpts);
        }

        /// <summary>工位/过站：station 为空时取当前机器码（本机）。</summary>
        private async Task<string> QueryByStationAsync(string station, int hours, int limit)
        {
            hours = ClampHours(hours);
            limit = ClampLimit(limit);
            var since = DateTime.Now.AddHours(-hours);
            var target = string.IsNullOrWhiteSpace(station) ? DetectionRecordService.CurrentStation : station.Trim();

            await using var db = new AppDbContext();
            var rows = await db.BarcodeData.AsNoTracking()
                .Where(x => x.DetectTime >= since && x.Station == target)
                .OrderByDescending(x => x.DetectTime)
                .Take(limit)
                .Select(x => new { x.Barcode, x.DetectTime, x.Score, x.Result, x.RecipeName, x.CostTime })
                .ToListAsync().ConfigureAwait(false);

            return JsonSerializer.Serialize(new { station = target, since, hours, count = rows.Count, rows }, JsonOpts);
        }

        /// <summary>时段对比：最近 periodHours vs 之前同长度时段。</summary>
        private async Task<string> ComparePeriodsAsync(int periodHours)
        {
            periodHours = Math.Clamp(periodHours <= 0 ? 24 : periodHours, 1, 24 * 30);
            var nowEnd = DateTime.Now;
            var aStart = nowEnd.AddHours(-periodHours);
            var bEnd = aStart;
            var bStart = bEnd.AddHours(-periodHours);

            var current = await AggregateRangeAsync(aStart, nowEnd).ConfigureAwait(false);
            var previous = await AggregateRangeAsync(bStart, bEnd).ConfigureAwait(false);

            return JsonSerializer.Serialize(new
            {
                current = new { from = aStart, to = nowEnd, data = current },
                previous = new { from = bStart, to = bEnd, data = previous },
            }, JsonOpts);
        }

        /// <summary>区间聚合（对比/班报共用）。</summary>
        private static async Task<object> AggregateRangeAsync(DateTime since, DateTime until)
        {
            await using var db = new AppDbContext();
            var data = await db.BarcodeData.AsNoTracking()
                .Where(x => x.DetectTime >= since && x.DetectTime < until)
                .Select(x => new { x.Score, x.CostTime, x.Result })
                .ToListAsync().ConfigureAwait(false);

            var count = data.Count;
            var ok = data.Count(x => string.Equals(x.Result, "OK", StringComparison.OrdinalIgnoreCase));
            return new
            {
                count,
                okCount = ok,
                ngCount = count - ok,
                passRate = count == 0 ? (double?)null : Math.Round(ok * 100.0 / count, 2),
                avgScore = count == 0 ? (double?)null : Math.Round(data.Average(x => x.Score), 2),
                avgCostMs = count == 0 ? (double?)null : Math.Round(data.Average(x => x.CostTime), 1),
            };
        }

        /// <summary>
        /// 切配方提议（写操作）：只记录待确认动作，用户确认后由 C# 执行
        /// <see cref="RecipesManage.SetAndSaveCurrentRecipe"/>（与界面/TC P 切配方同链路）。
        /// </summary>
        private Task<string> ProposeSwitchRecipeAsync(string recipeName)
        {
            recipeName = (recipeName ?? string.Empty).Trim();
            var recipe = RecipesManage.Instance.Recipes.FirstOrDefault(
                r => r.Name.Equals(recipeName, StringComparison.OrdinalIgnoreCase));
            if (recipe is null)
            {
                var known = string.Join("、", RecipesManage.Instance.Recipes.Select(r => r.Name));
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    success = false,
                    message = string.IsNullOrEmpty(known)
                        ? "系统中没有配方"
                        : $"没有名为「{recipeName}」的配方。可用配方：{known}",
                }, JsonOpts));
            }

            var current = RecipesManage.Instance.CurrentRecipe?.Name ?? "(无)";
            var description = $"把当前配方从「{current}」切换为「{recipe.Name}」";
            lock (_pendingWriteLock)
            {
                _pendingWrite = new PendingWriteAction(description, () =>
                {
                    RecipesManage.Instance.SetAndSaveCurrentRecipe(recipe);
                    LogService.Instance.Info($"[AI 审计] 配方已切换为 {recipe.Name}");
                    return Task.FromResult("配方已切换并保存");
                });
                _pendingWriteAtUtc = DateTime.UtcNow;
            }

            LogService.Instance.Info($"[AI 审计] 提议写操作（待确认）: {description}");
            return Task.FromResult($"已记录待确认操作：{description}\n请回复「确认」执行，或回复「取消」放弃。");
        }

        /// <summary>班报数据：区间聚合 + NG 明细 top + 按配方分布，交给总结按班报格式输出。</summary>
        private async Task<string> BuildDailyReportDataAsync(int hours)
        {
            hours = Math.Clamp(hours <= 0 ? 24 : hours, 1, 24 * 30);
            var since = DateTime.Now.AddHours(-hours);
            var summary = await AggregateRangeAsync(since, DateTime.Now).ConfigureAwait(false);

            await using var db = new AppDbContext();
            var ngTop = await db.BarcodeData.AsNoTracking()
                .Where(x => x.DetectTime >= since && x.Result == "NG")
                .OrderByDescending(x => x.DetectTime)
                .Take(10)
                .Select(x => new { x.Barcode, x.DetectTime, x.Score })
                .ToListAsync().ConfigureAwait(false);

            var byRecipe = await db.BarcodeData.AsNoTracking()
                .Where(x => x.DetectTime >= since)
                .GroupBy(x => x.RecipeName ?? "(未知)")
                .Select(g => new { recipe = g.Key, count = g.Count() })
                .ToListAsync().ConfigureAwait(false);

            return JsonSerializer.Serialize(new
            {
                reportType = "production_report",
                range = new { from = since, to = DateTime.Now, hours },
                summary,
                ngTop,
                byRecipe,
            }, JsonOpts);
        }

        /// <summary>按意图分发到对应的查询实现。</summary>
        private async Task<string?> ExecuteIntentAsync(JsonElement intent, CancellationToken cancellationToken)
        {
            var kind = intent.TryGetProperty("intent", out var ik) ? ik.GetString() : null;
            return kind switch
            {
                "by_barcode" => await QueryByBarcodeAsync(
                    GetStr(intent, "barcode") ?? string.Empty,
                    GetInt(intent, "limit", 20)).ConfigureAwait(false),
                "by_time_range" => await QueryRecentAsync(
                    GetInt(intent, "hours", 24),
                    GetDbl(intent, "min_score", 0),
                    GetStr(intent, "result") ?? string.Empty,
                    GetInt(intent, "limit", 20)).ConfigureAwait(false),
                "aggregate" => await AggregateRecentAsync(
                    GetInt(intent, "hours", 24)).ConfigureAwait(false),
                "camera_settings" => await GetCameraSettingsAsync().ConfigureAwait(false),
                "by_result" => await QueryByResultAsync(
                    GetStr(intent, "result") ?? "NG",
                    GetInt(intent, "hours", 24),
                    GetInt(intent, "limit", 20)).ConfigureAwait(false),
                "get_image" => await GetLatestImageAsync(
                    GetStr(intent, "result") ?? string.Empty,
                    GetStr(intent, "barcode") ?? string.Empty,
                    GetInt(intent, "hours", 24)).ConfigureAwait(false),
                "by_recipe" => await QueryByRecipeAsync(
                    GetStr(intent, "recipe") ?? string.Empty,
                    GetInt(intent, "hours", 24),
                    GetInt(intent, "limit", 20)).ConfigureAwait(false),
                "by_station" => await QueryByStationAsync(
                    GetStr(intent, "station") ?? string.Empty,
                    GetInt(intent, "hours", 24),
                    GetInt(intent, "limit", 20)).ConfigureAwait(false),
                "compare_periods" => await ComparePeriodsAsync(
                    GetInt(intent, "period_hours", 24)).ConfigureAwait(false),
                "switch_recipe" => await ProposeSwitchRecipeAsync(
                    GetStr(intent, "recipe") ?? string.Empty).ConfigureAwait(false),
                "daily_report" => await BuildDailyReportDataAsync(
                    GetInt(intent, "hours", 24)).ConfigureAwait(false),
                "search_knowledge" => await SearchKnowledgeAsync(
                    GetStr(intent, "query") ?? string.Empty).ConfigureAwait(false),
                "search_logs" => await SearchLogsJsonAsync(
                    GetStr(intent, "query") ?? string.Empty,
                    GetInt(intent, "hours", 24)).ConfigureAwait(false),
                "get_settings" => await Task.FromResult(
                    GetSettingsSnapshot(GetStr(intent, "group") ?? "all")).ConfigureAwait(false),
                "get_recipes" => await Task.FromResult(
                    GetRecipesSnapshot()).ConfigureAwait(false),
                "headtail_audit" => await BuildHeadTailAuditAsync(
                    GetInt(intent, "hours", 24),
                    GetStr(intent, "recipe")).ConfigureAwait(false),
                "save_case" => await SaveCaseAsync(
                    GetStr(intent, "title") ?? string.Empty,
                    GetStr(intent, "symptom") ?? string.Empty,
                    GetStr(intent, "cause") ?? string.Empty,
                    GetStr(intent, "solution") ?? string.Empty,
                    GetStr(intent, "keywords") ?? string.Empty).ConfigureAwait(false),
                "encode_range" => await QueryByEncodeRangeAsync(
                    GetLong(intent, "min_encode", 0),
                    GetLong(intent, "max_encode", 0),
                    GetInt(intent, "limit", 20),
                    cancellationToken).ConfigureAwait(false),
                "alert_summary" => await BuildAlertSummaryAsync(
                    GetInt(intent, "hours", 24),
                    cancellationToken).ConfigureAwait(false),
                "no_barcode_rate" => await BuildNoBarcodeRateAsync(
                    GetInt(intent, "hours", 24),
                    cancellationToken).ConfigureAwait(false),
                _ => null,
            };
        }

        /// <summary>
        /// 提取<b>第一个花括号配平的 JSON 对象</b>。
        /// 不能用「首 &#123; 到末 &#125;」：小模型常在 JSON 后面继续输出中文
        /// （实测报 "'0xE6' is an invalid start of a value"，0xE6 即中文 UTF-8 首字节），
        /// 甚至再吐第二个对象；首尾截取会把中文圈进去导致解析失败。
        /// </summary>
        /// <summary>
        /// 保存异常案例（AI 写入知识库的低风险加法操作——只创建新文件，不改设备状态，
        /// 故不走"提议→确认"流程，但记录 [AI 审计] 日志）。
        /// 写入 Saves/Knowledge/Cases/*.md 后调用 InvalidateIndex 让案例立即可检索。
        /// </summary>
        private static async Task<string> SaveCaseAsync(
            string title, string symptom, string cause, string solution, string keywords)
        {
            title = (title ?? string.Empty).Trim();
            symptom = (symptom ?? string.Empty).Trim();
            cause = (cause ?? string.Empty).Trim();
            solution = (solution ?? string.Empty).Trim();
            keywords = (keywords ?? string.Empty).Trim();

            if (title.Length == 0 || symptom.Length == 0 || solution.Length == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "标题/症状/解法不能为空——请先向用户确认这三项内容",
                }, JsonOpts);
            }

            try
            {
                // 2026-09-15: 跟随统一数据根，不再写死在 exe 目录
                var dir = DataPaths.KnowledgeCasesDir;
                Directory.CreateDirectory(dir);

                var safe = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
                if (safe.Length > 60) safe = safe[..60];
                var path = Path.Combine(dir, $"{safe}.md");
                if (File.Exists(path))
                {
                    path = Path.Combine(dir, $"{safe}_{DateTime.Now:HHmmss}.md");
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"# 案例：{title}");
                sb.AppendLine();
                sb.AppendLine($"症状：{symptom}");
                sb.AppendLine();
                sb.AppendLine($"原因：{(cause.Length > 0 ? cause : "（待补充）")}");
                sb.AppendLine();
                sb.AppendLine($"解法：{solution}");
                sb.AppendLine();
                sb.AppendLine($"关键词：{keywords}");
                sb.AppendLine();
                sb.AppendLine($"日期：{DateTime.Now:yyyy-MM-dd}");
                await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
                KnowledgeBase.InvalidateIndex();
                LogService.Instance.Info($"[AI 审计] 异常案例已保存: {path}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    savedPath = path,
                    message = "案例已保存并纳入知识检索（用户后续可按关键词命中）",
                }, JsonOpts);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"[AI 审计] 保存异常案例失败: {ex.Message}");
                return JsonSerializer.Serialize(new { success = false, error = $"保存失败: {ex.Message}" }, JsonOpts);
            }
        }

        /// <summary>
        /// 配方信息快照（只读白名单）：配方清单（名称/描述/修改时间）+ 当前配方名 +
        /// 当前配方关键参数（位置补偿/边缘检测参数/图像采集参数）。
        /// 不暴露模型路径、标定内部细节与 CUDA 配置——降低信息面。
        /// </summary>
        private static string GetRecipesSnapshot()
        {
            var rm = RecipesManage.Instance;
            var current = rm.CurrentRecipe;

            var list = rm.Recipes.Select(r => new
            {
                name = r.Name,
                description = r.Description,
                modifiedTime = r.ModifiedTime,
                isCurrent = current is not null && r.Name == current.Name,
            }).ToList();

            object currentParams = current is null
                ? new { message = "当前未设置配方" }
                : new
                {
                    name = current.Name,
                    offsetX = current.OffsetX,
                    offsetY = current.OffsetY,
                    offsetAngle = current.OffsetAngle,
                    edgeDetection = current.YoloTool?.EdgeDetection is null
                        ? null
                        : new
                        {
                            confidence = current.YoloTool.EdgeDetection.Confidence,
                            iou = current.YoloTool.EdgeDetection.IoU,
                        },
                    brightnessDirectionOverride = current.YoloTool?.IsBrightnessDirectionEnabled,
                    imageTool = current.ImageTool is null
                        ? null
                        : new
                        {
                            readFromScanner = current.ImageTool.ReadFromScanner,
                            exposureTime = current.ImageTool.ExposureTime,
                            gain = current.ImageTool.Gain,
                            removeCount = current.ImageTool.RemoveCount,
                        },
                };

            return JsonSerializer.Serialize(new
            {
                count = list.Count,
                current = current?.Name,
                recipes = list,
                currentRecipeParams = currentParams,
            }, JsonOpts);
        }

        /// <summary>
        /// 设置参数快照（只读白名单）。安全边界：
        /// <para>① 仅显式枚举 Algorithm/Storage/Database 三组低风险字段——不用反射，
        /// 反射会连带泄漏禁读字段（Security 含密码）；② 第一期只读，写操作走
        /// "提议→确认→Save()" 的第二期（含 Admin 角色校验）；③ Ai 组禁止暴露——
        /// AI 读写自身配置属自我授权风险。</para>
        /// </summary>
        private static string GetSettingsSnapshot(string group)
        {
            var s = MainAPP.Models.Settings.Instance;
            object result = group switch
            {
                "algorithm" => new { algorithm = SnapshotAlgorithm(s) },
                "storage" => new { storage = SnapshotStorage(s) },
                "database" => new { database = SnapshotDatabase(s) },
                "all" => (object)new
                {
                    algorithm = SnapshotAlgorithm(s),
                    storage = SnapshotStorage(s),
                    database = SnapshotDatabase(s),
                },
                _ => new { error = $"未知分组 '{group}'，可用：algorithm / storage / database" },
            };
            return JsonSerializer.Serialize(result, JsonOpts);
        }

        /// <summary>算法组快照（检测行为参数，即时生效）。</summary>
        private static object SnapshotAlgorithm(MainAPP.Models.Settings s) => new
        {
            s.Algorithm.DedupEnabled,
            s.Algorithm.DedupByEncoder,
            s.Algorithm.DedupTrackAxis,
            s.Algorithm.DedupPositionThreshold,
            s.Algorithm.DedupAngleThreshold,
            s.Algorithm.EdgeMarginLeftPixels,
            s.Algorithm.EdgeMarginTopPixels,
            s.Algorithm.EdgeMarginRightPixels,
            s.Algorithm.EdgeMarginBottomPixels,
            s.Algorithm.MinMaskAreaPixels,
            s.Algorithm.MaxMaskAreaPixels,
            s.Algorithm.BrightnessDirectionEnabled,
            s.Algorithm.BrightnessDirectionDeadband,
            s.Algorithm.BrightnessContrastStretchEnabled,
            s.Algorithm.BrightnessStretchLowPercentile,
            s.Algorithm.BrightnessStretchHighPercentile,
            // 2026-09-13: 头尾判定特征池（含并入的亮度判向）
            s.Algorithm.HeadTailFeaturePoolEnabled,
            s.Algorithm.HeadTailFeatureDeadband,
            s.Algorithm.TrackerExpireSeconds,
        };

        /// <summary>存储组快照。</summary>
        private static object SnapshotStorage(MainAPP.Models.Settings s) => new
        {
            s.Storage.IsSaveDraw,
            s.Storage.IsSaveSource,
            s.Storage.MinRecentDays,
            s.Storage.PicturesSaveFolder,
        };

        /// <summary>数据库组快照（保留期/上限/清理周期）。</summary>
        private static object SnapshotDatabase(MainAPP.Models.Settings s) => new
        {
            s.Database.BarcodeDataRetentionDays,
            s.Database.BarcodeDataMaxCount,
            s.Database.LogRetentionDays,
            s.Database.LogMaxCount,
            s.Database.DataCleanupIntervalHours,
        };

        private static string? ExtractJson(string text)
        {
            var start = text.IndexOf('{');
            if (start < 0) return null;

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaped) { escaped = false; }
                    else if (c == '\\') { escaped = true; }
                    else if (c == '"') { inString = false; }
                    continue;
                }
                if (c == '"') { inString = true; }
                else if (c == '{') { depth++; }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, i - start + 1);
                    }
                }
            }
            return null;
        }

        private static string? GetStr(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int GetInt(JsonElement e, string name, int fallback) =>
            e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;

        private static double GetDbl(JsonElement e, string name, double fallback) =>
            e.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : fallback;

        private static long GetLong(JsonElement e, string name, long fallback) =>
            e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : fallback;

        // ─────────────────────────────────────────────────────────────
        // 2026-09-13: 高频动态意图——编码器区间 / 告警摘要 / 无码率
        // ─────────────────────────────────────────────────────────────

        /// <summary>编码器区间查询（encode_range）：返回区间内记录数与最近若干条的条码/坐标/角度/结果。</summary>
        private static async Task<string> QueryByEncodeRangeAsync(long minEncode, long maxEncode, int limit, CancellationToken ct)
        {
            if (maxEncode <= minEncode)
            {
                return "参数错误：max_encode 应大于 min_encode";
            }

            limit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 50);
            var rows = await BarcodeDataService.Instance.GetByEncodeRangeAsync(minEncode, maxEncode, ct).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                return $"编码器 {minEncode}~{maxEncode} 区间内没有检测记录";
            }

            var sb = new StringBuilder();
            sb.Append($"编码器 {minEncode}~{maxEncode} 区间共 {rows.Count} 条记录，最近 {Math.Min(limit, rows.Count)} 条：\n");
            foreach (var m in rows.Take(limit))
            {
                sb.AppendLine($"  [{m.DetectTime:HH:mm:ss}] 编码器={m.Encode} 条码={m.Barcode} X={m.WorldX:F1} Y={m.WorldY:F1} 角度={m.Angle:F1} 结果={m.Result ?? "-"}");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>告警摘要（alert_summary）：最近 N 小时日志中 Error/Warning 数量与去重后的代表性样例（采样最近 200 条）。</summary>
        private static async Task<string> BuildAlertSummaryAsync(int hours, CancellationToken ct)
        {
            hours = ClampHours(hours);
            var since = DateTime.Now.AddHours(-hours);
            await using var db = new LogDbContext();
            var rows = await db.Logs.AsNoTracking()
                .Where(l => l.Timestamp >= since && (l.Level == "Error" || l.Level == "Warning"))
                .OrderByDescending(l => l.Id)
                .Take(200)
                .ToListAsync(ct).ConfigureAwait(false);

            if (rows.Count == 0)
            {
                return $"最近 {hours} 小时没有 Error/Warning 告警，状态正常。";
            }

            var errs = rows.Count(l => string.Equals(l.Level, "Error", StringComparison.OrdinalIgnoreCase));
            var warns = rows.Count(l => string.Equals(l.Level, "Warning", StringComparison.OrdinalIgnoreCase));
            var sb = new StringBuilder();
            sb.Append($"最近 {hours} 小时告警（采样最近 {rows.Count} 条）：Error {errs} 条，Warning {warns} 条。\n");
            var seen = new HashSet<string>();
            var shown = 0;
            foreach (var r in rows)
            {
                if (shown >= 8)
                {
                    break;
                }

                var key = (r.RenderedMessage ?? string.Empty).Trim();
                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                var brief = key.Length > 90 ? key[..90] + "…" : key;
                sb.AppendLine($"  [{r.Timestamp:MM-dd HH:mm}] [{r.Level}] {brief}");
                shown++;
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>无码率（no_barcode_rate）：最近 N 小时检测总数、无码（noread/空）数量与占比。</summary>
        private static async Task<string> BuildNoBarcodeRateAsync(int hours, CancellationToken ct)
        {
            hours = ClampHours(hours);
            var since = DateTime.Now.AddHours(-hours);
            await using var db = new AppDbContext();
            var total = await db.BarcodeData.CountAsync(m => m.DetectTime >= since, ct).ConfigureAwait(false);
            var noCode = await db.BarcodeData
                .CountAsync(m => m.DetectTime >= since && (m.Barcode == string.Empty || m.Barcode == "noread"), ct)
                .ConfigureAwait(false);
            var rate = total > 0 ? noCode * 100.0 / total : 0.0;
            return total == 0
                ? $"最近 {hours} 小时没有检测记录。"
                : $"最近 {hours} 小时共检测 {total} 条，无码 {noCode} 条，无码率 {rate:F1}%。";
        }

        /// <summary>清空多轮会话历史（开启新对话）。模型保持加载状态。</summary>
        public async Task ResetConversationAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_agent is null)
                {
                    return;
                }

                _session = await _agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
                LogService.Instance.Info("AI 对话：已开启新会话");
            }
            finally
            {
                _gate.Release();
            }
        }

        private const string SystemPrompt = """
你是工业视觉检测系统（CAV 物码绑定）的查询助手，用简洁中文回答。
规则：
1. 涉及检测数据的问题，必须调用工具查询数据库，绝不凭空编造数字。
2. 回答时给出关键数字（时间、评分、耗时、数量），并说明查询范围。
3. 工具返回空结果时，如实说明"没有查到"，不要猜测。
4. 用户要求修改设备参数（曝光/增益）时，调用对应的提议工具；工具会返回确认提示，把提示原样转告用户。
5. 除工具返回的确认提示外，不要自己发明"已修改"之类的说法——参数只有用户回复「确认」后才会真正生效。

/no_think
""";

        /// <summary>
        /// 意图翻译提示词（无状态，不进 MAF 会话历史）。
        /// 小模型只做「翻译」不做「计算」——数字全部来自数据库，从根上杜绝编造。
        /// 字段刻意设计成 C# 好解析的形态（hours 是整数，而不是"今天上午"这类自由文本）。
        /// </summary>
        private const string IntentSystemPrompt = """
把用户的中文问题翻译成 JSON 查询意图，只输出一行 JSON，不要任何解释、不要思考过程。
intent 与字段：
- by_barcode:  {"barcode":"条码字符串","limit":整数}
- by_time_range: {"hours":整数小时数,"min_score":数字或null,"result":"OK"或"NG"或null,"limit":整数}
- aggregate:   {"hours":整数小时数}
- camera_settings: {}
- by_result:   {"result":"NG","hours":整数,"limit":整数}   ← 查不良品(NG)记录
- get_image:   {"result":"NG或null","barcode":"条码或null","hours":整数}   ← 用户要看检测图片/看图
- by_recipe:   {"recipe":"配方名","hours":整数,"limit":整数}   ← 按配方查记录/合格率
- by_station:  {"station":"工位或null","hours":整数,"limit":整数}   ← 工位/过站记录；null 表示当前机器
- compare_periods: {"period_hours":整数}   ← "最近N小时 vs 之前N小时"对比
- switch_recipe: {"recipe":"目标配方名"}   ← 用户要求切换配方
- daily_report: {"hours":整数}   ← 用户要班报/日报/生产总结
- search_knowledge: {"query":"检索关键词"}   ← 问"怎么做/为什么/手册里怎么说/怎么调"这类知识问题
- search_logs: {"query":"关键词","hours":整数小时数}   ← 问"最近报错/日志里有没有"
- get_settings: {"group":"algorithm或storage或database，省略=全部"}   ← 问"某设置/参数是多少、图片保留几天"
- save_case: {"title":"标题","symptom":"症状","cause":"原因","solution":"解法","keywords":"关键词1,关键词2"}   ← 用户描述了一次异常的处理过程并要求记录/沉淀为案例
- get_recipes: {}   ← 用户问"有哪些配方/当前是什么配方/当前配方的参数"（返回配方清单+当前配方参数快照）
- headtail_audit: {"hours":24,"recipe":"配方B"}   ← 用户问"头尾判定靠哪个特征/哪个特征最有用/特征池效果/判得准不准/某特征有没有用"。返回逐特征三指标：出死区率（有无区分力）、裁决占比（谁在干活）、QR一致率（判得对不对）
- encode_range: {"min_encode":整数,"max_encode":整数,"limit":整数}   ← 用户问"编码器/N号到N号之间/第N个产品"按编码器区间查记录
- alert_summary: {"hours":整数小时数}   ← 用户问"最近告警/报错/警告/Warning/Error/有没有异常"
- no_barcode_rate: {"hours":整数小时数}   ← 用户问"无码率/没读到码的有多少/连续n条没码"
- unknown: {}
hours 用整数小时数表示时间范围（例如"今天"按 24，"最近一小时"按 1）。
示例：{"intent":"by_barcode","barcode":"ABC123","limit":20}
/no_think
""";

        // ─────────────────────────────────────────────────────────────
        // 工具声明方式用 MEAI 的 AIFunctionFactory，不自己解析模型输出。
        // 读工具始终可用；写工具只在 AiSettings.AllowWriteTools = true 时注册，
        // 且**只提议不执行**——真正执行要等人回复「确认」（见 TryResolvePendingWrite）。
        // ─────────────────────────────────────────────────────────────

        private IList<AITool> BuildTools()
        {
            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(
                    (string barcode, int limit) => QueryByBarcodeAsync(barcode, limit),
                    "query_by_barcode",
                    "按条码查询检测记录，返回该条码最近若干条的检测时间、评分、耗时、配方、结果与工位"),

                AIFunctionFactory.Create(
                    (int hours, double minScore, string result, int limit) => QueryRecentAsync(hours, minScore, result, limit),
                    "query_recent",
                    "查询最近 N 小时内的检测记录，可按最低评分和结果(OK/NG)过滤"),

                AIFunctionFactory.Create(
                    (int hours) => AggregateRecentAsync(hours),
                    "aggregate_recent",
                    "统计最近 N 小时内的检测总数、平均评分、平均耗时，以及 OK/NG 各自数量"),

                AIFunctionFactory.Create(
                    () => GetCameraSettingsAsync(),
                    "get_camera_settings",
                    "读取当前扫码枪的曝光时间、增益与触发模式"),
            };

            if (Config.AllowWriteTools)
            {
                tools.Add(AIFunctionFactory.Create(
                    (double exposureUs) => ProposeSetExposureAsync(exposureUs),
                    "propose_set_exposure",
                    "提议修改扫码枪曝光时间（微秒）。工具不会立即执行，会返回确认提示，需用户回复「确认」"));

                tools.Add(AIFunctionFactory.Create(
                    (double gainDb) => ProposeSetGainAsync(gainDb),
                    "propose_set_gain",
                    "提议修改扫码枪增益（dB）。工具不会立即执行，会返回确认提示，需用户回复「确认」"));
            }

            return tools;
        }

        // ─────────────────────────────────────────────────────────────
        // 写操作：AI 提议 → 人确认 → C# 执行（确定性，不经模型）
        // ─────────────────────────────────────────────────────────────

        /// <summary>作废当前待确认写操作（模型卸载 / 会话重置时调用）。</summary>
        private void ClearPendingWrite(string reason)
        {
            lock (_pendingWriteLock)
            {
                if (_pendingWrite is not null)
                {
                    LogService.Instance.Info($"[AI 审计] 待确认写操作已作废（{reason}）: {_pendingWrite.Description}");
                    _pendingWrite = null;
                }
            }
        }

        /// <summary>待确认写操作的存活时长。超时自动作废，避免陈旧动作被很久之后的一句「确认」误触发。</summary>
        private static readonly TimeSpan PendingWriteTtl = TimeSpan.FromMinutes(2);
        private DateTime _pendingWriteAtUtc = DateTime.MinValue;

        /// <summary>
        /// 处理待确认写操作的状态机（确定性，不经模型）：
        /// 「确认」→ <paramref name="toExecute"/> 带出待执行动作（调用方负责执行）；
        /// 「取消」→ 返回放弃文案；超时 → 自动作废并返回提醒；
        /// 其他输入 → 保留待确认动作，返回 null 让模型正常回答。
        /// 用**全等匹配**而非包含匹配：防止"确认一下刚才那条记录"这类话被误当确认。
        /// </summary>
        private string? ResolvePendingWriteReply(string question, out PendingWriteAction? toExecute)
        {
            toExecute = null;

            PendingWriteAction? pending;
            lock (_pendingWriteLock)
            {
                pending = _pendingWrite;
            }

            if (pending is null)
            {
                return null;
            }

            var q = question.Trim();
            var isConfirm = q.Equals("确认", StringComparison.OrdinalIgnoreCase) || q.Equals("yes", StringComparison.OrdinalIgnoreCase);
            var isCancel = q.Equals("取消", StringComparison.OrdinalIgnoreCase) || q.Equals("cancel", StringComparison.OrdinalIgnoreCase);

            if (isConfirm)
            {
                lock (_pendingWriteLock) { _pendingWrite = null; }
                LogService.Instance.Info($"[AI 审计] 写操作已确认: {pending.Description}");
                toExecute = pending;
                return null;
            }

            if (isCancel)
            {
                lock (_pendingWriteLock) { _pendingWrite = null; }
                LogService.Instance.Info("[AI 审计] 写操作已取消");
                return $"已取消：{pending.Description}";
            }

            if (DateTime.UtcNow - _pendingWriteAtUtc > PendingWriteTtl)
            {
                lock (_pendingWriteLock) { _pendingWrite = null; }
                LogService.Instance.Info("[AI 审计] 待确认写操作已超时作废");
                return $"（待确认操作「{pending.Description}」已超时作废，如仍需要请重新提出。）";
            }

            return null;
        }

        /// <summary>
        /// 执行已确认的写操作（调用方已把 pending 清空）。
        /// </summary>
        private async Task<string> ExecutePendingAsync(PendingWriteAction action, CancellationToken cancellationToken)
        {
            try
            {
                var result = await action.ExecuteAsync().ConfigureAwait(false);
                return result;
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"[AI 审计] 写操作执行失败: {action.Description} -> {ex}");
                return $"执行失败：{ex.Message}";
            }
        }

        /// <summary>提议改曝光。只记录待确认动作，不执行；相机参数写入走「暂停主循环 → 排空 → 改 → 恢复」的既有互斥时序。</summary>
        private async Task<string> ProposeSetExposureAsync(double exposureUs)
        {
            var scanner = Devices.Scanners.HikScaner;
            if (scanner is null || !scanner.IsConnected)
            {
                return JsonSerializer.Serialize(new { success = false, message = "扫码枪未连接，无法修改曝光" });
            }

            var service = new RecipeScannerService();
            var (min, max) = await service.GetExposureRangeAsync().ConfigureAwait(false);
            var clamped = Math.Clamp(exposureUs, min, max);
            var description = $"把扫码枪曝光时间设为 {clamped:F0} µs（当前范围 {min:F0}~{max:F0}）";

            lock (_pendingWriteLock)
            {
                _pendingWrite = new PendingWriteAction(description, async () => await ApplyCameraWriteAsync(s => s.SetExposureTimeAsync((float)clamped)).ConfigureAwait(false));
                _pendingWriteAtUtc = DateTime.UtcNow;
            }
            LogService.Instance.Info($"[AI 审计] 提议写操作（待确认）: {description}");
            return $"已记录待确认操作：{description}\n请回复「确认」执行，或回复「取消」放弃。";
        }

        /// <summary>提议改增益。同上，只记录不执行。</summary>
        private async Task<string> ProposeSetGainAsync(double gainDb)
        {
            var scanner = Devices.Scanners.HikScaner;
            if (scanner is null || !scanner.IsConnected)
            {
                return JsonSerializer.Serialize(new { success = false, message = "扫码枪未连接，无法修改增益" });
            }

            var service = new RecipeScannerService();
            var (min, max) = await service.GetGainRangeAsync().ConfigureAwait(false);
            var clamped = Math.Clamp(gainDb, min, max);
            var description = $"把扫码枪增益设为 {clamped:F2} dB（当前范围 {min:F2}~{max:F2}）";

            lock (_pendingWriteLock)
            {
                _pendingWrite = new PendingWriteAction(description, async () => await ApplyCameraWriteAsync(s => s.SetGainAsync((float)clamped)).ConfigureAwait(false));
                _pendingWriteAtUtc = DateTime.UtcNow;
            }
            LogService.Instance.Info($"[AI 审计] 提议写操作（待确认）: {description}");
            return $"已记录待确认操作：{description}\n请回复「确认」执行，或回复「取消」放弃。";
        }

        /// <summary>
        /// 相机参数写入的既有互斥时序：主循环未暂停时先暂停 → 排空在途推理 → 写参数 → 恢复。
        /// 若暂停本由他方（配方页/相机调试）持有，则只写参数、不触碰主循环状态。
        /// </summary>
        private static async Task<string> ApplyCameraWriteAsync(Func<RecipeScannerService, Task> write)
        {
            var wasPaused = HomeViewModel.IsLoopPaused;
            if (!wasPaused)
            {
                HomeViewModel.PauseLoop();
            }

            try
            {
                var drained = await HomeViewModel.WaitForMainLoopDrainAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
                if (!drained)
                {
                    LogService.Instance.Warning("[AI 审计] 相机参数写入前主循环排空超时，仍继续");
                }

                var service = new RecipeScannerService();
                await write(service).ConfigureAwait(false);
                return "参数已生效";
            }
            finally
            {
                if (!wasPaused)
                {
                    HomeViewModel.ResumeLoop();
                }
            }
        }

        private static int ClampLimit(int limit) =>
            Math.Clamp(limit <= 0 ? 20 : limit, 1, Math.Max(1, Config.MaxQueryRows));

        private static int ClampHours(int hours) =>
            Math.Clamp(hours <= 0 ? 24 : hours, 1, Math.Max(1, Config.MaxQueryDays) * 24);

        private async Task<string> QueryByBarcodeAsync(string barcode, int limit)
        {
            if (string.IsNullOrWhiteSpace(barcode))
            {
                return "参数错误：条码为空";
            }

            limit = ClampLimit(limit);
            await using var db = new AppDbContext();
            var rows = await db.BarcodeData
                .AsNoTracking()
                .Where(x => x.Barcode == barcode)
                .OrderByDescending(x => x.DetectTime)
                .Take(limit)
                .Select(x => new
                {
                    x.Barcode,
                    x.DetectTime,
                    x.Score,
                    x.BarcodeScore,
                    x.CostTime,
                    x.RecipeName,
                    x.Result,
                    x.Station,
                    x.ImageFullName,
                })
                .ToListAsync()
                .ConfigureAwait(false);

            return JsonSerializer.Serialize(new { count = rows.Count, rows });
        }

        private async Task<string> QueryRecentAsync(int hours, double minScore, string result, int limit)
        {
            hours = ClampHours(hours);
            limit = ClampLimit(limit);
            var since = DateTime.Now.AddHours(-hours);
            var hasResult = !string.IsNullOrWhiteSpace(result);

            await using var db = new AppDbContext();
            var query = db.BarcodeData.AsNoTracking().Where(x => x.DetectTime >= since);
            if (minScore > 0)
            {
                query = query.Where(x => x.Score >= minScore);
            }
            if (hasResult)
            {
                var normalized = result!.Trim().ToUpperInvariant();
                query = query.Where(x => x.Result == normalized);
            }

            var rows = await query
                .OrderByDescending(x => x.DetectTime)
                .Take(limit)
                .Select(x => new
                {
                    x.Barcode,
                    x.DetectTime,
                    x.Score,
                    x.BarcodeScore,
                    x.CostTime,
                    x.RecipeName,
                    x.Result,
                    x.Station,
                })
                .ToListAsync()
                .ConfigureAwait(false);

            return JsonSerializer.Serialize(new { since, hours, count = rows.Count, rows });
        }

        private async Task<string> AggregateRecentAsync(int hours)
        {
            hours = ClampHours(hours);
            var since = DateTime.Now.AddHours(-hours);

            await using var db = new AppDbContext();
            var data = await db.BarcodeData
                .AsNoTracking()
                .Where(x => x.DetectTime >= since)
                .Select(x => new { x.Score, x.CostTime, x.Result })
                .ToListAsync()
                .ConfigureAwait(false);

            var count = data.Count;
            var ok = data.Count(x => string.Equals(x.Result, "OK", StringComparison.OrdinalIgnoreCase));
            var ng = data.Count(x => string.Equals(x.Result, "NG", StringComparison.OrdinalIgnoreCase));

            return JsonSerializer.Serialize(new
            {
                since,
                hours,
                count,
                okCount = ok,
                ngCount = ng,
                passRate = count == 0 ? (double?)null : Math.Round(ok * 100.0 / count, 2),
                avgScore = count == 0 ? (double?)null : Math.Round(data.Average(x => x.Score), 2),
                avgCostMs = count == 0 ? (double?)null : Math.Round(data.Average(x => x.CostTime), 1),
            });
        }

        private async Task<string> GetCameraSettingsAsync()
        {
            var scanner = Devices.Scanners.HikScaner;
            if (scanner is null || !scanner.IsConnected)
            {
                return JsonSerializer.Serialize(new { connected = false, message = "扫码枪未连接" });
            }

            var service = new RecipeScannerService();
            try
            {
                var exposure = await service.GetExposureTimeAsync().ConfigureAwait(false);
                var gain = await service.GetGainAsync().ConfigureAwait(false);
                return JsonSerializer.Serialize(new
                {
                    connected = true,
                    model = scanner.DeviceInfo?.ModelName ?? string.Empty,
                    serial = scanner.DeviceInfo?.SerialNumber ?? string.Empty,
                    ip = scanner.DeviceInfo?.CurrentIp ?? string.Empty,
                    exposure,
                    gain,
                    triggerMode = scanner.GetCurrentTriggerMode().ToString(),
                });
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"AI 工具读取相机参数失败: {ex.Message}");
                return JsonSerializer.Serialize(new { connected = true, error = ex.Message });
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // 直接同步释放底层资源，不走 _gate 与 UnloadAsync：
            // Dispose 语义下不应再存在并发调用方，而 sync-over-async（GetAwaiter().GetResult()）
            // 会触发 VSTHRD002 警告并带来死锁风险。
            _chatClient?.Dispose();
            _chatClient = null;
            LlamaServerHost.Instance.Stop();
            _gate.Dispose();
        }
    }
}
