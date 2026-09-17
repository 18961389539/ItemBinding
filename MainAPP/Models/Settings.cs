using MainAPP.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows;

namespace MainAPP.Models
{
    /// <summary>
    /// 全局应用设置单例。
    /// 以 JSON 文件持久化存储（Saves/Settings/settings.json），
    /// 支持保存、加载和重新加载，属性变更时触发 Changed 事件。
    /// <para>内部委托给6个领域子配置类（Storage/Ui/Network/Database/Algorithm/Security），
    /// 所有原有属性作为转发属性保留，确保向后兼容与扁平 JSON 结构。</para>
    /// </summary>
    public class Settings : IAlgorithmSettings, INetworkSettings, IStorageSettings
    {
        private static readonly JsonSerializerOptions s_jsonOpts = new JsonSerializerOptions { WriteIndented = true };
        // 2026-09-15: 改为跟随统一数据根（DataPaths），不再写死在 exe 目录。
        // 用计算属性而非静态字段，确保取值发生在数据根解析与历史数据迁移之后。
        private static string SettingsFolder => DataPaths.SettingsDir;
        private static string SettingsFile => DataPaths.SettingsFile;

        // ===== 子配置类实例（不参与 JSON 序列化，保持扁平结构）=====
        // 子配置类的数据通过下方的转发属性被序列化/反序列化
        [JsonIgnore]
        public StorageSettings Storage { get; set; } = new();

        [JsonIgnore]
        public UiSettings Ui { get; set; } = new();

        [JsonIgnore]
        public NetworkSettings Network { get; set; } = new();

        [JsonIgnore]
        public DatabaseSettings Database { get; set; } = new();

        [JsonIgnore]
        public AlgorithmSettings Algorithm { get; set; } = new();

        [JsonIgnore]
        public SecuritySettings Security { get; set; } = new();

        /// <summary>AI 对话模块配置（2026-09-12 新增）</summary>
        [JsonIgnore]
        public AiSettings Ai { get; set; } = new();

        /// <summary>自定义通讯协议配置（2026-09-13 新增，模板段方式定义输出格式）</summary>
        [JsonIgnore]
        public ProtocolSettings Protocol { get; set; } = new();

        // ===== 转发属性（参与 JSON 序列化，保持扁平结构与向后兼容）=====
        // 所有 Settings.Instance.Xxx 调用与 _settings.Xxx 调用继续正常工作

        // --- Storage 文件保存策略 ---
        public bool IsSaveDraw { get => Storage.IsSaveDraw; set => Storage.IsSaveDraw = value; }
        public bool IsSaveSource { get => Storage.IsSaveSource; set => Storage.IsSaveSource = value; }
        public int MinRecentDays { get => Storage.MinRecentDays; set => Storage.MinRecentDays = value; }
        public string PicturesSaveFolder { get => Storage.PicturesSaveFolder; set => Storage.PicturesSaveFolder = value; }

        // --- Ui 界面配置 ---
        public int WindowWidth { get => Ui.WindowWidth; set => Ui.WindowWidth = value; }
        public int WindowHeight { get => Ui.WindowHeight; set => Ui.WindowHeight = value; }
        public string Language { get => Ui.Language; set => Ui.Language = value; }
        public string Theme { get => Ui.Theme; set => Ui.Theme = value; }
        public string WindowTitle { get => Ui.WindowTitle; set => Ui.WindowTitle = value; }

        // --- Network 网络配置 ---
        public string ConnectivityCheckIP { get => Network.ConnectivityCheckIP; set => Network.ConnectivityCheckIP = value; }
        public string DetectionResultSendIP { get => Network.DetectionResultSendIP; set => Network.DetectionResultSendIP = value; }
        public int EncoderReceiverPort { get => Network.EncoderReceiverPort; set => Network.EncoderReceiverPort = value; }
        public int DetectionResultSendPort { get => Network.DetectionResultSendPort; set => Network.DetectionResultSendPort = value; }
        public string MessageReceiver { get => Network.MessageReceiver; set => Network.MessageReceiver = value; }
        public int MaxRetries { get => Network.MaxRetries; set => Network.MaxRetries = value; }

