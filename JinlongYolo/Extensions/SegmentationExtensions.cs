using System;
using System.Collections.Generic;

/// <summary>
/// 为分割结果提供扩展方法。
/// Provides extension methods for segmentation results.
/// </summary>
public static class SegmentationExtensions
{
    /// <summary>
    /// 轴方向规范化时使用的参考框架角度（度）。
    /// <para>长轴是无向轴，θ 与 θ+180° 表示同一根轴。旋转卡壳在"矩形类"掩码上存在对边等价二义，
    /// 同一几何可能返回 θ 或 θ+180°，导致同一产品帧间 / 趟内角度跳 180°。规范化就是把两者
    /// 映射到同一个代表值，代价是引入一条"边界"——轴方向恰好落在边界上时，微小几何抖动会让
    /// 代表值跳 180°。因此边界应尽量远离产品实际长轴的分布峰。</para>
    /// <para>本项目实测（2026-09-11 诊断，386 帧 / 195 趟，见 tools/axis_conv_probe.py）：
    /// 长轴角呈双峰——68% 落在 0°±10°、31% 落在 90°±10°，而 30°~80° 区间完全为空。
    /// 若边界取 0°/180°（即按 uy&gt;0 规范）会新引入 12.3% 的趟内 180° 翻转；
    /// 取 ±90°（按 ux&gt;0 规范）为 6.2%；取本处 45°/225° 为 0.0%。
    /// 故取 45° 框架——它与两个分布峰都相距 45°，是实测最优。换产品/换摆放方式后应重新评估。</para>
    /// </summary>
    private const double AxisCanonicalFrameDegrees = 45.0;

    private static readonly double AxisFrameCos = Math.Cos(AxisCanonicalFrameDegrees * Math.PI / 180.0);
    private static readonly double AxisFrameSin = Math.Sin(AxisCanonicalFrameDegrees * Math.PI / 180.0);

    /// <summary>
    /// 把无向轴的方向角（弧度）规范化到以 <see cref="AxisCanonicalFrameDegrees"/> 为边界的固定半平面，
    /// 消除旋转卡壳"对边等价"导致的 ±180° 二义：θ 与 θ+180° 保证映射到同一代表值。
    /// <para>做法：把方向向量旋转到规范框架（旋 −45°），若其 yr &gt; 0（即落在边界另一侧）则整体取反，
    /// 再旋转回原框架；边界上（yr == 0）按 xr 符号固定取向。结果值域为 (−135°, 45°]（框架角 45° 时）。</para>
    /// <para>不改变任何几何信息：翻 180° 只等价于四点角点数组的循环移位（0↔2、1↔3），
    /// 四点集合、绘制多边形、旋转矩形包含判定均不受影响；宽度/高度标注也不受影响。</para>
    /// </summary>
    /// <param name="angleRad">轴方向角（弧度，无向轴，任意代表值）。</param>
    /// <returns>规范化后的轴方向角（弧度）。</returns>
    private static double CanonicalizeAxisAngle(double angleRad)
    {
        double xr = Math.Cos(angleRad) * AxisFrameCos + Math.Sin(angleRad) * AxisFrameSin;
        double yr = -Math.Cos(angleRad) * AxisFrameSin + Math.Sin(angleRad) * AxisFrameCos;
        // yr > 0 取反；恰好落在边界（yr == 0）时按 xr 符号固定取向，保证 θ 与 θ+180° 结果一致
        if (yr > 0 || (yr == 0 && xr < 0))
        {
            xr = -xr;
            yr = -yr;
        }
        double x = xr * AxisFrameCos - yr * AxisFrameSin;
        double y = xr * AxisFrameSin + yr * AxisFrameCos;
        return Math.Atan2(y, x);
    }

