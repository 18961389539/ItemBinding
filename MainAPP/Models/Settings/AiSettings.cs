namespace MainAPP.Models
{
    /// <summary>
    /// AI 对话模块配置（2026-09-12 新增）。
    ///
    /// 设计原则：只描述「接哪个模型、怎么接」，不内含任何业务逻辑。
    /// 上层统一通过 <c>Microsoft.Extensions.AI</c> 的 <c>IChatClient</c> 抽象访问模型，
    /// 因此换模型/换后端只改这里的配置，不改代码。
    /// </summary>
    public class AiSettings
    {
        /// <summary>
        /// 是否启用 AI 对话。默认关闭——本地模型要占 1~3 GB 显存/内存，
        /// 产线机节拍紧时应保持关闭，避免与 YOLO 推理抢资源。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// GGUF 模型文件的完整路径。
        /// ★ 硬约束：必须是<b>纯 ASCII 路径</b>。中文路径下 .NET 能读到文件与大小，
        /// 但 llama.cpp 原生层会报 "No such file or directory" 导致加载失败（本机已实测）。
        /// </summary>
        public string ModelPath { get; set; } = string.Empty;

        /// <summary>
        /// 推理后端：Vulkan / Cuda12 / Cpu。
        /// ★ Vulkan 只需显卡驱动；Cuda12 额外要求系统安装 CUDA 12 运行时
        /// （缺 cudart64_12 / cublas64_12 / cublasLt64_12 时会<b>静默回落 CPU</b>，本机已实测）。
        /// </summary>
        public string Backend { get; set; } = "Vulkan";

        /// <summary>
        /// 卸载到 GPU 的层数。0 = 纯 CPU；99 ≈ 全部卸载。
        /// </summary>
        public int GpuLayers { get; set; } = 99;

        /// <summary>
        /// 上下文长度。默认 32768（KV cache 约 1 GiB）。
        /// 实测：2048→64 MiB、32768→1024 MiB、131072→4096 MiB；本机 8GB 卡上限 131072，
        /// 标称 262144 会直接崩溃。改大前务必确认显存。
        /// </summary>
        public uint ContextSize { get; set; } = 32768;

        /// <summary>采样温度。意图翻译要求确定性，默认 0。</summary>
        public float Temperature { get; set; }

        /// <summary>单次生成的最大 token 数。</summary>
        public int MaxTokens { get; set; } = 1024;

        /// <summary>
        /// 是否按需加载模型（打开对话面板才加载、关闭即释放）。
        /// 默认 true——避免常驻占用显存。
        /// </summary>
        public bool LoadOnDemand { get; set; } = true;

        /// <summary>
        /// llama-server 侧车可执行文件（llama.cpp 官方预编译，OpenAI 兼容 HTTP 服务）。
        /// 进程内推理（LLamaSharp）已退役：0.27 的语法采样链有原生崩溃缺陷，
        /// 侧车方案同时解决 GBNF 约束与思考模式控制。
        /// </summary>
        public string LlamaServerExe { get; set; } = "D:/code/_llamasrv/bin/llama-server.exe";

        /// <summary>侧车监听端口（与 5188/5190/5000 错开）。</summary>
        public int LlamaServerPort { get; set; } = 8081;

        /// <summary>
        /// RAG 语料根目录（递归扫描 .md/.txt/.docx，目录黑名单剪枝）。
        /// 默认空 = 只用随应用分发的 <c>BaseDirectory/Saves/Knowledge/</c>；
        /// 需要额外语料时在设置页/配置里显式填写。
        /// （2026-09-13 起不再硬编码本机路径，避免换机部署即失效。）
        /// </summary>
        public string KnowledgeDocsFolder { get; set; } = string.Empty;

        /// <summary>
        /// 是否允许 AI 调用「写」类工具（调曝光/切配方等）。
        /// 默认 false：只读。开启后每次写操作仍会要求二次确认并落审计日志。
        /// </summary>
        public bool AllowWriteTools { get; set; }

        /// <summary>
        /// 单轮查询返回的最大行数。防止 AI 生成超大 limit 把界面和内存打爆。
        /// </summary>
        public int MaxQueryRows { get; set; } = 200;

        /// <summary>
        /// 单轮查询允许的最大时间跨度（天）。防止一次扫全表。
        /// </summary>
        public int MaxQueryDays { get; set; } = 30;
    }
}
