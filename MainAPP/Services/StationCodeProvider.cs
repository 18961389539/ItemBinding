using System;
using System.Threading;

namespace MainAPP.Services
{
    /// <summary>
    /// 工位标识（<c>DbModel.Station</c>）＝ 本机机器码的唯一来源。
    ///
    /// <para><b>为什么单独成类</b>：本值原先作为静态成员挂在
    /// <c>Application.DetectionRecordService</c> 上（<c>CurrentStation</c>），
    /// 而 <c>Services.AI.AiChatService</c> 也要用它 → 形成 Services → Application 的反向依赖。
    /// 该值的本质是"本机硬件身份"，属基础设施能力而非检测领域逻辑，故下沉到本层：
    /// Services 内部自用，Application 通过注入的访问器取值（见
    /// <see cref="MainAPP.Application.IDetectionRuntimeConfig.StationCode"/>），两侧都不再跨层。</para>
    ///
    /// <para><b>取值</b>：复用项目既有的机器身份 <see cref="LicenseService.MachineCodeText"/>
    /// —— 激活窗口里展示/可复制的那个「本机机器码」，由 CPU 标识 + 系统盘卷序列号 +
    /// 首个物理网卡 MAC + 机器名 哈希而成（见 <c>LicenseCore.HardwareFingerprint</c>，
    /// 零 WMI：注册表 + P/Invoke）。2026-09-16 起指纹已移除 Windows 用户名，
    /// 因此换账户运行不再使工位标识漂移。</para>
    ///
    /// <para>★ 必须缓存：采集含注册表读取、<c>GetVolumeInformation</c> P/Invoke 与网卡枚举，
    /// 若写在每条检测记录的热路径上（每秒数十条）会拖慢产线。
    /// 项目内既有先例——<c>LicenseService.IsActivated</c> 的注释即为
    /// 「缓存启动时结果，避免每帧采集硬件指纹」。</para>
    /// </summary>
    public static class StationCodeProvider
    {
        /// <summary>
        /// 当前工位标识（本机机器码）。进程内只采集一次；采集失败返回 <c>null</c>
        /// （列可空，NULL 表示"未记录"，比空串更利于 SQL 过滤与导出可读）。
        /// </summary>
        public static string? Current => Cached.Value;

        private static readonly Lazy<string?> Cached = new(
            static () =>
            {
                try
                {
                    var code = LicenseService.MachineCodeText;
                    return string.IsNullOrWhiteSpace(code) ? null : code;
                }
                catch (Exception ex)
                {
                    // 采集失败不应阻断检测与落库：Station 退化为 NULL（语义同"未记录"）
                    LogService.Instance.Warning($"采集本机机器码失败，Station 将写空: {ex.Message}");
                    return null;
                }
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
