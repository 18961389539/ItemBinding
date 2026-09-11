using OpenCvSharp;

namespace Blob
{
    /// <summary>
    /// Blob 检测流水线配置。封装"灰度图 → 预处理 → 二值化 → 形态学 → 检测 → 过滤"的常见组合。
    /// 所有阶段可选；不设置则跳过该阶段。
    /// </summary>
    public sealed class BlobPipelineOptions
    {
        /// <summary>高斯模糊核大小。&lt;= 0 表示跳过高斯模糊。默认 5。</summary>
        public int GaussianKernelSize { get; set; } = 5;

        /// <summary>是否启用直方图均衡化。默认 false。</summary>
        public bool EqualizeHistogram { get; set; } = false;

        /// <summary>二值化方法。默认自适应阈值。</summary>
        public BinarizationMethod Binarization { get; set; } = BinarizationMethod.Adaptive;

        /// <summary>二值化阈值（Otsu/Fixed 方法使用）。</summary>
        public double Threshold { get; set; } = 128;

        /// <summary>最大值（二值化后非零像素值）。</summary>
        public double MaxValue { get; set; } = 255;

        /// <summary>自适应阈值的邻域大小（必须为奇数）。</summary>
        public int AdaptiveBlockSize { get; set; } = 11;

        /// <summary>形态学操作类型。null 表示跳过形态学。默认 null。</summary>
        public MorphTypes? MorphologyOperation { get; set; } = null;

        /// <summary>形态学核大小。</summary>
        public int MorphologyKernelSize { get; set; } = 3;

        /// <summary>形态学迭代次数。</summary>
        public int MorphologyIterations { get; set; } = 1;

        /// <summary>最小面积过滤。&lt;= 0 表示不过滤。</summary>
        public double MinArea { get; set; } = 0;

        /// <summary>最大面积过滤。&lt;= 0 表示不过滤。</summary>
        public double MaxArea { get; set; } = 0;

        /// <summary>是否在最后填充孔洞。默认 false。</summary>
        public bool FillHoles { get; set; } = false;
    }

    /// <summary>二值化方法选择。</summary>
    public enum BinarizationMethod
    {
        /// <summary>自适应阈值（Cv2.AdaptiveThreshold）。</summary>
        Adaptive,
        /// <summary>Otsu 自动阈值（Cv2.Threshold + THRESH_OTSU）。</summary>
        Otsu,
        /// <summary>固定阈值（Cv2.Threshold + THRESH_BINARY）。</summary>
        Fixed
    }

    /// <summary>
    /// 流水线便捷方法。提供端到端的 Blob 检测流程。
    /// </summary>
    public static class Pipeline
    {
        /// <summary>
        /// 端到端检测流水线：灰度图 → 预处理 → 二值化 → 形态学 → 孔洞填充 → 检测 → 面积过滤。
        /// </summary>
        /// <param name="grayImage">输入灰度图（单通道）。</param>
        /// <param name="options">流水线配置。null 使用默认值（高斯 5 + 自适应阈值）。</param>
        /// <returns>检测到的 Blob 列表。</returns>
        public static List<BlobInfo> DetectPipeline(Mat grayImage, BlobPipelineOptions? options = null)
        {
            if (grayImage is null) throw new ArgumentNullException(nameof(grayImage));
            options ??= new BlobPipelineOptions();

            // 阶段 1：预处理
            using var preprocessed = Preprocess(grayImage, options);

            // 阶段 2：二值化
            using var binary = Binarize(preprocessed, options);

            // 阶段 3：形态学
            Mat morphologyResult = binary;
            Mat? morphologyOwned = null;
            if (options.MorphologyOperation is MorphTypes op)
            {
                morphologyOwned = Detector.ApplyMorphology(binary, op,
                    options.MorphologyKernelSize, options.MorphologyIterations);
                morphologyResult = morphologyOwned;
            }

            // 阶段 4：孔洞填充
            Mat fillResult = morphologyResult;
            Mat? fillOwned = null;
            try
            {
                if (options.FillHoles)
                {
                    fillOwned = Detector.FillHoles(morphologyResult);
                    fillResult = fillOwned;
                }

                // 阶段 5：检测 + 面积过滤
                return Detector.DetectBlobs(fillResult, options.MinArea, options.MaxArea);
            }
            finally
            {
                morphologyOwned?.Dispose();
                fillOwned?.Dispose();
            }
        }

        /// <summary>
        /// 预处理：高斯模糊 + 直方图均衡化。两个阶段可选。
        /// 返回新 Mat，调用方负责 Dispose。
        /// </summary>
        public static Mat Preprocess(Mat grayImage, BlobPipelineOptions options)
        {
            if (grayImage is null) throw new ArgumentNullException(nameof(grayImage));
            if (options is null) throw new ArgumentNullException(nameof(options));

            Mat current = grayImage;
            Mat? blurred = null;
            Mat? equalized = null;
            try
            {
                // 高斯模糊
                if (options.GaussianKernelSize > 0)
                {
                    blurred = Detector.GaussianBlur(current, options.GaussianKernelSize);
                    current = blurred;
                }

                // 直方图均衡化
                if (options.EqualizeHistogram)
                {
                    equalized = Detector.EqualizeHistogram(current);
                    current = equalized;
                }

                // 如果发生过处理，current 已是 owned Mat；否则需要 Clone 原图避免调用方 Dispose 影响原图
                if (ReferenceEquals(current, grayImage))
                {
                    return grayImage.Clone();
                }
                // current 是 owned Mat，但 caller 会 Dispose；这里转交所有权
                // blurred/equalized 在 finally 中不会被 Dispose（转交所有权）
                Mat result = current;
                blurred = null;
                equalized = null;
                return result;
            }
            finally
            {
                // 只 Dispose 未转交所有权的中间 Mat
                blurred?.Dispose();
                equalized?.Dispose();
            }
        }

        /// <summary>
        /// 二值化：根据 options 选择自适应/Otsu/固定阈值。
        /// 返回新 Mat，调用方负责 Dispose。
        /// </summary>
        private static Mat Binarize(Mat grayImage, BlobPipelineOptions options)
        {
            return options.Binarization switch
            {
                BinarizationMethod.Adaptive => Detector.AdaptiveThresholdSegmentation(
                    grayImage, options.MaxValue, blockSize: options.AdaptiveBlockSize),
                BinarizationMethod.Otsu => BinarizeOtsu(grayImage, options.MaxValue),
                BinarizationMethod.Fixed => BinarizeFixed(grayImage, options.Threshold, options.MaxValue),
                _ => throw new ArgumentOutOfRangeException(nameof(options.Binarization))
            };
        }

        private static Mat BinarizeOtsu(Mat grayImage, double maxValue)
        {
            var binary = new Mat();
            Cv2.Threshold(grayImage, binary, 0, maxValue, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            return binary;
        }

        private static Mat BinarizeFixed(Mat grayImage, double threshold, double maxValue)
        {
            var binary = new Mat();
            Cv2.Threshold(grayImage, binary, threshold, maxValue, ThresholdTypes.Binary);
            return binary;
        }
    }
}
