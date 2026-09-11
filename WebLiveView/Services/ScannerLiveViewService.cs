using HikScanner;
using HikScannerType = HikScanner.HikScanner;
using System.Drawing;
using System.Drawing.Imaging;

namespace WebLiveView.Services;

public sealed class ScannerLiveViewService : IAsyncDisposable
{
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private HikScannerType? _scanner;
    private CancellationTokenSource? _streamCts;
    private Task? _streamTask;
    private bool _disposed;
    private ScannerSnapshot? _latestSnapshot;
    private string? _latestError;
    private int _frameVersion;
    private MjpegFrame? _latestFrame;

    public event Func<ScannerSnapshot, Task>? SnapshotUpdated;
    public event Func<string?, Task>? StreamStatusChanged;

    public ScannerSnapshot? LatestSnapshot => _latestSnapshot;
    public string? LatestError => _latestError;

    public async Task WriteMjpegStreamAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        await EnsureStreamingAsync(cancellationToken).ConfigureAwait(false);
        response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
        response.ContentType = "multipart/x-mixed-replace; boundary=frame";

        var lastFrameVersion = -1;
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = await WaitForNextFrameAsync(lastFrameVersion, cancellationToken).ConfigureAwait(false);
            await response.WriteAsync($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {frame.ImageBytes.Length}\r\n\r\n", cancellationToken).ConfigureAwait(false);
            await response.Body.WriteAsync(frame.ImageBytes, cancellationToken).ConfigureAwait(false);
            await response.WriteAsync("\r\n", cancellationToken).ConfigureAwait(false);
            await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
            lastFrameVersion = frame.Version;
        }
    }

    public async Task EnsureStreamingAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_streamTask is { IsCompleted: false })
            {
                return;
            }

            _streamCts?.Cancel();
            _streamCts?.Dispose();
            _streamCts = new CancellationTokenSource();
            _streamTask = Task.Run(() => StreamLoopAsync(_streamCts.Token), _streamCts.Token);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<ScannerSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (_latestSnapshot is not null)
        {
            return _latestSnapshot;
        }

        return await CaptureSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScannerSettingsState> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var scanner = await EnsureScannerAsync().ConfigureAwait(false);
            scanner.SwitchToSoftwareTrigger();

            var exposureRange = scanner.GetFloatRange("ExposureTime");
            var gainRange = scanner.GetFloatRange("Gain");
            return new ScannerSettingsState(
                scanner.GetExposureTime(),
                exposureRange.Min,
                exposureRange.Max,
                scanner.GetGain(),
                gainRange.Min,
                gainRange.Max,
                scanner.GetCurrentTriggerMode().ToString(),
                scanner.DeviceInfo?.ModelName ?? string.Empty,
                scanner.DeviceInfo?.SerialNumber ?? string.Empty,
                scanner.DeviceInfo?.CurrentIp ?? string.Empty);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public async Task<ScannerSettingsState> UpdateSettingsAsync(float exposure, float gain, CancellationToken cancellationToken = default)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var scanner = await EnsureScannerAsync().ConfigureAwait(false);
            scanner.SwitchToSoftwareTrigger();
            var exposureRange = scanner.GetFloatRange("ExposureTime");
            var gainRange = scanner.GetFloatRange("Gain");
            var normalizedExposure = Math.Clamp(exposure, exposureRange.Min, exposureRange.Max);
            var normalizedGain = Math.Clamp(gain, gainRange.Min, gainRange.Max);
            scanner.SetExposureTime(normalizedExposure);
            scanner.SetGain(normalizedGain);

            return new ScannerSettingsState(
                scanner.GetExposureTime(),
                exposureRange.Min,
                exposureRange.Max,
                scanner.GetGain(),
                gainRange.Min,
                gainRange.Max,
                scanner.GetCurrentTriggerMode().ToString(),
                scanner.DeviceInfo?.ModelName ?? string.Empty,
                scanner.DeviceInfo?.SerialNumber ?? string.Empty,
                scanner.DeviceInfo?.CurrentIp ?? string.Empty);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task StreamLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await CaptureSnapshotAsync(cancellationToken).ConfigureAwait(false);
                _latestError = null;
                await PublishStreamStatusAsync(null).ConfigureAwait(false);
                await PublishSnapshotAsync(snapshot).ConfigureAwait(false);
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _latestError = ex.Message;
                await PublishStreamStatusAsync(_latestError).ConfigureAwait(false);
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ScannerSnapshot> CaptureSnapshotAsync(CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var scanner = await EnsureScannerAsync().ConfigureAwait(false);
            scanner.SwitchToSoftwareTrigger();
            scanner.ExecuteSoftwareTrigger();
            var frame = await scanner.GetImageAsync(5000).ConfigureAwait(false);
            var imageBytes = ConvertFrameToJpeg(frame);

            var snapshot = new ScannerSnapshot(
                Convert.ToBase64String(imageBytes),
                "image/jpeg",
                frame.Image?.Width ?? 0,
                frame.Image?.Height ?? 0,
                scanner.GetExposureTime(),
                scanner.GetGain(),
                scanner.DeviceInfo?.SerialNumber ?? string.Empty,
                scanner.DeviceInfo?.CurrentIp ?? string.Empty,
                DateTime.Now);

            _latestSnapshot = snapshot;
            _latestFrame = new MjpegFrame(Interlocked.Increment(ref _frameVersion), imageBytes, snapshot);
            return snapshot;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<HikScannerType> EnsureScannerAsync()
    {
        if (_scanner is { IsConnected: true })
        {
            return _scanner;
        }

        return await Task.Run(() =>
        {
            var devices = HikScannerType.EnumerateDevices(HikDeviceType.GigE);
            if (devices.Count == 0)
            {
                throw new InvalidOperationException("未检测到扫码枪，请确认设备已连接。");
            }

            _scanner?.Dispose();
            _scanner = new HikScannerType();
            _scanner.Connect(devices[0]);
            _scanner.SwitchToSoftwareTrigger();
            return _scanner;
        }).ConfigureAwait(false);
    }

    private static byte[] ConvertFrameToJpeg(HikGrabResult frame)
    {
        var image = frame.Image;
        if (image is null)
        {
            throw new NotSupportedException("帧不包含图像数据");
        }

        if (image.IsJpeg)
        {
            return image.RawData;
        }

        if (image.IsMono8)
        {
            return ConvertMono8ToJpeg(image);
        }

        // 非 jpeg、非 mono8 视作 RGB8 Packed
        return ConvertRgb8ToJpeg(image);
    }

    private async Task<MjpegFrame> WaitForNextFrameAsync(int lastFrameVersion, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var frame = _latestFrame;
            if (frame is not null && frame.Version != lastFrameVersion)
            {
                return frame;
            }

            if (frame is null)
            {
                await CaptureSnapshotAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private async Task PublishSnapshotAsync(ScannerSnapshot snapshot)
    {
        var handlers = SnapshotUpdated;
        if (handlers is null)
        {
            return;
        }

        var tasks = handlers.GetInvocationList()
            .Cast<Func<ScannerSnapshot, Task>>()
            .Select(handler => handler(snapshot));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task PublishStreamStatusAsync(string? errorMessage)
    {
        var handlers = StreamStatusChanged;
        if (handlers is null)
        {
            return;
        }

        var tasks = handlers.GetInvocationList()
            .Cast<Func<string?, Task>>()
            .Select(handler => handler(errorMessage));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static byte[] ConvertMono8ToJpeg(HikImageData image)
    {
        using var bitmap = new Bitmap((int)image.Width, (int)image.Height, PixelFormat.Format24bppRgb);
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var gray = image.RawData[(y * bitmap.Width) + x];
                bitmap.SetPixel(x, y, Color.FromArgb(gray, gray, gray));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    private static byte[] ConvertRgb8ToJpeg(HikImageData image)
    {
        using var bitmap = new Bitmap((int)image.Width, (int)image.Height, PixelFormat.Format24bppRgb);
        var index = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var r = image.RawData[index++];
                var g = image.RawData[index++];
                var b = image.RawData[index++];
                bitmap.SetPixel(x, y, Color.FromArgb(r, g, b));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ScannerLiveViewService));
        }
    }

    public async ValueTask DisposeAsync()
    {
        _streamCts?.Cancel();
        if (_streamTask is not null)
        {
            try
            {
                await _streamTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _syncLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _streamCts?.Dispose();
            _streamCts = null;
            _scanner?.Dispose();
            _scanner = null;
            _disposed = true;
        }
        finally
        {
            _syncLock.Release();
            _syncLock.Dispose();
        }
    }
}

internal sealed record MjpegFrame(int Version, byte[] ImageBytes, ScannerSnapshot Snapshot);

public sealed record ScannerSnapshot(
    string ImageBase64,
    string MimeType,
    uint Width,
    uint Height,
    float Exposure,
    float Gain,
    string SerialNumber,
    string IpAddress,
    DateTime CapturedAt);

public sealed record ScannerSettingsState(
    float Exposure,
    float ExposureMin,
    float ExposureMax,
    float Gain,
    float GainMin,
    float GainMax,
    string TriggerMode,
    string ModelName,
    string SerialNumber,
    string IpAddress);
