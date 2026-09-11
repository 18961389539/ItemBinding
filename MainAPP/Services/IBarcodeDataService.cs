using MainAPP.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MainAPP.Services
{
    /// <summary>
    /// 条码数据服务抽象。当前实现为单例 <see cref="BarcodeDataService"/>，
    /// 为后续 ViewModel 改造为构造函数注入做准备。
    /// 保留 <see cref="BarcodeDataService.Instance"/> 静态属性以向后兼容现有调用。
    /// </summary>
    public interface IBarcodeDataService : IDisposable
    {
        /// <summary>获取所有数据</summary>
        Task<List<DbModel>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>根据ID获取数据</summary>
        Task<DbModel?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>添加新数据</summary>
        Task<DbModel> AddAsync(DbModel model, CancellationToken cancellationToken = default);

        /// <summary>批量添加数据</summary>
        Task AddRangeAsync(IEnumerable<DbModel> models, CancellationToken cancellationToken = default);

        /// <summary>立即刷新待处理记录到数据库</summary>
        Task SavePendingAsync(CancellationToken cancellationToken = default);

        /// <summary>更新数据</summary>
        Task UpdateAsync(DbModel model, CancellationToken cancellationToken = default);

        /// <summary>删除数据</summary>
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>根据二维码查询数据</summary>
        Task<List<DbModel>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);

        /// <summary>获取最近N条数据</summary>
        Task<List<DbModel>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

        /// <summary>获取某个时间范围的数据</summary>
        Task<List<DbModel>> GetByTimeRangeAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default);

        /// <summary>获取数据总数</summary>
        Task<int> GetCountAsync(CancellationToken cancellationToken = default);

        /// <summary>清空所有数据</summary>
        Task ClearAllAsync(CancellationToken cancellationToken = default);

        /// <summary>按时间清理：删除 DetectTime 早于 cutoff 的所有记录，返回删除的行数</summary>
        Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default);

        /// <summary>按数量清理：删除超出 maxCount 的最旧记录，返回删除的行数</summary>
        Task<int> TrimToMaxCountAsync(int maxCount, CancellationToken cancellationToken = default);

        /// <summary>根据编码范围查询数据</summary>
        Task<List<DbModel>> GetByEncodeRangeAsync(long minEncode, long maxEncode, CancellationToken cancellationToken = default);

        /// <summary>根据位置范围查询数据</summary>
        Task<List<DbModel>> GetByPositionRangeAsync(double minX, double maxX, double minY, double maxY, CancellationToken cancellationToken = default);

        /// <summary>根据角度范围查询数据（角度域 (-180,180]，与 DbModel.Angle 落库/机器人发送域一致）</summary>
        Task<List<DbModel>> GetByAngelRangeAsync(double minAngel, double maxAngel, CancellationToken cancellationToken = default);

        /// <summary>分页获取数据</summary>
        Task<(List<DbModel> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);

        /// <summary>获取统计信息</summary>
        Task<(double AvgCostTime, double AvgX, double AvgY, double AvgAngel, double AvgWidth, double AvgHeight, int TotalRecords)> GetStatisticsAsync(CancellationToken cancellationToken = default);

        /// <summary>批量删除数据</summary>
        Task BulkDeleteAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);

        /// <summary>综合搜索数据</summary>
        Task<List<DbModel>> SearchAsync(string? barcode = null, long? minEncode = null, long? maxEncode = null,
            double? minX = null, double? maxX = null, double? minY = null, double? maxY = null,
            double? minAngel = null, double? maxAngel = null, double? minScore = null, double? maxScore = null,
            DateTime? startTime = null, DateTime? endTime = null, CancellationToken cancellationToken = default);

        /// <summary>分页综合搜索数据</summary>
        Task<(List<DbModel> Items, int TotalCount)> SearchPagedAsync(int pageNumber, int pageSize,
            string? barcode = null, long? minEncode = null, long? maxEncode = null,
            double? minX = null, double? maxX = null, double? minY = null, double? maxY = null,
            double? minAngel = null, double? maxAngel = null, double? minScore = null, double? maxScore = null,
            DateTime? startTime = null, DateTime? endTime = null, CancellationToken cancellationToken = default);

        /// <summary>导出数据到CSV文件（全表导出）</summary>
        Task ExportToCsvAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>按筛选条件导出数据到CSV文件</summary>
        Task ExportToCsvAsync(string filePath,
            string? barcode = null,
            long? minEncode = null, long? maxEncode = null,
            double? minScore = null, double? maxScore = null,
            DateTime? startTime = null, DateTime? endTime = null,
            CancellationToken cancellationToken = default);

        /// <summary>从CSV文件导入数据（分批独立事务）</summary>
        Task<CsvImportResult> ImportFromCsvAsync(string filePath, CancellationToken cancellationToken = default, IProgress<CsvImportProgress>? progress = null);
    }
}
