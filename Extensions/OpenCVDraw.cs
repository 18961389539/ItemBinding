using OpenCvSharp;
using System.Collections.Generic;
using System.Linq;

namespace Extensions
{
    public static class OpenCVDraw
    {
        // L323: 提取魔法数字为常量
        private const double DegreesToRadians = Math.PI / 180.0;
        private const int ScaleBarThickness = 10;
        /// <summary>
        /// 在图像上绘制旋转矩形
        /// </summary>
        /// <param name="image">目标图像</param>
        /// <param name="rotatedRect">旋转矩形</param>
        /// <param name="color">颜色 (默认: 红色)</param>
        /// <param name="thickness">线条粗细 (默认: 2)</param>
        /// <param name="lineType">线条类型 (默认: LineTypes.Link8)</param>
        /// <param name="shift">坐标精度 (默认: 0)</param>
        public static void DrawRotatedRect(
            this Mat image,
            RotatedRect rotatedRect,
            Scalar? color = null,
            int thickness = 2,
            LineTypes lineType = LineTypes.Link8,
            int shift = 0)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            var actualColor = color ?? Scalar.Red;

            // 获取旋转矩形的四个顶点
            Point2f[] vertices = rotatedRect.Points();

            // 将Point2f转换为Point
            OpenCvSharp.Point[] points = new OpenCvSharp.Point[4];
            for (int i = 0; i < 4; i++)
            {
                points[i] = new OpenCvSharp.Point((int)vertices[i].X, (int)vertices[i].Y);
            }

            // 绘制旋转矩形的四条边
            Cv2.Line(image, points[0], points[1], actualColor, thickness, lineType, shift);
            Cv2.Line(image, points[1], points[2], actualColor, thickness, lineType, shift);
            Cv2.Line(image, points[2], points[3], actualColor, thickness, lineType, shift);
            Cv2.Line(image, points[3], points[0], actualColor, thickness, lineType, shift);
        }

        /// <summary>
        /// 在图像上绘制旋转矩形（带填充）
        /// </summary>
        /// <param name="image">目标图像</param>
        /// <param name="rotatedRect">旋转矩形</param>
        /// <param name="color">填充颜色 (默认: 半透明红色)</param>
        public static void DrawRotatedRectFilled(
            this Mat image,
            RotatedRect rotatedRect,
            Scalar? color = null)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            var actualColor = color ?? new Scalar(255, 0, 0, 128); // 半透明红色

            // 获取旋转矩形的四个顶点
            Point2f[] vertices = rotatedRect.Points();

            // 将Point2f转换为Point
            OpenCvSharp.Point[] points = new OpenCvSharp.Point[4];
            for (int i = 0; i < 4; i++)
            {
                points[i] = new OpenCvSharp.Point((int)vertices[i].X, (int)vertices[i].Y);
            }

