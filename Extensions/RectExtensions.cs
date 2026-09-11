using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace Extensions
{
    public static class RectExtensions
    {
        #region Rect 扩展方法
        
        /// <summary>
        /// 返回矩形面积 (Width * Height)
        /// </summary>
        public static long Area(this Rect rect)
        {
            // M252: 归一化负的 Width/Height，避免面积计算为负
            // H86c: 返回类型改为 long，避免大矩形整数溢出
            return Math.Abs((long)rect.Width) * Math.Abs((long)rect.Height);
        }
        
        /// <summary>
        /// 返回矩形中心点 (整数坐标)
        /// </summary>
        public static Point Center(this Rect rect)
        {
            return new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        }
        
        /// <summary>
        /// 返回矩形中心点 (浮点坐标)
        /// </summary>
        public static Point2f CenterF(this Rect rect)
        {
            return new Point2f(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
        }
        
        /// <summary>
        /// 将矩形向四周扩展 (负值则缩小)
        /// </summary>
        public static Rect InflateBy(this Rect rect, int dx, int dy)
        {
            // L401a: 使用 checked 块检测整数溢出，避免静默溢出产生非法矩形
            checked
            {
                return new Rect(rect.X - dx, rect.Y - dy, rect.Width + 2 * dx, rect.Height + 2 * dy);
            }
        }
        
        /// <summary>
        /// 将矩形向四周缩小 (负值则扩展)
        /// </summary>
        public static Rect DeflateBy(this Rect rect, int dx, int dy)
        {
            return rect.InflateBy(-dx, -dy);
        }
        
        /// <summary>
        /// 返回两个矩形的交集矩形 (若无交集返回空矩形)
        /// </summary>
        public static Rect Intersect(this Rect rect, Rect other)
        {
            // M252: 归一化负的 Width/Height
            int w1 = Math.Abs(rect.Width), h1 = Math.Abs(rect.Height);
            int w2 = Math.Abs(other.Width), h2 = Math.Abs(other.Height);
            int x1 = Math.Max(rect.X, other.X);
            int y1 = Math.Max(rect.Y, other.Y);
            int x2 = Math.Min(rect.X + w1, other.X + w2);
            int y2 = Math.Min(rect.Y + h1, other.Y + h2);
            
            if (x2 <= x1 || y2 <= y1)
                return new Rect(0, 0, 0, 0);
                
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }
        
        /// <summary>
        /// 返回两个矩形的最小包围矩形
        /// </summary>
        public static Rect Union(this Rect rect, Rect other)
        {
            // M252: 归一化负的 Width/Height
            int w1 = Math.Abs(rect.Width), h1 = Math.Abs(rect.Height);
            int w2 = Math.Abs(other.Width), h2 = Math.Abs(other.Height);
            int x1 = Math.Min(rect.X, other.X);
            int y1 = Math.Min(rect.Y, other.Y);
            int x2 = Math.Max(rect.X + w1, other.X + w2);
            int y2 = Math.Max(rect.Y + h1, other.Y + h2);
            
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }
        
        /// <summary>
        /// 判断矩形是否包含点 (半开区间，与 OpenCvSharp 一致)
        /// </summary>
        public static bool Contains(this Rect rect, Point point)
        {
            // M252: 归一化负的 Width/Height
            // M253: 边界语义改为半开区间 < 而非 <=，与 OpenCvSharp 一致
            int w = Math.Abs(rect.Width);
            int h = Math.Abs(rect.Height);
            return point.X >= rect.X && point.X < rect.X + w &&
                   point.Y >= rect.Y && point.Y < rect.Y + h;
        }
        
        /// <summary>
        /// 判断矩形是否完全包含另一个矩形
        /// </summary>
        public static bool Contains(this Rect rect, Rect other)
        {
            // M301a: 归一化 other 的 Width/Height，避免负值导致顶点坐标计算错误
            int ow = Math.Abs(other.Width);
            int oh = Math.Abs(other.Height);
            return rect.Contains(new Point(other.X, other.Y)) &&
                   rect.Contains(new Point(other.X + ow, other.Y + oh));
        }
        
        /// <summary>
        /// 判断两个矩形是否有重叠区域
        /// </summary>
        public static bool Overlaps(this Rect rect, Rect other)
        {
            // M252: 归一化负的 Width/Height
            int w = Math.Abs(rect.Width), h = Math.Abs(rect.Height);
            int ow = Math.Abs(other.Width), oh = Math.Abs(other.Height);
            return rect.X < other.X + ow && rect.X + w > other.X &&
                   rect.Y < other.Y + oh && rect.Y + h > other.Y;
        }
        
        /// <summary>
        /// 计算两个矩形之间的最小欧氏距离
        /// </summary>
        public static double DistanceTo(this Rect rect, Rect other)
        {
            // M252: 归一化负的 Width/Height
            int w = Math.Abs(rect.Width), h = Math.Abs(rect.Height);
            int ow = Math.Abs(other.Width), oh = Math.Abs(other.Height);
            // 如果两个矩形相交，距离为0
            if (rect.Overlaps(other))
                return 0.0;
            
            // 计算水平方向的距离
            int dx = Math.Max(0, Math.Max(rect.X - (other.X + ow), other.X - (rect.X + w)));
            // 计算垂直方向的距离
            int dy = Math.Max(0, Math.Max(rect.Y - (other.Y + oh), other.Y - (rect.Y + h)));
            
            return Math.Sqrt(dx * dx + dy * dy);
        }
        
        /// <summary>
        /// 按比例缩放矩形 (中心点不变)
        /// </summary>
        public static Rect Scale(this Rect rect, float scale)
        {
            // M329c: 对 Width/Height 取绝对值，避免负值导致缩放结果异常
            int w = Math.Abs(rect.Width);
            int h = Math.Abs(rect.Height);
            int newWidth = (int)(w * scale);
            int newHeight = (int)(h * scale);
            int newX = rect.X + (w - newWidth) / 2;
            int newY = rect.Y + (h - newHeight) / 2;

            return new Rect(newX, newY, newWidth, newHeight);
        }
        
        /// <summary>
        /// 平移矩形
        /// </summary>
        public static Rect Move(this Rect rect, int dx, int dy)
        {
            return new Rect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
        }
        
        /// <summary>
        /// 将矩形裁剪到边界矩形内
        /// </summary>
        public static Rect Clip(this Rect rect, Rect bounds)
        {
            // M252: 归一化负的 Width/Height
            int w = Math.Abs(rect.Width), h = Math.Abs(rect.Height);
            int bw = Math.Abs(bounds.Width), bh = Math.Abs(bounds.Height);
            int x = Math.Max(rect.X, bounds.X);
            int y = Math.Max(rect.Y, bounds.Y);
            int right = Math.Min(rect.X + w, bounds.X + bw);
            int bottom = Math.Min(rect.Y + h, bounds.Y + bh);
            
            if (right <= x || bottom <= y)
                return new Rect(0, 0, 0, 0);
                
            return new Rect(x, y, right - x, bottom - y);
        }
        
        /// <summary>
        /// 转换为旋转矩形 (角度为0)
        /// </summary>
        public static RotatedRect ToRotatedRect(this Rect rect)
        {
            return new RotatedRect(rect.CenterF(), new Size2f(rect.Width, rect.Height), 0f);
        }
        
        /// <summary>
        /// 返回矩形的四个顶点 (顺时针)
        /// </summary>
        public static Point[] ToPoints(this Rect rect)
        {
            // M252: 归一化负的 Width/Height
            int w = Math.Abs(rect.Width);
            int h = Math.Abs(rect.Height);
            return new Point[]
            {
                new Point(rect.X, rect.Y),
                new Point(rect.X + w, rect.Y),
                new Point(rect.X + w, rect.Y + h),
                new Point(rect.X, rect.Y + h)
            };
        }
        
        /// <summary>
        /// 检查矩形是否有效 (Width > 0 && Height > 0)
        /// </summary>
        public static bool IsValid(this Rect rect)
        {
            return rect.Width > 0 && rect.Height > 0;
        }
        
        #endregion
        
        #region RotatedRect 扩展方法
        
        /// <summary>
        /// 返回旋转矩形的面积 (Width * Height)
        /// </summary>
        public static float Area(this RotatedRect rect)
        {
            return rect.Size.Width * rect.Size.Height;
        }
        
        /// <summary>
        /// 转换为非旋转矩形 (使用边界矩形)
        /// </summary>
        public static Rect ToRect(this RotatedRect rect)
        {
            return rect.BoundingRect();
        }

        // L113: 已删除 RotatedRect.Center() 扩展方法，该方法仅为内置 Center 属性的无意义包装

        /// <summary>
        /// 按比例缩放旋转矩形 (中心点不变)
        /// </summary>
        public static RotatedRect Scale(this RotatedRect rect, float scale)
        {
            return new RotatedRect(
                rect.Center,
                new Size2f(rect.Size.Width * scale, rect.Size.Height * scale),
                rect.Angle);
        }
        
        /// <summary>
        /// 附加旋转角度 (相对于当前角度)
        /// </summary>
        public static RotatedRect Rotate(this RotatedRect rect, double angle)
        {
            return new RotatedRect(rect.Center, rect.Size, (float)(rect.Angle + angle));
        }
        
        /// <summary>
        /// 检查旋转矩形是否有效 (Width > 0 && Height > 0)
        /// </summary>
        public static bool IsValid(this RotatedRect rect)
        {
            return rect.Size.Width > 0 && rect.Size.Height > 0;
        }
        
        #endregion
        
        #region 静态工具方法
        
        /// <summary>
        /// 根据两个点创建矩形
        /// </summary>
        public static Rect FromPoints(Point p1, Point p2)
        {
            int x = Math.Min(p1.X, p2.X);
            int y = Math.Min(p1.Y, p2.Y);
            int width = Math.Abs(p2.X - p1.X);
            int height = Math.Abs(p2.Y - p1.Y);
            
            return new Rect(x, y, width, height);
        }
        
        /// <summary>
        /// 根据中心点和尺寸创建矩形
        /// </summary>
        public static Rect FromCenterSize(Point center, Size size)
        {
            // L414b: 已知限制 - 对奇数尺寸做整除截断，左上角坐标会偏移 0.5 像素（向下取整），
            // 矩形中心点不严格等于传入的 center。如需精确中心请使用浮点坐标
            return new Rect(center.X - size.Width / 2, center.Y - size.Height / 2, size.Width, size.Height);
        }
        
        /// <summary>
        /// 合并多个矩形的最小包围矩形
        /// </summary>
        public static Rect Merge(this IEnumerable<Rect> rects)
        {
            // L114: 验证 null 输入
            ArgumentNullException.ThrowIfNull(rects);
            using var enumerator = rects.GetEnumerator();
            if (!enumerator.MoveNext())
                return new Rect(0, 0, 0, 0);
            
            var first = enumerator.Current;
            // M302a: 归一化 Width/Height，避免负值导致包围矩形计算错误
            int x1 = first.X;
            int y1 = first.Y;
            int x2 = first.X + Math.Abs(first.Width);
            int y2 = first.Y + Math.Abs(first.Height);

            while (enumerator.MoveNext())
            {
                var rect = enumerator.Current;
                x1 = Math.Min(x1, rect.X);
                y1 = Math.Min(y1, rect.Y);
                x2 = Math.Max(x2, rect.X + Math.Abs(rect.Width));
                y2 = Math.Max(y2, rect.Y + Math.Abs(rect.Height));
            }
            
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }
        
        /// <summary>
        /// 将矩形扩展为指定宽高比 (保持中心)
        /// </summary>
        public static Rect InflateToAspectRatio(this Rect rect, float aspectRatio)
        {
            // M351a: 校验 aspectRatio 为正数，避免 0 或负值导致除零产生 Infinity 或反向缩放
            if (aspectRatio <= 0) throw new ArgumentOutOfRangeException(nameof(aspectRatio), "aspectRatio 必须为正数");
            // M154: Height 为 0 时除零产生 Infinity，导致后续逻辑异常，直接返回原矩形
            // M331d: 方法开头对 Width/Height 取绝对值，统一处理负值，避免后续分支处理不一致
            int absWidth = Math.Abs(rect.Width);
            int absHeight = Math.Abs(rect.Height);
            if (absHeight == 0)
                return rect;

            float currentAspect = (float)absWidth / absHeight;

            // M330d: 改用 1e-6f 替代 float.Epsilon，float.Epsilon 过小（约 1.4e-45）无实际比较意义
            if (Math.Abs(currentAspect - aspectRatio) < 1e-6f)
                return rect;

            int newWidth, newHeight;
            if (currentAspect > aspectRatio)
            {
                // 当前较宽，增加高度
                newWidth = absWidth;
                newHeight = (int)(absWidth / aspectRatio);
            }
            else
            {
                // 当前较高，增加宽度
                newWidth = (int)(absHeight * aspectRatio);
                newHeight = absHeight;
            }

            int newX = rect.X + (absWidth - newWidth) / 2;
            int newY = rect.Y + (absHeight - newHeight) / 2;

            return new Rect(newX, newY, newWidth, newHeight);
        }
        
        /// <summary>
        /// 将矩形裁剪到容器尺寸内 (L115: 原 FitInto 重命名，命名更准确反映行为)
        /// </summary>
        public static Rect ClipTo(this Rect rect, Size containerSize)
        {
            // 确保矩形在容器范围内
            int x = Math.Max(0, Math.Min(rect.X, containerSize.Width - 1));
            int y = Math.Max(0, Math.Min(rect.Y, containerSize.Height - 1));
            int width = Math.Min(rect.Width, containerSize.Width - x);
            int height = Math.Min(rect.Height, containerSize.Height - y);

            // M303a: 避免产生负 Width/Height，矩形超出容器时返回空矩形
            if (width <= 0 || height <= 0)
                return new Rect(0, 0, 0, 0);

            return new Rect(x, y, width, height);
        }
        
        #endregion
    }
}
