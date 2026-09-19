using System.Reflection;

namespace JinlongYolo.YoloSharp.Services;

internal static class OpenVinoSessionConfigurator
{
    private static readonly OpenVinoOptions ProbeOptions = new();
    private static int _availabilityState;

    public static bool IsAvailable()
    {
        var cachedState = Volatile.Read(ref _availabilityState);

        if (cachedState != 0)
        {
            return cachedState > 0;
        }

        var detectedState = ProbeAvailability() ? 1 : -1;
        Interlocked.CompareExchange(ref _availabilityState, detectedState, 0);

        return Volatile.Read(ref _availabilityState) > 0;
    }

    /// <summary>
    /// 将 OpenVINO 选项应用到给定的 <see cref="SessionOptions"/>，尝试通过反射调用不同的
    /// AppendExecutionProvider 方法签名以兼容不同的 ONNX Runtime 发行版。
    /// </summary>
    public static void Apply(SessionOptions sessionOptions, OpenVinoOptions options)
    {
        // 2026-09-17: 显式 OpenVINO 路径先查探测缓存。ORT native 不含 OpenVINO EP 的构建
        // （Microsoft.ML.OnnxRuntime.Gpu 等官方包不带 --use_openvino）在 Append 时会抛
        // OnnxRuntimeException(InvalidArgument: "OpenVINO execution provider is not supported in this build")，
        // 此前该异常穿透 TryInvoke 的过滤直接炸到调用方（TargetInvocationException 包裹，Message 不可读）。
        // 现在：探测缓存命中后直接抛干净的 NotSupportedException——回退链日志显示可读原因，
        // 且 OpenVINO-GPU 失败后，下一级 OpenVINO-CPU 靠缓存秒级短路，不再重复六次反射尝试。
        if (!IsAvailable())
        {
            throw new NotSupportedException("OpenVINO execution provider is not available. Ensure the current build references Intel.ML.OnnxRuntime.OpenVino and that the OpenVINO runtime can be resolved at runtime.");
        }

        var providerOptions = options.CreateProviderOptions();

        if (TryInvoke(sessionOptions, "AppendExecutionProvider", "OpenVINO", providerOptions)
            || TryInvoke(sessionOptions, "AppendExecutionProvider", "OpenVINOExecutionProvider", providerOptions)
            || TryInvoke(sessionOptions, "AppendExecutionProvider_OpenVINO_V2", providerOptions)
            || TryInvoke(sessionOptions, "AppendExecutionProvider_OpenVINO", options.DeviceType)
            || TryInvoke(sessionOptions, "AppendExecutionProvider", "OpenVINO")
            || TryInvoke(sessionOptions, "AppendExecutionProvider", "OpenVINOExecutionProvider"))
        {
            // 如果成功附加 OpenVINO 提供器，则禁用图优化以避免与 OpenVINO 的兼容性问题
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_DISABLE_ALL;
            return;
        }

        throw new NotSupportedException("OpenVINO execution provider is not available. Ensure the current build references Intel.ML.OnnxRuntime.OpenVino and that the OpenVINO runtime can be resolved at runtime.");
    }

    private static bool ProbeAvailability()
    {
        var providerOptions = ProbeOptions.CreateProviderOptions();

         return TryProbeWithFreshSessionOptions("AppendExecutionProvider", "OpenVINO", providerOptions)
             || TryProbeWithFreshSessionOptions("AppendExecutionProvider", "OpenVINOExecutionProvider", providerOptions)
             || TryProbeWithFreshSessionOptions("AppendExecutionProvider_OpenVINO_V2", providerOptions)
             || TryProbeWithFreshSessionOptions("AppendExecutionProvider_OpenVINO", ProbeOptions.DeviceType)
             || TryProbeWithFreshSessionOptions("AppendExecutionProvider", "OpenVINO")
             || TryProbeWithFreshSessionOptions("AppendExecutionProvider", "OpenVINOExecutionProvider");
    }

    /// <summary>
    /// 通过反射尝试调用目标对象上的指定方法（methodName），并传入参数。
    /// 适配不同的 ONNX Runtime API 签名。
    /// </summary>
    private static bool TryInvoke(object target, string methodName, params object[] arguments)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();

            if (parameters.Length != arguments.Length || !CanBind(parameters, arguments))
            {
                continue;
            }

            try
            {
                method.Invoke(target, arguments);
                return true;
            }
            // 2026-09-17: 补 OnnxRuntimeException——无 OpenVINO EP 的 ORT 构建在 Append 时抛
            // OnnxRuntimeException(InvalidArgument) 而非 NotSupportedException，此前穿透过滤器
            // 导致异常带着反射包装炸到回退链上层。视为"该签名/该构建不可用"，继续尝试其它签名。
            catch (TargetInvocationException ex) when (ex.InnerException is EntryPointNotFoundException or DllNotFoundException or NotSupportedException or OnnxRuntimeException)
            {
                // 忽略由本机绑定失败导致的异常，继续尝试其它签名
                continue;
            }
        }

        return false;
    }

    private static bool TryProbe(object target, string methodName, params object[] arguments)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();

            if (parameters.Length != arguments.Length || !CanBind(parameters, arguments))
            {
                continue;
            }

            try
            {
                method.Invoke(target, arguments);
                return true;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (OnnxRuntimeException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryProbeWithFreshSessionOptions(string methodName, params object[] arguments)
    {
        using var sessionOptions = new SessionOptions();

        return TryProbe(sessionOptions, methodName, arguments);
    }

    /// <summary>
    /// 判断给定参数是否可绑定到目标方法的参数列表（包括对 null 与值类型的检查）。
    /// </summary>
    private static bool CanBind(ParameterInfo[] parameters, object[] arguments)
    {
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameterType = parameters[index].ParameterType;
            var argument = arguments[index];

            if (argument == null)
            {
                if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                {
                    return false;
                }

                continue;
            }

            if (!parameterType.IsInstanceOfType(argument))
            {
                return false;
            }
        }

        return true;
    }
}
