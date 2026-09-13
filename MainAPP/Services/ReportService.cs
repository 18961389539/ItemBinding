using MainAPP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services;

/// <summary>
/// 班报/日报自动产出（2026-09-13）：查询时间窗内的产量/OK/NG/无码/耗时/评分/配方分布，
/// 渲染为自包含 HTML（可打印/存档）到 <c>Saves/Reports/日报_yyyyMMdd.html</c>。
/// <para>定时：应用启动后 <see cref="Start"/>，后台循环检测跨天 → 自动生成昨日日报（与 DataRetention 同模式）。
/// 手动：<see cref="GenerateHtmlAsync(DateTime, DateTime)"/> 供图表页按钮/AI 复用。</para>
/// </summary>
public sealed class ReportService : IDisposable
{
    public static ReportService Instance { get; } = new();

    private readonly CancellationTokenSource _cts = new();
    private DateTime _lastCheckedDay = DateTime.Today;
    private bool _started;

    private ReportService() { }

    /// <summary>启动每日自动生成后台任务（App 启动时调用一次）。</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _ = RunLoopAsync(_cts.Token);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (DateTime.Today != _lastCheckedDay)
                {
                    _lastCheckedDay = DateTime.Today;
                    var yesterday = _lastCheckedDay.AddDays(-1);
                    try
                    {
                        var path = await GenerateHtmlAsync(yesterday, _lastCheckedDay, ct).ConfigureAwait(false);
                        LogService.Instance.Info($"[报告] 自动生成昨日日报: {path}");
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning($"[报告] 自动生成昨日日报失败: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try { await Task.Delay(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>生成 [from, to) 时间窗的 HTML 日报并落盘，返回文件路径。</summary>
    public async Task<string> GenerateHtmlAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var data = await LoadAsync(from, to, ct).ConfigureAwait(false);
        var html = RenderHtml(from, to, data);
        var dir = Path.Combine(AppContext.BaseDirectory, "Saves", "Reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"日报_{from:yyyyMMdd}.html");
        await File.WriteAllTextAsync(path, html, Encoding.UTF8, ct).ConfigureAwait(false);
        return path;
    }

    // ───────────────────────── 数据 ─────────────────────────

    private sealed record DailyData(
        long Total, long OkCount, long NgCount, long NoCodeCount,
        double AvgScore, double AvgCostMs,
        IReadOnlyList<(string Recipe, int Count)> ByRecipe,
        IReadOnlyList<(int Hour, int Count)> ByHour,
        IReadOnlyList<DbModel> NgTop);

    private static async Task<DailyData> LoadAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        await using var db = new AppDbContext();
        var q = db.BarcodeData.AsNoTracking().Where(x => x.DetectTime >= from && x.DetectTime < to);

        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var ok = await q.CountAsync(x => x.Result == "OK", ct).ConfigureAwait(false);
        var ng = await q.CountAsync(x => x.Result == "NG", ct).ConfigureAwait(false);
        var noCode = await q.CountAsync(x => x.Barcode == string.Empty || x.Barcode == "noread", ct).ConfigureAwait(false);
        var avgScore = total > 0 ? await q.AverageAsync(x => (double?)x.Score, ct).ConfigureAwait(false) ?? 0 : 0;
        var avgCost = total > 0 ? await q.AverageAsync(x => (double?)x.CostTime, ct).ConfigureAwait(false) ?? 0 : 0;

        var byRecipe = await q.GroupBy(x => x.RecipeName ?? "(未知)")
            .Select(g => new { k = g.Key, c = g.Count() })
            .OrderByDescending(g => g.c)
            .ToListAsync(ct).ConfigureAwait(false);

        var byHour = await q.GroupBy(x => x.DetectTime.Hour)
            .Select(g => new { k = g.Key, c = g.Count() })
            .OrderBy(g => g.k)
            .ToListAsync(ct).ConfigureAwait(false);

        var ngTop = await q.Where(x => x.Result == "NG")
            .OrderByDescending(x => x.DetectTime)
            .Take(10)
            .ToListAsync(ct).ConfigureAwait(false);

        return new DailyData(
            total, ok, ng, noCode, avgScore, avgCost,
            byRecipe.Select(r => (r.k, r.c)).ToList(),
            byHour.Select(h => (h.k, h.c)).ToList(),
            ngTop);
    }

    // ───────────────────────── HTML 渲染 ─────────────────────────

    private static string RenderHtml(DateTime from, DateTime to, DailyData d)
    {
        var sb = new StringBuilder();
        var pct = d.Total > 0 ? (double)d.OkCount * 100.0 / d.Total : 0.0;
        var noCodePct = d.Total > 0 ? (double)d.NoCodeCount * 100.0 / d.Total : 0.0;

        sb.AppendLine("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>生产日报</title><style>");
        sb.AppendLine("body{font:14px/1.7 -apple-system,'Microsoft YaHei',sans-serif;margin:24px;color:#1a1a1a}");
        sb.AppendLine("h1{font-size:20px}.sub{color:#666}h2{font-size:15px;border-left:4px solid #3b6fe0;padding-left:8px;margin-top:22px}");
        sb.AppendLine(".kpis{display:grid;grid-template-columns:repeat(6,1fr);gap:10px;margin-top:14px}");
        sb.AppendLine(".kpi{background:#f4f6fb;border-radius:8px;padding:12px 14px}");
        sb.AppendLine(".kpi b{display:block;font-size:22px}.kpi span{color:#666;font-size:12px}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin-top:8px}");
        sb.AppendLine("th,td{padding:6px 10px;border-bottom:1px solid #e3e6ec;text-align:left;font-size:13px}");
        sb.AppendLine("th{background:#eef1f7}.warn{color:#c44}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>生产日报</h1><div class=\"sub\">{from:yyyy-MM-dd HH:mm} ~ {to:yyyy-MM-dd HH:mm}</div>");
        sb.AppendLine($"<div class=\"sub\">生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>");

        sb.AppendLine("<div class=\"kpis\">");
        Kpi(sb, d.Total.ToString(), "检测总数");
        Kpi(sb, d.OkCount.ToString(), "OK");
        Kpi(sb, d.NgCount.ToString(), "NG");
        Kpi(sb, pct.ToString("F1") + "%", "OK 率");
        Kpi(sb, noCodePct.ToString("F1") + "%", "无码率");
        Kpi(sb, d.AvgCostMs.ToString("F0") + " ms", "平均耗时");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>按配方分布</h2><table><tr><th>配方</th><th>数量</th></tr>");
        foreach (var (recipe, count) in d.ByRecipe)
        {
            sb.AppendLine($"<tr><td>{Escape(recipe)}</td><td>{count}</td></tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<h2>按时段分布（小时）</h2><table><tr><th>时段</th><th>数量</th></tr>");
        foreach (var (hour, count) in d.ByHour)
        {
            sb.AppendLine($"<tr><td>{hour:00}:00-{hour:00}:59</td><td>{count}</td></tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("<h2>TOP-10 不良记录</h2><table><tr><th>时间</th><th>条码</th><th>评分</th><th>配方</th></tr>");
        if (d.NgTop.Count == 0)
        {
            sb.AppendLine("<tr><td colspan=\"4\">无</td></tr>");
        }
        else
        {
            foreach (var m in d.NgTop)
            {
                sb.AppendLine($"<tr><td>{m.DetectTime:HH:mm:ss}</td><td>{Escape(m.Barcode)}</td><td>{m.Score:F2}</td><td>{Escape(m.RecipeName ?? "-")}</td></tr>");
            }
        }

        sb.AppendLine("</table></body></html>");
        return sb.ToString();
    }

    private static void Kpi(StringBuilder sb, string value, string label)
        => sb.AppendLine($"<div class=\"kpi\"><b>{Escape(value)}</b><span>{Escape(label)}</span></div>");

    private static string Escape(string s)
        => string.IsNullOrEmpty(s) ? string.Empty
           : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public void Dispose() => _cts.Cancel();
}