using Extensions;
using Xunit;

namespace MainAPP.Tests.Extensions;

/// <summary>
/// FileHelper 文件操作辅助类集成测试。
/// 使用临时目录隔离测试，验证 SafeDelete、BatchRename、DeleteOldFiles 的核心行为。
/// </summary>
public class FileHelperTests : IDisposable
{
    private readonly string _tempRoot;

    public FileHelperTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"FileHelperTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // 测试清理忽略异常
        }
    }

    private string WriteFile(string relativePath, string content = "test")
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    #region SafeDelete

    [Fact]
    public void SafeDelete_ExistingFile_ReturnsTrue()
    {
        var file = WriteFile("delete-me.txt");

        var result = FileHelper.SafeDelete(file);

        Assert.True(result);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void SafeDelete_NonExistentFile_ReturnsFalse()
    {
        var nonExistent = Path.Combine(_tempRoot, "does-not-exist.txt");

        var result = FileHelper.SafeDelete(nonExistent);

        Assert.False(result);
    }

    [Fact]
    public void SafeDelete_EmptyPath_ReturnsFalse()
    {
        var result = FileHelper.SafeDelete(string.Empty);

        Assert.False(result);
    }

    [Fact]
    public void SafeDelete_NullPath_ReturnsFalse()
    {
        var result = FileHelper.SafeDelete(null!);

        Assert.False(result);
    }

    [Fact]
    public void SafeDelete_DefaultRetries_DeletesSuccessfully()
    {
        var file = WriteFile("default.txt");

        var result = FileHelper.SafeDelete(file, maxRetries: 3, retryDelayMs: 1);

        Assert.True(result);
    }

    [Fact]
    public void SafeDelete_NegativeRetryDelay_ThrowsArgumentOutOfRangeException()
    {
        var file = WriteFile("neg.txt");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FileHelper.SafeDelete(file, maxRetries: 1, retryDelayMs: -1));
    }

    [Fact]
    public void SafeDelete_ZeroMaxRetries_ForcesAtLeastOneAttempt()
    {
        var file = WriteFile("zero.txt");

        var result = FileHelper.SafeDelete(file, maxRetries: 0);

        Assert.True(result);
        Assert.False(File.Exists(file));
    }

    #endregion

    #region BatchRename

    [Fact]
    public void BatchRename_RenamesFilesWithPattern()
    {
        WriteFile("a.txt");
        WriteFile("b.txt");
        WriteFile("c.txt");

        var count = FileHelper.BatchRename(_tempRoot, "*.txt", "img_{2}{1}", startNumber: 1);

        Assert.Equal(3, count);
        Assert.True(File.Exists(Path.Combine(_tempRoot, "img_1.txt")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "img_2.txt")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "img_3.txt")));
    }

    [Fact]
    public void BatchRename_NonExistentDirectory_ReturnsZero()
    {
        var count = FileHelper.BatchRename(
            Path.Combine(_tempRoot, "does-not-exist"), "*");

        Assert.Equal(0, count);
    }

    [Fact]
    public void BatchRename_NullSearchPattern_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FileHelper.BatchRename(_tempRoot, null!));
    }

    [Fact]
    public void BatchRename_NullNewNamePattern_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FileHelper.BatchRename(_tempRoot, "*", null!));
    }

    [Fact]
    public void BatchRename_PreservesOriginalName()
    {
        WriteFile("photo.txt");

        FileHelper.BatchRename(_tempRoot, "*.txt", "{0}_{2}{1}", startNumber: 1);

        Assert.True(File.Exists(Path.Combine(_tempRoot, "photo_1.txt")));
    }

    [Fact]
    public void BatchRename_CustomStartNumber()
    {
        WriteFile("a.txt");
        WriteFile("b.txt");

        FileHelper.BatchRename(_tempRoot, "*.txt", "f_{2}{1}", startNumber: 100);

        Assert.True(File.Exists(Path.Combine(_tempRoot, "f_100.txt")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "f_101.txt")));
    }

    [Fact]
    public void BatchRename_Recursive_RenamesInSubdirectories()
    {
        WriteFile(Path.Combine("sub", "a.txt"));
        WriteFile(Path.Combine("sub", "b.txt"));

        var count = FileHelper.BatchRename(_tempRoot, "*.txt", "r_{2}{1}", startNumber: 1, recursive: true);

        Assert.Equal(2, count);
        Assert.True(File.Exists(Path.Combine(_tempRoot, "sub", "r_1.txt")));
        Assert.True(File.Exists(Path.Combine(_tempRoot, "sub", "r_2.txt")));
    }

    #endregion

    #region DeleteOldFiles

    [Fact]
    public void DeleteOldFiles_DaysZero_ReturnsWithoutDeleting()
    {
        var file = WriteFile("keep.txt");

        FileHelper.DeleteOldFiles(0, _tempRoot);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void DeleteOldFiles_NegativeDays_ReturnsWithoutDeleting()
    {
        var file = WriteFile("keep.txt");

        FileHelper.DeleteOldFiles(-1, _tempRoot);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void DeleteOldFiles_DeletesOldFilesOnly()
    {
        var oldFile = WriteFile("old.txt");
        // 将文件修改时间设为 30 天前
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));

        var newFile = WriteFile("new.txt");

        FileHelper.DeleteOldFiles(7, _tempRoot);

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    [Fact]
    public void DeleteOldFiles_NonExistentFolder_ReturnsQuietly()
    {
        var exception = Record.Exception(() =>
            FileHelper.DeleteOldFiles(7, Path.Combine(_tempRoot, "missing")));

        Assert.Null(exception);
    }

    [Fact]
    public void DeleteOldFiles_Recursive_DeletesInSubdirectories()
    {
        var oldFile = WriteFile(Path.Combine("sub", "old.txt"));
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));

        FileHelper.DeleteOldFiles(7, _tempRoot);

        Assert.False(File.Exists(oldFile));
    }

    #endregion
}
