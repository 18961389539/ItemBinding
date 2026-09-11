using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageViewer.Services
{
    internal static class BlobAnalysisService
    {
        /// <summary>
        /// 执行斑点分析，返回找到的连通域列表
        /// </summary>
        public static List<BlobFeature> DetectBlobs(BitmapSource bitmap, Rect searchRoi, bool useOtsu, int threshold, bool detectDark = false, int minArea = 10)
        {
            if (bitmap == null || searchRoi.Width <= 0 || searchRoi.Height <= 0)
                return new List<BlobFeature>();

            // 1. 裁剪并获取像素
            int x = Math.Max(0, (int)searchRoi.X);
            int y = Math.Max(0, (int)searchRoi.Y);
            int w = Math.Min(bitmap.PixelWidth - x, (int)searchRoi.Width);
            int h = Math.Min(bitmap.PixelHeight - y, (int)searchRoi.Height);

            if (w <= 0 || h <= 0) return new List<BlobFeature>();

            var format = bitmap.Format;
            int bytesPerPixel = (format.BitsPerPixel + 7) / 8;
            int stride = w * bytesPerPixel;
            byte[] pixels = new byte[h * stride];
            bitmap.CopyPixels(new Int32Rect(x, y, w, h), pixels, stride, 0);

            // 2. 转换为单通道灰度数据并应用阈值
            byte[] binaryMap = new byte[w * h];
            
            if (useOtsu)
            {
                threshold = CalculateOtsuThreshold(pixels, w, h, bytesPerPixel, stride);
            }

            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int pIndex = r * stride + c * bytesPerPixel;
                    byte gray = bytesPerPixel >= 3 
                        ? (byte)((pixels[pIndex] * 0.114) + (pixels[pIndex + 1] * 0.587) + (pixels[pIndex + 2] * 0.299)) 
                        : pixels[pIndex];

                    bool isForeground = detectDark ? gray <= threshold : gray >= threshold;
                    binaryMap[r * w + c] = isForeground ? (byte)255 : (byte)0;
                }
            }

            // 3. 连通域标记 (Connected Component Labeling - Two-Pass)
            int[] labels = new int[w * h];
            int nextLabel = 1;
            List<int> linked = new List<int> { 0 }; // 0 is background

            // First pass
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (binaryMap[r * w + c] == 255)
                    {
                        int left = c > 0 ? labels[r * w + (c - 1)] : 0;
                        int top = r > 0 ? labels[(r - 1) * w + c] : 0;

                        if (left == 0 && top == 0)
                        {
                            labels[r * w + c] = nextLabel;
                            linked.Add(nextLabel);
                            nextLabel++;
                        }
                        else if (left != 0 && top == 0)
                        {
                            labels[r * w + c] = left;
                        }
                        else if (left == 0 && top != 0)
                        {
                            labels[r * w + c] = top;
                        }
                        else
                        {
                            // Both are not 0
                            int minL = Math.Min(left, top);
                            int maxL = Math.Max(left, top);
                            labels[r * w + c] = minL;
                            Union(linked, minL, maxL);
                        }
                    }
                }
            }

            // Second pass & feature extraction
            var blobs = new Dictionary<int, BlobData>();

            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (binaryMap[r * w + c] == 255)
                    {
                        int label = Find(linked, labels[r * w + c]);
                        labels[r * w + c] = label;

                        if (!blobs.TryGetValue(label, out var blob))
                        {
                            blob = new BlobData { Label = label, MinX = c, MinY = r, MaxX = c, MaxY = r };
                            blobs[label] = blob;
                        }
                        
                        blob.Area++;
                        blob.SumX += c;
                        blob.SumY += r;
                        if (c < blob.MinX) blob.MinX = c;
                        if (c > blob.MaxX) blob.MaxX = c;
                        if (r < blob.MinY) blob.MinY = r;
                        if (r > blob.MaxY) blob.MaxY = r;
                    }
                }
            }

            // 4. 组装结果
            var results = new List<BlobFeature>();
            foreach (var blob in blobs.Values)
            {
                if (blob.Area >= minArea)
                {
                    double centroidX = (double)blob.SumX / blob.Area;
                    double centroidY = (double)blob.SumY / blob.Area;
                    results.Add(new BlobFeature(
                        blob.Label,
                        blob.Area,
                        new Point(centroidX + x, centroidY + y),
                        new Rect(blob.MinX + x, blob.MinY + y, blob.MaxX - blob.MinX + 1, blob.MaxY - blob.MinY + 1)
                    ));
                }
            }

            return results;
        }

        private static int Find(List<int> linked, int i)
        {
            while (linked[i] != i)
            {
                i = linked[i];
            }
            return i;
        }

        private static void Union(List<int> linked, int i, int j)
        {
            int rootI = Find(linked, i);
            int rootJ = Find(linked, j);
            if (rootI != rootJ)
            {
                linked[rootJ] = rootI;
            }
        }

        private static int CalculateOtsuThreshold(byte[] pixels, int w, int h, int bytesPerPixel, int stride)
        {
            int[] histogram = new int[256];
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int pIndex = r * stride + c * bytesPerPixel;
                    byte gray = bytesPerPixel >= 3 
                        ? (byte)((pixels[pIndex] * 0.114) + (pixels[pIndex + 1] * 0.587) + (pixels[pIndex + 2] * 0.299)) 
                        : pixels[pIndex];
                    histogram[gray]++;
                }
            }

            int total = w * h;
            double sum = 0;
            for (int i = 0; i < 256; i++) sum += i * histogram[i];

            double sumB = 0;
            int wB = 0;
            int wF = 0;

            double varMax = 0;
            int threshold = 0;

            for (int i = 0; i < 256; i++)
            {
                wB += histogram[i];
                if (wB == 0) continue;

                wF = total - wB;
                if (wF == 0) break;

                sumB += (double)(i * histogram[i]);

                double mB = sumB / wB;
                double mF = (sum - sumB) / wF;

                double varBetween = (double)wB * (double)wF * (mB - mF) * (mB - mF);

                if (varBetween > varMax)
                {
                    varMax = varBetween;
                    threshold = i;
                }
            }

            return threshold;
        }

        private class BlobData
        {
            public int Label { get; set; }
            public int Area { get; set; }
            public long SumX { get; set; }
            public long SumY { get; set; }
            public int MinX { get; set; }
            public int MinY { get; set; }
            public int MaxX { get; set; }
            public int MaxY { get; set; }
        }
    }
}
