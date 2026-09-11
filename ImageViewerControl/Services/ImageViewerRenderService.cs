using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ImageViewer.Abstractions;
using ImageViewer.Rendering;

namespace ImageViewer.Services
{
    public sealed class ImageViewerRenderService : IImageViewerRenderService
    {
        private const int PyramidMinDimension = 512;
        private const long LargeImageThresholdPixels = 4_000_000;
        private const double TileMarginScreenPixels = 192;

        private readonly ImageViewerRenderTileCache _tileCache = new();

        public ImageSource? BuildDisplaySource(ImageSource? source, PseudoColorPalette palette)
        {
            if (source is not BitmapSource bitmap || palette == PseudoColorPalette.None)
            {
                return source;
            }

            return ApplyPseudoColor(bitmap, palette);
        }

        public void ApplyGpuCaching(Canvas imageContainer, bool enableGpuRendering)
        {
            ArgumentNullException.ThrowIfNull(imageContainer);
            imageContainer.CacheMode = enableGpuRendering ? new BitmapCache() : null;
        }

        public void ClearTileCache()
        {
            _tileCache.Clear();
        }

        public BitmapSource? GetAnalysisBitmap(ImageSource? source)
        {
            if (source is not BitmapSource bitmap)
            {
                return null;
            }

            if (bitmap is RenderTargetBitmap)
            {
                var detachedBitmap = new WriteableBitmap(bitmap);
                if (detachedBitmap.CanFreeze)
                {
                    detachedBitmap.Freeze();
                }

                return detachedBitmap;
            }

            if (bitmap.IsFrozen)
            {
                return bitmap;
            }

            BitmapSource clone = bitmap.Clone();
            if (clone.CanFreeze)
            {
                clone.Freeze();
            }

            return clone;
        }

        public Effect? CreatePseudoColorEffect(PseudoColorPalette palette)
        {
            if (palette == PseudoColorPalette.None)
            {
                return null;
            }

            try
            {
                return new PseudoColorShaderEffect(palette);
            }
            catch
            {
                return null;
            }
        }

