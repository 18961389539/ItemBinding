using System;
using System.Runtime.InteropServices;
using System.Text;
using MvCodeReaderSDKNet;
using static MvCodeReaderSDKNet.MvCodeReader;

namespace HikCodeReader
{
    /// <summary>
    /// 图像结果，封装从设备获取的图像数据及相关信息
    /// </summary>
    public class ImageResult
    {
        /// <summary>
        /// 获取图像数据缓冲区
        /// </summary>
        public byte[] ImageData { get; }

        /// <summary>
        /// 接收到编码器的时间戳。
        /// </summary>
        public DateTime EncoderReceivedTime { get; set; }
        /// <summary>
        /// 编码器计数值，用于关联图像采集时刻的编码器位置，常用于流水线定位场景
        /// </summary>
        public uint EncoderValue { get; set; }
        /// <summary>
        /// 指示当前帧是否需要丢弃。当NeedDrop为true时，表明视觉的运算能力不足。丢弃该帧而不进行进一步处理，以节省资源和提高效率。
        /// </summary>
        public bool NeedDrop { get; set; } = false;

        /// <summary>
        /// 指示在图像采集过程中是否发生了帧丢失。当IsFrameLoss为true时，表示当前图像结果可能不完整或不可靠，
        /// </summary>
        public bool IsFrameLoss { get; set; } = false;

        /// <summary>
        /// 指示该帧是否由手动单次触发产生。
        /// </summary>
        public bool IsManualShot { get; set; } = false;

        /// <summary>
        /// 获取图像宽度
        /// </summary>
        public uint Width { get; }

        /// <summary>
        /// 获取图像高度
        /// </summary>
        public uint Height { get; }

        /// <summary>
        /// 获取图像像素格式
        /// </summary>
        public MvCodeReaderSDKNet.MvCodeReader.MvCodeReaderGvspPixelType PixelFormat { get; }

        /// <summary>
        /// 获取图像像素格式名称。
        /// </summary>
        public string PixelFormatName => PixelFormat.ToString();

        /// <summary>
        /// 当前图像是否为 JPEG 编码。
        /// </summary>
        public bool IsJpeg => PixelFormat == MvCodeReaderSDKNet.MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Jpeg;

        /// <summary>
        /// 当前图像是否为 Mono8。
        /// </summary>
        public bool IsMono8 => PixelFormat == MvCodeReaderSDKNet.MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Mono8;

        /// <summary>
        /// 当前图像是否为 RGB8 Packed。
        /// </summary>
        public bool IsRgb8Packed => PixelFormat == MvCodeReaderSDKNet.MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_RGB8_Packed;

        /// <summary>
        /// 获取图像时间戳
        /// </summary>
        public ulong TimeStamp { get; }

        /// <summary>
        /// 获取图像帧号
        /// </summary>
        public uint FrameNumber { get; }

        /// <summary>
        /// 获取条码识别结果列表
        /// </summary>
        public BarcodeResult[] BarcodeResults { get; }

        /// <summary>
        /// 获取OCR识别结果列表
        /// </summary>
        public OcrResult[] OcrResults { get; }

        /// <summary>
        /// 获取运单识别结果列表
        /// </summary>
        public WaybillResult[] WaybillResults { get; }

        /// <summary>
        /// 获取原始图像输出信息
        /// </summary>
        public MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2 RawImageInfo { get; }

