using Microsoft.ML.OnnxRuntime;
using Xunit;

namespace JinlongYolo.Tests;

/// <summary>
/// 2026-09-17: OpenVINO EP 可用性探测与显式 Apply 路径的行为验证。
/// 背景：Microsoft.ML.OnnxRuntime.Gpu 等官方包的 native 构建不含 OpenVINO EP，
/// AppendExecutionProvider 反射调用会抛 OnnxRuntimeException(InvalidArgument)。
/// 修复后 Apply 应短路抛干净的 NotSupportedException（回退链日志可直接显示原因），
/// 且探测结果进程级缓存（OpenVINO-GPU 失败后 OpenVINO-CPU 级不再重复反射尝试）。
/// </summary>
public class OpenVinoSessionConfiguratorTests
{
    [Fact]
    public void IsAvailable_IsStableAcrossCalls()
    {
        // 探测结果必须缓存：同一进程内多次询问结果一致（无论本机是否带 OpenVINO EP）
        var first = OpenVinoSessionConfigurator.IsAvailable();
        var second = OpenVinoSessionConfigurator.IsAvailable();
        Assert.Equal(first, second);
    }

    [Fact]
    public void Apply_WhenProviderMissing_ThrowsNotSupportedExceptionWithReadableMessage()
    {
        if (OpenVinoSessionConfigurator.IsAvailable())
        {
            // 本机 ORT 构建含 OpenVINO EP，缺失场景不可测（如 Intel.ML.OnnxRuntime.OpenVino 包）
            return;
        }

        using var sessionOptions = new SessionOptions();

        var ex = Assert.Throws<NotSupportedException>(
            () => OpenVinoSessionConfigurator.Apply(sessionOptions, new OpenVinoOptions()));

        // 关键断言：异常必须干净可读——不能再是 TargetInvocationException 的
        // "Exception has been thrown by the target of an invocation."
        Assert.DoesNotContain("target of an invocation", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenVINO", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
