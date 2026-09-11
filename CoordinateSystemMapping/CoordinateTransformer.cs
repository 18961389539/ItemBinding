using OpenCvSharp;
using System;
using System.Numerics;

namespace CoordinateSystemMapping
{
    /// <summary>
    /// 坐标系变换器，实现图像像素坐标与物理世界坐标之间的双向映射。
    /// 基于三点标定法：通过图像中的原点、X轴点和Y轴点，以及对应的物理距离，
    /// 计算出坐标轴的单位向量和比例因子，从而实现精确的坐标变换。
    /// 
    /// 变换原理：
    /// 1. 根据三个标定点计算图像中X轴和Y轴的方向向量
    /// 2. 根据物理距离计算比例因子（像素/物理单位）
    /// 3. 图像→物理：将像素偏移投影到坐标轴，再除以比例因子
    /// 4. 物理→图像：将物理坐标乘以比例因子，再沿坐标轴方向偏移
    /// </summary>
    public class CoordinateTransformer
    {
        /// <summary>图像坐标系中的原点</summary>
        private Point2f _po;

        /// <summary>图像坐标系中X轴上的标定点</summary>
        private Point2f _px;

        /// <summary>图像坐标系中Y轴上的标定点</summary>
        private Point2f _py;

        /// <summary>原点到X轴标定点的物理距离（mm）</summary>
        private double _distX;

        /// <summary>原点到Y轴标定点的物理距离（mm）</summary>
        private double _distY;

        /// <summary>X轴单位向量（图像坐标系中的归一化方向）</summary>
        private Point2f _unitX;

        /// <summary>Y轴单位向量（图像坐标系中的归一化方向）</summary>
        private Point2f _unitY;

        /// <summary>X方向比例因子：像素/物理单位（mm）</summary>
        private double _scaleX;

        /// <summary>Y方向比例因子：像素/物理单位（mm）</summary>
        private double _scaleY;

        /// <summary>
        /// 坐标系是否已初始化。调用 Initialize 后设为 true。
        /// </summary>
        public bool IsInitialized { get; set; } = false;

        /// <summary>
        /// 根据三个图像标定点和对应的物理距离建立坐标系映射。
        /// </summary>
        /// <param name="po">原点（图像坐标，像素）</param>
        /// <param name="px">X轴上的标定点（图像坐标，像素），与原点共同确定X轴方向</param>
        /// <param name="py">Y轴上的标定点（图像坐标，像素），与原点共同确定Y轴方向</param>
        /// <param name="distX">原点到X轴标定点的物理距离（mm）</param>
        /// <param name="distY">原点到Y轴标定点的物理距离（mm）</param>
        public void Initialize(Point2f po, Point2f px, Point2f py, double distX, double distY)
        {
            // REVIEW-FIX: 参数校验。原实现直接用 distX/distY 做除数，为 0 或负值时产生
            // Infinity/NaN 的比例因子，且标定点与原点重合时 pixelDist 为 0 同样除零。
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(distX, 0, nameof(distX));
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(distY, 0, nameof(distY));

            _po = po;
            _px = px;
            _py = py;
            _distX = distX;
            _distY = distY;

            // 计算图像中从原点到各标定点的向量
            double dxX = _px.X - _po.X;
            double dyX = _px.Y - _po.Y;
            double dxY = _py.X - _po.X;
            double dyY = _py.Y - _po.Y;

            // 计算图像中的像素距离
            double pixelDistX = Math.Sqrt(dxX * dxX + dyX * dyX);
            double pixelDistY = Math.Sqrt(dxY * dxY + dyY * dyY);
            if (pixelDistX <= 0 || pixelDistY <= 0)
            {
                throw new ArgumentException("标定点与原点重合，无法建立坐标系（X/Y 标定点不能与原点相同）", nameof(px));
            }

            // 计算比例因子（像素/物理单位）
            _scaleX = pixelDistX / _distX;
            _scaleY = pixelDistY / _distY;

            // 计算单位向量（图像坐标系中的归一化方向）
            _unitX = new Point2f((float)(dxX / pixelDistX), (float)(dyX / pixelDistX));
            _unitY = new Point2f((float)(dxY / pixelDistY), (float)(dyY / pixelDistY));
            IsInitialized = true;
        }

