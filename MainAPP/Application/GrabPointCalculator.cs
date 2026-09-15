using CoordinateSystemMapping;
using System;

namespace MainAPP.Application
{
    /// <summary>
    /// 抓取点计算（2026-09-15）。
    ///
    /// <para><b>设计原则</b>：抓取点定义在<b>产品局部坐标系</b>（掩码最小外接旋转矩形的局部系）里，
    /// 而不是在世界坐标系上叠加常量。夹爪偏心、抓取点在产品上的位置，本质都是产品坐标系里的固定量，
    /// 必须随产品角度一起旋转 —— 原先"世界系常量平移"的做法只在单一角度成立，产品一转就失准。</para>
    ///
    /// <para><b>为什么传方向向量而不是角度</b>：矩形是在推理图坐标系上算出来的，而调用方给出的中心
    /// 已按缩放比还原到原图坐标系；当 <c>ResizeScale != ResizeScaleY</c>（非等比缩放）时两个坐标系的
    /// 角度并不相同。传"同坐标系下的无向长轴方向"可彻底避开这一换算陷阱，也顺带省掉归一化问题。</para>
    ///
    /// <para><b>轴向约定</b>：
    /// 长轴正方向 = 朝向产品<b>头部</b>（由二维码 → 模型翻转 → 特征池的消歧链确定）；
    /// 短轴 = 长轴在图像坐标系内旋转 +90°（<c>(-dy, dx)</c>）。头尾翻转时两轴同时反向 ——
    /// 等价于"站到产品另一端回头看"，左右自然互换。</para>
    ///
    /// <para><b>短轴正方向的现场可能相反</b>：图像坐标系的"左右"取决于相机安装朝向，代码无法得知。
    /// 若现场发现短轴方向与预期相反，直接填<b>负值</b>即可，无需改代码。画面叠加会画出抓取点便于核对。</para>
    ///
    /// <para><b>为什么在图像坐标系里算</b>：镜像（<see cref="CoordinateTransformer.IsMirrored"/>）、
    /// 旋转与非均匀尺度会被标定变换统一吸收，不需要额外的符号判断。</para>
    /// </summary>
    internal static class GrabPointCalculator
    {
        /// <summary>
        /// 纯计算：由矩形中心、无向长轴方向、头尾翻转标志与 mm 偏移，算出抓取点的图像坐标。
        /// 不依赖标定与实际图像，便于单元测试。
        /// </summary>
        /// <param name="centerImageX">矩形中心 X（与 <paramref name="longAxisDirX"/> 同坐标系）。</param>
        /// <param name="centerImageY">矩形中心 Y。</param>
        /// <param name="longAxisDirX">长轴方向 X（无向，无需归一化；0,0 视为退化，直接返回中心）。</param>
        /// <param name="longAxisDirY">长轴方向 Y。</param>
        /// <param name="headFlipped">头尾是否与长轴无向正方向相反（真 = 头部在长轴负方向）。</param>
        /// <param name="offsetLongMm">沿长轴偏移（mm，正 = 朝头部）。</param>
        /// <param name="offsetShortMm">沿短轴偏移（mm，正 = 长轴顺时针 90° 侧）。</param>
        /// <param name="pixelsPerMmLong">长轴方向上的像素/mm。</param>
        /// <param name="pixelsPerMmShort">短轴方向上的像素/mm。</param>
        /// <returns>抓取点的图像坐标。两个偏移都为 0、或方向退化时，原样返回中心
        /// （保证默认行为与改造前逐位一致）。</returns>
        internal static (double X, double Y) ComputeImagePoint(
            double centerImageX,
            double centerImageY,
            double longAxisDirX,
            double longAxisDirY,
            bool headFlipped,
            double offsetLongMm,
            double offsetShortMm,
            double pixelsPerMmLong,
            double pixelsPerMmShort)
        {
            var (dx, dy) = ComputeImageOffset(
                longAxisDirX, longAxisDirY, headFlipped,
                offsetLongMm, offsetShortMm, pixelsPerMmLong, pixelsPerMmShort);

            return (centerImageX + dx, centerImageY + dy);
        }