            // 填充旋转矩形
            Cv2.FillConvexPoly(image, points, actualColor);
        }

        /// <summary>
        /// 在图像上绘制旋转矩形及其中心点和角度线
        /// </summary>
        /// <param name="image">目标图像</param>
        /// <param name="rotatedRect">旋转矩形</param>
        /// <param name="rectColor">矩形颜色 (默认: 红色)</param>
        /// <param name="centerColor">中心点颜色 (默认: 绿色)</param>
        /// <param name="angleColor">角度线颜色 (默认: 蓝色)</param>
        /// <param name="thickness">线条粗细 (默认: 2)</param>
        public static void DrawRotatedRectDetailed(
            this Mat image,
            RotatedRect rotatedRect,
            Scalar? rectColor = null,
            Scalar? centerColor = null,
            Scalar? angleColor = null,
            int thickness = 2)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            var actualRectColor = rectColor ?? Scalar.Red;
            var actualCenterColor = centerColor ?? Scalar.Green;
            var actualAngleColor = angleColor ?? Scalar.Blue;

            // 绘制旋转矩形
            image.DrawRotatedRect(rotatedRect, actualRectColor, thickness);

            // 绘制中心点
            OpenCvSharp.Point center = new OpenCvSharp.Point((int)rotatedRect.Center.X, (int)rotatedRect.Center.Y);
            Cv2.Circle(image, center, thickness * 2, actualCenterColor, -1); // 实心圆点

            // 绘制角度线（从中心指向矩形的长边方向）
            double angleRad = rotatedRect.Angle * DegreesToRadians;
            double lineLength = Math.Max(rotatedRect.Size.Width, rotatedRect.Size.Height) / 2;
            OpenCvSharp.Point endPoint = new OpenCvSharp.Point(
                center.X + (int)(lineLength * Math.Cos(angleRad)),
                center.Y + (int)(lineLength * Math.Sin(angleRad))
            );

            Cv2.Line(image, center, endPoint, actualAngleColor, thickness);
        }

        public static void DrawRotatedRectDetailedByContours(
            this Mat image,
            IEnumerable<IEnumerable<OpenCvSharp.Point>> contours,
            Scalar? rectColor = null,
            Scalar? centerColor = null,
            Scalar? angleColor = null,
            int thickness = 2)
        {
            ArgumentNullException.ThrowIfNull(image);

            // M258: 转为 List 避免双重枚举 IEnumerable
            var list = contours?.ToList();
            if (list is null || list.Count == 0)
            {
                return;
            }

            foreach (var contour in list)
            {
                // L363a: 跳过顶点数不足的轮廓，避免 MinAreaRect 抛出异常
                // L428b: MinAreaRect 实际需至少 3 个点才能计算外接矩形，< 2 仍会让 2 点轮廓通过导致异常
                var contourList = contour.ToList();
                if (contourList.Count < 3) continue;
                var rotatedRect = Cv2.MinAreaRect(contourList);
                image.DrawRotatedRectDetailed(rotatedRect, rectColor, centerColor, angleColor, thickness);
            }
        }

        // ========== 基础几何图形绘制 ==========

        /// <summary>
        /// 绘制带标签的矩形（用于目标检测标注）
        /// </summary>
        public static void DrawLabeledRect(this Mat image, Rect rect, string label,
            Scalar color, int thickness = 2, double fontScale = 0.5, int padding = 2)
        {
            ArgumentNullException.ThrowIfNull(image);
            // L397b: label 参数需 null 检查
            ArgumentNullException.ThrowIfNull(label);
            if (image.Empty())
                return;

            // 绘制矩形
            Cv2.Rectangle(image, rect, color, thickness);

            // 计算文本尺寸
            var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, fontScale, 1, out _);
            int textWidth = textSize.Width;
            int textHeight = textSize.Height;

            // 计算标签背景矩形
            Rect labelRect = new Rect(rect.X, rect.Y - textHeight - padding * 2,
                textWidth + padding * 2, textHeight + padding * 2);

            // 确保标签背景在图像范围内
            if (labelRect.Y < 0)
                labelRect.Y = rect.Y + rect.Height;
            // L362a: 确保标签背景不超出图像下边界
            // L398c: 用 Math.Max(0, ...) 保护，避免标签高度超过图像高度时 Y 为负
            if (labelRect.Y + labelRect.Height > image.Height)
                labelRect.Y = Math.Max(0, image.Height - labelRect.Height);

            // 绘制标签背景
            Cv2.Rectangle(image, labelRect, color, -1); // 填充

            // 绘制文本
            Point textOrigin = new Point(labelRect.X + padding, labelRect.Y + textHeight + padding);
            Cv2.PutText(image, label, textOrigin, HersheyFonts.HersheySimplex, fontScale, Scalar.White, 1);
        }

        /// <summary>
        /// 绘制多边形（任意顶点）
        /// </summary>
        public static void DrawPolygon(this Mat image, IEnumerable<OpenCvSharp.Point> points,
            Scalar color, int thickness = 2, bool fill = false)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            var ptsArray = points.ToArray();
            if (ptsArray.Length < 2)
                return;

            if (fill)
                Cv2.FillConvexPoly(image, ptsArray, color);
            else
                Cv2.Polylines(image, new[] { ptsArray }, true, color, thickness);
        }

        /// <summary>
        /// 绘制箭头（指示方向）
        /// </summary>
        /// <param name="tipLengthPercent">箭头尖端长度占线段长度的百分比（0-100）</param>
        public static void DrawArrow(this Mat image, OpenCvSharp.Point pt1, OpenCvSharp.Point pt2,
            Scalar color, int thickness = 2, int tipLengthPercent = 10)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            // L404a: 参数重命名为 tipLengthPercent，原命名 tipLength 误导（实际为百分比）
            Cv2.ArrowedLine(image, pt1, pt2, color, thickness, LineTypes.Link8, 0, tipLengthPercent / 100.0);
        }

        /// <summary>
        /// 绘制椭圆（带旋转）
        /// </summary>
        public static void DrawEllipse(this Mat image, RotatedRect ellipse,
            Scalar color, int thickness = 2, bool fill = false)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            int drawThickness = fill ? -1 : thickness;
            Cv2.Ellipse(image, ellipse, color, drawThickness);
        }

        // ========== 文本与标签绘制 ==========

        /// <summary>
        /// 在指定位置绘制多行文本
        /// </summary>
        public static void DrawText(this Mat image, string text, OpenCvSharp.Point origin,
            Scalar color, double fontScale = 0.5, int thickness = 1,
            HersheyFonts font = HersheyFonts.HersheySimplex)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            Cv2.PutText(image, text, origin, font, fontScale, color, thickness);
        }

        /// <summary>
        /// 绘制带背景框的文本（提高可读性）
        /// </summary>
        public static void DrawTextBox(this Mat image, string text, OpenCvSharp.Point origin,
            Scalar textColor, Scalar bgColor, double fontScale = 0.5, int padding = 3)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            var textSize = Cv2.GetTextSize(text, HersheyFonts.HersheySimplex, fontScale, 1, out _);
            Rect bgRect = new Rect(origin.X - padding, origin.Y - textSize.Height - padding,
                textSize.Width + padding * 2, textSize.Height + padding * 2);

            Cv2.Rectangle(image, bgRect, bgColor, -1);
            Cv2.PutText(image, text, new OpenCvSharp.Point(origin.X, origin.Y),
                HersheyFonts.HersheySimplex, fontScale, textColor, 1);
        }

        /// <summary>
        /// 在矩形上方绘制标签（自动计算文本位置）
        /// </summary>
        public static void DrawLabelAboveRect(this Mat image, Rect rect, string label,
            Scalar rectColor, Scalar textColor, int thickness = 2)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            Cv2.Rectangle(image, rect, rectColor, thickness);

            var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.5, 1, out _);
            OpenCvSharp.Point textOrigin = new OpenCvSharp.Point(
                rect.X + (rect.Width - textSize.Width) / 2,
                rect.Y - textSize.Height - 5);

            // 如果上方空间不足，则在矩形内部绘制
            if (textOrigin.Y < 0)
                textOrigin.Y = rect.Y + rect.Height + textSize.Height + 5;
            // L411b: 标签 Y 坐标超出图像下边界时回退到图像底部内侧，避免文本绘制到图像外
            if (textOrigin.Y > image.Height)
                textOrigin.Y = Math.Max(0, image.Height - textSize.Height - 5);

            Cv2.PutText(image, label, textOrigin, HersheyFonts.HersheySimplex, 0.5, textColor, 1);
        }

        // ========== 高级可视化 ==========

        /// <summary>
        /// 绘制关键点及其序号
        /// </summary>
        public static void DrawKeypoints(this Mat image, IEnumerable<OpenCvSharp.Point> keypoints,
            Scalar color, int radius = 3, bool drawNumbers = false)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            int index = 0;
            foreach (var pt in keypoints)
            {
                Cv2.Circle(image, pt, radius, color, -1);
                if (drawNumbers)
                {
                    Cv2.PutText(image, index.ToString(), new OpenCvSharp.Point(pt.X + radius, pt.Y - radius),
                        HersheyFonts.HersheySimplex, 0.4, color, 1);
                }
                index++;
            }
        }

        /// <summary>
        /// 绘制轮廓层次结构（不同层级用不同颜色）
        /// </summary>
        public static void DrawContourHierarchy(this Mat image,
            IEnumerable<IEnumerable<OpenCvSharp.Point>> contours, IEnumerable<HierarchyIndex> hierarchy,
            Scalar[] levelColors, int thickness = 1)
        {
            ArgumentNullException.ThrowIfNull(image);
            // L371a: contours 和 hierarchy 参数 null 检查
            ArgumentNullException.ThrowIfNull(contours);
            ArgumentNullException.ThrowIfNull(hierarchy);
            // M315b: levelColors 参数需 null 检查
            ArgumentNullException.ThrowIfNull(levelColors);
            if (image.Empty())
                return;

            var contourList = contours.ToList();
            var hierarchyList = hierarchy.ToList();

            // M152: 校验 contours 与 hierarchy 长度一致，避免越界访问
            if (hierarchyList.Count < contourList.Count)
                return;

            // L420a: 在循环外声明 HashSet，循环内 Clear() 复用，避免每个轮廓分配新 HashSet
            var visited = new HashSet<int>();
            for (int i = 0; i < contourList.Count; i++)
            {
                int level = 0;
                var h = hierarchyList[i];
                // M250: 使用 HashSet 跟踪已访问节点，避免循环引用导致无限循环
                visited.Clear();
                visited.Add(i);
                while (h.Parent >= 0 && h.Parent < hierarchyList.Count && visited.Add(h.Parent))
                {
                    level++;
                    h = hierarchyList[h.Parent];
                }

                Scalar color = level < levelColors.Length ? levelColors[level] : Scalar.White;
                // M353a: 显式指定 maxLevel: 0 仅绘制当前轮廓本身，避免在传入 hierarchy 时
                // 默认 maxLevel=int.MaxValue 导致子轮廓被重复绘制（与父轮廓叠加）。
                // 此方法已通过 levelColors 为不同层级分配颜色，重复绘制会破坏层级色彩区分。
                Cv2.DrawContours(image, contourList, i, color, thickness, LineTypes.Link8, hierarchyList, 0);
            }
        }

        /// <summary>
        /// 绘制热力图叠加（颜色映射）
        /// </summary>
        public static void DrawHeatmapOverlay(this Mat image, Mat heatmap,
            ColormapTypes colormap = ColormapTypes.Jet, double alpha = 0.5)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty() || heatmap.Empty())
                return;

            // M254: alpha 限制在 [0, 1] 范围内，避免越界值导致 AddWeighted 结果异常
            alpha = Math.Clamp(alpha, 0.0, 1.0);

            // M134: 不修改输入 heatmap，缩放到副本
            using var resized = new Mat();
            Cv2.Resize(heatmap, resized, image.Size());

            // L109: 使用 using 声明确保 Mat 释放
            using var coloredHeatmap = new Mat();
            Cv2.ApplyColorMap(resized, coloredHeatmap, colormap);

            // 叠加到原图
            Cv2.AddWeighted(image, 1.0 - alpha, coloredHeatmap, alpha, 0, image);
        }

        // ========== 批量绘图与性能优化 ==========

        /// <summary>
        /// 批量绘制矩形（减少循环开销）
        /// </summary>
        public static void DrawRects(this Mat image, IEnumerable<Rect> rects,
            Scalar color, int thickness = 2)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            foreach (var rect in rects)
                Cv2.Rectangle(image, rect, color, thickness);
        }

        /// <summary>
        /// 批量绘制旋转矩形并填充（支持不同颜色）
        /// </summary>
        public static void DrawRotatedRectsFilled(this Mat image,
            IEnumerable<RotatedRect> rotatedRects, Scalar[] colors)
        {
            ArgumentNullException.ThrowIfNull(image);
            // M270b: colors 参数需 null 检查
            ArgumentNullException.ThrowIfNull(colors);
            if (image.Empty())
                return;

            int index = 0;
            foreach (var rotatedRect in rotatedRects)
            {
                Scalar color = index < colors.Length ? colors[index] : Scalar.Red;
                image.DrawRotatedRectFilled(rotatedRect, color);
                index++;
            }
        }

        // ========== 图像处理辅助绘图 ==========

        /// <summary>
        /// 绘制轮廓的几何特征（面积、周长、中心点）
        /// </summary>
        public static void DrawContourFeatures(this Mat image,
            IEnumerable<OpenCvSharp.Point> contour, Scalar contourColor, Scalar featureColor)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            // M352a: 一次性转为数组，避免后续多次枚举 IEnumerable（原代码枚举 4 次：
            // DrawContours/ContourArea/ArcLength/Moments 各一次）
            var arr = contour.ToArray();

            // 绘制轮廓
            Cv2.DrawContours(image, new[] { arr }, 0, contourColor, 2);

            // 计算特征
            double area = Cv2.ContourArea(arr);
            double perimeter = Cv2.ArcLength(arr, true);
            var moments = Cv2.Moments(arr);
            // M247/M249: 统一使用 1e-4 阈值判断 M00 是否为零，避免除零产生无效坐标
            OpenCvSharp.Point center = Math.Abs(moments.M00) > 1e-4
                ? new OpenCvSharp.Point((int)(moments.M10 / moments.M00), (int)(moments.M01 / moments.M00))
                : new OpenCvSharp.Point(0, 0);

            // 绘制中心点
            Cv2.Circle(image, center, 5, featureColor, -1);

            // 显示特征文本
            string text = $"Area: {area:F1}, Perimeter: {perimeter:F1}";
            Cv2.PutText(image, text, new OpenCvSharp.Point(center.X + 10, center.Y),
                HersheyFonts.HersheySimplex, 0.5, featureColor, 1);
        }

        /// <summary>
        /// 绘制最小外接圆与拟合椭圆（对比展示）
        /// </summary>
        public static void DrawEnclosingShapes(this Mat image,
            IEnumerable<OpenCvSharp.Point> contour, Scalar circleColor, Scalar ellipseColor)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            // 最小外接圆
            Cv2.MinEnclosingCircle(contour, out Point2f center, out float radius);
            Cv2.Circle(image, (OpenCvSharp.Point)center, (int)radius, circleColor, 2);

            // 拟合椭圆
            try
            {
                var ellipse = Cv2.FitEllipse(contour);
                Cv2.Ellipse(image, ellipse, ellipseColor, 2);
            }
            catch (Exception ex)
            {
                // L254: 如果无法拟合椭圆（点数不足等），记录异常而非静默吞掉
                System.Diagnostics.Trace.WriteLine($"FitEllipse 失败: {ex}");
            }
        }

        /// <summary>
        /// 绘制凸包与原始轮廓（可视化凸性）
        /// </summary>
        public static void DrawConvexHull(this Mat image,
            IEnumerable<OpenCvSharp.Point> contour, Scalar hullColor, Scalar contourColor)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            // 绘制原始轮廓
            Cv2.DrawContours(image, new[] { contour.ToArray() }, 0, contourColor, 1);

            // 计算凸包
            var hull = Cv2.ConvexHull(contour);
            Cv2.DrawContours(image, new[] { hull }, 0, hullColor, 2);
        }

        // ========== 实用工具方法 ==========

        /// <summary>
        /// 创建透明图层并在指定位置绘制
        /// </summary>
        public static Mat CreateDrawingLayer(this Mat background,
            System.Action<Mat> drawActions, double opacity = 0.7)
        {
            ArgumentNullException.ThrowIfNull(background);
            if (background.Empty())
                return background.Clone();

            // M314b: opacity 限制在 [0, 1] 范围内，避免越界值导致 AddWeighted 结果异常
            opacity = Math.Clamp(opacity, 0.0, 1.0);

            // M135: 不修改输入 background，在克隆图层上操作后混合到新 Mat 返回
            using var layer = background.Clone();
            drawActions?.Invoke(layer);

            // 将图层与背景混合，结果写入新 Mat（L109: using 声明管理 layer 生命周期）
            // M314b: 用 try-catch 保护 result Mat，异常时释放避免内存泄漏
            var result = new Mat();
            try
            {
                Cv2.AddWeighted(background, 1.0 - opacity, layer, opacity, 0, result);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 绘制网格或标尺（用于坐标参考）
        /// </summary>
        public static void DrawGrid(this Mat image, int gridSize,
            Scalar color, int thickness = 1)
        {
            ArgumentNullException.ThrowIfNull(image);
            if (image.Empty())
                return;

            // M248: gridSize<=0 会导致无限循环，需校验
            if (gridSize <= 0) throw new ArgumentOutOfRangeException(nameof(gridSize));

            int width = image.Width;
            int height = image.Height;

            // 绘制垂直线
            for (int x = gridSize; x < width; x += gridSize)
                Cv2.Line(image, new OpenCvSharp.Point(x, 0), new OpenCvSharp.Point(x, height), color, thickness);

            // 绘制水平线
            for (int y = gridSize; y < height; y += gridSize)
                Cv2.Line(image, new OpenCvSharp.Point(0, y), new OpenCvSharp.Point(width, y), color, thickness);
        }

        /// <summary>
        /// 绘制比例尺（显示实际尺寸对应像素）
        /// </summary>
        public static void DrawScaleBar(this Mat image, OpenCvSharp.Point start,
            int pixelLength, string realLengthUnit, Scalar color)
        {
            ArgumentNullException.ThrowIfNull(image);
            // L431b: realLengthUnit 用于字符串插值拼接，null 会导致 NullReferenceException
            ArgumentNullException.ThrowIfNull(realLengthUnit);
            if (image.Empty())
                return;
            // L412b: 校验 pixelLength 非负，避免绘制负长度比例尺
            if (pixelLength < 0) throw new ArgumentOutOfRangeException(nameof(pixelLength));

            // 绘制比例尺条
            OpenCvSharp.Point end = new OpenCvSharp.Point(start.X + pixelLength, start.Y);
            // L323: 使用提取的常量 ScaleBarThickness
            Cv2.Line(image, start, end, color, ScaleBarThickness, LineTypes.Link8);

            // 绘制标注
            string label = $"{pixelLength} px = {realLengthUnit}";
            Cv2.PutText(image, label, new OpenCvSharp.Point(start.X, start.Y - 10),
                HersheyFonts.HersheySimplex, 0.5, color, 1);
        }
    }
}