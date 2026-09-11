using MainAPP.Models;
using MainAPP.Services;
using MainAPP.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ScottPlot;
using ScottPlot.WPF;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace MainAPP.Views
{
    /// <summary>
    /// HomeView.xaml 的交互逻辑
    /// </summary>
    public partial class HomeView : UserControl, IDisposable
    {
        private readonly HomeViewModel _viewModel;
        // H87a: 使用 volatile 保证多线程可见性，避免指令重排序导致 Dispose 状态读取不一致
        private volatile bool _isDisposed;

        // 耗时曲线刷新定时器（500ms 节流，仅"耗时曲线"Tab 选中时渲染，避免隐藏时无谓开销）
        private readonly DispatcherTimer _timingChartTimer;

        public HomeView()
        {
            InitializeComponent();
            // 设计时（VS 设计器）App.Services 为 null，不构造 ViewModel，DataContext 由 d:DataContext 提供
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                _viewModel = null!;
                _timingChartTimer = null!;
                return;
            }
            // 从 DI 容器解析 HomeViewModel（单例），确保与注册的实例一致
            _viewModel = App.Services.GetRequiredService<HomeViewModel>();
            DataContext = _viewModel;

            // L31: LogService 改为尾部追加后，通过 CollectionView 按时间戳倒序显示
            var view = CollectionViewSource.GetDefaultView(LogService.Instance.Logs);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(nameof(LogEntry.Timestamp), ListSortDirection.Descending));

            // 耗时曲线：500ms 刷新一次，数据来自 TimingLogger 最近 60 帧推理耗时环形缓冲
            _timingChartTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timingChartTimer.Tick += TimingChartTimer_Tick;
            _timingChartTimer.Start();

            // H50c: 不在 Unloaded 中调用 Dispose，避免 TabControl 切换标签导致主页功能永久失效。
            // Dispose 仅由 MainWindow 关闭时（Window.OnClosed）调用。
        }

        /// <summary>
        /// 刷新"耗时曲线"Tab：读取 TimingLogger 最近 100 帧的推理/总耗时及加载/绘制/保存分段耗时，
        /// 绘制多条事件曲线。仅在 Tab 选中时渲染，避免隐藏状态下重复绘制浪费 UI 资源。
        /// </summary>
        private void TimingChartTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_isDisposed) return;
                // 仅当"耗时曲线"Tab 可见时刷新
                if (TimingTabItem == null || !TimingTabItem.IsSelected) return;

                var inferenceTimes = TimingLogger.RecentInferenceMs;
                var totalTimes = TimingLogger.RecentTotalMs;
                var loadTimes = TimingLogger.RecentLoadMs;
                if (inferenceTimes.Count == 0 || totalTimes.Count == 0)
                {
                    TimingPlot.Plot.Clear();
                    TimingPlot.Refresh();
                    return;
                }

                // 多事件曲线：推理/总耗时 + 图像加载耗时，同图对比各阶段占比
                // 配色用色相差大的红/蓝/橙，保证一眼可辨（白底对比度均 ≥3:1）
                // REVIEW: 按用户要求不显示 Draw/Save 曲线（TimingLogger 仍在记录，需要时可恢复）
                var plot = TimingPlot.Plot;
                plot.Clear();
                AddTimingSeries(plot, totalTimes, "#D32F2F", "Total");
                AddTimingSeries(plot, inferenceTimes, "#1976D2", "Inference");
                AddTimingSeries(plot, loadTimes, "#EF6C00", "Load");
                plot.Axes.AutoScale();
                // Y 轴下限 0，上限取总耗时最大值的 1.2 倍，曲线变化更直观
                plot.Axes.SetLimitsY(0, Math.Max(totalTimes.Max() * 1.2, 1));
                // 图表文字用英文：ScottPlot 默认字体不含中文字形，中文会渲染为方块
                plot.Title("Per-Frame Processing Time (ms)");
                plot.Axes.Bottom.Label.Text = $"Frame (last {inferenceTimes.Count})";
                plot.Axes.Left.Label.Text = "ms";
                plot.ShowLegend();
                TimingPlot.Refresh();
            }
            catch (Exception ex)
            {
                // 刷新失败仅记录 Warning，不影响主页主流程
                LogService.Instance.Warning($"耗时曲线刷新失败: {ex.Message}");
            }
        }

        /// <summary>在图上追加一条耗时曲线（无数据时跳过）</summary>
        private static void AddTimingSeries(ScottPlot.Plot plot, IReadOnlyList<long> values, string colorHex, string legend)
        {
            if (values.Count == 0) return;
            var xs = new double[values.Count];
            var ys = new double[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                xs[i] = i;
                ys[i] = values[i];
            }
            var scatter = plot.Add.Scatter(xs, ys);
            scatter.LineColor = ScottPlot.Color.FromHex(colorHex);
            scatter.MarkerSize = 0;
            scatter.LineWidth = 1.5f;
            // ScottPlot 5: Legend 由 Add.Scatter 的 LegendText 控制
            scatter.LegendText = legend;
        }

        /// <summary>
        /// 暴露 ViewModel 供外部（如 MainWindow.OnClosed）安全释放，避免反射查找
        /// </summary>
        public HomeViewModel ViewModel => _viewModel;


        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            LogService.Instance.ClearLogs();
        }

        // 2026-09-07: "发送"Tab 右键菜单：清空发送记录
        private void ClearRobotSends_Click(object sender, RoutedEventArgs e)
        {
            RobotSendMonitor.Instance.Clear();
        }

        private void LogDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid != null && dataGrid.SelectedItem is LogEntry selectedLog)
            {
                // M122/L104: 时间戳转换为本地时间，统一格式为 yyyy-MM-dd HH:mm:ss.fff
                string logInfo = $"[{selectedLog.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{selectedLog.Level}] {selectedLog.Message}";
                // M240: 剪贴板可能被其他进程占用，包裹 try-catch 避免异常崩溃
                try
                {
                    Clipboard.SetDataObject(logInfo);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"复制日志到剪贴板失败: {ex}");
                    NotificationService.Error("复制日志到剪贴板失败，请稍后重试。");
                    return;
                }

                // 显示提示信息
                NotificationService.Success($"日志信息已复制到剪贴板:\n{logInfo}");
            }
        }

        // H50c: 移除 Unloaded 中的 Dispose 调用，避免 TabControl 切换标签时主页功能永久失效。
        // Dispose 方法保留，由 MainWindow.OnClosed 在窗口关闭时调用。

        // 推理结果 DataGrid 右键菜单：复制条码
        private void CopyBarcodeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (InferenceDataGrid?.SelectedItem is InferenceResultItem item && !string.IsNullOrEmpty(item.Barcode))
            {
                try { Clipboard.SetDataObject(item.Barcode); }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"复制条码到剪贴板失败: {ex}");
                    NotificationService.Error("复制条码到剪贴板失败，请稍后重试。");
                    return;
                }
                NotificationService.Success($"条码已复制: {item.Barcode}");
            }
        }

        // 推理结果 DataGrid 右键菜单：复制整行
        private void CopyRowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (InferenceDataGrid?.SelectedItem is InferenceResultItem item)
            {
                string row = $"置信度={item.ConfidencePercent}; 中心=({item.CenterX:F1},{item.CenterY:F1}); " +
                             $"宽高={item.Width:F1}x{item.Height:F1}; 角度={item.AngleDisplay}; 条码={item.Barcode}";
                try { Clipboard.SetDataObject(row); }
                catch (Exception ex)
                {
                    LogService.Instance.Error($"复制推理行到剪贴板失败: {ex}");
                    NotificationService.Error("复制到剪贴板失败，请稍后重试。");
                    return;
                }
                NotificationService.Success("推理结果已复制到剪贴板");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // 停止耗时曲线定时器，释放绘图资源
            if (_timingChartTimer != null)
            {
                _timingChartTimer.Stop();
                _timingChartTimer.Tick -= TimingChartTimer_Tick;
            }

            // H28: 释放 ImageViewer，取消其内部的事件订阅
            DisplayControl?.Dispose();
        }
    }
}