        /// <summary>
        /// 创建ImageResult实例
        /// </summary>
        /// <param name="imageData">图像数据</param>
        /// <param name="imageInfo">图像输出信息</param>
        public ImageResult(byte[] imageData, MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2 imageInfo)
        {
            ImageData = imageData ?? throw new ArgumentNullException(nameof(imageData));
            RawImageInfo = imageInfo;

            Width = imageInfo.nWidth;
            Height = imageInfo.nHeight;
            PixelFormat = imageInfo.enPixelType;
            TimeStamp = (ulong)imageInfo.nTimeStampHigh << 32 | imageInfo.nTimeStampLow;
            FrameNumber = imageInfo.nFrameNum;

            // 解析条码结果
            if (imageInfo.UnparsedBcrList.pstCodeListEx2 != IntPtr.Zero)
            {
                var bcrList = (MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2)Marshal.PtrToStructure(
                    imageInfo.UnparsedBcrList.pstCodeListEx2, typeof(MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2));

                if (bcrList.nCodeNum > 0)
                {
                    BarcodeResults = new BarcodeResult[bcrList.nCodeNum];
                    for (int i = 0; i < bcrList.nCodeNum; i++)
                    {
                        BarcodeResults[i] = new BarcodeResult(bcrList.stBcrInfoEx2[i]);
                    }
                }
                else
                {
                    BarcodeResults = Array.Empty<BarcodeResult>();
                }
            }
            else
            {
                BarcodeResults = Array.Empty<BarcodeResult>();
            }

            // OCR解析暂时禁用
            OcrResults = Array.Empty<OcrResult>();

            // 解析运单结果（如果存在）
            if (imageInfo.pstWaybillList != IntPtr.Zero)
            {
                var waybillList = (MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_WAYBILL_LIST)Marshal.PtrToStructure(
                    imageInfo.pstWaybillList, typeof(MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_WAYBILL_LIST));

                if (waybillList.nWaybillNum > 0)
                {
                    WaybillResults = new WaybillResult[waybillList.nWaybillNum];
                    for (int i = 0; i < waybillList.nWaybillNum; i++)
                    {
                        WaybillResults[i] = new WaybillResult(waybillList.stWaybillInfo[i]);
                    }
                }
                else
                {
                    WaybillResults = Array.Empty<WaybillResult>();
                }
            }
            else
            {
                WaybillResults = Array.Empty<WaybillResult>();
            }
        }

        /// <summary>
        /// 获取图像数据的大小（字节数）
        /// </summary>
        public uint ImageSize => Width * Height * GetBytesPerPixel();

        /// <summary>
        /// 根据像素格式获取每个像素的字节数
        /// </summary>
        /// <returns>每个像素的字节数</returns>
        public uint GetBytesPerPixel()
        {
            switch (PixelFormat)
            {
                case MvCodeReaderSDKNet.MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Mono8:
                    return 1;
                case MvCodeReaderSDKNet.MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_RGB8_Packed:
                    return 3;
                case MvCodeReaderSDKNet.MvCodeReader.MvCodeReaderGvspPixelType.PixelType_CodeReader_Gvsp_Jpeg:
                    return 1; // Jpeg is compressed, but usually we handle as byte stream
                default:
                    return 1;
            }
        }

        /// <summary>
        /// 检查图像是否包含条码识别结果
        /// </summary>
        public bool HasBarcodeResults => BarcodeResults.Length > 0;

        /// <summary>
        /// 检查图像是否包含OCR识别结果
        /// </summary>
        public bool HasOcrResults => OcrResults.Length > 0;

        /// <summary>
        /// 检查图像是否包含运单识别结果
        /// </summary>
        public bool HasWaybillResults => WaybillResults.Length > 0;
    }

    /// <summary>
    /// 条码识别结果
    /// </summary>
    public class BarcodeResult
    {
        /// <summary>
        /// 获取条码类型
        /// </summary>
        public MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_CODE_TYPE CodeType { get; }

        /// <summary>
        /// 获取条码类型名称。
        /// </summary>
        public string CodeTypeName => Utilities.GetBarcodeTypeString(CodeType);

        /// <summary>
        /// 当前条码是否为二维码。
        /// </summary>
        public bool IsQrCode => CodeTypeName == "QR Code";

        /// <summary>
        /// 获取条码字符串
        /// </summary>
        public string CodeString { get; }

        /// <summary>
        /// 获取条码置信度
        /// </summary>
        public uint Confidence { get; }

