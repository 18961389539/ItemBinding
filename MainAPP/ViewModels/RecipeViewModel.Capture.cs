using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoordinateSystemMapping;
using Extensions;
using HikScanner;
using JinlongYolo.YoloSharp;
using JinlongYolo.YoloSharp.Data;
using JinlongYolo.YoloSharp.Extensions;
using JinlongYolo.YoloSharp.Plotting;
using MainAPP.Application;
using MainAPP.Models;
using MainAPP.Services;
using MainAPP.Views;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Image = SixLabors.ImageSharp.Image;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;
// ============================================================
// Split from the original monolithic file: RecipeViewModel image capture + device parameters
// Reason: original file too large (>90KB); split by single responsibility into partial class files
// to improve navigability and review locality. See docs/knowledge/代码结构与维护约定.md for the file index
// VSTHRD001: Dispatcher.BeginInvoke/InvokeAsync to switch to UI thread is standard WPF pattern
#pragma warning disable VSTHRD001

namespace MainAPP.ViewModels
{
    public partial class RecipeViewModel
    {
        #region 设备参数
        /// <summary>
        /// 设置识别设备参数
        /// </summary>
        [RelayCommand]
        private async Task SetParameterAsync()
        {
            try
            {
                if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
                {
                    return;
                }

                if (ImageTool != null)
                {
                    // H84: 传递 CancellationToken
                    await _scannerService.SetExposureTimeAsync(ImageTool.ExposureTime, _cts.Token).ConfigureAwait(false);
                    await _scannerService.SetGainAsync(ImageTool.Gain, _cts.Token).ConfigureAwait(false);
                }

                ShowInfo("设置参数成功");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"设置参数失败: {ex}");
                ShowError($"设置参数失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取识别设备参数
        /// </summary>
        [RelayCommand]
        private async Task GetParameterAsync()
        {
            try
            {
                if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
                {
                    return;
                }

                if (ImageTool != null)
                {
                    // H84: 传递 CancellationToken
                    ImageTool.ExposureTime = await _scannerService.GetExposureTimeAsync(_cts.Token).ConfigureAwait(false);
                    ImageTool.Gain = await _scannerService.GetGainAsync(_cts.Token).ConfigureAwait(false);
                    OnPropertyChanged(nameof(ImageTool));
                }

                ShowInfo("获取参数成功");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"获取参数失败: {ex}");
                ShowError($"获取参数失败: {ex.Message}");
            }
        }
        #endregion


        #region 获取图像
        /// <summary>
        /// 获取图像——按「图像来源」分流：
        /// <list type="bullet">
        /// <item>从扫码枪读取（ReadFromScanner=true）：相机软触发取单帧（ActivateAsync 已预先设软触发），失败自动重试。</item>
        /// <item>本地目录（ReadFromScanner=false）：逐个读取 DirectoryPath 下图片，跳过开头 RemoveCount 张，
        /// 到末尾自动回绕；不依赖扫码枪设备。</item>
        /// </list>
        /// </summary>
        [RelayCommand]
        private async Task GetImageAsync()
        {
            try
            {
                // 防御：VM 已被列表销毁（删除配方/卸载）时禁止继续操作，避免访问已释放 _cts
                if (_disposed)
                {
                    LogService.Instance.Warning("获取图像：配方视图模型已释放，请关闭并重新打开配方后再试。");
                    ShowWarning("配方视图已失效，请关闭后重新打开配方。");
                    return;
                }

                // 文件夹取图分支：不依赖扫码枪
                if (ImageTool is { ReadFromScanner: false } folderTool)
                {
                    await LoadNextFolderImageAsync(folderTool).ConfigureAwait(false);
                    return;
                }

                if (!await EnsureScannerReadyAsync().ConfigureAwait(false))
                {
                    return;
                }

                // 1. SDK 取图（带自动重试）
                var swGrab = Stopwatch.StartNew();
                FrameResult? imageResult = null;
                Exception? grabException = null;
                for (int attempt = 1; attempt <= GetImageMaxAttempts; attempt++)
                {
                    try
                    {
                        imageResult = await _scannerService.GetImageAsync().ConfigureAwait(false);
                        break; // 成功则退出重试循环
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // 取消不重试，向上传播
                    }
                    catch (Exception ex)
                    {
                        grabException = ex;
                        LogService.Instance.Warning($"获取图像失败(尝试 {attempt}/{GetImageMaxAttempts}): {ex.Message}");
                        if (attempt < GetImageMaxAttempts)
                        {
                            await Task.Delay(GetImageRetryDelayMs, _cts.Token).ConfigureAwait(false);
                        }
                    }
                }
                swGrab.Stop();

                if (imageResult is null)
                {
                    // L: 所有重试均已失败，记录错误并提示用户可手动重试
                    LogService.Instance.Error($"获取图像失败(已重试 {GetImageMaxAttempts} 次): {grabException}");
                    ShowError($"获取图像失败（已重试 {GetImageMaxAttempts} 次），可点击\"获取图像\"按钮手动重试。");
                    return;
                }

                // P0-FIX: 用 using 模式包装 imageResult，确保 ImDecode 抛异常或提前 return 时也能归还 ArrayPool 缓冲区
                using (imageResult)
                {
                    if (imageResult.ImageData is null)
                    {
                        LogService.Instance.Warning("拍照返回空图像数据，跳过处理");
                        return;
                    }
                    var imageDataSize = imageResult.ImageData.Length;
                    MemoryDiagnostics.LogAllocation("RecopeVM(GetImage)", imageDataSize,
                        $"WxH={imageResult.Width}x{imageResult.Height}, Barcodes={imageResult.BarcodeResults?.Length ?? 0}");

                    // 2. 解码
                    var swDecode = Stopwatch.StartNew();
                    using var grayMat = Mat.ImDecode(imageResult.ImageData, ImreadModes.Grayscale);
                    // M700: ImageData 已解码，归还 ArrayPool 缓冲区（Dispose 幂等，重复调用安全）
                    imageResult.ReleaseImageData();
                    using var colorMat = grayMat.CvtColor(ColorConversionCodes.GRAY2BGR);
                    _originalMat?.Dispose();
                    _originalMat = grayMat.Clone();
                    var matSize = (long)_originalMat.Width * _originalMat.Height * _originalMat.Channels();
                    swDecode.Stop();

                    MemoryDiagnostics.LogAllocation("RecopeVM(OriginalMat)", matSize,
                        $"WxH={_originalMat.Width}x{_originalMat.Height}, Channels={_originalMat.Channels()}");

                    // 3. 条码绘制
                    var swDraw = Stopwatch.StartNew();
                    if (imageResult.HasBarcodeResults)
                    {
                        Tools.DrawBarcodeResults(colorMat, imageResult.BarcodeResults!);
                    }
                    swDraw.Stop();

                    UpdateImageForShow(colorMat.ToBitmapSource());

                    ShowInfo($"图像采集完成\n"
                        + $" SDK取图: {swGrab.Elapsed.TotalMilliseconds:F0} ms\n"
                        + $" 解码:     {swDecode.Elapsed.TotalMilliseconds:F0} ms\n"
                        + $" 条码绘制: {swDraw.Elapsed.TotalMilliseconds:F0} ms");
                }
            }
            catch (OperationCanceledException)
            {
                // L: 取消时静默退出
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"获取图像失败: {ex}");
                ShowError($"获取图像失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 文件夹取图：从 DirectoryPath 逐个读取图片。
        /// 起点 = RemoveCount（跳过开头不稳定帧）；每次读取后游标 +1，到末尾回绕到起点。
        /// 读取结果与相机路径对齐：_originalMat 存灰度克隆（供测试推理/标定复用），画面按 BGR 显示。
        /// </summary>
        private async Task LoadNextFolderImageAsync(ImageTool tool)
        {
            if (string.IsNullOrWhiteSpace(tool.DirectoryPath) || !Directory.Exists(tool.DirectoryPath))
            {
                LogService.Instance.Warning($"[文件夹取图] 目录无效: {tool.DirectoryPath}");
                ShowWarning($"图像目录不存在或为空：\n{tool.DirectoryPath}");
                RefreshCaptureAvailability();
                return;
            }

            List<string> files;
            try
            {
                files = await Task.Run(() => ListFolderImages(tool.DirectoryPath)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"[文件夹取图] 枚举目录失败: {ex}");
                ShowError($"读取图像目录失败: {ex.Message}");
                return;
            }

            if (files.Count == 0)
            {
                LogService.Instance.Warning($"[文件夹取图] 目录内没有图片: {tool.DirectoryPath}");
                ShowWarning("目录内没有可用的图片文件。");
                RefreshCaptureAvailability();
                return;
            }

            // 起点=RemoveCount（跳过开头帧）；越界保护到最后一个可用下标；RemoveCount/目录变化时重置
            int start = Math.Min(Math.Max(tool.RemoveCount, 0), Math.Max(files.Count - 1, 0));
            if (_folderImageStartIndex != start || !string.Equals(_folderImageListPath, tool.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                _folderImageStartIndex = start;
                _folderImageListPath = tool.DirectoryPath;
                _folderImageIndex = start;
            }

            // 回绕区间 [start, files.Count)，游标按自然递增取模
            int span = files.Count - start;
            int index = start + ((_folderImageIndex - start) % span + span) % span;
            _folderImageIndex = index + 1;
            string filePath = files[index];

            try
            {
                using var grayMat = await Task.Run(() =>
                {
                    var mat = Cv2.ImRead(filePath, ImreadModes.Grayscale);
                    return mat is null || mat.Empty() ? null : mat;
                }).ConfigureAwait(false);

                if (grayMat is null)
                {
                    LogService.Instance.Warning($"[文件夹取图] 解码失败，已跳过: {filePath}");
                    ShowWarning($"图片解码失败：{Path.GetFileName(filePath)}");
                    return;
                }

                _originalMat?.Dispose();
                _originalMat = grayMat.Clone();
                using var colorMat = grayMat.CvtColor(ColorConversionCodes.GRAY2BGR);
                UpdateImageForShow(colorMat.ToBitmapSource());

                MemoryDiagnostics.LogAllocation("RecipeVM(FolderGetImage)",
                    (long)_originalMat.Width * _originalMat.Height * _originalMat.Channels(),
                    $"File={Path.GetFileName(filePath)} WxH={_originalMat.Width}x{_originalMat.Height}");
                ShowInfo($"已从文件夹读取（{index + 1}/{files.Count}）：{Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"[文件夹取图] 读取失败: {ex}");
                ShowError($"读取图片失败: {ex.Message}");
            }
        }
        #endregion

        #region 保存当前显示的图像
        /// <summary>
        /// 保存当前显示的图像
        /// </summary>
        [RelayCommand]
        private void SaveImage()
        {
            if (ImageForShow == null)
            {
                ShowWarning("未获取到图像");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存图像",
                Filter = "PNG图像|*.png|JPEG图像|*.jpg|BMP图像|*.bmp|所有文件|*.*",
                DefaultExt = ".png",
                FileName = $"Image_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                // L71: 强制转换前做类型检查，避免 InvalidCastException
                if (ImageForShow is not BitmapSource bmp)
                {
                    ShowWarning("当前图像格式不支持保存");
                    return;
                }
                using var mat = BitmapSourceConverter.ToMat(bmp);
                mat.SaveImage(dialog.FileName);
                ShowInfo($"图像已保存至:\n{dialog.FileName}");
            }
        }
        #endregion
    }
}