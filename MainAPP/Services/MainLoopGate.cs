using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MainAPP.Services
{
    /// <summary>
    /// 主检测循环的暂停闸门（2026-09-16 重构为"持有者"语义）。
    ///
    /// <para><b>原先的问题</b>：主循环暂停原是一个静态布尔 <c>HomeViewModel.s_isPaused</c> + 三个静态方法，
    /// 而三方使用者（相机调试 Web / 配方页 / AI 对话）各自手写了一套
    /// "读 <c>IsLoopPaused</c> → 若未暂停则 <c>PauseLoop()</c> → finally 若本次没暂停过则 <c>ResumeLoop()</c>"
    /// 的防踩踏逻辑（三处几乎逐字重复）。这套写法有两个固有缺陷：</para>
    /// <list type="number">
    ///   <item><b>读-改-写不是原子的</b>：两方可能同时读到"未暂停"，于是都暂停、都恢复——
    ///   停/恢复次数错配。极端情况下 A 已经恢复、B 还以为该暂停着，主循环在 B 仍需要静止时恢复取帧。</item>
    ///   <item><b>没有"谁在持有"的信息</b>：一旦某条异常路径漏了恢复，主循环就永久停住，
    ///   而日志里只有一句"循环已暂停"，现场完全无从定位是谁停的、停了多久。</item>
    /// </list>
    ///
    /// <para><b>现在的语义</b>：按<b>持有者</b>记账。同一持有者重复 <see cref="Pause"/> 只算一次
    /// （支持嵌套调用）；<see cref="Resume"/> 只能释放自己持有的，<b>他人的持有不受影响</b>；
    /// <see cref="IsPaused"/> 表示"还有人持有"。同时记录每个持有者的开始时间，
    /// <see cref="Describe"/> 可给出"谁把循环停住了、停了多久"，这既是日志线索也是运维门户的状态展示。</para>
    ///
    /// <para><b>关于可变单例</b>：这里刻意保留一个进程级共享实例（<see cref="Default"/>），
    /// 因为 <c>AiChatService</c> 是静态单例、无法走构造函数注入；但本类型本身是<b>可实例化</b>的，
    /// 单元测试请自建实例（<c>new MainLoopGate()</c>）以免相互污染。</para>
    /// </summary>
    public sealed class MainLoopGate
    {
        /// <summary>相机调试 Web 宿主（远程看图/改参数）。</summary>
        public const string CameraDebug = "camera-debug";

        /// <summary>配方页（实时预览 / 画面示教 / 参数优化）。</summary>
        public const string RecipePage = "recipe-page";

        /// <summary>AI 对话（工具调用需要独占相机写参数）。</summary>
        public const string AiChat = "ai-chat";

        /// <summary>进程级共享实例（生产代码用这个）。</summary>
        public static MainLoopGate Default { get; } = new();

        private readonly object _lock = new();
        private readonly Dictionary<string, DateTime> _holders = new(StringComparer.Ordinal);

        /// <summary>是否有人持有暂停（即主循环此刻应保持静止）。</summary>
        public bool IsPaused
        {
            get
            {
                lock (_lock)
                {
                    return _holders.Count > 0;
                }
            }
        }

        /// <summary>当前持有者快照（诊断用）。</summary>
        public IReadOnlyList<string> CurrentHolders
        {
            get
            {
                lock (_lock)
                {
                    return _holders.Keys.ToList();
                }
            }
        }

        /// <summary>
        /// 请求暂停（幂等：同一持有者重复调用只记一次，便于嵌套/重入调用）。
        /// </summary>
        /// <param name="owner">持有者标识，建议用本类的 <c>CameraDebug</c>/<c>RecipePage</c>/<c>AiChat</c> 常量。</param>
        /// <returns>true = 本次是新的持有（暂停从"未暂停"变为"暂停"）；false = 已持有或已有人持有。</returns>
        public bool Pause(string owner)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(owner);

            lock (_lock)
            {
                if (_holders.ContainsKey(owner))
                {
                    return false; // 同一持有者重入：不改变状态
                }

                var wasPaused = _holders.Count > 0;
                _holders[owner] = DateTime.Now;
                return !wasPaused;
            }
        }

        /// <summary>
        /// 释放本持有者的暂停。只有真正持有过才会生效——<b>他人的持有不会被误释放</b>。
        /// </summary>
        /// <param name="owner">持有者标识。</param>
        /// <returns>true = 本次确实释放了自己的一份持有；false = 本持有者并未持有（多为重复恢复）。</returns>
        public bool Resume(string owner)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(owner);

            lock (_lock)
            {
                return _holders.Remove(owner);
            }
        }

        /// <summary>
        /// 人类可读的暂停状态（日志与运维门户展示）：
        /// "未暂停" / "暂停中：camera-debug（已持有 12s）、recipe-page（已持有 3s）"。
        /// ★ 这条信息是"谁把主循环停住了"的唯一线索，排查"主循环不动了"时先看它。
        /// </summary>
        public string Describe()
        {
            lock (_lock)
            {
                if (_holders.Count == 0)
                {
                    return "未暂停";
                }

                var now = DateTime.Now;
                var parts = _holders
                    .OrderByDescending(kv => kv.Value) // 先列最近持有的
                    .Select(kv => $"{kv.Key}（已持有 {(now - kv.Value).TotalSeconds:F0}s）");

                return $"暂停中：{string.Join("、", parts)}";
            }
        }
    }
}