        // --- Database 数据库配置 ---
        public int BarcodeDataRetentionDays { get => Database.BarcodeDataRetentionDays; set => Database.BarcodeDataRetentionDays = value; }
        public int BarcodeDataMaxCount { get => Database.BarcodeDataMaxCount; set => Database.BarcodeDataMaxCount = value; }
        public int LogRetentionDays { get => Database.LogRetentionDays; set => Database.LogRetentionDays = value; }
        public int LogMaxCount { get => Database.LogMaxCount; set => Database.LogMaxCount = value; }
        public int DataCleanupIntervalHours { get => Database.DataCleanupIntervalHours; set => Database.DataCleanupIntervalHours = value; }
        public int BackupExpireDays { get => Database.BackupExpireDays; set => Database.BackupExpireDays = value; }
        public int BarcodeRemoveCount { get => Database.BarcodeRemoveCount; set => Database.BarcodeRemoveCount = value; }
        public int LogsCount { get => Database.LogsCount; set => Database.LogsCount = value; }

        // --- Algorithm 算法参数 ---
        public bool DedupEnabled { get => Algorithm.DedupEnabled; set => Algorithm.DedupEnabled = value; }
        public bool DedupByEncoder { get => Algorithm.DedupByEncoder; set => Algorithm.DedupByEncoder = value; }
        public string DedupTrackAxis { get => Algorithm.DedupTrackAxis; set => Algorithm.DedupTrackAxis = value; }
        public double DedupPositionThreshold { get => Algorithm.DedupPositionThreshold; set => Algorithm.DedupPositionThreshold = value; }
        public double DedupAngleThreshold { get => Algorithm.DedupAngleThreshold; set => Algorithm.DedupAngleThreshold = value; }
        public int TrackerExpireSeconds { get => Algorithm.TrackerExpireSeconds; set => Algorithm.TrackerExpireSeconds = value; }

        // --- 四边独立边缘最小间距（2026-09-05 由单一 EdgeMinMarginPixels 拆分）---
        public double EdgeMarginLeftPixels { get => Algorithm.EdgeMarginLeftPixels; set => Algorithm.EdgeMarginLeftPixels = value; }
        public double EdgeMarginTopPixels { get => Algorithm.EdgeMarginTopPixels; set => Algorithm.EdgeMarginTopPixels = value; }
        public double EdgeMarginRightPixels { get => Algorithm.EdgeMarginRightPixels; set => Algorithm.EdgeMarginRightPixels = value; }
        public double EdgeMarginBottomPixels { get => Algorithm.EdgeMarginBottomPixels; set => Algorithm.EdgeMarginBottomPixels = value; }

        // --- 产品掩码面积上下限过滤（2026-09-07，原图像素；0=禁用）---
        public double MinMaskAreaPixels { get => Algorithm.MinMaskAreaPixels; set => Algorithm.MinMaskAreaPixels = value; }
        public double MaxMaskAreaPixels { get => Algorithm.MaxMaskAreaPixels; set => Algorithm.MaxMaskAreaPixels = value; }
        public bool SpcEnabled { get => Algorithm.SpcEnabled; set => Algorithm.SpcEnabled = value; }
        public int SpcHours { get => Algorithm.SpcHours; set => Algorithm.SpcHours = value; }
        public int SpcSubgroupSize { get => Algorithm.SpcSubgroupSize; set => Algorithm.SpcSubgroupSize = value; }

        // --- Security 安全配置 ---
        public string UserName { get => Security.UserName; set => Security.UserName = value; }
        public string Password { get => Security.Password; set => Security.Password = value; }
        public int ExistLoginTimeout { get => Security.ExistLoginTimeout; set => Security.ExistLoginTimeout = value; }
        public string CurrentRecipeName { get => Security.CurrentRecipeName; set => Security.CurrentRecipeName = value; }
        /// <summary>2026-09-16: 授权门禁开关（运行期，默认启用）。替代原 App 内的编译期常量。</summary>
        public bool LicenseRequired { get => Security.LicenseRequired; set => Security.LicenseRequired = value; }

