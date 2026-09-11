using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MvCodeReaderSDKNet;

namespace HikScanner
{
    /// <summary>文件存取</summary>
    public partial class HikScanner
    {
        /// <summary>文件读写进度变化事件</summary>
        public event EventHandler<FileAccessProgress> FileAccessProgressChanged;
        private CancellationTokenSource _fileAccessCts;

        /// <summary>同步读取设备文件到本地路径</summary>
        /// <param name="localFilePath">本地文件路径</param>
        /// <param name="deviceFileName">设备端文件名</param>
        public void FileAccessRead(string localFilePath, string deviceFileName)
        {
            ThrowIfDisposed();
            EnsureConnected();
            var fileAccess = new MvCodeReader.MV_CODEREADER_FILE_ACCESS { pUserFileName = localFilePath, pDevFileName = deviceFileName };
            HikScannerException.Check(_device.MV_CODEREADER_FileAccessRead_NET(ref fileAccess), "文件读取");
        }

        /// <summary>同步写入本地文件到设备</summary>
        /// <param name="localFilePath">本地文件路径</param>
        /// <param name="deviceFileName">设备端文件名</param>
        public void FileAccessWrite(string localFilePath, string deviceFileName)
        {
            ThrowIfDisposed();
            EnsureConnected();
            if (!File.Exists(localFilePath)) throw new FileNotFoundException("源文件不存在", localFilePath);
            var fileAccess = new MvCodeReader.MV_CODEREADER_FILE_ACCESS { pUserFileName = localFilePath, pDevFileName = deviceFileName };
            HikScannerException.Check(_device.MV_CODEREADER_FileAccessWrite_NET(ref fileAccess), "文件写入");
        }

        /// <summary>获取当前文件读写进度</summary>
        public FileAccessProgress GetFileAccessProgress()
        {
            ThrowIfDisposed();
            EnsureConnected();
            var progress = new MvCodeReader.MV_CODEREADER_FILE_ACCESS_PROGRESS();
            if (_device.MV_CODEREADER_GetFileAccessProgress_NET(ref progress) != MvCodeReader.MV_CODEREADER_OK) return null;
            return new FileAccessProgress { Completed = progress.nCompleted, Total = progress.nTotal };
        }

        /// <summary>异步读取设备文件，通过 FileAccessProgressChanged 事件推送进度。</summary>
        /// <remarks>注意：SDK 的 FileAccessRead 本身是同步阻塞调用，此方法的"异步"仅指
        /// 文件传输完成后的进度轮询阶段。读取大文件时此方法仍会阻塞调用线程。
        /// #7: 新增 cancellationToken 参数，阻塞前检查取消。</remarks>
        /// <param name="localFilePath">本地文件路径</param>
        /// <param name="deviceFileName">设备端文件名</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>表示异步操作的任务</returns>
        public Task FileAccessReadAsync(string localFilePath, string deviceFileName, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            EnsureConnected();
            // #7: 阻塞前检查取消
            cancellationToken.ThrowIfCancellationRequested();
            var fileAccess = new MvCodeReader.MV_CODEREADER_FILE_ACCESS { pUserFileName = localFilePath, pDevFileName = deviceFileName };
            HikScannerException.Check(_device.MV_CODEREADER_FileAccessRead_NET(ref fileAccess), "文件读取");
            return PollProgressAsync(cancellationToken);
        }

        /// <summary>异步写入本地文件到设备，通过 FileAccessProgressChanged 事件推送进度。</summary>
        /// <remarks>注意：SDK 的 FileAccessWrite 本身是同步阻塞调用，此方法的"异步"仅指
        /// 文件传输完成后的进度轮询阶段。写入大文件时此方法仍会阻塞调用线程。
        /// #7: 新增 cancellationToken 参数，阻塞前检查取消。</remarks>
        /// <param name="localFilePath">本地文件路径</param>
        /// <param name="deviceFileName">设备端文件名</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>表示异步操作的任务</returns>
        public Task FileAccessWriteAsync(string localFilePath, string deviceFileName, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            EnsureConnected();
            // #7: 阻塞前检查取消
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(localFilePath)) throw new FileNotFoundException("源文件不存在", localFilePath);
            var fileAccess = new MvCodeReader.MV_CODEREADER_FILE_ACCESS { pUserFileName = localFilePath, pDevFileName = deviceFileName };
            HikScannerException.Check(_device.MV_CODEREADER_FileAccessWrite_NET(ref fileAccess), "文件写入");
            return PollProgressAsync(cancellationToken);
        }

        private async Task PollProgressAsync(CancellationToken externalToken = default)
        {
            // #6: 移除冗余的 CancelFileAccessPolling()，Interlocked.Exchange 已保证线程安全
            // 如果并发调用 FileAccessReadAsync，旧 CTS 会被 Exchange 取消，进度轮询正确终止
            var newCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var oldCts = Interlocked.Exchange(ref _fileAccessCts, newCts);
            if (oldCts != null) { oldCts.Cancel(); oldCts.Dispose(); }
            var token = newCts.Token;

            try
            {
                // #6: 检查 _disposed，防止 Dispose 后访问已释放的 _device
                while (!token.IsCancellationRequested && !_disposed)
                {
                    var progress = GetFileAccessProgress();
                    if (progress == null) break;
                    // #6: 事件触发加 _eventLock 保护，与其他事件一致
                    EventHandler<FileAccessProgress> handler;
                    lock (_eventLock) { handler = FileAccessProgressChanged; }
                    handler?.Invoke(this, progress);
                    if (progress.Completed >= progress.Total && progress.Total > 0) break;
                    await Task.Delay(100, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void CancelFileAccessPolling()
        {
            var cts = Interlocked.Exchange(ref _fileAccessCts, null);
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }
}
