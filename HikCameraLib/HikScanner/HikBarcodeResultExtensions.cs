using System;
using System.Drawing;
using System.Linq;

namespace HikScanner
{
    /// <summary>
    /// HikBarcodeResult 兼容扩展，提供与旧版 BarcodeResult 等价的访问 API。
    /// </summary>
    public static class HikBarcodeResultExtensions
    {
        /// <summary>条码字符串（兼容 BarcodeResult.CodeString）</summary>
        public static string CodeString(this HikBarcodeResult barcode) => barcode.Code ?? string.Empty;

        /// <summary>条码置信度（兼容 BarcodeResult.Confidence，uint 类型）</summary>
        public static uint Confidence(this HikBarcodeResult barcode) => (uint)Math.Max(0, barcode.IDRScore);

        /// <summary>条码位置四个顶点（兼容 BarcodeResult.Location，返回 int 坐标 Point 数组）</summary>
        public static Point[] Location(this HikBarcodeResult barcode)
        {
            if (barcode.BoundingPoints == null || barcode.BoundingPoints.Length < 4)
                return Array.Empty<Point>();
            var pts = new Point[barcode.BoundingPoints.Length];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new Point((int)barcode.BoundingPoints[i].X, (int)barcode.BoundingPoints[i].Y);
            return pts;
        }

        /// <summary>条码中心点（兼容 BarcodeResult.Center）</summary>
        public static Point Center(this HikBarcodeResult barcode)
        {
            if (barcode.BoundingPoints == null || barcode.BoundingPoints.Length == 0)
                return new Point();
            float x = barcode.BoundingPoints.Average(p => p.X);
            float y = barcode.BoundingPoints.Average(p => p.Y);
            return new Point((int)x, (int)y);
        }

        /// <summary>当前条码是否为二维码（兼容 BarcodeResult.IsQrCode）</summary>
        public static bool IsQrCode(this HikBarcodeResult barcode)
        {
            var name = barcode.CodeTypeName ?? string.Empty;
            return name.Contains("QR");
        }
    }
}