        // --- AI 对话模块（2026-09-12 新增，扁平转发以参与 JSON 序列化）---
        public bool AiEnabled { get => Ai.Enabled; set => Ai.Enabled = value; }
        public string AiModelPath { get => Ai.ModelPath; set => Ai.ModelPath = value; }
        public string AiBackend { get => Ai.Backend; set => Ai.Backend = value; }
        public int AiGpuLayers { get => Ai.GpuLayers; set => Ai.GpuLayers = value; }
        public uint AiContextSize { get => Ai.ContextSize; set => Ai.ContextSize = value; }
        public float AiTemperature { get => Ai.Temperature; set => Ai.Temperature = value; }
        public int AiMaxTokens { get => Ai.MaxTokens; set => Ai.MaxTokens = value; }
        public bool AiLoadOnDemand { get => Ai.LoadOnDemand; set => Ai.LoadOnDemand = value; }
        public bool AiAllowWriteTools { get => Ai.AllowWriteTools; set => Ai.AllowWriteTools = value; }
        public int AiMaxQueryRows { get => Ai.MaxQueryRows; set => Ai.MaxQueryRows = value; }
        public int AiMaxQueryDays { get => Ai.MaxQueryDays; set => Ai.MaxQueryDays = value; }
        public string AiLlamaServerExe { get => Ai.LlamaServerExe; set => Ai.LlamaServerExe = value; }
        public int AiLlamaServerPort { get => Ai.LlamaServerPort; set => Ai.LlamaServerPort = value; }
        public string AiKnowledgeDocsFolder { get => Ai.KnowledgeDocsFolder; set => Ai.KnowledgeDocsFolder = value; }

        // --- 自定义通讯协议（2026-09-13 新增，扁平转发参与 JSON 序列化）---
        public List<ProtocolTemplateConfig> ProtocolTemplates { get => Protocol.Protocols; set => Protocol.Protocols = value; }

        // --- 知识库语料噪声排除（2026-09-13 配置化，替代硬编码黑名单）---
        public List<string> AiKnowledgeExcludedFiles { get => Ai.KnowledgeExcludedFiles; set => Ai.KnowledgeExcludedFiles = value; }

        /// <summary>
        /// 设置变更事件（弱事件模式，订阅者被 GC 回收时自动取消订阅，无内存泄漏风险）
        /// </summary>
        public event EventHandler Changed
        {
            add => SettingsChangedEventManager.Current.AddSubscriber(this, value);
            remove => SettingsChangedEventManager.Current.RemoveSubscriber(this, value);
        }

        internal void RaiseChanged() => SettingsChangedEventManager.Current.HandleChanged(this);

        /// <summary>
        /// 私有构造函数，供 JSON 反序列化和单例创建使用
        /// </summary>
        [JsonConstructor]
        private Settings() { }

        /// <summary>
        /// 单例实例，首次访问时从 JSON 文件加载。
        ///
        /// <para>2026-09-16: 由"双重检查 + volatile"改为 <see cref="Lazy{T}"/>。
        /// 原写法是 <c>if (_instance == null) _instance = Load();</c> —— <b>没有锁</b>，
        /// 多线程同时首访会各自 <c>Load()</c> 出不同实例，后写入者覆盖先写入者；
        /// 更麻烦的是先拿到的调用方可能一直握着那个被丢弃的实例（订阅/写了属性都不会生效）。
        /// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/> 保证只执行一次且各线程拿到同一实例。</para>
        /// </summary>
        public static Settings Instance => s_instance.Value;

        private static readonly Lazy<Settings> s_instance = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 串行化 <see cref="Save"/> 与 <see cref="Reload"/>：
        /// 两者都读写同一批属性、并操作同一个文件，并发时可能写出"半更新"状态
        /// （例如 Reload 正把新实例的属性拷回，Save 已把中间态序列化落盘），
        /// 或两个 Save 同时走 <c>File.Replace</c> 互相踩踏。
        /// </summary>
        private readonly object _persistLock = new();

        /// <summary>定期落盘的间隔（秒）。</summary>
        private const int PeriodicPersistIntervalSec = 60;

        private Timer? _persistTimer;
        private int _persistRunning;

        /// <summary>最近一次实际写入文件的 JSON（用于"内容未变则不写"判断）。</summary>
        private string? _lastWrittenJson;

        /// <summary>
        /// 将当前设置序列化为 JSON 并原子写入文件，同时触发 Changed 事件。
        /// </summary>
        public void Save()
        {
            var json = SerializeAndWrite();
            _lastWrittenJson = json;
            RaiseChanged();
        }

