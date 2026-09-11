using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Services;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Effects;

namespace ImageViewer.Abstractions
{
    public interface IImageViewerRenderService
    {
        ImageSource? BuildDisplaySource(ImageSource? source, PseudoColorPalette palette);

        void ApplyGpuCaching(System.Windows.Controls.Canvas imageContainer, bool enableGpuRendering);

        void ClearTileCache();

        BitmapSource? GetAnalysisBitmap(ImageSource? source);

        Effect? CreatePseudoColorEffect(PseudoColorPalette palette);

        Task<IReadOnlyList<ImagePyramidLevel>> BuildPyramidAsync(BitmapSource? source, CancellationToken cancellationToken);

        ImageViewerRenderFrame BuildRenderFrame(BitmapSource? source, IReadOnlyList<ImagePyramidLevel>? pyramid, Size viewport, double scale, Point translation, PseudoColorPalette palette, bool enableTiledRendering, bool autoSelectPyramidLevel, bool prefetchAdjacentTiles, int tileCacheMaximumMegabytes, int tilePrefetchRadius);

        Task<int[]?> CreateHistogramAsync(BitmapSource? source, int binCount, CancellationToken cancellationToken);

        Task<byte[]?> CreateProfileAsync(ImageViewerAnalysisRequest request, CancellationToken cancellationToken);
    }
}
