using System;
using System.Windows;

namespace MainAPP.Services
{
    /// <summary>
    /// 窗口尺寸自适应工具。
    ///
    /// 背景（2026-09-15 分辨率适配复查）：此前窗口尺寸直接取用户设置值，并以
    /// <c>Math.Max(1024, …)</c> / <c>Math.Max(700, …)</c> 作为下限。在 1024×768 的
    /// 小屏工业机上，1200×800 的默认窗口会比屏幕还大——超出部分（含左侧导航栏、
    /// 顶栏按钮、底部操作区）落在可视区之外，而 1024 的下限又恰好等于屏宽，
    /// 用户无法通过拖拽把窗口缩到屏幕以内。
    ///
    /// 本工具把「期望尺寸」夹到 [安全下限, 当前显示器工作区] 区间：
    /// - 期望值大于工作区 → 收敛到工作区，保证窗口完整可见；
    /// - 期望值小于安全下限 → 抬到安全下限，避免布局被压垮；
    /// - 工作区本身小于安全下限（极端小屏）→ 以工作区为准，宁小勿溢出。
    ///
    /// 注：工作区取 <see cref="SystemParameters.WorkArea"/>，即主显示器且已排除任务栏。
    /// 目标机型为单屏工业 PC，暂不处理多屏异尺寸场景。
    /// </summary>
    public static class WindowSizing
    {
        /// <summary>窗口可用的最小宽度（DIP）。低于此值时界面布局会明显失效。</summary>
        public const int MinUsableWidth = 900;

        /// <summary>窗口可用的最小高度（DIP）。</summary>
        public const int MinUsableHeight = 600;

        /// <summary>
        /// 纯计算：把期望尺寸夹到 [安全下限, 工作区] 区间。
        /// 不读取任何屏幕状态，便于单元测试。
        /// </summary>
        /// <param name="desiredWidth">期望宽度（通常来自用户设置）。</param>
        /// <param name="desiredHeight">期望高度（通常来自用户设置）。</param>
        /// <param name="workAreaWidth">当前显示器工作区宽度（已排除任务栏）。</param>
        /// <param name="workAreaHeight">当前显示器工作区高度（已排除任务栏）。</param>
        /// <returns>夹取后的 (宽度, 高度)。</returns>
        public static (double Width, double Height) Fit(
            double desiredWidth,
            double desiredHeight,
            double workAreaWidth,
            double workAreaHeight)
        {
            // 工作区比安全下限还小时，以工作区为准；否则窗口永远塞不进屏幕
            var minWidth = Math.Min(MinUsableWidth, workAreaWidth);
            var minHeight = Math.Min(MinUsableHeight, workAreaHeight);

            // Math.Clamp 要求 min <= max。max 取 max(min, 工作区)，该不等式恒成立
            var width = Math.Clamp(desiredWidth, minWidth, Math.Max(minWidth, workAreaWidth));
            var height = Math.Clamp(desiredHeight, minHeight, Math.Max(minHeight, workAreaHeight));
            return (width, height);
        }

        /// <summary>
        /// 把窗口尺寸收敛到当前显示器工作区，并同步下调 <see cref="Window.MinWidth"/> /
        /// <see cref="Window.MinHeight"/>——否则 XAML 中较大的最小尺寸仍会阻止用户把窗口缩到屏幕以内。
        /// 需在 UI 线程调用。
        /// </summary>
        public static void Apply(Window window, double desiredWidth, double desiredHeight)
        {
            var workArea = SystemParameters.WorkArea;
            var (width, height) = Fit(desiredWidth, desiredHeight, workArea.Width, workArea.Height);

            window.MinWidth = Math.Min(MinUsableWidth, workArea.Width);
            window.MinHeight = Math.Min(MinUsableHeight, workArea.Height);
            window.Width = width;
            window.Height = height;
        }

        /// <summary>
        /// 只收敛窗口尺寸，不改动调用方自己声明的 <see cref="Window.MinWidth"/> /
        /// <see cref="Window.MinHeight"/>（适用于本身就没有设最小尺寸的简单对话框）。
        /// 需在 UI 线程调用。
        /// </summary>
        public static void ApplySizeOnly(Window window, double desiredWidth, double desiredHeight)
        {
            var workArea = SystemParameters.WorkArea;
            var (width, height) = Fit(desiredWidth, desiredHeight, workArea.Width, workArea.Height);

            window.Width = width;
            window.Height = height;
        }
    }
}