        /// <summary>
        /// 序列化并原子写入（不触发 Changed）。
        /// L15: 先写临时文件再替换，避免崩溃导致配置文件损坏。
        /// </summary>
        private string SerializeAndWrite()
        {
            var json = JsonSerializer.Serialize(this, s_jsonOpts);
            lock (_persistLock)
            {
                if (!Directory.Exists(SettingsFolder))
                    Directory.CreateDirectory(SettingsFolder);

                var tempFile = SettingsFile + ".tmp";
                File.WriteAllText(tempFile, json);
                if (File.Exists(SettingsFile))
                {
                    File.Replace(tempFile, SettingsFile, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(tempFile, SettingsFile);
                }
            }

            return json;
        }

        /// <summary>
        /// 内容有变化才落盘，返回是否真的写了文件。
        ///
        /// <para>2026-09-16: 用于<b>定期落盘</b>，消除"配置只在显式 Save() 时才保存"的丢失窗口——
        /// 原先只有设置页/切配方/退出三处会调 <see cref="Save"/>，
        /// 任何"直接改了属性但没调 Save"的路径在进程崩溃/断电时都会静默丢失。
        /// 现在由 <see cref="StartPeriodicPersist"/> 起的定时器周期性调用本方法，
        /// 把任何运行期改动的最长丢失窗口收敛到 <see cref="PeriodicPersistIntervalSec"/> 秒。</para>
        ///
        /// <para>两个刻意的设计：① <b>内容相同就不写</b>（避免无谓的磁盘写入与 mtime 抖动）；
        /// ② <b>不触发 Changed 事件</b>——纯持久化刷新并不是"设置被改动了"，
        /// 若在此处发事件，订阅方（UI/算法）会每隔一个周期被无意义地触发一次。</para>
        /// </summary>
        /// <returns>true = 内容有变化并已落盘；false = 无变化，未写盘。</returns>
        public bool TryFlushToDiskIfChanged()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, s_jsonOpts);
                if (string.Equals(json, _lastWrittenJson, StringComparison.Ordinal))
                {
                    return false;
                }

