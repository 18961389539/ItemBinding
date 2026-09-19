using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HikScanner
{
    /// <summary>设备传输层类型</summary>
    public enum HikDeviceType { GigE = 1, USB = 3 }

    /// <summary>IP配置类型 (GigE设备)</summary>
    public enum HikIpConfigType { Static = 0, DHCP = 1, LLA = 2 }

    /// <summary>设备访问模式</summary>
    public enum HikAccessMode { Exclusive = 1, Control = 2 }

    /// <summary>触发模式</summary>
    public enum HikTriggerMode { Continuous = 0, Trigger = 1 }

    /// <summary>触发源</summary>
    public enum HikTriggerSource
    {
        Line0 = 0, Line1 = 1, Line2 = 2, Line3 = 3,
        Counter = 4, Software = 7, SerialStart = 8, SelfTrigger = 9
    }

    /// <summary>采集结果状态</summary>
    public enum HikGrabStatus
    {
        Success = 0, Timeout = 1, NoData = 2, BufferOverflow = 3, Error = 4
    }

    /// <summary>采集模式</summary>
    public enum HikGrabMode
    {
        Polling, Callback, MscDualChannel
    }

    /// <summary>连接状态</summary>
    public enum HikConnectionState
    {
        Disconnected, Connected, Reconnecting
    }

    /// <summary>连接状态变化事件参数</summary>
    public class HikConnectionStateChangedEventArgs : EventArgs
    {
        /// <summary>当前状态</summary>
        public HikConnectionState State { get; init; }
        /// <summary>之前状态</summary>
        public HikConnectionState PreviousState { get; init; }
        /// <summary>异常信息（断线时有值）</summary>
        public Exception Exception { get; init; }
    }

    // ─── 数据模型 ──────────────────────────────────

    /// <summary>海康读码器设备信息</summary>
    public class HikDeviceInfo
    {
        public string SerialNumber { get; set; }
        public string ManufacturerName { get; set; }
        public string ModelName { get; set; }
        public string UserDefinedName { get; set; }
        public string DeviceVersion { get; set; }
        public uint DeviceType { get; set; }
        public uint TLayerType { get; set; }
        public string CurrentIp { get; set; }
        public string SubnetMask { get; set; }
        public string DefaultGateway { get; set; }
        public string MacAddress { get; set; }
        public uint DeviceNumber { get; set; }
        public uint IpConfigOption { get; set; }
        public uint IpConfigCurrent { get; set; }
        public uint NetExport { get; set; }

        internal MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_DEVICE_INFO RawDeviceInfo { get; set; }

        public override string ToString()
        {
            string name = !string.IsNullOrEmpty(UserDefinedName) ? UserDefinedName :
                          $"{ManufacturerName} {ModelName}";
            // #4: 使用 SDK 常量比较，而非强制转换 HikDeviceType 枚举（值不匹配）
            if (TLayerType == MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_GIGE_DEVICE)
                return $"GEV: {name} ({SerialNumber})";
            return $"USB: {name} ({SerialNumber})";
        }
    }

    /// <summary>#1: 持有 Mono8 Bitmap 的 GCHandle，绑定到 Bitmap.Tag 防止 GC 回收底层缓冲区</summary>
    internal class Mono8BitmapPin
    {
        private readonly GCHandle _handle;
        public Mono8BitmapPin(GCHandle handle) { _handle = handle; }
        ~Mono8BitmapPin() { if (_handle.IsAllocated) _handle.Free(); }
    }

    /// <summary>图像采集数据</summary>
    public class HikImageData
    {
        public byte[] RawData { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint FrameNum { get; set; }
        public uint TriggerIndex { get; set; }
        public uint ChannelId { get; set; }
        public bool IsMono8 { get; set; }
        public bool IsJpeg { get; set; }

        /// <summary>返回图像摘要信息</summary>
        public override string ToString()
        {
            string pixel = IsMono8 ? "Mono8" : IsJpeg ? "JPEG" : "Unknown";
            return $"{Width}x{Height} Frame#{FrameNum} {pixel} {RawData?.Length ?? 0}B";
        }

        /// <summary>将图像数据转换为 GDI+ Bitmap。
        /// 调用方拥有返回的 Bitmap 所有权，使用完毕后必须调用 Dispose() 释放 GDI+ 资源。</summary>
        /// <returns>Bitmap 实例，图像数据无效时返回 null</returns>
        public Bitmap ToBitmap()
        {
            if (RawData == null || RawData.Length == 0) return null;
            if (IsJpeg)
            {
                // #14: 捕获 GDI+ 异常，无效 JPEG 数据时返回 null 而非崩溃
                try
                {
                    using (var ms = new MemoryStream(RawData))
                        return new Bitmap(ms);
                }
                catch (ArgumentException) { return null; }
                catch (System.Runtime.InteropServices.ExternalException) { return null; }
            }
            if (IsMono8)
            {
                // GDI+ 要求 stride 4 字节对齐；当 Width % 4 != 0 时需要填充补齐字节
                int stride = ((int)Width + 3) & ~3;
                byte[] paddedData = RawData;
                if (stride != (int)Width)
                {
                    // 逐行拷贝，每行末尾补零到 stride 对齐
                    paddedData = new byte[stride * (int)Height];
                    for (int y = 0; y < (int)Height; y++)
                    {
                        Array.Copy(RawData, y * (int)Width, paddedData, y * stride, (int)Width);
                    }
                }
                // #1: GCHandle 必须保持存活直到 Bitmap Dispose，否则 GC 回收 paddedData 导致图像损坏
                var handle = GCHandle.Alloc(paddedData, GCHandleType.Pinned);
                try
                {
                    var bmp = new Bitmap((int)Width, (int)Height, stride,
                        PixelFormat.Format8bppIndexed, handle.AddrOfPinnedObject());
                    var cp = bmp.Palette;
                    for (int i = 0; i < 256; i++) cp.Entries[i] = Color.FromArgb(i, i, i);
                    bmp.Palette = cp;
                    // 将 GCHandle 绑定到 Bitmap.Tag，Bitmap Dispose 时 GC 会回收 Tag 引用
                    // 用户 Dispose Bitmap 后 handle 失去引用，GC 回收 paddedData 和 handle
                    bmp.Tag = new Mono8BitmapPin(handle);
                    return bmp;
                }
                catch
                {
                    handle.Free();
                    throw;
                }
            }
            return null;
        }

        /// <summary>返回 JPEG 格式字节数据</summary>
        /// <remarks>#14: 验证 JPEG 数据有效性（SOI 标记 0xFFD8），无效时返回 null</remarks>
        public byte[] ToJpegBytes()
        {
            if (RawData == null || RawData.Length == 0) return null;
            // #14: 验证 JPEG SOI 标记（0xFF 0xD8），防止无效数据被当作 JPEG
            if (IsJpeg)
            {
                if (RawData.Length < 2 || RawData[0] != 0xFF || RawData[1] != 0xD8) return null;
                return RawData;
            }
            using (var bmp = ToBitmap())
            {
                if (bmp == null) return null;
                using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Jpeg); return ms.ToArray(); }
            }
        }
    }

    /// <summary>条码识别结果</summary>
    public class HikBarcodeResult
    {
        public string Code { get; set; }
        public int CodeType { get; set; }
        public string CodeTypeName { get; set; }
        public int CodeId { get; set; }
        public PointF[] BoundingPoints { get; set; } = Array.Empty<PointF>();
        public short Angle { get; set; }
        public short PPM { get; set; }
        public short AlgorithmCost { get; set; }
        public short Sharpness { get; set; }
        public int TotalProcCost { get; set; }
        public int OverQuality { get; set; }
        public int IDRScore { get; set; }

        /// <summary>
        /// 2026-09-18: 设备端触发时刻（条码结果 nTriggerTimeTvLow/UtvLow 解码，设备时钟，未对时）。
        /// 解码失败或固件未填时为 null。
        /// </summary>
        public DateTime? DeviceTriggerTime { get; set; }

        /// <summary>
        /// 2026-09-18: 估算触发时刻 = 主机当前时间 − TotalProcCost（PC 时钟，非设备精确时间）。
        /// </summary>
        public DateTime TriggerTime { get; set; }

        public override string ToString() => $"{CodeTypeName}: {Code} (ID={CodeId})";

        public static string GetCodeTypeName(int codeType)
        {
            switch ((MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE)codeType)
            {
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_DM: return "DM码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_QR: return "QR码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN8: return "EAN8码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_UPCE: return "UPCE码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_UPCA: return "UPCA码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN13: return "EAN13码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ISBN13: return "ISBN13码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODABAR: return "库德巴码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ITF25: return "交叉25码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE39: return "Code 39码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE93: return "Code 93码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE128: return "Code 128码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_PDF417: return "PDF417码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_MATRIX25: return "MATRIX25码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_MSI: return "MSI码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE11: return "Code 11码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_INDUSTRIAL25: return "Industrial25码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CHINAPOST: return "中国邮政码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_ITF14: return "交叉14码";
                case MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_ECC140: return "ECC140码";
                default: return "未知码";
            }
        }
    }

    /// <summary>OCR识别结果</summary>
    public class HikOcrResult
    {
        public int Id { get; set; }
        public string Text { get; set; }
        public int Length { get; set; }
        public float CharConfidence { get; set; }
        public float DetectConfidence { get; set; }
        public int CenterX { get; set; }
        public int CenterY { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float Angle { get; set; }
        public short AlgorithmCost { get; set; }

        public override string ToString() => $"OCR#{Id}: \"{Text}\" Conf={CharConfidence:F2} {Width}x{Height}@({CenterX},{CenterY})";
    }

    /// <summary>面单检测结果</summary>
    public class HikWaybillResult
    {
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Angle { get; set; }
        public float Confidence { get; set; }
        public byte[] WaybillImage { get; set; }
        public uint ImageLength { get; set; }

        public override string ToString() => $"Waybill Conf={Confidence:F2} {Width:F0}x{Height:F0}@({CenterX:F0},{CenterY:F0}) ImgLen={ImageLength}";
    }

    /// <summary>文件存取进度</summary>
    public class FileAccessProgress
    {
        public long Completed { get; set; }
        public long Total { get; set; }
        public double Percent => Total > 0 ? (double)Completed / Total * 100.0 : 0;
    }

    /// <summary>枚举类型参数信息</summary>
    public class HikEnumInfo
    {
        public uint CurrentValue { get; set; }
        public uint[] SupportedValues { get; set; }
    }

    /// <summary>一帧图像的完整采集结果</summary>
    public class HikGrabResult
    {
        public HikGrabStatus Status { get; set; } = HikGrabStatus.Success;
        /// <summary>SDK 原始错误码（Status != Success 时有效，用于诊断具体失败原因）</summary>
        public int RawErrorCode { get; set; }
        public HikImageData Image { get; set; }
        // #14: 初始化为空列表，防止调用方直接访问 .Count 时 NRE
        public List<HikBarcodeResult> Barcodes { get; set; } = new();
        public List<HikOcrResult> OcrResults { get; set; } = new();
        public List<HikWaybillResult> Waybills { get; set; } = new();
        public bool HasBarcode => Barcodes != null && Barcodes.Count > 0;
        public bool HasOcr => OcrResults != null && OcrResults.Count > 0;
        public bool HasWaybill => Waybills != null && Waybills.Count > 0;
        /// <summary>SDK bIsGetCode 标志：相机固件判定"此帧有码"，true 时 Barcodes/Waybills 通常非空</summary>
        public bool IsGetCode { get; set; }

        /// <summary>
        /// 2026-09-18: 帧级设备时间戳原始 tick（nTimeStampHigh/Low 拼接，设备时钟，未对时；0=未获取）。
        /// </summary>
        public ulong DeviceTimeStampTick { get; set; }

        /// <summary>
        /// 2026-09-18: 软件对时偏移（ms）= 主机时钟 − 设备时钟（最近一次硬触发观测）。
        /// null=尚无硬触发/对时不可用。换算：PC墙钟ms = DeviceTimeStampTick/1000 + ClockOffsetMs。
        /// </summary>
        public double? ClockOffsetMs { get; set; }

        public override string ToString()
        {
            string img = Image != null ? $"{Image.Width}x{Image.Height} Frame#{Image.FrameNum}" : "无图像";
            return $"{Status} | {img} | 条码={Barcodes?.Count ?? 0} OCR={OcrResults?.Count ?? 0} 面单={Waybills?.Count ?? 0}";
        }
    }

    /// <summary>图像框坐标缩放与绘制辅助</summary>
    public static class DetectionBoxHelper
    {
        /// <summary>将条码 4 顶点坐标从图像像素坐标系映射到显示控件坐标系</summary>
        public static PointF[] ScaleBarcodeBounds(HikBarcodeResult barcode, uint imageWidth, uint imageHeight, Size displaySize)
        {
            var points = new PointF[4];
            if (barcode.BoundingPoints == null || barcode.BoundingPoints.Length < 4) return points;
            float sx = (float)displaySize.Width / imageWidth;
            float sy = (float)displaySize.Height / imageHeight;
            for (int i = 0; i < 4; i++)
                points[i] = new PointF(barcode.BoundingPoints[i].X * sx, barcode.BoundingPoints[i].Y * sy);
            return points;
        }

        /// <summary>在 Graphics 上绘制条码检测框（蓝色多边形，匹配 SDK Demo 行为）</summary>
        public static void DrawBarcode(Graphics g, HikBarcodeResult barcode, uint imageWidth, uint imageHeight, Size displaySize)
        {
            var pts = ScaleBarcodeBounds(barcode, imageWidth, imageHeight, displaySize);
            using var pen = new Pen(Color.Blue, 2);
            g.DrawPolygon(pen, pts);
        }

        /// <summary>将 OCR 区域从图像像素坐标系映射到显示控件坐标系（旋转矩形）</summary>
        public static RectangleF ScaleOcrBounds(HikOcrResult ocr, uint imageWidth, uint imageHeight, Size displaySize)
        {
            float sx = (float)displaySize.Width / imageWidth;
            float sy = (float)displaySize.Height / imageHeight;
            return new RectangleF(ocr.CenterX * sx - ocr.Width * sx / 2,
                                   ocr.CenterY * sy - ocr.Height * sy / 2,
                                   ocr.Width * sx, ocr.Height * sy);
        }

        /// <summary>在 Graphics 上绘制 OCR 检测框（黄色旋转矩形，匹配 SDK Demo 行为）</summary>
        public static void DrawOcr(Graphics g, HikOcrResult ocr, uint imageWidth, uint imageHeight, Size displaySize)
        {
            var rect = ScaleOcrBounds(ocr, imageWidth, imageHeight, displaySize);
            using var pen = new Pen(Color.Yellow, 1);
            var oldTransform = g.Transform;
            g.TranslateTransform(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            g.RotateTransform(ocr.Angle);
            g.DrawRectangle(pen, -rect.Width / 2, -rect.Height / 2, rect.Width, rect.Height);
            g.Transform = oldTransform;
        }

        /// <summary>将面单区域从图像像素坐标系映射到显示控件坐标系（旋转矩形）</summary>
        public static RectangleF ScaleWaybillBounds(HikWaybillResult waybill, uint imageWidth, uint imageHeight, Size displaySize)
        {
            float sx = (float)displaySize.Width / imageWidth;
            float sy = (float)displaySize.Height / imageHeight;
            return new RectangleF(waybill.CenterX * sx - waybill.Width * sx / 2,
                                   waybill.CenterY * sy - waybill.Height * sy / 2,
                                   waybill.Width * sx, waybill.Height * sy);
        }

        /// <summary>在 Graphics 上绘制面单检测框（红色旋转矩形，匹配 SDK Demo 行为）</summary>
        public static void DrawWaybill(Graphics g, HikWaybillResult waybill, uint imageWidth, uint imageHeight, Size displaySize)
        {
            var rect = ScaleWaybillBounds(waybill, imageWidth, imageHeight, displaySize);
            using var pen = new Pen(Color.Red, 2);
            var oldTransform = g.Transform;
            g.TranslateTransform(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            g.RotateTransform(waybill.Angle);
            g.DrawRectangle(pen, -rect.Width / 2, -rect.Height / 2, rect.Width, rect.Height);
            g.Transform = oldTransform;
        }
    }

    /// <summary>重连过程单步失败事件参数，携带步骤名和具体错误码</summary>
    public class HikReconnectErrorEventArgs : EventArgs
    {
        public string Step { get; set; }
        public int ErrorCode { get; set; }
        public string ErrorDescription { get; set; }
    }
}
