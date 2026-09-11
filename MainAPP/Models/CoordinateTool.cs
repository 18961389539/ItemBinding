using OpenCvSharp;
using System;

namespace MainAPP.Models
{
    /// <summary>
    /// 坐标系标定工具配置。
    /// 包含三点标定参数（原点、X轴点、Y轴点）和棋盘格检测参数，
    /// 用于将图像像素坐标映射到物理世界坐标（mm）。
    /// L408c: 以下属性包含大量魔法数字默认值（如 100, 13, 9, 50, 500_000, 0.85, 9, 75, 100, 200, 3 等），
    /// 这些值基于实际标定经验设定。暂不提取为常量或配置项（改动大，需重新验证标定流程），后续统一规划。
    /// </summary>
    public class CoordinateTool
    {
        #region 三点标定结果

        /// <summary>
        /// 坐标系原点在图像中的位置（像素）
        /// </summary>
        public Point2f Origin { get; set; } = new Point2f(0, 0);

        /// <summary>
        /// X轴方向标定点在图像中的位置（像素），与原点共同确定X轴方向和比例
        /// </summary>
        public Point2f XPoint { get; set; } = new Point2f(100, 0);

        /// <summary>
        /// Y轴方向标定点在图像中的位置（像素），与原点共同确定Y轴方向和比例
        /// </summary>
        public Point2f YPoint { get; set; } = new Point2f(0, 100);
        #endregion

        #region 棋盘格参数

        /// <summary>
        /// 棋盘格单个方格的物理尺寸（mm），用于计算像素到物理坐标的比例因子
        /// </summary>
        public double SquareSize { get; set; } = 100;
        #endregion

        #region 圆点检测参数
        /// <summary>
        /// 棋盘内角点宽度（列数-1）
        /// </summary>
        public int PatternWidth { get; set; } = 13;

        /// <summary>
        /// 棋盘内角点高度（行数-1）
        /// </summary>
        public int PatternHeight { get; set; } = 9;

        /// <summary>
        /// 棋盘内角点尺寸，用于预先定位棋盘区域并屏蔽区域外干扰（可选）
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public Size? PatternSize => PatternWidth > 0 && PatternHeight > 0 ? new Size(PatternWidth, PatternHeight) : null;

        /// <summary>
        /// 棋盘区域外扩像素数，避免边缘圆点被裁剪
        /// </summary>
        public int RoiPadding { get; set; } = 50;

        /// <summary>
        /// 最小轮廓面积，面积小于此值的轮廓将被过滤
        /// </summary>
        public int MinArea { get; set; } = 100;

        /// <summary>
        /// 最大轮廓面积，面积大于此值的轮廓将被过滤
        /// </summary>
        public int MaxArea { get; set; } = 500_000;

        /// <summary>
        /// 最小圆度阈值，用于筛选接近圆形的轮廓（1.0为完美圆）
        /// </summary>
        public double MinCircularity { get; set; } = 0.85;

        /// <summary>
        /// 双边滤波直径参数，值越大平滑范围越广
        /// </summary>
        public int BilateralD { get; set; } = 9;

        /// <summary>
        /// 双边滤波颜色空间sigma，控制颜色差异的平滑强度
        /// </summary>
        public double BilateralSigmaColor { get; set; } = 75;

        /// <summary>
        /// 双边滤波坐标空间sigma，控制空间距离的平滑强度
        /// </summary>
        public double BilateralSigmaSpace { get; set; } = 75;

        /// <summary>
        /// Canny边缘检测低阈值
        /// </summary>
        public double CannyThreshold1 { get; set; } = 100;

        /// <summary>
        /// Canny边缘检测高阈值
        /// </summary>
        public double CannyThreshold2 { get; set; } = 200;

        /// <summary>
        /// 形态学操作核大小（奇数），用于膨胀/腐蚀操作
        /// </summary>
        public int KernelSize { get; set; } = 3;

        /// <summary>
        /// 是否启用CLAHE（限制对比度自适应直方图均衡化）光照均衡化，
        /// 可改善光照不均匀条件下的圆点检测效果
        /// </summary>
        public bool EnableCLAHE { get; set; } = true;

        /// <summary>
        /// 是否启用多策略重试，当默认参数检测失败时尝试不同的预处理参数组合
        /// </summary>
        public bool EnableMultiStrategy { get; set; } = true;
        #endregion
    }
}