        /// <summary>
        /// 获取条码识别的触发时间。该时间通过当前时间减去总处理耗时估算得出，非设备端精确时间。
        /// </summary>
        public DateTime TriggerTime { get; }
        

        /// <summary>
        /// 获取条码位置（四个顶点坐标）
        /// </summary>
        public Point[] Location { get; }

        /// <summary>
        /// 图像清晰度(10倍)
        /// </summary>
        public ushort Sharpness { get; }

        /// <summary>
        /// 条码被识别的次数
        /// </summary>
        public ushort AppearCount { get; }
       /// <summary>
       /// 算法耗时
       /// </summary>
        public ushort AlgoCost { get; }
        /// <summary>
        /// 从触发开始到APP输出时间统计(ms)
        /// </summary>
        public uint TotalProcCost { get; }

        /// <summary>
        /// 条码角度10倍(0-3600)
        /// </summary>
        public int Angle { get;}
        /// <summary>
        /// 条码质量信息，包含条码图像的质量评估详细数据
        /// </summary>
        public MV_CODEREADER_CODE_INFO Quality { get; set; }

        /// <summary>
        /// 获取条码的中心点坐标。通过对四个顶点坐标取平均值计算得出，用于定位条码在图像中的大致位置。
        /// </summary>
        public Point Center
        {
            get
            {
                var x = Location.Average(p => p.X);
                var y = Location.Average(p => p.Y);
                return new Point() { X = (int)x, Y = (int)y };
            }
        }

        /// <summary>
        /// 创建BarcodeResult实例
        /// </summary>
        /// <param name="bcrInfo">原始条码识别信息</param>
        public BarcodeResult(MvCodeReader.MV_CODEREADER_BCR_INFO_EX2 bcrInfo)
        {
            CodeType = (MvCodeReader.MV_CODEREADER_CODE_TYPE)bcrInfo.nBarType;
            // REVIEW-FIX: 条码字符串改为 ASCII→UTF8→GB2312 顺序探测解码，替代 Encoding.Default
            CodeString = DecodeCodeString(bcrInfo.chCode);
            Confidence = bcrInfo.nIDRScore;
            Quality = bcrInfo.stCodeQuality;
            Angle = bcrInfo.nAngle;
            AppearCount = bcrInfo.sAppearCount;
            AlgoCost = bcrInfo.sAlgoCost;
            Sharpness = bcrInfo.sSharpness;
            TotalProcCost = bcrInfo.nTotalProcCost;


            //var firstThree= bcrInfo.nTriggerTimeUtvLow.ToString()[..Math.Min(3, bcrInfo.nTriggerTimeUtvLow.ToString().Length)];
            //DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(bcrInfo.nTriggerTimeTvLow );
            // UTC时间
            //DateTime utcTime = dateTimeOffset.UtcDateTime+ TimeSpan.FromMilliseconds(uint.Parse(firstThree));
            // 本地时间
            //DateTime localTime = dateTimeOffset.LocalDateTime;
            TriggerTime = DateTime.Now-TimeSpan.FromMilliseconds(TotalProcCost);

            Location = new Point[4];
         
            for (int i = 0; i < 4; i++)
            {
                Location[i] = new Point
                {
                    X = (int)bcrInfo.pt[i].x,
                    Y = (int)bcrInfo.pt[i].y
                };
            }
        }

        override public string ToString()
        {
            return $"Type: {CodeType}, String: {CodeString}, Confidence: {Confidence}, Location: [{string.Join(", ", Location.Select(p => $"({p.X},{p.Y})"))}]";
        }

        // REVIEW-FIX: 参考 HikScanner.BcrCodeToString 的实现思路：ASCII → UTF8 → GB2312 顺序探测
        private static readonly Lazy<Encoding> Gb2312EncodingLazy = new(() =>
        {
            try { return Encoding.GetEncoding("GB2312"); }
            catch { return Encoding.Default; } // 回退到系统默认编码
        });
        private static Encoding Gb2312Encoding => Gb2312EncodingLazy.Value;