        public Task<IReadOnlyList<ImagePyramidLevel>> BuildPyramidAsync(BitmapSource? source, CancellationToken cancellationToken)
        {
            if (source == null)
            {
                return Task.FromResult<IReadOnlyList<ImagePyramidLevel>>([]);
            }

            BitmapSource workingSource = EnsureFrozenBitmap(source);

            return Task.Run<IReadOnlyList<ImagePyramidLevel>>(() =>
            {
                List<ImagePyramidLevel> levels = [new(workingSource, 1.0)];
                BitmapSource current = workingSource;
                double scaleFactor = 1.0;

                while (!cancellationToken.IsCancellationRequested &&
                       (current.PixelWidth > PyramidMinDimension || current.PixelHeight > PyramidMinDimension))
                {
                    scaleFactor *= 0.5;
                    var next = new TransformedBitmap(current, new ScaleTransform(0.5, 0.5));
                    if (next.CanFreeze)
                    {
                        next.Freeze();
                    }

                    current = next;
                    levels.Add(new ImagePyramidLevel(current, scaleFactor));

                    if (current.PixelWidth <= 1 || current.PixelHeight <= 1)
                    {
                        break;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                return (IReadOnlyList<ImagePyramidLevel>)levels;
            }, cancellationToken);
        }

        public ImageViewerRenderFrame BuildRenderFrame(BitmapSource? source, IReadOnlyList<ImagePyramidLevel>? pyramid, Size viewport, double scale, Point translation, PseudoColorPalette palette, bool enableTiledRendering, bool autoSelectPyramidLevel, bool prefetchAdjacentTiles, int tileCacheMaximumMegabytes, int tilePrefetchRadius)
        {
            if (source == null)
            {
                return new ImageViewerRenderFrame(null, 0, 0, 0, 0, 1.0, false);
            }

            _tileCache.SetMaximumBytes(Math.Max(1, tileCacheMaximumMegabytes) * 1024L * 1024L);

            IReadOnlyList<ImagePyramidLevel> levels = pyramid is { Count: > 0 }
                ? pyramid
                : [new ImagePyramidLevel(EnsureFrozenBitmap(source), 1.0)];
            BitmapSource workingSource = levels[0].Bitmap;
            ImagePyramidLevel level = SelectLevel(levels, scale, autoSelectPyramidLevel);
            bool useTiledRendering = enableTiledRendering && IsLargeImage(workingSource) && IsValid(viewport);

            if (!useTiledRendering)
            {
                return new ImageViewerRenderFrame(workingSource, 0, 0, workingSource.PixelWidth, workingSource.PixelHeight, 1.0, false);
            }

            Rect visibleRegion = GetVisibleRegion(workingSource, viewport, scale, translation, prefetchAdjacentTiles);
            if (visibleRegion.IsEmpty)
            {
                return new ImageViewerRenderFrame(level.Bitmap, 0, 0, workingSource.PixelWidth, workingSource.PixelHeight, level.ScaleFactor, false);
            }

            Int32Rect sourceCrop = ToCropRect(level.Bitmap, visibleRegion, level.ScaleFactor);
            if (_tileCache.TryGet(level.Bitmap, sourceCrop, out BitmapSource? cachedFrame))
            {
                int exactPrefetchRadius = prefetchAdjacentTiles ? Math.Max(0, tilePrefetchRadius) : 0;
                if (exactPrefetchRadius > 0)
                {
                    Int32Rect cacheCropForPrefetch = ImageViewerRenderTileCache.ExpandToTileGrid(sourceCrop, level.Bitmap.PixelWidth, level.Bitmap.PixelHeight);
                    _tileCache.Prefetch(level.Bitmap, ImageViewerRenderTileCache.BuildPrefetchRects(cacheCropForPrefetch, level.Bitmap.PixelWidth, level.Bitmap.PixelHeight, exactPrefetchRadius));
                }

                return new ImageViewerRenderFrame(cachedFrame, visibleRegion.X, visibleRegion.Y, visibleRegion.Width, visibleRegion.Height, level.ScaleFactor, true);
            }

            Int32Rect cacheCrop = ImageViewerRenderTileCache.ExpandToTileGrid(sourceCrop, level.Bitmap.PixelWidth, level.Bitmap.PixelHeight);
            BitmapSource cachedTile = _tileCache.GetOrCreate(level.Bitmap, cacheCrop);
            Int32Rect cropWithinTile = new(sourceCrop.X - cacheCrop.X, sourceCrop.Y - cacheCrop.Y, sourceCrop.Width, sourceCrop.Height);
            BitmapSource tiledSource = cropWithinTile.X == 0 && cropWithinTile.Y == 0 && cropWithinTile.Width == cachedTile.PixelWidth && cropWithinTile.Height == cachedTile.PixelHeight
                ? cachedTile
                : new CroppedBitmap(cachedTile, cropWithinTile);

            if (tiledSource.CanFreeze)
            {
                tiledSource.Freeze();
            }

            _tileCache.Store(level.Bitmap, sourceCrop, tiledSource);

            int prefetchRadius = prefetchAdjacentTiles ? Math.Max(0, tilePrefetchRadius) : 0;
            if (prefetchRadius > 0)
            {
                _tileCache.Prefetch(level.Bitmap, ImageViewerRenderTileCache.BuildPrefetchRects(cacheCrop, level.Bitmap.PixelWidth, level.Bitmap.PixelHeight, prefetchRadius));
            }

            return new ImageViewerRenderFrame(tiledSource, visibleRegion.X, visibleRegion.Y, visibleRegion.Width, visibleRegion.Height, level.ScaleFactor, true);
        }

        public Task<int[]?> CreateHistogramAsync(BitmapSource? source, int binCount, CancellationToken cancellationToken)
        {
            if (source == null)
            {
                return Task.FromResult<int[]?>(null);
            }

            return Task.Run<int[]?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ImageAnalysisService.CreateHistogram(source, binCount);
            }, cancellationToken);
        }

