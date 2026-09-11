namespace JinlongYolo.YoloSharp;

/// <summary>
/// 用于测量预处理、推理与后处理时间的轻量计时器。
/// Lightweight timer used for measuring preprocessing, inference, and postprocessing times.
///
/// 以零分配的方式记录三个阶段的耗时，供结果返回时用于性能统计与诊断。
/// Records the duration of three phases with zero allocation, used for performance statistics and diagnostics when results are returned.
/// </summary>
internal ref struct PredictorTimer()
{
    private readonly Stopwatch _stopwatch = new();

    private TimeSpan _preprocess;
    private TimeSpan _inference;
    private TimeSpan _postprocess;

    /// <summary>
    /// 开始记录预处理时间。
    /// Starts recording preprocessing time.
    /// </summary>
    public readonly void StartPreprocess()
    {
        _stopwatch.Restart();
    }

    /// <summary>
    /// 结束预处理时间记录，并开始记录推理时间。
    /// Ends preprocessing time recording and starts recording inference time.
    /// </summary>
    public void StartInference()
    {
        _preprocess = _stopwatch.Elapsed;
        _stopwatch.Restart();
    }

    /// <summary>
    /// 结束推理时间记录，并开始记录后处理时间。
    /// Ends inference time recording and starts recording postprocessing time.
    /// </summary>
    public void StartPostprocess()
    {
        _inference = _stopwatch.Elapsed;
        _stopwatch.Restart();
    }

    /// <summary>
    /// 停止所有计时并返回各个阶段的速度结果。
    /// Stops all timing and returns the speed results for each phase.
    /// </summary>
    /// <returns>包含各个阶段耗时的速度结果对象 / The speed result object containing durations for each phase.</returns>
    public SpeedResult Stop()
    {
        _postprocess = _stopwatch.Elapsed;
        _stopwatch.Stop();

        return new SpeedResult(_preprocess, _inference, _postprocess);
    }
}