        private static bool IsTextUtf8(byte[] input)
        {
            int remainingBytes = 0;
            bool hasNonAscii = false;
            for (int i = 0; i < input.Length; i++)
            {
                byte b = input[i];
                if ((b & 0x80) != 0) hasNonAscii = true;
                if (remainingBytes == 0)
                {
                    if ((b & 0x80) != 0)
                    {
                        if ((b & 0xC0) != 0xC0) return false;
                        remainingBytes = 1;
                        b <<= 2;
                        while ((b & 0x80) != 0) { b <<= 1; remainingBytes++; }
                    }
                }
                else
                {
                    if ((b & 0xC0) != 0x80) return false;
                    remainingBytes--;
                }
            }
            return remainingBytes == 0 && hasNonAscii;
        }

        private static string DecodeCodeString(byte[] chCode)
        {
            if (chCode == null || chCode.Length == 0) return string.Empty;
            bool isAscii = true;
            for (int i = 0; i < chCode.Length; i++) { if (chCode[i] >= 128) { isAscii = false; break; } }
            if (isAscii) return Encoding.ASCII.GetString(chCode).Trim().TrimEnd('\0');
            if (IsTextUtf8(chCode)) return Encoding.UTF8.GetString(chCode).Trim().TrimEnd('\0');
            return Gb2312Encoding.GetString(chCode).Trim().TrimEnd('\0');
        }
    }

    /// <summary>
    /// OCR识别结果
    /// </summary>
    public class OcrResult
    {
        /// <summary>
        /// 获取OCR字符串
        /// </summary>
        public string OcrString { get; }

        /// <summary>
        /// 获取OCR置信度
        /// </summary>
        public uint Confidence { get; }

        /// <summary>
        /// 获取OCR位置（四个顶点坐标）
        /// </summary>
        public Point[] Location { get; }

        /// <summary>
        /// 创建OcrResult实例
        /// </summary>
        /// <param name="ocrString">OCR字符串</param>
        /// <param name="confidence">置信度</param>
        /// <param name="location">位置</param>
        public OcrResult(string ocrString, uint confidence, Point[] location)
        {
            OcrString = ocrString;
            Confidence = confidence;
            Location = location;
        }
    }

    /// <summary>
    /// 运单识别结果
    /// </summary>
    public class WaybillResult
    {
        /// <summary>
        /// 获取运单类型
        /// </summary>
        public uint WaybillType { get; }

        /// <summary>
        /// 获取运单字符串
        /// </summary>
        public string WaybillString { get; }

        /// <summary>
        /// 获取运单置信度
        /// </summary>
        public uint Confidence { get; }

        /// <summary>
        /// 获取运单位置（四个顶点坐标）
        /// </summary>
        public Point[] Location { get; }

        /// <summary>
        /// 创建WaybillResult实例
        /// </summary>
        /// <param name="waybillInfo">原始运单信息</param>
        public WaybillResult(MvCodeReaderSDKNet.MvCodeReader.MV_CODEREADER_WAYBILL_INFO waybillInfo)
        {
            WaybillType = 0; // fAngle is not type
            WaybillString = "";
            Confidence = (uint)waybillInfo.fConfidence;

            Location = new Point[4];
            // Center to 4 points placeholder
            Location[0] = new Point { X = (int)waybillInfo.fCenterX, Y = (int)waybillInfo.fCenterY };
            Location[1] = new Point { X = (int)waybillInfo.fCenterX, Y = (int)waybillInfo.fCenterY };
            Location[2] = new Point { X = (int)waybillInfo.fCenterX, Y = (int)waybillInfo.fCenterY };
            Location[3] = new Point { X = (int)waybillInfo.fCenterX, Y = (int)waybillInfo.fCenterY };
        }
    }

    /// <summary>
    /// 点坐标
    /// </summary>
    public struct Point
    {
        /// <summary>
        /// X坐标
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Y坐标
        /// </summary>
        public int Y { get; set; }

    }
}