using System.Collections.Generic;

namespace MainAPP.Models
{
    /// <summary>
    /// 数据库结构同步结果（2026-09-16）。
    /// 用于把"本次启动对库结构做了什么"如实写进日志——结构变更必须是**可见**的，
    /// 而不是静默发生在启动路径里。
    /// </summary>
    /// <param name="AddedColumns">本次补上的列名（老库缺列时非空；已是最新库时为空）。</param>
    /// <param name="AddedIndexes">本次创建的索引名。</param>
    /// <param name="SkippedColumns">
    /// 模型中存在、但**无法安全自动补**的列（非空且无默认值，或 CLR 类型无法映射到 SQLite）。
    /// 非空即表示需要人工提供一个显式迁移步骤——见 <see cref="AppDbContext.ApplySchemaSyncAsync"/> 注释。
    /// </param>
    public sealed record SchemaSyncResult(
        IReadOnlyList<string> AddedColumns,
        IReadOnlyList<string> AddedIndexes,
        IReadOnlyList<string> SkippedColumns)
    {
        /// <summary>本次是否发生了任何结构变更。</summary>
        public bool HasChanges => AddedColumns.Count > 0 || AddedIndexes.Count > 0;

        /// <summary>无变更的空结果。</summary>
        public static SchemaSyncResult Empty { get; } =
            new(System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>());
    }
}
