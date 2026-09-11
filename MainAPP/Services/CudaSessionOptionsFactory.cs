using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;

namespace MainAPP.Services
{
    /// <summary>
    /// 构建带显存约束的 ONNX Runtime CUDA 会话选项。
    /// </summary>
    /// <remarks>
    /// REVIEW(2026-09-04): 为什么需要显式约束？
    /// <para>
    /// 部署的分割模型输入为 [1,3,1280,992]，首个卷积即在全分辨率上计算。
    /// 而 ONNX Runtime CUDA EP 的默认策略是
    /// <c>cudnn_conv_algo_search = EXHAUSTIVE</c> + <c>cudnn_conv_use_max_workspace = 1</c>：
    /// 建会话时会用「cuDNN 允许的最大工作区」去穷举卷积算法，1280×992 下该工作区可达 GB 级。
    /// </para>
    /// <para>
    /// 叠加主分割池 2 个 CUDA 会话 + 角度池 1 个会话并发，在 8 GB 显存的卡上极易打满，
    /// 表现为首个 Conv 节点抛 <c>CUDNN_FE failure 7: GRAPH_EXECUTION_FAILED</c>。
    /// 因为取决于并发时序，它会「同一份程序、这台正常那台报错、时好时坏」。
    /// </para>
    /// <para>
    /// 本工厂把四个维度收敛：限制单会话 arena、arena 按需扩展（不翻倍）、
    /// 改用启发式算法搜索、关闭 max workspace。
    /// </para>
    /// </remarks>
    public static class CudaSessionOptionsFactory
    {
        private const long BytesPerMiB = 1024L * 1024L;

        /// <summary>主分割模型单个会话的显存上限（默认 2 GiB）。</summary>
        public const int SegmentationGpuMemoryMiB = 2048;

        /// <summary>角度模型单个会话的显存上限（默认 1 GiB，输入更小）。</summary>
        public const int AngleGpuMemoryMiB = 1024;

        public static long FromMiB(int mib) => mib * BytesPerMiB;

        /// <summary>
        /// 创建启用 CUDA EP 并施加显存约束的会话选项。
        /// </summary>
        /// <param name="deviceId">CUDA 设备号。</param>
        /// <param name="gpuMemLimitBytes">该会话 arena 的显存上限（字节）。</param>
        /// <remarks>
        /// 返回的实例可安全地被多个 <see cref="InferenceSession"/> 复用，按进程生命周期持有即可，
        /// 不要在使用后释放。
        /// </remarks>
        public static SessionOptions Create(int deviceId, long gpuMemLimitBytes)
        {
            var options = new SessionOptions();

            var providerOptions = new Dictionary<string, string>
            {
                ["device_id"] = deviceId.ToString(),
                // 单会话 arena 上限，防止多个会话合起来吃光整卡
                ["gpu_mem_limit"] = gpuMemLimitBytes.ToString(),
                // kSameAsRequested：按需扩展，避免 kNextPowerOfTwo 的翻倍超配
                ["arena_extend_strategy"] = "kSameAsRequested",
                // HEURISTIC：避免 EXHAUSTIVE 穷举期间申请巨大临时工作区
                // （注意：这两项是字符串枚举名，传数字会在 ORT 解析时报
                //   "Failed to map enum name to value"）
                ["cudnn_conv_algo_search"] = "HEURISTIC",
                // 0：不为卷积申请 cuDNN 最大工作区（1280×992 输入下该值可达 GB 级）
                ["cudnn_conv_use_max_workspace"] = "0",
                // 1：在默认流上做拷贝，避免与推理流的竞态
                ["do_copy_in_default_stream"] = "1",
            };

            // ORT 1.24 托管层没有 AppendExecutionProvider_CUDA(Dictionary) 重载，
            // 需经由 OrtCUDAProviderOptions.UpdateOptions 传入（AppendExecutionProvider_CUDA(int) 不支持任何配置）。
            using var cudaOptions = new OrtCUDAProviderOptions();
            cudaOptions.UpdateOptions(providerOptions);
            options.AppendExecutionProvider_CUDA(cudaOptions);

            LogService.Instance.Info(
                $"[CUDA] 会话选项: device_id={deviceId}, gpu_mem_limit={gpuMemLimitBytes / BytesPerMiB} MiB, " +
                "arena_extend_strategy=kSameAsRequested, cudnn_conv_algo_search=HEURISTIC, cudnn_conv_use_max_workspace=0");

            return options;
        }
    }
}