        /// <summary>
        /// 纯计算：抓取点相对矩形中心的<b>偏移向量</b>（与 <paramref name="longAxisDirX"/> 同坐标系）。
        ///
        /// <para>单独暴露偏移量是为了让画面叠加能复用同一套几何 —— 绘制发生在推理图分辨率上，
        /// 而标定换算针对原图分辨率，需要先拿到偏移向量再做等比折算。</para>
        /// </summary>
        /// <returns>偏移向量；偏移为 0 或方向退化时返回 (0, 0)。</returns>
        internal static (double Dx, double Dy) ComputeImageOffset(
            double longAxisDirX,
            double longAxisDirY,
            bool headFlipped,
            double offsetLongMm,
            double offsetShortMm,
            double pixelsPerMmLong,
            double pixelsPerMmShort)
        {
            if (offsetLongMm == 0 && offsetShortMm == 0)
            {
                return (0, 0);
            }

            var len = Math.Sqrt((longAxisDirX * longAxisDirX) + (longAxisDirY * longAxisDirY));
            if (len <= 1e-12)
            {
                // 方向退化（如单点/两点凸包的退化矩形）：无法定义轴向，退回中心
                return (0, 0);
            }

            var luX = longAxisDirX / len;
            var luY = longAxisDirY / len;
            if (headFlipped)
            {
                // 翻到头部方向；短轴由长轴旋转 90° 导出，故两轴同时反向
                luX = -luX;
                luY = -luY;
            }

            // 短轴 = 长轴旋转 +90°（图像坐标系 Y 向下，视觉上为顺时针）
            var svX = -luY;
            var svY = luX;

            return (
                (offsetLongMm * pixelsPerMmLong * luX) + (offsetShortMm * pixelsPerMmShort * svX),
                (offsetLongMm * pixelsPerMmLong * luY) + (offsetShortMm * pixelsPerMmShort * svY));
        }

        /// <summary>角度版纯计算（供测试与角度已知的调用方使用）。</summary>
        /// <param name="longAxisAngleDeg">长轴角（度，与中心同坐标系的无向角）。</param>
        internal static (double X, double Y) ComputeImagePoint(
            double centerImageX,
            double centerImageY,
            double longAxisAngleDeg,
            bool headFlipped,
            double offsetLongMm,
            double offsetShortMm,
            double pixelsPerMmLong,
            double pixelsPerMmShort)
        {
            var theta = longAxisAngleDeg * Math.PI / 180.0;
            return ComputeImagePoint(
                centerImageX, centerImageY, Math.Cos(theta), Math.Sin(theta), headFlipped,
                offsetLongMm, offsetShortMm, pixelsPerMmLong, pixelsPerMmShort);
        }

        /// <summary>
        /// 抓取点（带标定换算）。两个偏移都为 0 时短路返回矩形中心，不触碰标定变换。
        /// </summary>
        internal static (double X, double Y) ComputeImagePoint(
            CoordinateTransformer transformer,
            double centerImageX,
            double centerImageY,
            double longAxisDirX,
            double longAxisDirY,
            bool headFlipped,
            double offsetLongMm,
            double offsetShortMm)
        {
            if (offsetLongMm == 0 && offsetShortMm == 0)
            {
                return (centerImageX, centerImageY);
            }

            var len = Math.Sqrt((longAxisDirX * longAxisDirX) + (longAxisDirY * longAxisDirY));
            if (len <= 1e-12)
            {
                return (centerImageX, centerImageY);
            }

            var luX = longAxisDirX / len;
            var luY = longAxisDirY / len;

            // 尺度取无向轴方向即可（正负不影响长度比例）
            var pixelsPerMmLong = PixelsPerMmAlong(transformer, centerImageX, centerImageY, luX, luY);
            var pixelsPerMmShort = PixelsPerMmAlong(transformer, centerImageX, centerImageY, -luY, luX);

            return ComputeImagePoint(
                centerImageX, centerImageY, longAxisDirX, longAxisDirY, headFlipped,
                offsetLongMm, offsetShortMm, pixelsPerMmLong, pixelsPerMmShort);
        }

        /// <summary>
        /// 求图像中某个方向上的"像素/mm"。
        ///
        /// <para>做法：把中心与该方向 +1 像素处的两点分别转到物理坐标，用它们的距离反推局部比例。
        /// 这样可自动吸收三点标定的<b>非正交、X/Y 非均匀比例与镜像</b>，无需给 <see cref="CoordinateTransformer"/>
        /// 新增尺度接口（其 <c>_scaleX/_scaleY</c> 为私有，且直接使用会在斜轴方向产生偏差）。</para>
        ///
        /// <para>未标定时（<see cref="CoordinateTransformer.IsInitialized"/> 为 false）坐标变换是恒等映射，
        /// 此时返回 1，等价于"1 单位 = 1 像素"，与未标定分支的既有语义一致。</para>
        /// </summary>
        internal static double PixelsPerMmAlong(
            CoordinateTransformer transformer,
            double imageX,
            double imageY,
            double unitX,
            double unitY)
        {
            if (!transformer.IsInitialized)
            {
                return 1.0;
            }

            var (w0x, w0y) = transformer.ImageToPhysical(imageX, imageY);
            var (w1x, w1y) = transformer.ImageToPhysical(imageX + unitX, imageY + unitY);
            var mm = Math.Sqrt(((w1x - w0x) * (w1x - w0x)) + ((w1y - w0y) * (w1y - w0y)));

            // 退化标定（两轴重合/极短）时避免除零；退化为像素当量
            return mm > 1e-9 ? 1.0 / mm : 1.0;
        }
    }
}
