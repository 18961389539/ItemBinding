using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImageViewer.Controls
{
    public sealed class ImageViewerRuntimeOptions : INotifyPropertyChanged
    {
        private bool _enableImagePyramid = true;
        private bool _autoSelectPyramidLevel = true;
        private bool _enableTiledRendering = true;
        private bool _prefetchAdjacentTiles = true;
        private int _tileCacheMaximumMegabytes = 128;
        private int _tilePrefetchRadius = 1;
        private int _imageLoadRetryCount = 2;
        private int _imageLoadRetryDelayMilliseconds = 250;
        private bool _enableAsyncAnalysis = true;
        private bool _pauseRealtimeHistogram;
        private bool _pauseRealtimeProfile;
        private bool _preferShaderPseudoColor = true;
        private bool _allowCpuPseudoColorFallback = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool EnableImagePyramid
        {
            get => _enableImagePyramid;
            set => SetProperty(ref _enableImagePyramid, value);
        }

        public bool AutoSelectPyramidLevel
        {
            get => _autoSelectPyramidLevel;
            set => SetProperty(ref _autoSelectPyramidLevel, value);
        }

        public bool EnableTiledRendering
        {
            get => _enableTiledRendering;
            set => SetProperty(ref _enableTiledRendering, value);
        }

        public bool PrefetchAdjacentTiles
        {
            get => _prefetchAdjacentTiles;
            set => SetProperty(ref _prefetchAdjacentTiles, value);
        }

        public int TileCacheMaximumMegabytes
        {
            get => _tileCacheMaximumMegabytes;
            set => SetProperty(ref _tileCacheMaximumMegabytes, Math.Max(1, value));
        }

        public int TilePrefetchRadius
        {
            get => _tilePrefetchRadius;
            set => SetProperty(ref _tilePrefetchRadius, Math.Max(0, value));
        }

        public int ImageLoadRetryCount
        {
            get => _imageLoadRetryCount;
            set => SetProperty(ref _imageLoadRetryCount, Math.Max(0, value));
        }

        public int ImageLoadRetryDelayMilliseconds
        {
            get => _imageLoadRetryDelayMilliseconds;
            set => SetProperty(ref _imageLoadRetryDelayMilliseconds, Math.Max(0, value));
        }

        public bool EnableAsyncAnalysis
        {
            get => _enableAsyncAnalysis;
            set => SetProperty(ref _enableAsyncAnalysis, value);
        }

        public bool PauseRealtimeHistogram
        {
            get => _pauseRealtimeHistogram;
            set => SetProperty(ref _pauseRealtimeHistogram, value);
        }

        public bool PauseRealtimeProfile
        {
            get => _pauseRealtimeProfile;
            set => SetProperty(ref _pauseRealtimeProfile, value);
        }

        public bool PreferShaderPseudoColor
        {
            get => _preferShaderPseudoColor;
            set => SetProperty(ref _preferShaderPseudoColor, value);
        }

        public bool AllowCpuPseudoColorFallback
        {
            get => _allowCpuPseudoColorFallback;
            set => SetProperty(ref _allowCpuPseudoColorFallback, value);
        }

        private bool SetProperty(ref bool storage, bool value, [CallerMemberName] string? propertyName = null)
        {
            if (storage == value)
            {
                return false;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private bool SetProperty(ref int storage, int value, [CallerMemberName] string? propertyName = null)
        {
            if (storage == value)
            {
                return false;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}