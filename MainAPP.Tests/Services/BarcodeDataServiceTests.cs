using MainAPP.Services;
using Xunit;

namespace MainAPP.Tests.Services;

/// <summary>
/// BarcodeDataService 单元测试。
/// 重点验证 ImportFromCsvAsync 的参数校验、空表头短路返回、CsvImportResult/CsvImportProgress 数据结构。
/// 不依赖实际数据库写入（空表头路径在访问 DB 前即返回 Empty）。
/// </summary>
public class BarcodeDataServiceTests
{
    [Fact]
    public async Task ImportFromCsvAsync_NullFilePath_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BarcodeDataService.Instance.ImportFromCsvAsync(null!));
    }

    [Fact]
    public async Task ImportFromCsvAsync_EmptyFilePath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BarcodeDataService.Instance.ImportFromCsvAsync(string.Empty));
    }

    [Fact]
    public async Task ImportFromCsvAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.csv");

        await Assert.ThrowsAsync<System.IO.FileNotFoundException>(() =>
            BarcodeDataService.Instance.ImportFromCsvAsync(missingPath));
    }

    [Fact]
    public async Task ImportFromCsvAsync_EmptyHeader_ReturnsEmptyResult()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllLinesAsync(tempFile, new[] { "" });

            var result = await BarcodeDataService.Instance.ImportFromCsvAsync(tempFile);

            Assert.NotNull(result);
            Assert.Same(CsvImportResult.Empty, result);
            Assert.Equal(0, result.TotalRows);
            Assert.Equal(0, result.ImportedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.True(result.IsAllSuccess);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ImportFromCsvAsync_WhitespaceHeader_ReturnsEmptyResult()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"ws_{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllLinesAsync(tempFile, new[] { "   " });

            var result = await BarcodeDataService.Instance.ImportFromCsvAsync(tempFile);

            Assert.Same(CsvImportResult.Empty, result);
            Assert.True(result.IsAllSuccess);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CsvImportResult_Empty_HasZeroCountsAndNoErrors()
    {
        var empty = CsvImportResult.Empty;

        Assert.Equal(0, empty.TotalRows);
        Assert.Equal(0, empty.ImportedCount);
        Assert.Equal(0, empty.FailedCount);
        Assert.Equal(0, empty.BatchCount);
        Assert.Equal(0, empty.FailedBatchCount);
        Assert.Empty(empty.Errors);
        Assert.True(empty.IsAllSuccess);
    }

    [Fact]
    public void CsvImportResult_AllSuccess_WhenNoFailedBatches()
    {
        var result = new CsvImportResult(
            totalRows: 100,
            importedCount: 100,
            failedCount: 0,
            batchCount: 1,
            failedBatchCount: 0,
            errors: Array.Empty<string>());

        Assert.True(result.IsAllSuccess);
        Assert.Equal(100, result.TotalRows);
        Assert.Equal(100, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
    }

    [Fact]
    public void CsvImportResult_PartialFailure_RecordsErrorsAndFlagsNotAllSuccess()
    {
        var errors = new[] { "批次 1（100 条）写入失败" };

        var result = new CsvImportResult(
            totalRows: 200,
            importedCount: 100,
            failedCount: 100,
            batchCount: 2,
            failedBatchCount: 1,
            errors: errors);

        Assert.False(result.IsAllSuccess);
        Assert.Equal(200, result.TotalRows);
        Assert.Equal(100, result.ImportedCount);
        Assert.Equal(100, result.FailedCount);
        Assert.Equal(2, result.BatchCount);
        Assert.Equal(1, result.FailedBatchCount);
        Assert.Single(result.Errors);
        Assert.Equal("批次 1（100 条）写入失败", result.Errors[0]);
    }

    [Fact]
    public void CsvImportProgress_Record_HoldsCorrectValues()
    {
        var progress = new CsvImportProgress(ImportedCount: 50, FailedCount: 5, BatchCount: 1);

        Assert.Equal(50, progress.ImportedCount);
        Assert.Equal(5, progress.FailedCount);
        Assert.Equal(1, progress.BatchCount);
    }

    [Fact]
    public void CsvImportProgress_Equality_WorksForRecordSemantics()
    {
        var a = new CsvImportProgress(1, 0, 1);
        var b = new CsvImportProgress(1, 0, 1);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }
}
