using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// CudaRuntimeLoader 静态工具单元测试。
/// 验证 IsCudaAvailable/TryPreloadCudaDlls 返回 bool 且不抛异常，
/// 不依赖实际 CUDA 运行时是否安装（结果取决于运行环境）。
/// </summary>
public class CudaRuntimeLoaderTests
{
    [Fact]
    public void IsCudaAvailable_ReturnsBoolWithoutThrowing()
    {
        var exception = Record.Exception(() => CudaRuntimeLoader.IsCudaAvailable());

        Assert.Null(exception);
        var result = CudaRuntimeLoader.IsCudaAvailable();
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void TryPreloadCudaDlls_ReturnsBoolWithoutThrowing()
    {
        var exception = Record.Exception(() => CudaRuntimeLoader.TryPreloadCudaDlls());

        Assert.Null(exception);
        var result = CudaRuntimeLoader.TryPreloadCudaDlls();
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void IsCudaAvailable_ResultIsConsistentWithinSession()
    {
        // 同一进程内、无外部环境变更时，重复调用应返回一致结果
        var first = CudaRuntimeLoader.IsCudaAvailable();
        var second = CudaRuntimeLoader.IsCudaAvailable();

        Assert.Equal(first, second);
    }
}
