using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// 工位标识（<c>DbModel.Station</c>）依赖的本机机器码可用性测试（2026-09-12 新增）。
///
/// <para>背景：<c>DbModel.Station</c> 的取值复用项目既有的机器身份
/// <see cref="LicenseService.MachineCodeText"/>（激活窗口展示的那个「本机机器码」），
/// 由 CPU 标识 + 系统盘卷序列号 + 首个物理网卡 MAC + 机器名 哈希而成
/// （2026-09-16 起指纹已移除 Windows 用户名，换账户不再使工位漂移）。</para>
///
/// <para>2026-09-16：采集逻辑由 <c>Application.DetectionRecordService</c> 的静态成员
/// 下沉到 <see cref="StationCodeProvider"/>（Services 层）—— 该值本质是"本机硬件身份"，
/// 且被 Services 层（AiChatService）使用，挂在 Application 层会构成反向依赖。</para>
///
/// <para>为什么要测：采集链上有注册表读取、<c>GetVolumeInformation</c> P/Invoke 与网卡枚举，
/// 任一环节异常都会被上层 <c>try-catch</c> 吞掉并让 Station 退化为 NULL——
/// <b>功能不会报错，但追溯链会静默断裂</b>（同一台机器的记录不再归到同一工位）。
/// 这里把「必须能拿到非空值」与「多次取值必须一致」两条前提钉住。</para>
/// </summary>
public class MachineCodeForStationTests
{
    /// <summary>机器码必须非空，否则 Station 会写 NULL、过站链断掉。</summary>
    [Fact]
    public void MachineCodeText_IsNotEmpty()
    {
        string code = LicenseService.MachineCodeText;

        Assert.False(string.IsNullOrWhiteSpace(code), "机器码为空：Station 将写 NULL，追溯链会静默断裂");
    }

    /// <summary>
    /// 同一进程内多次取值必须一致。
    /// <see cref="StationCodeProvider"/> 用 Lazy 只采集一次——若底层每次返回不同值，
    /// 缓存反而会掩盖"同机不同工位"的数据错乱，故这里显式验证幂等性。
    /// </summary>
    [Fact]
    public void MachineCodeText_IsStableAcrossCalls()
    {
        string first = LicenseService.MachineCodeText;
        string second = LicenseService.MachineCodeText;

        Assert.Equal(first, second);
    }

    /// <summary>落库所用的工位标识本身必须可用（它是 Station 列的唯一来源）。</summary>
    [Fact]
    public void StationCodeProvider_ReturnsUsableCode()
    {
        string? code = StationCodeProvider.Current;

        Assert.False(string.IsNullOrWhiteSpace(code), "工位标识为空：Station 将写 NULL，追溯链会静默断裂");
        Assert.Equal(code, StationCodeProvider.Current); // 进程内缓存，多次取值一致
    }
}