        /// <summary>
        /// 将图像像素坐标转换为物理世界坐标（Point2f 版本）。
        /// 通过将像素偏移投影到坐标轴单位向量，再除以比例因子得到物理坐标。
        /// </summary>
        /// <param name="imagePoint">图像中的点（像素坐标）</param>
        /// <returns>对应的物理世界坐标（mm）</returns>
        public Point2f ImageToPhysical(Point2f imagePoint)
        {
            // 相对于原点的像素偏移
            double dx = imagePoint.X - _po.X;
            double dy = imagePoint.Y - _po.Y;

            // 投影到X轴和Y轴（点积运算）
            double projX = dx * _unitX.X + dy * _unitX.Y;
            double projY = dx * _unitY.X + dy * _unitY.Y;

            // 除以比例因子，转换为物理坐标
            double physX = projX / _scaleX;
            double physY = projY / _scaleY;

            return new Point2f((float)physX, (float)physY);
        }

        /// <summary>
        /// 将图像像素坐标转换为物理世界坐标（Point2d 版本）
        /// </summary>
        /// <param name="imagePoint">图像中的点（像素坐标）</param>
        /// <returns>对应的物理世界坐标（mm）</returns>
        public Point2d ImageToPhysical(Point2d imagePoint)
        {
            var imagePoint2f = new Point2f((float)imagePoint.X, (float)imagePoint.Y);
            var physPoint = ImageToPhysical(imagePoint2f);
            return new Point2d(physPoint.X, physPoint.Y);
        }

        /// <summary>
        /// 将图像像素坐标转换为物理世界坐标（float 分量版本）
        /// </summary>
        /// <param name="imageX">图像X坐标（像素）</param>
        /// <param name="imageY">图像Y坐标（像素）</param>
        /// <returns>物理世界坐标元组 (WorldX, WorldY)，单位mm</returns>
        public (float WorldX, float WorldY) ImageToPhysical(float imageX, float imageY)
        {
            var physicalPoint = ImageToPhysical(new Point2f(imageX, imageY));
            return (physicalPoint.X, physicalPoint.Y);
        }

        /// <summary>
        /// 将图像像素坐标转换为物理世界坐标（double 分量版本）
        /// </summary>
        /// <param name="imageX">图像X坐标（像素）</param>
        /// <param name="imageY">图像Y坐标（像素）</param>
        /// <returns>物理世界坐标元组 (WorldX, WorldY)，单位mm</returns>
        public (double WorldX, double WorldY) ImageToPhysical(double imageX, double imageY)
        {
            var physicalPoint = ImageToPhysical(new Point2d((double)imageX, imageY));
            return (physicalPoint.X, physicalPoint.Y);
        }

        /// <summary>
        /// 将物理世界坐标转换为图像像素坐标。
        /// 通过将物理坐标乘以比例因子，再沿坐标轴单位向量方向偏移原点得到。
        /// </summary>
        /// <param name="physicalPoint">物理世界坐标（mm）</param>
        /// <returns>对应的图像像素坐标</returns>
        public Point2f PhysicalToImage(Point2f physicalPoint)
        {
            // 物理坐标乘以比例因子，得到像素偏移量
            double offsetX = physicalPoint.X * _scaleX;
            double offsetY = physicalPoint.Y * _scaleY;

            // 沿坐标轴单位向量方向偏移原点，得到图像坐标
            double imgX = _po.X + offsetX * _unitX.X + offsetY * _unitY.X;
            double imgY = _po.Y + offsetX * _unitX.Y + offsetY * _unitY.Y;

            return new Point2f((float)imgX, (float)imgY);
        }

        /// <summary>
        /// 输出坐标系信息到控制台，用于调试和验证标定结果
        /// </summary>
        public void PrintInfo()
        {
            Console.WriteLine($"原点(图像): ({_po.X}, {_po.Y})");
            Console.WriteLine($"X轴单位向量: ({_unitX.X:F4}, {_unitX.Y:F4})");
            Console.WriteLine($"Y轴单位向量: ({_unitY.X:F4}, {_unitY.Y:F4})");
            Console.WriteLine($"比例因子 X: {_scaleX:F4} 像素/单位");
            Console.WriteLine($"比例因子 Y: {_scaleY:F4} 像素/单位");
        }
    }
}