                SerializeAndWrite();
                _lastWrittenJson = json;
                return true;
            }
            catch (Exception ex)
            {
                // 落盘失败不抛给调用方（定时器回调里抛异常会静默丢失）：记日志，下一轮会重试
                try { LogService.Instance.Warning($"设置定期落盘失败（下一周期重试）: {ex.Message}"); } catch { }
                return false;
            }
        }

        /// <summary>
        /// 启动定期落盘定时器（幂等）。由 <c>App.ReloadSettingsAndStartCleanup</c> 在设置加载完成后调用。
        /// </summary>
        public void StartPeriodicPersist()
        {
            if (Interlocked.Exchange(ref _persistRunning, 1) == 1)
            {
                return;
            }

            _persistTimer = new Timer(
                static state => ((Settings)state!).TryFlushToDiskIfChanged(),
                this,
                TimeSpan.FromSeconds(PeriodicPersistIntervalSec),
                TimeSpan.FromSeconds(PeriodicPersistIntervalSec));
        }

        /// <summary>停止定期落盘（退出流程调用；此后由显式的 <see cref="Save"/> 负责最后一次落盘）。</summary>
        public void StopPeriodicPersist()
        {
            if (Interlocked.Exchange(ref _persistRunning, 0) == 0)
            {
                return;
            }

            var timer = _persistTimer;
            _persistTimer = null;
            timer?.Dispose();
        }

        /// <summary>
        /// 从 JSON 文件加载设置，文件不存在或损坏时返回默认实例
        /// </summary>
        private static Settings Load()
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                // 2026-09-05：旧版 settings.json 只有 EdgeMinMarginPixels 单值 → 拆分为四条边同值，避免升级后阈值被默认值覆盖
                json = MigrateLegacyEdgeMargins(json);
                try
                {
                    return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
                catch (JsonException)
                {
                    // M183: 损坏的 JSON 文件备份为 settings.json.corrupt，返回默认实例
                    var corruptPath = SettingsFile + ".corrupt";
                    try
                    {
                        if (File.Exists(corruptPath)) File.Delete(corruptPath);
                        File.Move(SettingsFile, corruptPath);
                        LogService.Instance.Warning($"设置文件已损坏，已备份为 {corruptPath}");
                    }
                    catch (Exception backupEx)
                    {
                        LogService.Instance.Warning($"备份损坏的设置文件失败: {backupEx}");
                    }
                    return new Settings();
                }
            }
            return new Settings();
        }

        /// <summary>
        /// 旧版 settings.json 迁移：若存在单值 EdgeMinMarginPixels 且尚无四条新键，
        /// 将原值复制到 EdgeMargin{Left,Top,Right,Bottom}Pixels 后删除旧键。
        /// 已是新版（含任一新键）或文件非法时原样返回。
        /// </summary>
        private static string MigrateLegacyEdgeMargins(string json)
        {
            try
            {
                // JsonNode 非 IDisposable（不同于 JsonDocument），无需 using
                var node = JsonNode.Parse(json);
                if (node is not JsonObject root
                    || !root.TryGetPropertyValue("EdgeMinMarginPixels", out var legacy)
                    || legacy is null)
                {
                    return json;
                }
                bool hasNew = root.ContainsKey("EdgeMarginLeftPixels")
                    || root.ContainsKey("EdgeMarginTopPixels")
                    || root.ContainsKey("EdgeMarginRightPixels")
                    || root.ContainsKey("EdgeMarginBottomPixels");
                if (hasNew)
                {
                    return json;
                }

                double value = 10;
                if (legacy is not JsonValue legacyValue || !legacyValue.TryGetValue<double>(out value))
                {
                    return json;
                }
                root["EdgeMarginLeftPixels"] = value;
                root["EdgeMarginTopPixels"] = value;
                root["EdgeMarginRightPixels"] = value;
                root["EdgeMarginBottomPixels"] = value;
                root.Remove("EdgeMinMarginPixels");
                return root.ToJsonString(s_jsonOpts);
            }
            catch (Exception)
            {
                // 解析失败交由上层 JsonException 处理（备份 corrupt 文件并返回默认实例）
                return json;
            }
        }

        /// <summary>
        /// 从文件重新加载所有设置属性到当前实例，并触发 Changed 事件
        /// M51: 使用反射自动复制所有可写公共属性，避免新增属性时遗漏同步
        ///
        /// <para>2026-09-16: 属性拷贝阶段整体持 <see cref="_persistLock"/>，
        /// 避免与并发的 <see cref="Save"/> 交叉——否则 Save 可能把"重载到一半"的中间态序列化落盘。</para>
        /// </summary>
        public void Reload()
        {
            var reloaded = Load();
            lock (_persistLock)
            {
                foreach (var prop in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.CanWrite && prop.GetSetMethod() is not null)
                    {
                        // L298: 每个属性单独 try-catch，避免单个属性异常中断整个 Reload
                        try
                        {
                            prop.SetValue(this, prop.GetValue(reloaded));
                        }
                        catch (Exception ex)
                        {
                            LogService.Instance.Warning($"重载设置属性 {prop.Name} 失败: {ex}");
                        }
                    }
                }
            }

            // 重载后内存态已与文件一致：刷新基线，避免定期落盘立刻再写一次同样的内容
            _lastWrittenJson = JsonSerializer.Serialize(this, s_jsonOpts);

            RaiseChanged();
        }
    }

    /// <summary>
    /// Settings.Changed 事件的弱事件管理器，避免长生命周期订阅者阻止 GC 回收 Settings 实例
    /// </summary>
    internal sealed class SettingsChangedEventManager : WeakEventManager
    {
        private static readonly SettingsChangedEventManager s_manager = new();

        internal static SettingsChangedEventManager Current => s_manager;

        private SettingsChangedEventManager() { }

        public void AddSubscriber(Settings source, EventHandler handler) => ProtectedAddHandler(source, handler);

        public void RemoveSubscriber(Settings source, EventHandler handler) => ProtectedRemoveHandler(source, handler);

        public void HandleChanged(Settings source)
        {
            DeliverEvent(source, EventArgs.Empty);
        }

        protected override void StartListening(object source)
        {
            // Settings 通过 RaiseChanged() 主动触发，无需在此订阅底层事件
        }

        protected override void StopListening(object source)
        {
            // Settings 通过 RaiseChanged() 主动触发，无需在此取消订阅底层事件
        }
    }
}
