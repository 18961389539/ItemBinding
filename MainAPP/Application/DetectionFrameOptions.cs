namespace MainAPP.Application;

/// <summary>
/// 单帧检测生效参数快照（2026-09-16）。
///
/// <para><b>为什么要有它</b>：这些值原先在 <see cref="DetectionRecordService.BuildAndSaveAsync"/>
/// 里**每个检测框**都重新解析一次（同一帧内重复读 <c>Settings.Instance.Algorithm</c>，
/// 单帧多目标即多读若干次），且取值路径埋在循环体内不易测试。
/// 现在改为帧首解析一次、循环内只读快照：语义更明确、热路径更省、也可单测。</para>
///
/// <para><b>为什么是"帧快照"而不是"构造期缓存"</b>：配置在运行期可被设置页改写，
/// 且 <c>Settings.Reload()</c> 会**替换**子配置对象，故必须逐帧重新取值
/// ——详见 <see cref="IDetectionRuntimeConfig"/> 的说明。</para>
/// </summary>
/// <param name="BrightnessDirectionEnabled">灰度判向有效开关（配方级覆盖 ?? 全局默认）。</param>
/// <param name="MarginLeft">检测框距图像左边的最小间距（原图像素）。</param>
/// <param name="MarginTop">检测框距图像上边的最小间距（原图像素）。</param>
/// <param name="MarginRight">检测框距图像右边的最小间距（原图像素）。</param>
/// <param name="MarginBottom">检测框距图像下边的最小间距（原图像素）。</param>
/// <param name="MaskAreaMinPixels">掩码面积下限（原图像素，0 = 禁用）。</param>
/// <param name="MaskAreaMaxPixels">掩码面积上限（原图像素，0 = 禁用）。</param>
/// <param name="FeaturePoolEnabled">头尾特征池是否启用。</param>
/// <param name="AdaptiveDeadbandEnabled">特征池自适应死区是否启用。</param>
/// <param name="FeatureDeadband">特征池固定死区基准。</param>
/// <param name="AdaptiveDeadbandK">自适应死区系数（基准 = k × 窗口统计）。</param>
/// <param name="StretchEnabled">灰度对比度拉伸是否启用。</param>
/// <param name="StretchLowPercentile">拉伸窗口低分位（0~100）。</param>
/// <param name="StretchHighPercentile">拉伸窗口高分位（0~100）。</param>
/// <param name="ResultOkScorePercent">OK/NG 判定阈值（百分比）。</param>
/// <param name="RecipeName">本帧记录归属的配方名；无当前配方时为 null。</param>
/// <param name="StationCode">工位标识（本机机器码）；采集失败时为 null。</param>
internal readonly record struct DetectionFrameOptions(
    bool BrightnessDirectionEnabled,
    double MarginLeft,
    double MarginTop,
    double MarginRight,
    double MarginBottom,
    double MaskAreaMinPixels,
    double MaskAreaMaxPixels,
    bool FeaturePoolEnabled,
    bool AdaptiveDeadbandEnabled,
    double FeatureDeadband,
    double AdaptiveDeadbandK,
    bool StretchEnabled,
    double StretchLowPercentile,
    double StretchHighPercentile,
    double ResultOkScorePercent,
    string? RecipeName,
    string? StationCode);
