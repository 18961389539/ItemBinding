using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ImageViewer.Services
{
    internal sealed class ImageViewerRenderTileCache
    {
        private const int TileSizePixels = 512;
        private const long DefaultMaximumBytes = 128L * 1024L * 1024L;

        private readonly object _gate = new();
        private readonly Dictionary<RenderTileCacheKey, CacheEntry> _entries = new();
        private readonly LinkedList<RenderTileCacheKey> _lru = new();
        private readonly HashSet<RenderTileCacheKey> _inFlightPrefetches = new();

        private long _currentBytes;
        private long _usageSequence;
        private long _maximumBytes = DefaultMaximumBytes;

        public void SetMaximumBytes(long maximumBytes)
        {
            lock (_gate)
            {
                _maximumBytes = Math.Max(1, maximumBytes);
                TrimToBudget();
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _lru.Clear();
                _inFlightPrefetches.Clear();
                _currentBytes = 0;
                _usageSequence = 0;
            }
        }

        public BitmapSource GetOrCreate(BitmapSource source, Int32Rect rect)
        {
            ArgumentNullException.ThrowIfNull(source);

            RenderTileCacheKey key = new(source, rect);
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? entry))
                {
                    Touch(entry);
                    return entry.Bitmap;
                }
            }

            BitmapSource created = CreateCroppedBitmap(source, rect);

            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? existing))
                {
                    Touch(existing);
                    return existing.Bitmap;
                }

                AddEntry(key, created);
                return created;
            }
        }

        public bool TryGet(BitmapSource source, Int32Rect rect, out BitmapSource? bitmap)
        {
            ArgumentNullException.ThrowIfNull(source);

            RenderTileCacheKey key = new(source, rect);
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? entry))
                {
                    Touch(entry);
                    bitmap = entry.Bitmap;
                    return true;
                }
            }

            bitmap = null;
            return false;
        }

        public void Store(BitmapSource source, Int32Rect rect, BitmapSource bitmap)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(bitmap);

            RenderTileCacheKey key = new(source, rect);
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out CacheEntry? existing))
                {
                    Touch(existing);
                    return;
                }

                AddEntry(key, bitmap);
            }
        }

        public void Prefetch(BitmapSource source, IEnumerable<Int32Rect> rects)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(rects);

            foreach (Int32Rect rect in rects)
            {
                QueuePrefetch(source, rect);
            }
        }

        private void QueuePrefetch(BitmapSource source, Int32Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            RenderTileCacheKey key = new(source, rect);
            lock (_gate)
            {
                if (_entries.ContainsKey(key) || !_inFlightPrefetches.Add(key))
                {
                    return;
                }
            }

            _ = Task.Run(() =>
            {
                try
                {
                    GetOrCreate(source, rect);
                }
                finally
                {
                    lock (_gate)
                    {
                        _inFlightPrefetches.Remove(key);
                    }
                }
            });
        }

        private void AddEntry(RenderTileCacheKey key, BitmapSource bitmap)
        {
            long byteCount = EstimateByteCount(bitmap);
            var node = new LinkedListNode<RenderTileCacheKey>(key);
            _lru.AddFirst(node);
            _entries[key] = new CacheEntry(bitmap, node, byteCount, _usageSequence++);
            _currentBytes += byteCount;
            TrimToBudget();
        }

        private void Touch(CacheEntry entry)
        {
            if (entry.Node.List != null)
            {
                _lru.Remove(entry.Node);
                _lru.AddFirst(entry.Node);
            }

            entry.UsageSequence = _usageSequence++;
        }

        private void TrimToBudget()
        {
            while (_currentBytes > _maximumBytes && _lru.Last != null)
            {
                LinkedListNode<RenderTileCacheKey> node = _lru.Last;
                _lru.RemoveLast();

                if (_entries.Remove(node.Value, out CacheEntry? entry))
                {
                    _currentBytes = Math.Max(0, _currentBytes - entry.ByteCount);
                }
            }
        }

        private static long EstimateByteCount(BitmapSource bitmap)
        {
            int bytesPerPixel = Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);
            return (long)bitmap.PixelWidth * bitmap.PixelHeight * bytesPerPixel;
        }

        private static BitmapSource CreateCroppedBitmap(BitmapSource source, Int32Rect rect)
        {
            if (rect.X == 0 && rect.Y == 0 && rect.Width == source.PixelWidth && rect.Height == source.PixelHeight)
            {
                return source;
            }

            var cropped = new CroppedBitmap(source, rect);
            if (cropped.CanFreeze)
            {
                cropped.Freeze();
            }

            return cropped;
        }

        internal static Int32Rect ExpandToTileGrid(Int32Rect rect, int maxWidth, int maxHeight)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return rect;
            }

            int left = Math.Max(0, FloorToTile(rect.X));
            int top = Math.Max(0, FloorToTile(rect.Y));
            int right = Math.Min(maxWidth, CeilToTile(rect.X + rect.Width));
            int bottom = Math.Min(maxHeight, CeilToTile(rect.Y + rect.Height));

            int width = Math.Max(1, right - left);
            int height = Math.Max(1, bottom - top);
            return new Int32Rect(left, top, width, height);
        }

        internal static IReadOnlyList<Int32Rect> BuildPrefetchRects(Int32Rect rect, int maxWidth, int maxHeight, int radius)
        {
            if (radius <= 0 || rect.Width <= 0 || rect.Height <= 0)
            {
                return [];
            }

            var rects = new List<Int32Rect>();
            for (int distance = 1; distance <= radius; distance++)
            {
                int horizontalOffset = rect.Width * distance;
                int verticalOffset = rect.Height * distance;

                AddIfValid(rects, Shift(rect, horizontalOffset, 0, maxWidth, maxHeight));
                AddIfValid(rects, Shift(rect, -horizontalOffset, 0, maxWidth, maxHeight));
                AddIfValid(rects, Shift(rect, 0, verticalOffset, maxWidth, maxHeight));
                AddIfValid(rects, Shift(rect, 0, -verticalOffset, maxWidth, maxHeight));
            }

            return rects;
        }

        private static void AddIfValid(List<Int32Rect> rects, Int32Rect rect)
        {
            if (rect.Width > 0 && rect.Height > 0)
            {
                rects.Add(rect);
            }
        }

        private static Int32Rect Shift(Int32Rect rect, int deltaX, int deltaY, int maxWidth, int maxHeight)
        {
            int left = Math.Clamp(rect.X + deltaX, 0, Math.Max(0, maxWidth - 1));
            int top = Math.Clamp(rect.Y + deltaY, 0, Math.Max(0, maxHeight - 1));
            int right = Math.Min(maxWidth, left + rect.Width);
            int bottom = Math.Min(maxHeight, top + rect.Height);
            return new Int32Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        private static int FloorToTile(int value) => (value / TileSizePixels) * TileSizePixels;

        private static int CeilToTile(int value) => ((value + TileSizePixels - 1) / TileSizePixels) * TileSizePixels;

        private sealed class CacheEntry
        {
            public CacheEntry(BitmapSource bitmap, LinkedListNode<RenderTileCacheKey> node, long byteCount, long usageSequence)
            {
                Bitmap = bitmap;
                Node = node;
                ByteCount = byteCount;
                UsageSequence = usageSequence;
            }

            public BitmapSource Bitmap { get; }

            public LinkedListNode<RenderTileCacheKey> Node { get; }

            public long ByteCount { get; }

            public long UsageSequence { get; set; }
        }

        private readonly record struct RenderTileCacheKey(BitmapSource Source, Int32Rect Rect);
    }
}