using System;
using System.Buffers;
using System.Linq;
using HikScanner;

namespace MainAPP.Models
{
    /// <summary>
    /// 应用层帧结果，继承 HikGrabResult 并添加编码器、丢帧标记等业务属性。
    /// 同时提供与旧版 ImageResult 兼容的访问属性。
    /// 实现 IDisposable 以保证 ArrayPool 租用缓冲区在异常路径也能被归还，推荐使用 using 模式。
    /// </summary>
    public class FrameResult : HikGrabResult, IDisposable
    {
        /// <summary>接收到编码器的时间戳（本地时间）
        /// L413c: 默认值在构造时求值（DateTime.Now），语义上表示"接收到编码器的时间"，
        /// 但对象创建后若未赋值即使用，时间可能与实际接收时间有偏差。暂不改为 nullable（改动大）。
        /// </summary>
        public DateTime EncoderReceivedTime { get; set; } = DateTime.Now;

        /// <summary>编码器计数值，用于关联图像采集时刻的编码器位置</summary>
        public uint EncoderValue { get; set; }

        /// <summary>指示当前帧是否需要丢弃（视觉运算能力不足时）</summary>
        public bool NeedDrop { get; set; }

        /// <summary>指示图像采集过程中是否发生了帧丢失</summary>
        public bool IsFrameLoss { get; set; }

        /// <summary>指示该帧是否由手动单次触发产生</summary>
        public bool IsManualShot { get; set; }

        // ─── ImageResult 兼容属性 ───────────────────────────

        /// <summary>图像数据缓冲区（兼容 ImageResult.ImageData）</summary>
        public byte[]? ImageData => Image?.RawData;

        /// <summary>图像宽度（兼容 ImageResult.Width）</summary>
        public uint Width => Image?.Width ?? 0;

        /// <summary>图像高度（兼容 ImageResult.Height）</summary>
        public uint Height => Image?.Height ?? 0;

        /// <summary>图像帧号（兼容 ImageResult.FrameNumber）</summary>
        public uint FrameNumber => Image?.FrameNum ?? 0;

        /// <summary>当前图像是否为 JPEG 编码（兼容 ImageResult.IsJpeg）</summary>
        public bool IsJpeg => Image?.IsJpeg ?? false;

        /// <summary>当前图像是否为 Mono8（兼容 ImageResult.IsMono8）</summary>
        public bool IsMono8 => Image?.IsMono8 ?? false;

        /// <summary>是否为 RGB8 Packed</summary>
        public bool IsRgb8Packed => !IsJpeg && !IsMono8 && ImageData != null;

        /// <summary>像素格式名称</summary>
        public string PixelFormatName => IsJpeg ? "Jpeg" : IsMono8 ? "Mono8" : "RGB8_Packed";

        /// <summary>
        /// 条码识别结果数组（兼容 ImageResult.BarcodeResults，返回数组形式）。
        /// M245: 首次访问时会缓存 Barcodes 的快照，之后即使修改 Barcodes 也不会刷新缓存。
        /// 因此在调用 <see cref="From(HikGrabResult)"/> 完成后不应再修改 Barcodes 集合。
        /// </summary>
        // L32: 缓存数组避免每次访问都 ToArray() 产生新分配。在 From 方法中一次性赋值。
        private HikBarcodeResult[]? _cachedBarcodeResults;
        public HikBarcodeResult[] BarcodeResults
        {
            get
            {
                return _cachedBarcodeResults ??= Barcodes?.ToArray() ?? Array.Empty<HikBarcodeResult>();
            }
        }

        /// <summary>是否包含条码识别结果（兼容 ImageResult.HasBarcodeResults）</summary>
        public bool HasBarcodeResults => HasBarcode;

        /// <summary>从 HikGrabResult 创建 FrameResult。
        /// 使用 ArrayPool 租用缓冲区替代 byte[].Clone()，避免每帧在 LOH 分配 ~2.6 MB 大数组。
        /// 调用方必须在 Image.Load 完成后调用 <see cref="ReleaseImageData"/> 归还缓冲区。</summary>
        public static FrameResult From(HikGrabResult source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            byte[]? rentedRawData = null;
            if (source.Image?.RawData is { Length: > 0 } rawData)
            {
                rentedRawData = ArrayPool<byte>.Shared.Rent(rawData.Length);
                Array.Copy(rawData, rentedRawData, rawData.Length);
            }

            var r = new FrameResult
            {
                Status = source.Status,
                RawErrorCode = source.RawErrorCode,
                Image = source.Image is null ? null : new HikImageData
                {
                    RawData = rentedRawData,
                    Width = source.Image.Width,
                    Height = source.Image.Height,
                    FrameNum = source.Image.FrameNum,
                    TriggerIndex = source.Image.TriggerIndex,
                    ChannelId = source.Image.ChannelId,
                    IsMono8 = source.Image.IsMono8,
                    IsJpeg = source.Image.IsJpeg,
                },
                // M184: 对集合做浅拷贝，避免与源共享同一 List 实例
                Barcodes = source.Barcodes?.ToList(),
                OcrResults = source.OcrResults?.ToList(),
                Waybills = source.Waybills?.ToList(),
                IsGetCode = source.IsGetCode,
                _poolRented = rentedRawData is not null,
            };
            return r;
        }

        private bool _poolRented;
        private int _disposed; // 0=未释放, 1=已释放，使用 Interlocked 实现幂等 Dispose

        /// <summary>
        /// 归还通过 ArrayPool 租用的 ImageData 缓冲区。
        /// 必须在 Image.Load 完成后、FrameResult 被丢弃前调用。
        /// 幂等：重复调用无副作用。
        /// </summary>
        public void ReleaseImageData()
        {
            if (_poolRented && Image?.RawData is { Length: > 0 } data)
            {
                ArrayPool<byte>.Shared.Return(data);
                Image.RawData = null;
                _poolRented = false;
            }
        }

        /// <summary>
        /// 释放 FrameResult 持有的 ArrayPool 租用缓冲区。
        /// 调用此方法后不应再访问 <see cref="ImageData"/>。
        /// 幂等：重复调用无副作用。推荐使用 using 模式自动调用。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ReleaseImageData();
            }
        }
    }
}