        public Task<byte[]?> CreateProfileAsync(ImageViewerAnalysisRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Task.Run<byte[]?>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ImageAnalysisService.CreateProfile(request.Bitmap, request.P1, request.P2);
            }, cancellationToken);
        }

        private static BitmapSource ApplyPseudoColor(BitmapSource source, PseudoColorPalette palette)
        {
            BitmapSource normalizedSource = source.Format == PixelFormats.Gray8
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);

            int width = normalizedSource.PixelWidth;
            int height = normalizedSource.PixelHeight;
            int stride = width;
            byte[] grayPixels = new byte[stride * height];
            normalizedSource.CopyPixels(grayPixels, stride, 0);

            byte[] colorPixels = new byte[width * height * 4];
            for (int i = 0; i < grayPixels.Length; i++)
            {
                Color color = GetPaletteColor(grayPixels[i] / 255d, palette);
                int colorIndex = i * 4;
                colorPixels[colorIndex] = color.B;
                colorPixels[colorIndex + 1] = color.G;
                colorPixels[colorIndex + 2] = color.R;
                colorPixels[colorIndex + 3] = 255;
            }

            var result = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, colorPixels, width * 4);
            result.Freeze();
            return result;
        }

        private static bool IsLargeImage(BitmapSource source)
        {
            return (long)source.PixelWidth * source.PixelHeight >= LargeImageThresholdPixels;
        }

        private static BitmapSource EnsureFrozenBitmap(BitmapSource source)
        {
            if (source.IsFrozen)
            {
                return source;
            }

            BitmapSource clone = source.Clone();
            if (clone.CanFreeze)
            {
                clone.Freeze();
            }

            return clone;
        }

        private static bool IsValid(Size size)
        {
            return !double.IsNaN(size.Width) && !double.IsNaN(size.Height) && size.Width > 0 && size.Height > 0;
        }

        private static ImagePyramidLevel SelectLevel(IReadOnlyList<ImagePyramidLevel> levels, double scale, bool autoSelectPyramidLevel)
        {
            if (!autoSelectPyramidLevel || levels.Count == 0)
            {
                return levels[0];
            }

            ImagePyramidLevel selected = levels[0];
            double target = Math.Clamp(scale, levels[^1].ScaleFactor, 1.0);
            double bestDistance = double.MaxValue;

            foreach (ImagePyramidLevel level in levels)
            {
                double distance = Math.Abs(Math.Log(level.ScaleFactor, 2) - Math.Log(target, 2));
                if (distance < bestDistance)
                {
                    selected = level;
                    bestDistance = distance;
                }
            }

            return selected;
        }

        private static Rect GetVisibleRegion(BitmapSource source, Size viewport, double scale, Point translation, bool prefetchAdjacentTiles)
        {
            if (!IsValid(viewport) || scale <= 0)
            {
                return new Rect(0, 0, source.PixelWidth, source.PixelHeight);
            }

            double left = Math.Max(0, -translation.X / scale);
            double top = Math.Max(0, -translation.Y / scale);
            double right = Math.Min(source.PixelWidth, (viewport.Width - translation.X) / scale);
            double bottom = Math.Min(source.PixelHeight, (viewport.Height - translation.Y) / scale);
            if (right <= left || bottom <= top)
            {
                return Rect.Empty;
            }

            double margin = (prefetchAdjacentTiles ? TileMarginScreenPixels : 32) / Math.Max(scale, 0.1);
            return ClampRect(new Rect(left - margin, top - margin, (right - left) + margin * 2, (bottom - top) + margin * 2), source.PixelWidth, source.PixelHeight);
        }

        private static Rect ClampRect(Rect rect, int maxWidth, int maxHeight)
        {
            double x = Math.Clamp(rect.X, 0, maxWidth);
            double y = Math.Clamp(rect.Y, 0, maxHeight);
            double right = Math.Clamp(rect.Right, x, maxWidth);
            double bottom = Math.Clamp(rect.Bottom, y, maxHeight);
            return new Rect(x, y, right - x, bottom - y);
        }

        private static Int32Rect ToCropRect(BitmapSource source, Rect visibleRegion, double scaleFactor)
        {
            int x = Math.Clamp((int)Math.Floor(visibleRegion.X * scaleFactor), 0, source.PixelWidth - 1);
            int y = Math.Clamp((int)Math.Floor(visibleRegion.Y * scaleFactor), 0, source.PixelHeight - 1);
            int width = Math.Max(1, Math.Min(source.PixelWidth - x, (int)Math.Ceiling(visibleRegion.Width * scaleFactor)));
            int height = Math.Max(1, Math.Min(source.PixelHeight - y, (int)Math.Ceiling(visibleRegion.Height * scaleFactor)));
            return new Int32Rect(x, y, width, height);
        }

        private static Color GetPaletteColor(double value, PseudoColorPalette palette)
        {
            value = Math.Clamp(value, 0d, 1d);
            return palette switch
            {
                PseudoColorPalette.Hot => Interpolate(value,
                    (0.0, Colors.Black),
                    (0.33, Colors.DarkRed),
                    (0.66, Colors.Orange),
                    (1.0, Colors.Yellow)),
                PseudoColorPalette.Jet => Interpolate(value,
                    (0.0, Color.FromRgb(0, 0, 128)),
                    (0.35, Colors.Cyan),
                    (0.66, Colors.Yellow),
                    (1.0, Color.FromRgb(128, 0, 0))),
                PseudoColorPalette.Viridis => Interpolate(value,
                    (0.0, Color.FromRgb(68, 1, 84)),
                    (0.33, Color.FromRgb(59, 82, 139)),
                    (0.66, Color.FromRgb(33, 145, 140)),
                    (1.0, Color.FromRgb(253, 231, 37))),
                _ => Colors.Transparent
            };
        }

        private static Color Interpolate(double value, params (double Stop, Color Color)[] stops)
        {
            if (stops.Length == 0)
            {
                return Colors.Transparent;
            }

            if (value <= stops[0].Stop)
            {
                return stops[0].Color;
            }

            for (int i = 1; i < stops.Length; i++)
            {
                if (value <= stops[i].Stop)
                {
                    double range = stops[i].Stop - stops[i - 1].Stop;
                    double t = range <= 0 ? 0 : (value - stops[i - 1].Stop) / range;
                    return Color.FromRgb(
                        (byte)Math.Round(stops[i - 1].Color.R + (stops[i].Color.R - stops[i - 1].Color.R) * t),
                        (byte)Math.Round(stops[i - 1].Color.G + (stops[i].Color.G - stops[i - 1].Color.G) * t),
                        (byte)Math.Round(stops[i - 1].Color.B + (stops[i].Color.B - stops[i - 1].Color.B) * t));
                }
            }

            return stops[^1].Color;
        }
    }
}
