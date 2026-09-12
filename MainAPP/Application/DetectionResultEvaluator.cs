namespace MainAPP.Application
{
    /// <summary>
    /// 检测结果 OK/NG 判定（2026-09-12 新增）。
    ///
    /// 抽成纯静态函数的原因：判定规则横跨「条码状态 + 置信度量纲 + 阈值单位」三件事，
    /// 埋在 <see cref="DetectionRecordService"/> 的长循环里既难读也难测。
    /// 这里无副作用、不依赖设置单例，可直接单测——尤其是**量纲换算**这一处最容易写错。
    /// </summary>
    public static class DetectionResultEvaluator
    {
        /// <summary>合格。</summary>
        public const string Ok = "OK";

        /// <summary>不合格。</summary>
        public const string Ng = "NG";

        /// <summary>
        /// 判定单条检测结果。
        ///
        /// 规则（2026-09-12 由用户定义）：**条码读取成功 且 置信度 ≥ 阈值 → OK，否则 NG**。
        ///
        /// ★ 量纲约定（本方法存在的核心原因）：
        /// <paramref name="confidence"/> 来自 <c>InferenceResultItem.Confidence</c>，量纲是 <b>0~1</b>
        /// （见该类注释与 <c>ConfidencePercent =&gt; Confidence * 100</c>）；
        /// 而 <paramref name="thresholdPercent"/> 是<b>百分比 0~100</b>（默认 75，与界面/文档口径一致）。
        /// 两者单位不同，故内部先 ×100 再比较。
        /// <b>若日后有人把某个调用方的实参传反或改了单位，单测会直接失败。</b>
        /// </summary>
        /// <param name="hasBarcode">条码是否读取成功（含 "noread" 哨兵值排除）。</param>
        /// <param name="confidence">边缘检测置信度，量纲 0~1。</param>
        /// <param name="thresholdPercent">判定阈值，百分比 0~100。</param>
        public static string Evaluate(bool hasBarcode, double confidence, double thresholdPercent)
        {
            if (!hasBarcode)
            {
                return Ng;
            }

            return confidence * 100.0 >= thresholdPercent ? Ok : Ng;
        }
    }
}
