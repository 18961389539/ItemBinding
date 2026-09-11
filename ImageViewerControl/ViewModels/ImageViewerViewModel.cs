using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ImageViewer.Abstractions;
using ImageViewer.Common;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;

namespace ImageViewer.ViewModels
{
    public partial class ImageViewerViewModel : BaseViewModel
    {
        /// <summary>
        /// 图像查看器视图模型
        /// Chinese: 封装 ImageViewer 控件的状态与数据，如 ROI 集合、缩放、偏移与显示选项。
        /// English: ViewModel containing state for the ImageViewer control such as ROIs, scale, offsets and display flags.
        /// </summary>

        private ImageSource? _imageSource;
        private readonly Dictionary<Type, object> _roiCollections = new();
        private double _scale = 1.0;
        private double _offsetX;
        private double _offsetY;
        private bool _showPixelGrid;
        private bool _showCrosshair;
        private bool _showInfoPanel;
        private string _infoText = string.Empty;
        private RoiBase? _selectedRoi;
        private readonly UndoRedoManager _undoRedoManager = new UndoRedoManager();
        private readonly ISelectedRoiDetectionService _selectedRoiDetectionService;
        private RoiPluginRegistry _pluginRegistry;
        private bool _isRebuildingAllRois;
        private bool _isSynchronizingAllRois;

        /// <summary>
        /// 尝试对当前选中的直线查找卡尺 ROI 执行边缘检测。
        /// Chinese: Attempts to detect the true line for the currently selected line-caliper ROI.
        /// </summary>
        /// <param name="result">如果检测成功，返回检测结果。</param>
        /// <returns>检测成功返回 true，否则 false。</returns>
        public bool TryDetectSelectedLineCaliperEdges(out LineCaliperDetectionResult result)
        {
            return _selectedRoiDetectionService.TryDetectSelectedLineCaliperEdges(ImageSource, SelectedRoi, out result);
        }

        public UndoRedoManager UndoRedo => _undoRedoManager;

        public ImageViewerViewModel(RoiPluginRegistry? pluginRegistry = null, ISelectedRoiDetectionService? selectedRoiDetectionService = null)
        {
            _selectedRoiDetectionService = selectedRoiDetectionService ?? SelectedRoiDetectionService.Default;
            _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
            RebuildAllRois();
        }

        public RoiPluginRegistry PluginRegistry
        {
            get => _pluginRegistry;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (ReferenceEquals(_pluginRegistry, value))
                {
                    return;
                }

                _pluginRegistry = value;
                RebuildAllRois();
            }
        }

        public ImageSource? ImageSource
        {
            get => _imageSource;
            set => SetProperty(ref _imageSource, value);
        }

        public RoiBase? SelectedRoi
        {
            get => _selectedRoi;
            set
            {
                if (ReferenceEquals(_selectedRoi, value))
                {
                    return;
                }

                if (_selectedRoi != null)
                {
                    _selectedRoi.IsSelected = false;
                }

                _selectedRoi = value;

                if (_selectedRoi != null)
                {
                    _selectedRoi.IsSelected = true;
                }

                OnPropertyChanged();
            }
        }

        public double Scale
        {
            get => _scale;
            set => SetProperty(ref _scale, value);
        }

        public double OffsetX
        {
            get => _offsetX;
            set => SetProperty(ref _offsetX, value);
        }

        public double OffsetY
        {
            get => _offsetY;
            set => SetProperty(ref _offsetY, value);
        }

        public bool ShowPixelGrid
        {
            get => _showPixelGrid;
            set => SetProperty(ref _showPixelGrid, value);
        }

        public bool ShowCrosshair
        {
            get => _showCrosshair;
            set => SetProperty(ref _showCrosshair, value);
        }

        public bool ShowInfoPanel
        {
            get => _showInfoPanel;
            set => SetProperty(ref _showInfoPanel, value);
        }