    /// <summary>
    /// 计算分割掩码的质心（使用像素值加权），并返回在原图坐标系下的浮点坐标。
    /// 如果掩码中没有大于阈值的像素，会退回到检测框中心。
    /// Calculates the centroid of the segmentation mask (weighted by pixel values) and returns the float coordinates in the original image coordinate system.
    /// If no pixels in the mask are above the threshold, it falls back to the center of the bounding box.
    /// </summary>
    /// <param name="segmentation">分割结果对象 / The segmentation result object.</param>
    /// <param name="threshold">像素阈值，低于或等于该值的像素将被忽略（默认 0.5） / Pixel threshold, pixels less than or equal to this value will be ignored (default 0.5).</param>
    /// <returns>在原图坐标系下的质心坐标 / Centroid coordinates in the original image coordinate system.</returns>
    public static SixLabors.ImageSharp.PointF GetMaskCentroid(this JinlongYolo.YoloSharp.Data.Segmentation segmentation, float threshold = 0.5f)
    {
        var mask = segmentation.Mask;
        var width = mask.Width;
        var height = mask.Height;

        double sum = 0.0;
        double sumX = 0.0;
        double sumY = 0.0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var v = mask[y, x];

                if (v <= threshold)
                    continue;

                sum += v;
                sumX += x * v;
                sumY += y * v;
            }
        }

        // 若无有效像素，返回 Bounds 的中心点
        if (sum == 0.0)
        {
            return new SixLabors.ImageSharp.PointF(segmentation.Bounds.X + segmentation.Bounds.Width / 2f,
                               segmentation.Bounds.Y + segmentation.Bounds.Height / 2f);
        }

        // 质心在掩码局部坐标系中（以掩码左上为原点），需要加上检测框的位置转换为图像坐标
        var cx = (float)(sumX / sum);
        var cy = (float)(sumY / sum);

        return new SixLabors.ImageSharp.PointF(segmentation.Bounds.X + cx, segmentation.Bounds.Y + cy);
    }

    /// <summary>
    /// 计算掩码的外接矩形（基于阈值），并返回在原图坐标系下的浮点矩形。
    /// 如果掩码中没有大于阈值的像素，会退回到检测框的 Bounds。
    /// Calculates the bounding rectangle of the segmentation mask (pixels > threshold) and returns it in the original image coordinate system.
    /// If no pixels are above the threshold, it falls back to the segmentation Bounds.
    /// </summary>
    /// <param name="segmentation">分割结果对象 / The segmentation result object.</param>
    /// <param name="threshold">像素阈值，低于或等于该值的像素将被忽略（默认 0.5） / Pixel threshold.</param>
    /// <returns>在原图坐标系下的外接矩形 / Bounding rectangle in the original image coordinate system.</returns>
    public static SixLabors.ImageSharp.RectangleF GetMaskBoundingRect(this JinlongYolo.YoloSharp.Data.Segmentation segmentation, float threshold = 0.5f)
    {
        var mask = segmentation.Mask;
        var width = mask.Width;
        var height = mask.Height;

        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var v = mask[y, x];
                if (v <= threshold)
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        // 若无有效像素，返回 Bounds
        if (maxX < 0 || maxY < 0)
        {
            return segmentation.Bounds;
        }

        // 转换为图像坐标系
        var rectX = segmentation.Bounds.X + minX;
        var rectY = segmentation.Bounds.Y + minY;
        var rectW = (maxX - minX) + 1;
        var rectH = (maxY - minY) + 1;

        return new SixLabors.ImageSharp.RectangleF(rectX, rectY, rectW, rectH);
    }

    /// <summary>
    /// 计算掩码的最小外接矩形（可能为旋转矩形），并返回一个包含中心、尺寸和角度的结构。
    /// 如果掩码中没有大于阈值的像素，会退回到检测框的 Bounds（角度为 0）。
    /// Calculates the minimum-area enclosing rectangle (rotated) of the segmentation mask.
    /// If no pixels are above the threshold, it falls back to the segmentation Bounds (angle = 0).
    /// </summary>
    /// <param name="segmentation">分割结果对象 / The segmentation result object.</param>
    /// <param name="threshold">像素阈值（默认 0.5） / Pixel threshold.</param>
    /// <returns>最小外接矩形信息 / Min-area rotated rectangle info.</returns>
    public static MinAreaRect GetMaskMinAreaRect(this JinlongYolo.YoloSharp.Data.Segmentation segmentation, float threshold = 0.5f)
    {
        var mask = segmentation.Mask;
        var width = mask.Width;
        var height = mask.Height;

        // 防御 CUDA 极端值：mask 尺寸异常时直接回退到 Bounds
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
        {
            return FallbackToBounds(segmentation);
        }

        var rowMinX = new int[height];
        var rowMaxX = new int[height];
        var colMinY = new int[width];
        var colMaxY = new int[width];

        Array.Fill(rowMinX, width);
        Array.Fill(rowMaxX, -1);
        Array.Fill(colMinY, height);
        Array.Fill(colMaxY, -1);

        var maskArea = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var v = mask[y, x];
                if (v <= threshold) continue;

                maskArea++;

                if (x < rowMinX[y]) rowMinX[y] = x;
                if (x > rowMaxX[y]) rowMaxX[y] = x;
                if (y < colMinY[x]) colMinY[x] = y;
                if (y > colMaxY[x]) colMaxY[x] = y;
            }
        }

        // no valid pixels -> return Bounds
        if (maskArea == 0)
        {
            return FallbackToBounds(segmentation);
        }

        // REVIEW(2026-08-05): minAreaRect 构建逻辑提取为 BuildMinAreaRect，供 GetMaskStats 复用
        return BuildMinAreaRect(rowMinX, rowMaxX, colMinY, colMaxY, maskArea, segmentation);
    }

    /// <summary>
    /// 单次遍历掩码，同时计算灰度加权质心与最小外接旋转矩形（原 GetMaskCentroid + GetMaskMinAreaRect 两次全扫描合并为一次）。
    /// REVIEW(2026-08-05): 每帧每产品节省一次 O(W×H) 扫描，供 DetectionRecordService 使用。
    /// </summary>
    /// <param name="segmentation">分割结果对象 / The segmentation result object.</param>
    /// <param name="threshold">像素阈值（默认 0.5） / Pixel threshold.</param>
    /// <returns>质心（原图坐标系）与最小外接矩形信息。</returns>
    public static (SixLabors.ImageSharp.PointF Centroid, MinAreaRect MinAreaRect) GetMaskStats(this JinlongYolo.YoloSharp.Data.Segmentation segmentation, float threshold = 0.5f)
    {
        var mask = segmentation.Mask;
        var width = mask.Width;
        var height = mask.Height;

        // 防御 CUDA 极端值（同 GetMaskMinAreaRect）
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
        {
            var fb = FallbackToBounds(segmentation);
            return (new SixLabors.ImageSharp.PointF(fb.Center.X, fb.Center.Y), fb);
        }

        var rowMinX = new int[height];
        var rowMaxX = new int[height];
        var colMinY = new int[width];
        var colMaxY = new int[width];

        Array.Fill(rowMinX, width);
        Array.Fill(rowMaxX, -1);
        Array.Fill(colMinY, height);
        Array.Fill(colMaxY, -1);

        double sum = 0.0;
        double sumX = 0.0;
        double sumY = 0.0;
        var maskArea = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var v = mask[y, x];
                if (v <= threshold) continue;

                sum += v;
                sumX += x * v;
                sumY += y * v;
                maskArea++;

                if (x < rowMinX[y]) rowMinX[y] = x;
                if (x > rowMaxX[y]) rowMaxX[y] = x;
                if (y < colMinY[x]) colMinY[x] = y;
                if (y > colMaxY[x]) colMaxY[x] = y;
            }
        }

        // 无有效像素：质心回退 Bounds 中心，矩形回退 Bounds
        if (maskArea == 0)
        {
            var fb = FallbackToBounds(segmentation);
            return (new SixLabors.ImageSharp.PointF(fb.Center.X, fb.Center.Y), fb);
        }

        // 质心在掩码局部坐标系中，需加上检测框位置转换为图像坐标
        var cx = (float)(sumX / sum);
        var cy = (float)(sumY / sum);
        var centroid = new SixLabors.ImageSharp.PointF(segmentation.Bounds.X + cx, segmentation.Bounds.Y + cy);

        var rect = BuildMinAreaRect(rowMinX, rowMaxX, colMinY, colMaxY, maskArea, segmentation);
        return (centroid, rect);
    }

    /// <summary>
    /// 由行/列极值构建最小外接旋转矩形（凸包 + 旋转卡壳）。
    /// REVIEW(2026-08-05): 从 GetMaskMinAreaRect 提取，供 GetMaskMinAreaRect 与 GetMaskStats 复用。
    /// </summary>
    private static MinAreaRect BuildMinAreaRect(int[] rowMinX, int[] rowMaxX, int[] colMinY, int[] colMaxY, int maskArea, JinlongYolo.YoloSharp.Data.Segmentation segmentation)
    {
        var height = rowMinX.Length;
        var width = colMinY.Length;
        var pts = new List<PointD>((height + width) * 2);
        for (int y = 0; y < height; y++)
        {
            var minX = rowMinX[y];
            var maxX = rowMaxX[y];
            if (maxX < 0)
                continue;

            pts.Add(new PointD(minX, y));
            if (maxX != minX)
                pts.Add(new PointD(maxX, y));
        }

        for (int x = 0; x < width; x++)
        {
            var minY = colMinY[x];
            var maxY = colMaxY[x];
            if (maxY < 0)
                continue;

            pts.Add(new PointD(x, minY));
            if (maxY != minY)
                pts.Add(new PointD(x, maxY));
        }

        var hull = ConvexHull(pts);

        // handle trivial hulls
        if (hull.Count == 1)
        {
            var p = hull[0];
            return new MinAreaRect
            {
                Center = new SixLabors.ImageSharp.PointF((float)(segmentation.Bounds.X + p.X), (float)(segmentation.Bounds.Y + p.Y)),
                Width = 1f,
                Height = 1f,
                MaskArea = maskArea,
                Angle = 0f,
                Points = new SixLabors.ImageSharp.PointF[] {
                    new SixLabors.ImageSharp.PointF((float)(segmentation.Bounds.X + p.X), (float)(segmentation.Bounds.Y + p.Y)),
                    new SixLabors.ImageSharp.PointF((float)(segmentation.Bounds.X + p.X), (float)(segmentation.Bounds.Y + p.Y)),
                    new SixLabors.ImageSharp.PointF((float)(segmentation.Bounds.X + p.X), (float)(segmentation.Bounds.Y + p.Y)),
                    new SixLabors.ImageSharp.PointF((float)(segmentation.Bounds.X + p.X), (float)(segmentation.Bounds.Y + p.Y))
                }
            };
        }

        if (hull.Count == 2)
        {
            var p0 = hull[0];
            var p1 = hull[1];
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            // 2026-09-11: 同样做轴向符号规范化（退化 2 点凸包的方向由凸包点序决定，同样带 ±180° 二义）
            var angle = (float)(CanonicalizeAxisAngle(Math.Atan2(dy, dx)) * 180.0 / Math.PI);
            var cx = (float)((p0.X + p1.X) / 2.0 + segmentation.Bounds.X);
            var cy = (float)((p0.Y + p1.Y) / 2.0 + segmentation.Bounds.Y);
            return new MinAreaRect
            {
                Center = new SixLabors.ImageSharp.PointF(cx, cy),
                Width = (float)len,
                Height = 1f,
                MaskArea = maskArea,
                Angle = angle,
                Points = new SixLabors.ImageSharp.PointF[] {
                    new SixLabors.ImageSharp.PointF(cx - (float)(len/2.0), cy - 0.5f),
                    new SixLabors.ImageSharp.PointF(cx + (float)(len/2.0), cy - 0.5f),
                    new SixLabors.ImageSharp.PointF(cx + (float)(len/2.0), cy + 0.5f),
                    new SixLabors.ImageSharp.PointF(cx - (float)(len/2.0), cy + 0.5f)
                }
            };
        }

        double bestArea = double.MaxValue;
        double bestWidth = 0, bestHeight = 0, bestAngle = 0;
        PointD bestCenter = new PointD(0, 0);

        int m = hull.Count;
        for (int i = 0; i < m; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % m];
            var edgeX = b.X - a.X;
            var edgeY = b.Y - a.Y;
            var theta = -Math.Atan2(edgeY, edgeX);
            var cos = Math.Cos(theta);
            var sin = Math.Sin(theta);

            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;

            for (int j = 0; j < m; j++)
            {
                var p = hull[j];
                var rx = p.X * cos - p.Y * sin;
                var ry = p.X * sin + p.Y * cos;
                if (rx < minX) minX = rx;
                if (rx > maxX) maxX = rx;
                if (ry < minY) minY = ry;
                if (ry > maxY) maxY = ry;
            }

            var w = maxX - minX;
            var h = maxY - minY;
            var area = w * h;
            if (area < bestArea)
            {
                bestArea = area;
                bestWidth = w;
                bestHeight = h;
                bestAngle = -theta; // rotate back angle
                // center in rotated coords
                var cxr = (minX + maxX) / 2.0;
                var cyr = (minY + maxY) / 2.0;
                // rotate center back using inverse rotation (rotate by -theta)
                var cx = cxr * cos + cyr * sin;
                var cy = -cxr * sin + cyr * cos;
                bestCenter = new PointD(cx, cy);
            }
        }

        // 2026-09-08: 轴标注归一化——保证 Width >= Height（宽度轴恒为产品长轴/主轴）。
        // 背景：旋转卡壳对"矩形类"掩码存在轴标注二义——长边对齐与短边对齐两组解面积相同，
        // 由首个达到最小值的凸包边决定标注。若不加约束，同一产品可能帧间 Angle 跳 90°，
        // 且宽度轴未必是产品长轴，导致：掩码回退角度方向错误、参考线分割与灰度判向
        // （DetectionRecordService.ApplyBrightnessHeadDirectionIfEnabled 按宽度轴投影）轴语义错位。
        // 此处交换宽高并把角度转 90°（+h 轴 = 宽度轴逆时针转 90°，见角点布局），
        // 几何外接矩形完全不变（角点重合），仅统一标注：Angle = 产品长轴方向（度，逆时针、相对 X 轴）。
        // 角点随后按归一化后的 Width/Height/Angle 计算，保证 Points 与字段一致。
        if (bestWidth < bestHeight)
        {
            (bestWidth, bestHeight) = (bestHeight, bestWidth);
            bestAngle += Math.PI / 2;
        }

        // 2026-09-11: 轴向符号规范化——消除长轴 ±180° 二义（详见 CanonicalizeAxisAngle 注释）。
        // 背景：旋转卡壳对矩形类掩码存在"对边等价"二义——同一条长边的两个方向（θ 与 θ+180°）
        // 给出完全相同的面积，谁被选中取决于凸包起点；掩码只差一个像素就可能让起点换到对边，
        // 于是同一趟通过内 Angle 突然翻 180°。实测 1666 趟中 28.2% 在趟内出现该翻转，
        // 且翻转前后灰度判向的 BrightnessDiff 同时变号（83.6% 印证）——因为轴向一翻，
        // +u/−u 两个半区就对调，使 MaskAngle 输出与判向统计都无法复现。
        // 规范化后 θ 与 θ+180° 映射到同一代表值，输出对"凸包起点"不再敏感。
        // 注意顺序：必须在"宽≥高"归一化之后——该步决定宽度轴为长轴，规范化只定方向符号，两者互不干扰。
        bestAngle = CanonicalizeAxisAngle(bestAngle);

        // convert bestCenter from mask local to image coords by adding segmentation.Bounds
        var centerMaskX = bestCenter.X;
        var centerMaskY = bestCenter.Y;
        var centerImgX = (float)(centerMaskX + segmentation.Bounds.X);
        var centerImgY = (float)(centerMaskY + segmentation.Bounds.Y);

        // bestAngle is in radians; bestWidth/Height are in mask pixels
        var halfW = bestWidth / 2.0;
        var halfH = bestHeight / 2.0;
        var cosA = Math.Cos(bestAngle);
        var sinA = Math.Sin(bestAngle);

        // compute four corners in image coordinates based on centerImg and rotation
        var corners = new SixLabors.ImageSharp.PointF[4];
        corners[0] = new SixLabors.ImageSharp.PointF(
            centerImgX + (float)(-halfW * cosA + halfH * sinA),
            centerImgY + (float)(-halfW * sinA - halfH * cosA));
        corners[1] = new SixLabors.ImageSharp.PointF(
            centerImgX + (float)(halfW * cosA + halfH * sinA),
            centerImgY + (float)(halfW * sinA - halfH * cosA));
        corners[2] = new SixLabors.ImageSharp.PointF(
            centerImgX + (float)(halfW * cosA - halfH * sinA),
            centerImgY + (float)(halfW * sinA + halfH * cosA));
        corners[3] = new SixLabors.ImageSharp.PointF(
            centerImgX + (float)(-halfW * cosA - halfH * sinA),
            centerImgY + (float)(-halfW * sinA + halfH * cosA));

        var rect = new MinAreaRect
        {
            Center = new SixLabors.ImageSharp.PointF(centerImgX, centerImgY),
            Width = (float)bestWidth,
            Height = (float)bestHeight,
            MaskArea = maskArea,
            Angle = (float)(bestAngle * 180.0 / Math.PI),
            Points = corners
        };

        return rect;
    }

    /// <summary>
    /// 最小外接矩形结构，表示中心、尺寸和角度（度）。
    /// Min-area rotated rectangle: center, size and angle (degrees).
    /// </summary>
    public struct MinAreaRect
    {
        public SixLabors.ImageSharp.PointF Center { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        /// <summary>
        /// 矩形面积。
        /// </summary>
        public float Area => Width * Height;
        /// <summary>
        /// 分割掩码面积（阈值过滤后参与计算的像素数）。
        /// </summary>
        public float MaskArea { get; set; }
        /// <summary>Angle in degrees, counter-clockwise.</summary>
        public float Angle { get; set; }
        /// <summary>四个角点的数组（原图坐标系），顺时针或逆时针顺序。可为 null，表示未预计算。</summary>
        public SixLabors.ImageSharp.PointF[] Points { get; set; }

        /// <summary>返回按角度旋转的四个角点（顺时针或逆时针顺序）。</summary>
        public SixLabors.ImageSharp.PointF[] GetCorners()
        {
            if (Points != null && Points.Length == 4)
                return Points;

            var w = Width / 2f;
            var h = Height / 2f;
            var a = Angle * (float)(Math.PI / 180.0);
            var cos = (float)Math.Cos(a);
            var sin = (float)Math.Sin(a);

            var pts = new SixLabors.ImageSharp.PointF[4];
            // local corners
            var local = new (float X, float Y)[] { (-w, -h), (w, -h), (w, h), (-w, h) };
            for (int i = 0; i < 4; i++)
            {
                var lx = local[i].X;
                var ly = local[i].Y;
                var rx = lx * cos - ly * sin;
                var ry = lx * sin + ly * cos;
                pts[i] = new SixLabors.ImageSharp.PointF(Center.X + rx, Center.Y + ry);
            }

            Points = pts;
            return pts;
        }
    }

    private struct PointD
    {
        public double X;
        public double Y;
        public PointD(double x, double y) { X = x; Y = y; }
    }

    // Monotone chain convex hull
    private static List<PointD> ConvexHull(List<PointD> pts)
    {
        pts.Sort(static (p1, p2) =>
        {
            var xCompare = p1.X.CompareTo(p2.X);
            return xCompare != 0 ? xCompare : p1.Y.CompareTo(p2.Y);
        });

        var a = pts;
        int n = a.Count;
        if (n <= 1) return new List<PointD>(a);

        var lower = new List<PointD>();
        foreach (var p in a)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        var upper = new List<PointD>();
        for (int i = n - 1; i >= 0; i--)
        {
            var p = a[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross(PointD o, PointD a, PointD b)
    {
        return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }

    private static MinAreaRect FallbackToBounds(JinlongYolo.YoloSharp.Data.Segmentation segmentation)
    {
        var b = segmentation.Bounds;
        return new MinAreaRect
        {
            Center = new SixLabors.ImageSharp.PointF(b.X + b.Width / 2f, b.Y + b.Height / 2f),
            Width = b.Width,
            Height = b.Height,
            MaskArea = 0f,
            Angle = 0f,
            Points = new SixLabors.ImageSharp.PointF[] {
                new(b.X, b.Y),
                new(b.X + b.Width, b.Y),
                new(b.X + b.Width, b.Y + b.Height),
                new(b.X, b.Y + b.Height)
            }
        };
    }
}
