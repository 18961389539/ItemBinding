namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        public ImageViewerRuntimeOptions RuntimeOptions { get; } = new();

        public bool EnableImagePyramid
        {
            get => RuntimeOptions.EnableImagePyramid;
            set => RuntimeOptions.EnableImagePyramid = value;
        }

        public bool AutoSelectPyramidLevel
        {
            get => RuntimeOptions.AutoSelectPyramidLevel;
            set => RuntimeOptions.AutoSelectPyramidLevel = value;
        }

        public bool EnableTiledRendering
        {
            get => RuntimeOptions.EnableTiledRendering;
            set => RuntimeOptions.EnableTiledRendering = value;
        }

        public bool PrefetchAdjacentTiles
        {
            get => RuntimeOptions.PrefetchAdjacentTiles;
            set => RuntimeOptions.PrefetchAdjacentTiles = value;
        }

        public int TileCacheMaximumMegabytes
        {
            get => RuntimeOptions.TileCacheMaximumMegabytes;
            set => RuntimeOptions.TileCacheMaximumMegabytes = value;
        }

        public int TilePrefetchRadius
        {
            get => RuntimeOptions.TilePrefetchRadius;
            set => RuntimeOptions.TilePrefetchRadius = value;
        }

        public int ImageLoadRetryCount
        {
            get => RuntimeOptions.ImageLoadRetryCount;
            set => RuntimeOptions.ImageLoadRetryCount = value;
        }

        public int ImageLoadRetryDelayMilliseconds
        {
            get => RuntimeOptions.ImageLoadRetryDelayMilliseconds;
            set => RuntimeOptions.ImageLoadRetryDelayMilliseconds = value;
        }

        public bool EnableAsyncAnalysis
        {
            get => RuntimeOptions.EnableAsyncAnalysis;
            set => RuntimeOptions.EnableAsyncAnalysis = value;
        }

        public bool PauseRealtimeHistogram
        {
            get => RuntimeOptions.PauseRealtimeHistogram;
            set => RuntimeOptions.PauseRealtimeHistogram = value;
        }

        public bool PauseRealtimeProfile
        {
            get => RuntimeOptions.PauseRealtimeProfile;
            set => RuntimeOptions.PauseRealtimeProfile = value;
        }

        public bool PreferShaderPseudoColor
        {
            get => RuntimeOptions.PreferShaderPseudoColor;
            set => RuntimeOptions.PreferShaderPseudoColor = value;
        }

        public bool AllowCpuPseudoColorFallback
        {
            get => RuntimeOptions.AllowCpuPseudoColorFallback;
            set => RuntimeOptions.AllowCpuPseudoColorFallback = value;
        }
    }
}