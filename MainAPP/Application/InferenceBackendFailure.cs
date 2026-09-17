using System;
using System.Runtime.InteropServices;

namespace MainAPP.Application;

/// <summary>
/// 「推理后端是否已失效」判定（纯函数，无状态，便于单测）。
/// <para>用途：分割推理失败后决定是否值得触发后端降级重建（CUDA → OpenVINO GPU → OpenVINO CPU → CPU）。
/// 判错两个方向都有代价：漏判 ⇒ "会话建得起、一跑就炸"时每帧静默失败且永不自愈；
/// 误判 ⇒ 设备正常却把业务异常当后端损坏，一路无谓降级到 CPU（性能塌陷且现场难以察觉）。</para>
/// <para>2026-09-16: 从 HomeViewModel 抽出并扩展判定范围。原实现只认
/// <c>Microsoft.ML.OnnxRuntime.OnnxRuntimeException</c>，但 CUDA / cuDNN / OpenVINO 的原生失败
/// 常常不以该类型出现——有的被包装成其它托管异常（内层才带原始错误码），
/// 有的只有一句消息（如 <c>CUDNN_FE failure 7</c>、<c>cudaErrorIllegalAddress</c>、
/// <c>AssertionFailed: device 0</c>），甚至直接以访问冲突 / SEH 形式抛出。</para>
/// </summary>
public static class InferenceBackendFailure
{
    /// <summary>
    /// 判断异常是否属于「推理后端会话已失效 / 原生后端执行失败」。
    /// 会遍历 <see cref="Exception.InnerException"/> 链（原生错误常被外层包装）。
    /// </summary>
    public static bool IsBackendFailure(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            // 取消与「已释放」不是后端失效，必须优先排除：
            // 否则退出、暂停或换池过程中的正常取消会被误判为后端损坏并触发降级重建。
            if (current is OperationCanceledException || current is ObjectDisposedException)
            {
                return false;
            }

            if (current is Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
            {
                return true;
            }

            // 原生后端崩溃常见的冒泡形态：
            // 非托管内存越界 / SEH / 运行期 DLL 缺失 / 架构不符 / 类型初始化失败
            if (current is AccessViolationException
                or SEHException
                or DllNotFoundException
                or BadImageFormatException
                or TypeInitializationException)
            {
                return true;
            }

            if (ContainsBackendFailureMarker(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 消息中是否含原生推理后端（CUDA / cuDNN / cuBLAS / OpenVINO / oneDNN）的失败特征词。
    /// 这些库多以裸错误码或英文短语抛出（无托管异常类型可用），只能按特征词识别。
    /// 特征词刻意保持"窄"：宁可漏判（保持现状不改），也不要误判成后端损坏。
    /// </summary>
    public static bool ContainsBackendFailureMarker(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        foreach (var marker in BackendFailureMarkers)
        {
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>原生推理后端的失败特征词（大小写不敏感匹配）。</summary>
    private static readonly string[] BackendFailureMarkers =
    {
        "CUDNN",            // cuDNN：CUDNN_FE failure / CUDNN_STATUS_*
        "CUDA",             // cudaError* / CUDA failure / CUDA_ERROR_*
        "CUBLAS",
        "NVRTC",
        "ONNXRUNTIME",      // 非托管侧以 RuntimeException 文本冒泡时
        "OPENVINO",
        "INFERENCEENGINE",
        "IE_CORE",
        "MKLDNN",           // oneDNN / MKLDNN 后端
        "DNNL",
        "FAILED TO RUN",    // ORT 的 "Failed to run ... kernel"
        "ASSERTIONFAILED",  // ORT 原生断言，常伴 device 0 / provider 名
        "DEVICE 0",         // 原生侧报错常带设备号
    };
}
