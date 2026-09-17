using MainAPP.Application;
using System;
using System.Runtime.InteropServices;
using Xunit;

namespace MainAPP.Tests.Application;

/// <summary>
/// 「推理后端是否失效」判定的回归测试。
/// 背景：该判定决定是否触发 CUDA → OpenVINO → CPU 的降级重建。
/// 漏判会让"会话建得起、一跑就炸"的场景每帧静默失败且永不自愈；
/// 误判会把正常业务的异常当成后端损坏，把设备一路无谓降到 CPU。
/// </summary>
public class InferenceBackendFailureTests
{
    [Fact]
    public void OnnxRuntimeException_IsBackendFailure()
    {
        // OnnxRuntimeException 的构造函数是 internal（由 ORT 内部抛出），测试无法直接 new。
        // GetUninitializedObject 跳过构造创建实例——本用例只验证"类型判定"这一分支，
        // 不涉及 Message 内容，因此不需要可用的状态。
        var ex = (Exception)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(Microsoft.ML.OnnxRuntime.OnnxRuntimeException));

        Assert.True(InferenceBackendFailure.IsBackendFailure(ex));
    }

    [Fact]
    public void Cancellation_IsNotBackendFailure()
    {
        Assert.False(InferenceBackendFailure.IsBackendFailure(new OperationCanceledException()));
        Assert.False(InferenceBackendFailure.IsBackendFailure(new TaskCanceledException()));
    }

    [Fact]
    public void ObjectDisposed_IsNotBackendFailure()
    {
        // 换池/退出期间已释放的池会抛此异常，触发重建没有意义
        Assert.False(InferenceBackendFailure.IsBackendFailure(new ObjectDisposedException("YoloPredictorPool")));
    }

    [Fact]
    public void GenericBusinessException_IsNotBackendFailure()
    {
        Assert.False(InferenceBackendFailure.IsBackendFailure(new InvalidOperationException("EdgeDetection 未配置")));
        Assert.False(InferenceBackendFailure.IsBackendFailure(new ArgumentException("图像为空")));
        Assert.False(InferenceBackendFailure.IsBackendFailure(new FormatException("barcode 解析失败")));
    }

    [Fact]
    public void NullException_IsNotBackendFailure()
    {
        Assert.False(InferenceBackendFailure.IsBackendFailure(null));
    }

    [Theory]
    // 原生后端最常见的"纯消息"抛出形式（无托管异常类型可判）
    [InlineData("CUDNN_FE failure 7")]
    [InlineData("CUDNN_STATUS_INTERNAL_ERROR")]
    [InlineData("cudaErrorIllegalAddress")]
    [InlineData("CUDA failure: cublasCreate returned 1")]
    [InlineData("Failed to run DnnlExecutionProvider kernel")]
    [InlineData("AssertionFailed: device 0")]
    [InlineData("OpenVINO: IE_CORE device not found")]
    public void NativeBackendMessages_AreBackendFailure(string message)
    {
        Assert.True(InferenceBackendFailure.IsBackendFailure(new InvalidOperationException(message)));
    }

    [Fact]
    public void WrappedNativeFailure_IsDetectedThroughInnerChain()
    {
        // 原生错误常被 ORT/上层包装一层，必须能穿透 InnerException 链
        var inner = new InvalidOperationException("CUDNN_FE failure 7");
        var outer = new AggregateException(new InvalidOperationException("推理失败", inner));
        Assert.True(InferenceBackendFailure.IsBackendFailure(outer));
    }

    [Fact]
    public void CancellationWrappingBackendFailure_IsNotBackendFailure()
    {
        // 最外层是取消 → 优先判定为"不是后端失效"，避免退出流程触发无谓重建
        var ex = new OperationCanceledException("取消", new InvalidOperationException("CUDA failure"));
        Assert.False(InferenceBackendFailure.IsBackendFailure(ex));
    }

    [Fact]
    public void NativeCrashShapes_AreBackendFailure()
    {
        Assert.True(InferenceBackendFailure.IsBackendFailure(new AccessViolationException()));
        Assert.True(InferenceBackendFailure.IsBackendFailure(new SEHException()));
        Assert.True(InferenceBackendFailure.IsBackendFailure(new DllNotFoundException("cudnn64_9.dll")));
        Assert.True(InferenceBackendFailure.IsBackendFailure(new BadImageFormatException()));
        Assert.True(InferenceBackendFailure.IsBackendFailure(new TypeInitializationException("OrtEnv", new InvalidOperationException())));
    }

    [Fact]
    public void MarkerMatching_IsCaseInsensitive()
    {
        Assert.True(InferenceBackendFailure.ContainsBackendFailureMarker("cudnn_fe failure"));
        Assert.True(InferenceBackendFailure.ContainsBackendFailureMarker("Cudnn_Fe Failure"));
    }

    [Fact]
    public void EmptyMessage_HasNoMarker()
    {
        Assert.False(InferenceBackendFailure.ContainsBackendFailureMarker(null));
        Assert.False(InferenceBackendFailure.ContainsBackendFailureMarker(string.Empty));
    }
}
