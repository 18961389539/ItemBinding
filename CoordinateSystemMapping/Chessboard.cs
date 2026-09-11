using Extensions;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoordinateSystemMapping
{
    public struct ThreePointCoordinateSystem
    {
        public Point2f Origin; // 原点
        public Point2f XPoint; // X轴方向点
        public Point2f YPoint; // Y轴方向点

        public readonly void DrawOnMat(Mat source)
        {
            ArgumentNullException.ThrowIfNull(source);

            // 绘制原点（红色）
            Cv2.Circle(source, new OpenCvSharp.Point((int)Origin.X, (int)Origin.Y), 15, Scalar.Red, -1);
            Cv2.PutText(source, "O", new OpenCvSharp.Point((int)Origin.X + 20, (int)Origin.Y - 10),
                HersheyFonts.HersheySimplex, 1.0, Scalar.Red, 2);

            // 绘制X轴点（绿色）
            Cv2.Circle(source, new OpenCvSharp.Point((int)XPoint.X, (int)XPoint.Y), 15, Scalar.Green, -1);
            Cv2.PutText(source, "X", new OpenCvSharp.Point((int)XPoint.X + 20, (int)XPoint.Y - 10),
                HersheyFonts.HersheySimplex, 1.0, Scalar.Green, 2);

            // 绘制Y轴点（蓝色）
            Cv2.Circle(source, new OpenCvSharp.Point((int)YPoint.X, (int)YPoint.Y), 15, Scalar.Blue, -1);
            Cv2.PutText(source, "Y", new OpenCvSharp.Point((int)YPoint.X + 20, (int)YPoint.Y - 10),
                HersheyFonts.HersheySimplex, 1.0, Scalar.Blue, 2);

            // 绘制坐标轴
            Cv2.ArrowedLine(source, new OpenCvSharp.Point((int)Origin.X, (int)Origin.Y),
                new OpenCvSharp.Point((int)XPoint.X, (int)XPoint.Y), Scalar.Green, 2, LineTypes.AntiAlias, 0, 0.1);
            Cv2.ArrowedLine(source, new OpenCvSharp.Point((int)Origin.X, (int)Origin.Y),
                new OpenCvSharp.Point((int)YPoint.X, (int)YPoint.Y), Scalar.Blue, 2, LineTypes.AntiAlias, 0, 0.1);
        }



    }
    public enum DetectionMethod
    {
        /// <summary>Canny 边缘检测 + 轮廓拟合</summary>
        Contour,
        /// <summary>自适应阈值 + 轮廓拟合（替代 Canny）</summary>
        Adaptive,
        /// <summary>霍夫梯度圆检测 (HoughCircles)</summary>
        Hough
    }

    public class Chessboard
    {
        /// <summary>
        /// 是否启用调试信息保存
        /// </summary>
        public static bool EnableDebugOutput { get; set; } = false;

        /// <summary>
        /// 调试图像保存目录（为空时使用运行目录下的 DebugImages 文件夹）
        /// </summary>
        public static string DebugOutputDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 保存调试图像到指定目录
        /// </summary>
        private static void SaveDebugImage(string name, Mat image)
        {
            if (!EnableDebugOutput || image == null || image.Empty())
                return;

            var directory = string.IsNullOrEmpty(DebugOutputDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves/DebugImages")
                : DebugOutputDirectory;

            Directory.CreateDirectory(directory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            var filePath = Path.Combine(directory, $"{timestamp}_{safeName}.png");

            image.SaveImage(filePath);
        }

        #region DrawCircle
        static void DrawCircle(Mat image, int centerX, int centerY, int squareSize, int thickness = 5)
        {
            var grayOfCenter = image.At<byte>(centerY, centerX);
            var grayOfDraw = grayOfCenter < 100 ? 255 : 0;
            image.Circle(new Point(centerX, centerY), squareSize / 3, new Scalar(grayOfDraw), -1);
            image.DrawMarker(new Point(centerX, centerY), new Scalar(255 - grayOfDraw), MarkerTypes.Cross, squareSize / 4, thickness);
        }
        #endregion
        #region Generate
        private static Mat Generate(int rows, int cols, int squareSize, int marginSize = 0)
        {
            if (cols <= 0 || rows <= 0 || squareSize <= 0)
            {
                throw new ArgumentException("行列数和方块大小必须大于0");
            }

            // 1. 计算核心区域尺寸
            int width = cols * squareSize;
            int height = rows * squareSize;

            // 2. 创建基础图像 (黑色背景)
            // REVIEW-FIX: 绘制阶段抛异常时释放 baseImage，避免泄漏（原注释声称用 using 但实际未用）。
            Mat baseImage = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
            try
            {
                // 3. 绘制白色方块
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        // 偶数位置绘制白色 (行+列 为偶数)
                        if ((r + c) % 2 == 0)
                        {
                            Rect rect = new Rect(c * squareSize, r * squareSize, squareSize, squareSize);
                            Cv2.Rectangle(baseImage, rect, Scalar.White, -1); // -1 表示填充
                        }
                    }
                }
                var centerOfX = width / 2;
                var centerOfY = height / 2;
                DrawCircle(baseImage, centerOfX, centerOfY, squareSize);
                DrawCircle(baseImage, centerOfX, centerOfY - squareSize, squareSize);
                DrawCircle(baseImage, centerOfX + squareSize * 2, centerOfY, squareSize);
            }
            catch
            {
                baseImage.Dispose();
                throw;
            }
            // 4. 处理边框
            if (marginSize > 0)
            {
                Mat borderedImage = new Mat();
                // 增加白色常量边框
                Cv2.CopyMakeBorder(
                    baseImage,
                    borderedImage,
                    marginSize, marginSize, marginSize, marginSize,
                    BorderTypes.Constant,
                    Scalar.White
                );

                // baseImage 不再需要，释放它
                baseImage.Dispose();

                return borderedImage;
            }
            else
            {
                // 不需要边框，直接返回基础图
                return baseImage;
            }
        }

        public static Mat GenerateForA4(int squareSizeMM, int dpi = 300)
        {
            // A4 纸的标准尺寸 (mm)
            double pageW_mm = 210.0;
            double pageH_mm = 297.0;

            // 1. 像素转换系数
            double pxPerMM = dpi / 25.4;

            // 2. 计算 size (方块像素边长)
            int sizePixel = (int)Math.Round(squareSizeMM * pxPerMM);

            // 3. 定义物理页边距 (比如留 10mm 空白防止打印机截断)
            double marginMM = 10.0;
            int marginPixel = (int)Math.Round(marginMM * pxPerMM);

            // 4. 计算在这个方块大小下，A4纸横竖能放下多少个格子
            // 有效宽度 = 总宽 - 左右边距
            double validWidthMM = pageW_mm - (marginMM * 2);
            double validHeightMM = pageH_mm - (marginMM * 2);

            int cols = (int)Math.Floor(validWidthMM / squareSizeMM);
            int rows = (int)Math.Floor(validHeightMM / squareSizeMM);

            return Generate(cols, rows, sizePixel, marginPixel);
        }
        #endregion
        #region Find

        public static Point2f[] Find(Mat sourceMat, Size patternSize)
        {
            var flags = ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage;
            _ = Cv2.FindChessboardCorners(sourceMat, patternSize, out Point2f[] corners, flags);
            return corners;
        }
        private static Rect FindRect(Mat sourceMat, Size patternSize)
        {
            var corners = Find(sourceMat, patternSize);
            return Cv2.BoundingRect(corners);
        }
        #endregion
        #region AreSizesSimilar
        /// <summary>
        /// 判断RotatedRect列表中的尺寸是否相似
        /// </summary>
        /// <param name="rects">RotatedRect列表</param>
        /// <param name="similarityThreshold">相似度阈值，默认0.8（80%）</param>
        /// <returns>如果尺寸相似返回true，否则返回false</returns>
        private static bool AreSizesSimilar(List<RotatedRect> rects, double similarityThreshold = 0.8)
        {
            if (rects == null || rects.Count < 2)
                return true;

            // 计算所有矩形的面积
            List<double> areas = new List<double>();
            foreach (var rect in rects)
            {
                double area = rect.Size.Width * rect.Size.Height;
                areas.Add(area);
            }

            // 计算平均面积
            double avgArea = areas.Average();

            // 检查每个面积与平均面积的差异是否在阈值范围内

            foreach (double area in areas)
            {
                double similarity = Math.Min(area, avgArea) / Math.Max(area, avgArea);
                if (similarity < similarityThreshold)
                {
                    return false;
                }
            }

            return true;
        }
        #endregion

        /// <summary>
        /// 检测图像中的三个标定圆点（工业级鲁棒性版本）
        /// </summary>
        /// <param name="sourceMat">输入的单通道灰度图像</param>
        /// <param name="patternSize">棋盘内角点尺寸，用于预先定位棋盘区域并屏蔽区域外干扰（可选）</param>
        /// <param name="roiPadding">棋盘区域外扩像素数，避免边缘圆点被裁剪</param>
        /// <param name="debugCallback">调试回调函数，参数为(阶段名称, 图像)，用于可视化各处理阶段</param>
        public static List<RotatedRect> GetThreePoints(
            Mat sourceMat,
            Size? patternSize = null,
            int roiPadding = 50,
            int minArea = 100,
            int maxArea = 50_0000,
            double minCircularity = 0.85,
            int bilateralD = 7,
            double bilateralSigmaColor = 75,
            double bilateralSigmaSpace = 75,
            double cannyThreshold1 = 100,
            double cannyThreshold2 = 200,
            int kernelSize = 3,
            bool enableCLAHE = true,
            bool enableMultiStrategy = true,
            Action<string, Mat>? debugCallback = null)
        {
            if (sourceMat.Channels() != 1)
            {
                throw new ArgumentException("输入图像必须为单通道灰度图像");
            }

            // 输出原始图像
            if (EnableDebugOutput)
            {
                debugCallback?.Invoke("01_原始输入", sourceMat);
                SaveDebugImage("01_原始输入", sourceMat);
            }

            // 预处理：如果提供了patternSize，先定位棋盘区域并屏蔽区域外的像素
            using var processedMat = ApplyChessboardMask(sourceMat, patternSize, roiPadding);
            if (EnableDebugOutput)
            {
                debugCallback?.Invoke("02_棋盘区域掩码", processedMat);
                SaveDebugImage("02_棋盘区域掩码", processedMat);
            }

            // 定义多策略参数组合 — 覆盖不同检测算法和参数变体
            var strategies = new List<(string name, DetectionMethod method,
                double canny1, double canny2, double circularity, double claheClipLimit, int claheTileGridSize,
                bool enableCLAHE, bool enableMorphology, double houghDp, double houghMinDist, double houghParam1, double houghParam2)>
            {
                // === 轮廓法 (Contour) — Canny + Morphology ===
                ("Canny_默认",       DetectionMethod.Contour, cannyThreshold1, cannyThreshold2, minCircularity, 2.0,  8, true,  true,  0, 0, 0, 0),
                ("Canny_低阈值严格",  DetectionMethod.Contour, 50,  150, 0.80, 2.0,  8, true,  true,  0, 0, 0, 0),
                ("Canny_超低阈值",    DetectionMethod.Contour, 20,  70,  0.65, 3.5,  6, true,  true,  0, 0, 0, 0),
                ("Canny_高阈值",      DetectionMethod.Contour, 120, 250, 0.70, 1.5, 12, true,  true,  0, 0, 0, 0),
                ("Canny_无CLAHE",     DetectionMethod.Contour, 80,  180, 0.75, 2.0,  8, false, true,  0, 0, 0, 0),

                // === 自适应阈值法 (Adaptive) — 替代 Canny，对光照不均更鲁棒 ===
                ("Adaptive_默认",     DetectionMethod.Adaptive, 0,  0,  0.70, 2.0,  8, true,  true,  0, 0, 0, 0),
                ("Adaptive_无CLAHE",  DetectionMethod.Adaptive, 0,  0,  0.60, 2.0,  8, false, true,  0, 0, 0, 0),

                // === 霍夫圆法 (HoughCircles) — 互补算法，对标准圆效果极好 ===
                ("Hough_标准",        DetectionMethod.Hough,   0,  0,  0.85, 2.0,  8, true,  false, 1.0, 20, 100, 30),
                ("Hough_低阈值",      DetectionMethod.Hough,   0,  0,  0.70, 2.0,  8, true,  false, 1.5, 15, 80,  25),
                ("Hough_严格",        DetectionMethod.Hough,   0,  0,  0.90, 2.0,  8, true,  false, 1.0, 30, 120, 35),
            };

            if (!enableMultiStrategy)
            {
                strategies = strategies.Take(1).ToList();
            }

            List<string> attemptLogs = [];
            Exception? lastException = null;
            int strategyIndex = 0;

            foreach (var strategy in strategies)
            {
                strategyIndex++;
                var (result, debugInfo) = strategy.method switch
                {
                    DetectionMethod.Hough => TryDetectHough(
                        processedMat, minArea, maxArea, strategy.circularity,
                        strategy.enableCLAHE, strategy.claheClipLimit, strategy.claheTileGridSize,
                        strategy.houghDp, strategy.houghMinDist, strategy.houghParam1, strategy.houghParam2,
                        $"策略{strategyIndex}_{strategy.name}"),
                    DetectionMethod.Adaptive => TryDetectCirclesWithDebug(
                        processedMat, minArea, maxArea, strategy.circularity,
                        bilateralD, bilateralSigmaColor, bilateralSigmaSpace,
                        0, 0, kernelSize, strategy.enableCLAHE,
                        strategy.claheClipLimit, strategy.claheTileGridSize,
                        $"策略{strategyIndex}_{strategy.name}", useAdaptive: true),
                    _ => TryDetectCirclesWithDebug(
                        processedMat, minArea, maxArea, strategy.circularity,
                        bilateralD, bilateralSigmaColor, bilateralSigmaSpace,
                        strategy.canny1, strategy.canny2, kernelSize, strategy.enableCLAHE,
                        strategy.claheClipLimit, strategy.claheTileGridSize,
                        $"策略{strategyIndex}_{strategy.name}"),
                };

                // 输出调试图像
                foreach (var (name, mat) in debugInfo)
                {
                    if (EnableDebugOutput)
                    {
                        debugCallback?.Invoke(name, mat);
                        SaveDebugImage(name, mat);
                    }
                    mat.Dispose();
                }

                bool sizeValid = AreSizesSimilar(result);

                // 检测到恰好 3 个圆且通过几何/尺寸验证 → 直接返回
                if (result.Count == 3 && AreSizesSimilar(result) && ValidateGeometry(result))
                {
                    ReturnResult(result, strategy.name);
                    return result;
                }

                // 检测到 4+ 个圆 → 枚举所有 3-圆组合，每个组合独立验证
                if (result.Count > 3)
                {
                    var bestCombo = SelectBestThree(result);
                    if (bestCombo is not null)
                    {
                        ReturnResult(bestCombo, $"{strategy.name}(组合选择)");
                        return bestCombo;
                    }
                }

                attemptLogs.Add($"[{strategy.name}] {result.Count}个圆, 尺寸验证={sizeValid}");
            }

            var logMessage = string.Join(Environment.NewLine, attemptLogs);
            throw new Exception($"所有检测策略均失败，请检查图像质量或调整参数。{Environment.NewLine}尝试记录:{Environment.NewLine}{logMessage}", lastException);
        }

        /// <summary>输出最终结果调试图并返回</summary>
        private static void ReturnResult(List<RotatedRect> result, string label)
        {
            if (!EnableDebugOutput) return;
            // 注意: sourceMat 在此作用域内不可直接访问，这里用第一个圆的 size 作为参考
        }

        /// <summary>
        /// 从 4+ 个圆中枚举所有 C(n,3) 组合，选几何验证通过且尺寸最相似的组合。
        /// 几何容忍度从严格到宽松渐进尝试。
        /// </summary>
        private static List<RotatedRect>? SelectBestThree(List<RotatedRect> candidates)
        {
            if (candidates.Count < 4) return null;

            foreach (double tolerance in new[] { 0.4, 0.55, 0.7 })
            {
                List<RotatedRect>? best = null;
                double bestScore = 0;

                for (int i = 0; i < candidates.Count; i++)
                {
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        for (int k = j + 1; k < candidates.Count; k++)
                        {
                            var combo = new List<RotatedRect> { candidates[i], candidates[j], candidates[k] };
                            if (!ValidateGeometry(combo, tolerance)) continue;
                            if (!AreSizesSimilar(combo)) continue;

                            double score = GeometryScore(combo) + SizeSimilarity(combo);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                best = combo;
                            }
                        }
                    }
                }

                if (best is not null) return best;
            }

            return null;
        }

        /// <summary>计算三点与 L 形布局的匹配度（越高越好）</summary>
        private static double GeometryScore(List<RotatedRect> rects)
        {
            var centers = rects.Select(r => r.Center).ToList();
            var distances = new List<double>
            {
                Distance(centers[0], centers[1]),
                Distance(centers[1], centers[2]),
                Distance(centers[0], centers[2])
            };
            distances.Sort();
            double ratio1 = distances[1] / distances[0]; // 期望 ~2.0
            double ratio2 = distances[2] / distances[0]; // 期望 ~2.236
            return 1.0 / (1.0 + Math.Abs(ratio1 - 2.0) + Math.Abs(ratio2 - 2.236));
        }

        /// <summary>计算三个 RotatedRect 的尺寸相似度（越高越好）</summary>
        private static double SizeSimilarity(List<RotatedRect> rects)
        {
            double avgArea = rects.Average(r => r.Size.Width * r.Size.Height);
            double maxDeviation = rects.Max(r => Math.Abs(r.Size.Width * r.Size.Height - avgArea) / avgArea);
            return 1.0 / (1.0 + maxDeviation);
        }

        #region HoughCircles
        /// <summary>
        /// 霍夫梯度圆检测——对标准圆形效果极好，与轮廓法互补。
        /// </summary>
        private static (List<RotatedRect> results, List<(string name, Mat mat)> debugInfo) TryDetectHough(
            Mat sourceMat,
            int minArea, int maxArea, double minCircularity,
            bool enableCLAHE, double claheClipLimit, int claheTileGridSize,
            double dp, double minDist, double param1, double param2,
            string prefix)
        {
            List<RotatedRect> results = [];
            List<(string name, Mat mat)> debugInfo = [];

            // 1. CLAHE 预处理
            using var preprocessed = enableCLAHE
                ? ApplyCLAHE(sourceMat, claheClipLimit, new Size(claheTileGridSize, claheTileGridSize))
                : sourceMat.Clone();
            if (EnableDebugOutput) debugInfo.Add(($"{prefix}_03_CLAHE预处理", preprocessed.Clone()));

            // 2. 高斯模糊降噪（HoughCircles 内置模糊不够，需要预模糊）
            using var blurred = preprocessed.GaussianBlur(new Size(9, 9), 2);

            // 3. HoughCircles 梯度法
            var circles = Cv2.HoughCircles(blurred, HoughModes.Gradient, dp, minDist,
                param1: param1, param2: param2, minRadius: 0, maxRadius: 0);

            if (EnableDebugOutput)
            {
                using var debugHough = new Mat();
                Cv2.CvtColor(sourceMat, debugHough, ColorConversionCodes.GRAY2BGR);
                if (circles is { Length: > 0 })
                {
                    foreach (var c in circles)
                        Cv2.Circle(debugHough, (int)c.Center.X, (int)c.Center.Y, (int)c.Radius, Scalar.Green, 2);
                }
                debugInfo.Add(($"{prefix}_05_Hough检测({(circles?.Length ?? 0)}个)", debugHough));
            }

            if (circles is not { Length: >= 3 }) return (results, debugInfo);

            // 4. 转换为 RotatedRect，按圆度+面积筛选
            foreach (var circle in circles)
            {
                double area = Math.PI * circle.Radius * circle.Radius;
                if (area < minArea || area > maxArea) continue;

                // Hough 返回的圆本身圆度=1.0，按半径排序评分
                results.Add(new RotatedRect(
                    new Point2f(circle.Center.X, circle.Center.Y),
                    new Size2f(circle.Radius * 2, circle.Radius * 2),
                    0));
            }

            // 5. NMS 去重，保留最大半径
            results.Sort((a, b) =>
            {
                double rA = a.Size.Width * a.Size.Height;
                double rB = b.Size.Width * b.Size.Height;
                return rB.CompareTo(rA);
            });
            var nmsResults = results.ApplyNMS();

            if (EnableDebugOutput)
                debugInfo.Add(($"{prefix}_09_NMS去重后({nmsResults.Count}个)",
                    DrawDetectionResult(sourceMat, nmsResults, false)));

            return (nmsResults, debugInfo);
        }
        #endregion

        #region TryDetectCirclesWithDebug
        /// <summary>
        /// 核心检测逻辑（带调试输出）
        /// </summary>
        private static (List<RotatedRect> results, List<(string name, Mat mat)> debugInfo) TryDetectCirclesWithDebug(
            Mat sourceMat,
            int minArea,
            int maxArea,
            double minCircularity,
            int bilateralD,
            double bilateralSigmaColor,
            double bilateralSigmaSpace,
            double cannyThreshold1,
            double cannyThreshold2,
            int kernelSize,
            bool enableCLAHE,
            double claheClipLimit,
            int claheTileGridSize,
            string prefix,
            bool useAdaptive = false)
        {
            List<RotatedRect> results = [];
            List<(string name, Mat mat)> debugInfo = [];

            // 1. 可选CLAHE预处理（处理光照不均）
            using var preprocessed = enableCLAHE ? ApplyCLAHE(sourceMat, claheClipLimit, new Size(claheTileGridSize, claheTileGridSize)) : sourceMat.Clone();
            if (EnableDebugOutput) debugInfo.Add(($"{prefix}_03_CLAHE预处理", preprocessed.Clone()));

            // 2. 双边滤波降噪
            using var filtered = preprocessed.BilateralFilter(bilateralD, bilateralSigmaColor, bilateralSigmaSpace);
            if (EnableDebugOutput) debugInfo.Add(($"{prefix}_04_双边滤波", filtered.Clone()));

            // 3. 边缘/轮廓检测
            using var edges = useAdaptive
                ? filtered.AdaptiveThreshold(255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.BinaryInv, 11, 2)
                : filtered.Canny(cannyThreshold1, cannyThreshold2);
            if (EnableDebugOutput) debugInfo.Add(($"{prefix}_05_{((useAdaptive ? "自适应阈值" : "Canny边缘"))}", edges.Clone()));

            // 4. 形态学闭运算
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kernelSize, kernelSize));
            using var closed = edges.MorphologyEx(MorphTypes.Close, kernel);
            if (EnableDebugOutput) debugInfo.Add(($"{prefix}_06_形态学闭运算", closed.Clone()));

            // 5. 查找轮廓
            Cv2.FindContours(closed, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            // 绘制所有轮廓（仅调试模式）
            if (EnableDebugOutput)
            {
                using var contourImg = new Mat();
                Cv2.CvtColor(sourceMat, contourImg, ColorConversionCodes.GRAY2BGR);
                Cv2.DrawContours(contourImg, contours, -1, Scalar.Yellow, 1);
                debugInfo.Add(($"{prefix}_07_所有轮廓({contours.Length}个)", contourImg.Clone()));
            }

            // 6. 筛选轮廓
            List<(Point[] contour, double area, double circularity, double axisRatio, string rejectReason)>? contourAnalysis =
                EnableDebugOutput ? [] : null;

            foreach (var contour in contours)
            {
                if (contour.Length < 10)
                {
                    contourAnalysis?.Add((contour, 0, 0, 0, "点数不足"));
                    continue;
                }

                double area = Cv2.ContourArea(contour);
                if (area < minArea || area > maxArea)
                {
                    contourAnalysis?.Add((contour, area, 0, 0, $"面积{area:F0}不在范围[{minArea},{maxArea}]"));
                    continue;
                }

                double perimeter = Cv2.ArcLength(contour, true);
                if (perimeter < 5)
                {
                    contourAnalysis?.Add((contour, area, 0, 0, "周长过小"));
                    continue;
                }

                double circularity = 4 * Math.PI * area / (perimeter * perimeter);
                if (circularity < minCircularity)
                {
                    contourAnalysis?.Add((contour, area, circularity, 0, $"圆度{circularity:F2}<{minCircularity}"));
                    continue;
                }

                RotatedRect ellipse = Cv2.FitEllipse(contour);
                double axisRatio = Math.Min(ellipse.Size.Width, ellipse.Size.Height) /
                                   Math.Max(ellipse.Size.Width, ellipse.Size.Height);
                if (axisRatio < 0.6)
                {
                    contourAnalysis?.Add((contour, area, circularity, axisRatio, $"长短轴比{axisRatio:F2}<0.6"));
                    continue;
                }

                // 通过所有检查
                contourAnalysis?.Add((contour, area, circularity, axisRatio, "通过"));
                results.Add(ellipse);
            }

            // 绘制筛选分析图（仅调试模式）
            if (EnableDebugOutput && contourAnalysis != null)
            {
                debugInfo.Add(($"{prefix}_08_轮廓筛选分析", DrawContourAnalysis(sourceMat, contourAnalysis)));
            }

            // 7. 按圆度+面积排序后 NMS — 优先保留最圆且最大的轮廓
            results.Sort((a, b) =>
            {
                double scoreA = (Math.Min(a.Size.Width, a.Size.Height) / Math.Max(a.Size.Width, a.Size.Height))
                                * (a.Size.Width * a.Size.Height);
                double scoreB = (Math.Min(b.Size.Width, b.Size.Height) / Math.Max(b.Size.Width, b.Size.Height))
                                * (b.Size.Width * b.Size.Height);
                return scoreB.CompareTo(scoreA); // 降序
            });

            var nmsResults = results.ApplyNMS();

            // 绘制NMS结果（仅调试模式）
            if (EnableDebugOutput)
            {
                debugInfo.Add(($"{prefix}_09_NMS去重后({nmsResults.Count}个)", DrawDetectionResult(sourceMat, nmsResults, false)));
            }

            return (nmsResults, debugInfo);
        }
        #endregion

        #region DrawContourAnalysis
        /// <summary>
        /// 绘制轮廓筛选分析图
        /// </summary>
        private static Mat DrawContourAnalysis(Mat source, List<(Point[] contour, double area, double circularity, double axisRatio, string rejectReason)> analysis)
        {
            var result = new Mat();
            Cv2.CvtColor(source, result, ColorConversionCodes.GRAY2BGR);

            foreach (var (contour, area, circularity, axisRatio, rejectReason) in analysis)
            {
                if (contour.Length < 5) continue;

                Scalar color;
                int thickness;

                if (rejectReason == "通过")
                {
                    color = Scalar.Green;
                    thickness = 2;
                }
                else if (rejectReason.Contains("面积"))
                {
                    color = Scalar.Red;
                    thickness = 1;
                }
                else if (rejectReason.Contains("圆度"))
                {
                    color = Scalar.Orange;
                    thickness = 1;
                }
                else if (rejectReason.Contains("长短轴"))
                {
                    color = Scalar.Magenta;
                    thickness = 1;
                }
                else
                {
                    color = Scalar.Gray;
                    thickness = 1;
                }

                Cv2.DrawContours(result, [contour], 0, color, thickness);

                // 在轮廓中心标注原因
                var moments = Cv2.Moments(contour);
                if (moments.M00 > 0)
                {
                    int cx = (int)(moments.M10 / moments.M00);
                    int cy = (int)(moments.M01 / moments.M00);

                    if (rejectReason != "通过" && rejectReason != "点数不足")
                    {
                        Cv2.PutText(result, rejectReason, new Point(cx - 50, cy),
                            HersheyFonts.HersheySimplex, 0.3, color, 1);
                    }
                }
            }

            // 添加图例
            int legendY = 20;
            Cv2.PutText(result, "绿色=通过", new Point(10, legendY), HersheyFonts.HersheySimplex, 0.5, Scalar.Green, 1);
            Cv2.PutText(result, "红色=面积不符", new Point(10, legendY + 20), HersheyFonts.HersheySimplex, 0.5, Scalar.Red, 1);
            Cv2.PutText(result, "橙色=圆度不足", new Point(10, legendY + 40), HersheyFonts.HersheySimplex, 0.5, Scalar.Orange, 1);
            Cv2.PutText(result, "紫色=长短轴比不符", new Point(10, legendY + 60), HersheyFonts.HersheySimplex, 0.5, Scalar.Magenta, 1);

            return result;
        }
        #endregion

        #region DrawDetectionResult
        /// <summary>
        /// 绘制检测结果
        /// </summary>
        private static Mat DrawDetectionResult(Mat source, List<RotatedRect> rects, bool drawGeometry)
        {
            var result = new Mat();
            Cv2.CvtColor(source, result, ColorConversionCodes.GRAY2BGR);

            for (int i = 0; i < rects.Count; i++)
            {
                var rect = rects[i];
                Cv2.Ellipse(result, rect, Scalar.Green, 2);
                Cv2.Circle(result, new Point((int)rect.Center.X, (int)rect.Center.Y), 5, Scalar.Red, -1);
                Cv2.PutText(result, $"#{i + 1} ({rect.Center.X:F0},{rect.Center.Y:F0})",
                    new Point((int)rect.Center.X + 10, (int)rect.Center.Y),
                    HersheyFonts.HersheySimplex, 0.5, Scalar.Yellow, 1);
            }

            // 绘制几何关系
            if (drawGeometry && rects.Count == 3)
            {
                var centers = rects.Select(r => new Point((int)r.Center.X, (int)r.Center.Y)).ToList();

                // 绘制三角形连线
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    double dist = Distance(rects[i].Center, rects[j].Center);
                    Cv2.Line(result, centers[i], centers[j], Scalar.Cyan, 1);

                    var midPoint = new Point((centers[i].X + centers[j].X) / 2, (centers[i].Y + centers[j].Y) / 2);
                    Cv2.PutText(result, $"{dist:F0}px", midPoint, HersheyFonts.HersheySimplex, 0.4, Scalar.Cyan, 1);
                }

                // 显示几何验证结果
                bool geometryValid = ValidateGeometry(rects);
                string geometryText = geometryValid ? "几何验证: 通过" : "几何验证: 失败";
                Scalar geometryColor = geometryValid ? Scalar.Green : Scalar.Red;
                Cv2.PutText(result, geometryText, new Point(10, result.Height - 20),
                    HersheyFonts.HersheySimplex, 0.6, geometryColor, 2);
            }

            return result;
        }
        #endregion

        #region ApplyChessboardMask
        /// <summary>
        /// 根据棋盘区域创建掩码，将区域外的像素设为黑色
        /// </summary>
        private static Mat ApplyChessboardMask(Mat sourceMat, Size? patternSize, int roiPadding)
        {
            if (patternSize == null)
            {
                return sourceMat.Clone();
            }

            // 查找棋盘区域
            var chessboardRect = FindRect(sourceMat, patternSize.Value);

            if (chessboardRect.Width == 0 || chessboardRect.Height == 0)
            {
                // 未找到棋盘，返回原图副本
                return sourceMat.Clone();
            }

            // 扩展ROI区域，避免边缘圆点被裁剪
            var expandedRect = new Rect(
                Math.Max(0, chessboardRect.X - roiPadding),
                Math.Max(0, chessboardRect.Y - roiPadding),
                Math.Min(sourceMat.Width - Math.Max(0, chessboardRect.X - roiPadding), chessboardRect.Width + roiPadding * 2),
                Math.Min(sourceMat.Height - Math.Max(0, chessboardRect.Y - roiPadding), chessboardRect.Height + roiPadding * 2)
            );

            // 创建全黑图像
            var maskedMat = new Mat(sourceMat.Size(), sourceMat.Type(), Scalar.Black);

            // REVIEW-FIX: 原实现 `roi.CopyTo(new Mat(maskedMat, expandedRect))` 中临时创建的
            // ROI Mat 未 Dispose，每帧标定泄漏一个 Mat 对象。目标 ROI 视图同样用 using 管理。
            using var roi = new Mat(sourceMat, expandedRect);
            using var targetRoi = new Mat(maskedMat, expandedRect);
            roi.CopyTo(targetRoi);

            return maskedMat;
        }
        #endregion

        #region ApplyCLAHE
        /// <summary>
        /// 应用CLAHE（对比度受限的自适应直方图均衡化）处理光照不均
        /// </summary>
        private static Mat ApplyCLAHE(Mat source, double clipLimit = 2.0, Size? tileGridSize = null)
        {
            var gridSize = tileGridSize ?? new Size(8, 8);
            using var clahe = Cv2.CreateCLAHE(clipLimit, gridSize);
            var result = new Mat();
            clahe.Apply(source, result);
            return result;
        }
        #endregion

        #region ValidateGeometry
        /// <summary>
        /// 验证三点的几何关系是否符合预期的L形布局
        /// </summary>
        private static bool ValidateGeometry(List<RotatedRect> rects, double toleranceRatio = 0.4)
        {
            if (rects.Count != 3)
                return false;

            // 获取三个圆心
            var centers = rects.Select(r => r.Center).ToList();

            // 计算三点之间的距离
            var distances = new List<double>
            {
                Distance(centers[0], centers[1]),
                Distance(centers[1], centers[2]),
                Distance(centers[0], centers[2])
            };

            // 根据棋盘布局，三个圆应形成L形：
            // - Origin 在左侧
            // - YPoint 在 Origin 上方（距离1个方格）
            // - XPoint 在 Origin 右侧（距离2个方格）
            // 所以距离比例大约是 1:2:√5

            distances.Sort();
            double d1 = distances[0]; // 最短边 (Origin-YPoint)
            double d2 = distances[1]; // 中等边 (Origin-XPoint 或 YPoint-XPoint)
            double d3 = distances[2]; // 最长边

            // 验证距离比例是否合理
            // d2/d1 应该接近 2（Origin到XPoint是2个方格，到YPoint是1个方格）
            // d3/d1 应该接近 √5 ≈ 2.236
            double ratio1 = d2 / d1;
            double ratio2 = d3 / d1;

            bool ratio1Valid = ratio1 >= (2.0 - toleranceRatio) && ratio1 <= (2.0 + toleranceRatio);
            bool ratio2Valid = ratio2 >= (2.236 - toleranceRatio) && ratio2 <= (2.236 + toleranceRatio);

            return ratio1Valid && ratio2Valid;
        }
        #endregion

        #region Distance
        private static double Distance(Point2f p1, Point2f p2)
        {
            return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        }
        #endregion

        public static ThreePointCoordinateSystem GetCoordinateSystem(IEnumerable<Point2f> points)
        {
            var pointList = points.ToList();
            if (pointList.Count != 3)
            {
                throw new ArgumentException("输入必须为三个点");
            }

            // 计算三点之间的距离
            double d01 = Distance(pointList[0], pointList[1]);
            double d12 = Distance(pointList[1], pointList[2]);
            double d02 = Distance(pointList[0], pointList[2]);

            // 找到最长边（斜边），其对面的点就是 Origin（直角顶点）
            // L形布局：Origin-YPoint 距离1格，Origin-XPoint 距离2格，YPoint-XPoint 距离√5格（最长）
            Point2f origin, xPoint, yPoint;

            if (d12 > d01 && d12 > d02)
            {
                // d12 最长，pointList[0] 是 Origin
                origin = pointList[0];
                // 距离 Origin 较近的是 YPoint（1格），较远的是 XPoint（2格）
                if (d01 < d02)
                {
                    yPoint = pointList[1];
                    xPoint = pointList[2];
                }
                else
                {
                    yPoint = pointList[2];
                    xPoint = pointList[1];
                }
            }
            else if (d02 > d01 && d02 > d12)
            {
                // d02 最长，pointList[1] 是 Origin
                origin = pointList[1];
                if (d01 < d12)
                {
                    yPoint = pointList[0];
                    xPoint = pointList[2];
                }
                else
                {
                    yPoint = pointList[2];
                    xPoint = pointList[0];
                }
            }
            else
            {
                // d01 最长，pointList[2] 是 Origin
                origin = pointList[2];
                if (d02 < d12)
                {
                    yPoint = pointList[0];
                    xPoint = pointList[1];
                }
                else
                {
                    yPoint = pointList[1];
                    xPoint = pointList[0];
                }
            }

            return new ThreePointCoordinateSystem
            {
                Origin = origin,
                XPoint = xPoint,
                YPoint = yPoint
            };
        }

    }
}