using MainAPP.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
        private static volatile Settings? _instance;
        private static readonly JsonSerializerOptions s_jsonOpts = new JsonSerializerOptions { WriteIndented = true };
        // L266: 路径使用多参数 Path.Combine，避免正斜杠跨平台兼容性问题
        private static readonly string SettingsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves", "Settings");
        private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

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

        // --- Security 安全配置 ---
        public string UserName { get => Security.UserName; set => Security.UserName = value; }
        public string Password { get => Security.Password; set => Security.Password = value; }
        public int ExistLoginTimeout { get => Security.ExistLoginTimeout; set => Security.ExistLoginTimeout = value; }
        public string CurrentRecipeName { get => Security.CurrentRecipeName; set => Security.CurrentRecipeName = value; }

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
        /// 单例实例，首次访问时从 JSON 文件加载
        /// </summary>
        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        /// <summary>
        /// 将当前设置序列化为 JSON 并写入文件，同时触发 Changed 事件
        /// </summary>
        public void Save()
        {
            if (!Directory.Exists(SettingsFolder))
                Directory.CreateDirectory(SettingsFolder);

            var json = JsonSerializer.Serialize(this, s_jsonOpts);
            // L15: 原子写入，先写临时文件再替换，避免崩溃导致配置文件损坏
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
            RaiseChanged();
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
        /// </summary>
        public void Reload()
        {
            var reloaded = Load();
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
