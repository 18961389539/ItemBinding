using System;
using MainAPP.Models;

namespace MainAPP.Application;

/// <summary>
/// 检测落库所需「运行期上下文」的只读端口：本帧生效的算法参数 / 当前配方名 / 工位标识。
///
/// <para><b>为什么需要它</b>（2026-09-16）：<see cref="DetectionRecordService"/> 原先在
/// 检测热路径上直接读进程级单例（<c>Models.Settings.Instance.Algorithm.*</c> 6 处、
/// <c>RecipesManage.Instance.CurrentRecipe</c> 2 处），带来三个问题：</para>
/// <list type="number">
///   <item><b>不可单测</b>：构造与行为都绑定在全局单例上，测试无法脱离宿主准备配置；</item>
///   <item><b>依赖不可见</b>：从构造函数签名看不出这个服务需要哪些外部状态；</item>
///   <item><b>热路径重复取值</b>：同一帧内每个检测框都重读一遍同样的配置。</item>
/// </list>
///
/// <para><b>为什么用"每次读取都重新解析"的属性，而不是注入 AlgorithmSettings 实例</b>：
/// <c>Settings.Reload()</c> 用反射把新实例的**所有可写公共属性**拷回，
/// 其中包含 <c>Algorithm</c> 这类子配置对象——也就是 Reload 之后
/// <c>Settings.Instance.Algorithm</c> 指向的是**另一个对象**。
/// 若在构造期把子对象引用缓存下来，设置页任何一次重载都会让本服务读到**过期的配置**
/// （且这种错误是静默的）。因此本端口一律在**访问时**向数据源取值，
/// 由调用方负责"每帧只解析一次"（见 <see cref="DetectionRecordService"/> 的按帧快照）。</para>
/// </summary>
public interface IDetectionRuntimeConfig
{
    /// <summary>
    /// 当前生效的算法参数。★ 每次访问都会重新解析，不要跨帧缓存本引用
    /// （设置重载会替换子配置对象，缓存即读到过期值）。
    /// </summary>
    AlgorithmSettings Algorithm { get; }

    /// <summary>当前配方名；未设置当前配方时为 <c>null</c>。</summary>
    string? CurrentRecipeName { get; }

    /// <summary>工位标识（本机机器码）；采集失败时为 <c>null</c>。</summary>
    string? StationCode { get; }
}

/// <summary>
/// <see cref="IDetectionRuntimeConfig"/> 的委托实现：领域层提供实现骨架，
/// 组合根（<c>App.ConfigureServices</c>）把"去哪个单例取值"的具体逻辑作为委托注入。
///
/// <para>这样领域层里不出现任何 <c>XxxService.Instance</c> / <c>Settings.Instance</c>，
/// 而运行期仍读到实时值；测试则可直接传入自建委托，无需初始化宿主。</para>
/// </summary>
public sealed class DetectionRuntimeConfig : IDetectionRuntimeConfig
{
    private readonly Func<AlgorithmSettings> _algorithm;
    private readonly Func<string?> _currentRecipeName;
    private readonly Func<string?> _stationCode;

    /// <param name="algorithm">解析当前算法参数的委托（每次调用都应返回当前生效实例）。</param>
    /// <param name="currentRecipeName">解析当前配方名的委托。</param>
    /// <param name="stationCode">解析工位标识的委托。</param>
    public DetectionRuntimeConfig(
        Func<AlgorithmSettings> algorithm,
        Func<string?> currentRecipeName,
        Func<string?> stationCode)
    {
        _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        _currentRecipeName = currentRecipeName ?? throw new ArgumentNullException(nameof(currentRecipeName));
        _stationCode = stationCode ?? throw new ArgumentNullException(nameof(stationCode));
    }

    /// <inheritdoc />
    public AlgorithmSettings Algorithm => _algorithm();

    /// <inheritdoc />
    public string? CurrentRecipeName => _currentRecipeName();

    /// <inheritdoc />
    public string? StationCode => _stationCode();
}