        public string InfoText
        {
            get => _infoText;
            set => SetProperty(ref _infoText, value);
        }

        public ObservableCollection<RoiBase> AllRois { get; } = new ObservableCollection<RoiBase>();

        public ObservableCollection<T> GetRoiCollection<T>() where T : RoiBase
        {
            if (_roiCollections.TryGetValue(typeof(T), out var existingCollection))
            {
                return (ObservableCollection<T>)existingCollection;
            }

            var collection = new ObservableCollection<T>();
            AttachCollection(collection);
            _roiCollections.Add(typeof(T), collection);
            return collection;
        }

        public bool AddRoi(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            return AddRoiToTypedCollection(roi);
        }

        public bool RemoveRoi(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            return RemoveRoiFromTypedCollection(roi);
        }

        public void ClearAllRois()
        {
            _isRebuildingAllRois = true;
            try
            {
                ClearTypedCollections();
                SelectedRoi = null;
            }
            finally
            {
                _isRebuildingAllRois = false;
            }

            RebuildAllRois();
        }

        public void ReplaceAllRois(IEnumerable<RoiBase> rois)
        {
            _isRebuildingAllRois = true;
            try
            {
                ClearTypedCollections();

                foreach (var roi in rois)
                {
                    AddRoiToTypedCollection(roi);
                }

                SelectedRoi = null;
            }
            finally
            {
                _isRebuildingAllRois = false;
            }

            RebuildAllRois();
        }

        private void AttachCollection<T>(ObservableCollection<T> collection) where T : RoiBase
        {
            collection.CollectionChanged += OnRoiCollectionChanged;
        }

        private void OnRoiCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isRebuildingAllRois || _isSynchronizingAllRois)
            {
                return;
            }

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    AppendAllRois(e.NewItems);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    RemoveAllRois(e.OldItems);
                    break;
                default:
                    RebuildAllRois();
                    break;
            }
        }

        private void RebuildAllRois(IEnumerable<RoiBase>? orderedRois = null)
        {
            _isSynchronizingAllRois = true;
            try
            {
                AllRois.Clear();
                foreach (var roi in orderedRois ?? EnumerateRois())
                {
                    AllRois.Add(roi);
                }
            }
            finally
            {
                _isSynchronizingAllRois = false;
            }
        }

        private void AppendAllRois(System.Collections.IList? items)
        {
            if (items == null)
            {
                return;
            }

            _isSynchronizingAllRois = true;
            try
            {
                foreach (var item in items)
                {
                    if (item is RoiBase roi && !AllRois.Contains(roi))
                    {
                        AllRois.Add(roi);
                    }
                }
            }
            finally
            {
                _isSynchronizingAllRois = false;
            }
        }

        private void RemoveAllRois(System.Collections.IList? items)
        {
            if (items == null)
            {
                return;
            }

            _isSynchronizingAllRois = true;
            try
            {
                foreach (var item in items)
                {
                    if (item is RoiBase roi)
                    {
                        AllRois.Remove(roi);
                    }
                }
            }
            finally
            {
                _isSynchronizingAllRois = false;
            }
        }

        private IEnumerable<RoiBase> EnumerateRois()
        {
            foreach (var plugin in PluginRegistry.Plugins)
            {
                foreach (var roi in plugin.GetRois(this))
                {
                    yield return roi;
                }
            }
        }

        private void ClearTypedCollections()
        {
            foreach (var plugin in PluginRegistry.Plugins)
            {
                plugin.ClearCollection(this);
            }
        }

        private bool AddRoiToTypedCollection(RoiBase roi)
        {
            return PluginRegistry.FindByRoi(roi)?.AddToCollection(this, roi) == true;
        }

        private bool RemoveRoiFromTypedCollection(RoiBase roi)
        {
            return PluginRegistry.FindByRoi(roi)?.RemoveFromCollection(this, roi) == true;
        }
    }
}
