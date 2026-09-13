using MainAPP.Services;
using System;

namespace MainAPP.Services;

/// <summary>
/// 操作审计（2026-09-13）：人工对设置/配方/协议/账号的变更统一记录"操作人 + 动作 + 对象 + 前后值"。
/// <para>落点：LogService（Serilog，同时进日志文件与 Logs.db），前缀 <c>[操作审计]</c> 固定，
/// 便于检索/导出；配合既有 [AI 审计] 覆盖全部写操作面。</para>
/// </summary>
public sealed class AuditLogService
{
    public static AuditLogService Instance { get; } = new();

    private AuditLogService() { }

    /// <summary>当前操作者（登录用户名；未登录 = "未登录"）。</summary>
    public string CurrentActor
        => AuthService.Instance.CurrentUser?.Username ?? "未登录";

    /// <summary>记录一次操作审计。</summary>
    /// <param name="action">动作（如 保存设置 / 切换配方 / 保存协议 / 新增账号）。</param>
    /// <param name="subject">对象（如 自定义通讯协议 / 配方 / 账号）。</param>
    /// <param name="before">变更前值（可为 null 表示无前值）。</param>
    /// <param name="after">变更后值（可为 null 表示无后值）。</param>
    public void Record(string action, string subject, string? before = null, string? after = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("[操作审计] 操作人=").Append(CurrentActor);
        sb.Append(" 动作=").Append(action);
        sb.Append(" 对象=").Append(subject);
        if (before is not null)
        {
            sb.Append(" 前=").Append(before);
        }

        if (after is not null)
        {
            sb.Append(" 后=").Append(after);
        }

        LogService.Instance.Info(sb.ToString());
    }
}