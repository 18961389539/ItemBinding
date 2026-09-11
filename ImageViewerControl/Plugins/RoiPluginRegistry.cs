using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ImageViewer.Abstractions;
using ImageViewer.Controls;
using ImageViewer.Models;
using ImageViewer.Rendering;
using ImageViewer.Services;
using ImageViewer.ViewModels;

namespace ImageViewer.Plugins
{
    public sealed partial class RoiPluginRegistry
    {
        private const string ThirdPartyDiamondToolMarker = "第三方菱形";
        private readonly List<IRoiPlugin> _plugins = new();
        private readonly Dictionary<Type, IRoiPlugin> _pluginsByType = new();
        private readonly Dictionary<string, IRoiPlugin> _pluginsByTypeKey = new(StringComparer.OrdinalIgnoreCase);

        [Obsolete("Prefer passing an explicit registry instance.")]
        public static RoiPluginRegistry Default { get; } = CreateBuiltIn();

        public IReadOnlyList<IRoiPlugin> Plugins => _plugins;

        public IReadOnlyCollection<string> RegisteredTypeKeys => _pluginsByTypeKey.Keys;

        public void Register(IRoiPlugin plugin)
        {
            ArgumentNullException.ThrowIfNull(plugin);

            bool containsFilteredDrawingTool = ContainsFilteredDrawingTool(plugin);
            if (containsFilteredDrawingTool && plugin.DrawingTools.Count > 0 && plugin.DrawingTools.All(tool => !ShouldExposeDrawingTool(tool)))
            {
                return;
            }

            plugin = FilterDrawingTools(plugin);

            if (_pluginsByType.ContainsKey(plugin.RoiType))
            {
                throw new InvalidOperationException($"ROI plugin for type '{plugin.RoiType.FullName}' is already registered.");
            }

            if (_pluginsByTypeKey.ContainsKey(plugin.TypeKey))
            {
                throw new InvalidOperationException($"ROI plugin with key '{plugin.TypeKey}' is already registered.");
            }

            _plugins.Add(plugin);
            _pluginsByType.Add(plugin.RoiType, plugin);
            _pluginsByTypeKey.Add(plugin.TypeKey, plugin);
        }

        public IRoiPlugin? FindByRoi(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            return FindByType(roi.GetType());
        }

        public IRoiPlugin? FindByType(Type roiType)
        {
            ArgumentNullException.ThrowIfNull(roiType);
            return _pluginsByType.TryGetValue(roiType, out var plugin) ? plugin : null;
        }

        public IRoiPlugin? FindByTypeKey(string typeKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
            return _pluginsByTypeKey.TryGetValue(typeKey, out var plugin) ? plugin : null;
        }

        public bool Unregister(string typeKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);

            if (!_pluginsByTypeKey.TryGetValue(typeKey, out var plugin))
            {
                return false;
            }

            _plugins.Remove(plugin);
            _pluginsByType.Remove(plugin.RoiType);
            _pluginsByTypeKey.Remove(typeKey);
            return true;
        }

        public IEnumerable<IRoiPlugin> GetPluginsInHitTestOrder()
        {
            return _plugins.OrderByDescending(plugin => plugin.HitTestOrder);
        }

        public IEnumerable<RoiToolDescriptor> GetDrawingTools()
        {
            return _plugins
                .SelectMany(plugin => plugin.DrawingTools)
                .Where(ShouldExposeDrawingTool)
                .OrderBy(static tool => tool.MenuOrder)
                .ThenBy(static tool => tool.Header, StringComparer.CurrentCultureIgnoreCase);
        }

        private static bool ShouldExposeDrawingTool(RoiToolDescriptor tool)
        {
            return tool.Header?.Contains(ThirdPartyDiamondToolMarker, StringComparison.Ordinal) != true;
        }

        private static bool ContainsFilteredDrawingTool(IRoiPlugin plugin)
        {
            return plugin.DrawingTools.Any(tool => !ShouldExposeDrawingTool(tool));
        }

        private static IRoiPlugin FilterDrawingTools(IRoiPlugin plugin)
        {
            if (!ContainsFilteredDrawingTool(plugin))
            {
                return plugin;
            }

            RoiToolDescriptor[] filteredTools = plugin.DrawingTools
                .Where(ShouldExposeDrawingTool)
                .ToArray();

            return filteredTools.Length == 0
                ? new FilteredRoiPlugin(plugin, [])
                : new FilteredRoiPlugin(plugin, filteredTools);
        }

        private sealed class FilteredRoiPlugin : IRoiPlugin
        {
            private readonly IRoiPlugin _inner;

            public FilteredRoiPlugin(IRoiPlugin inner, IReadOnlyList<RoiToolDescriptor> drawingTools)
            {
                _inner = inner;
                DrawingTools = drawingTools;
            }

            public string TypeKey => _inner.TypeKey;

            public Type RoiType => _inner.RoiType;

            public int HitTestOrder => _inner.HitTestOrder;

            public IReadOnlyList<RoiToolDescriptor> DrawingTools { get; }

            public IRoiBehavior Behavior => _inner.Behavior;

            public IRoiRenderer Renderer => _inner.Renderer;

            public IEnumerable<RoiBase> GetRois(ImageViewerViewModel viewModel) => _inner.GetRois(viewModel);

            public void ClearCollection(ImageViewerViewModel viewModel) => _inner.ClearCollection(viewModel);

            public bool AddToCollection(ImageViewerViewModel viewModel, RoiBase roi) => _inner.AddToCollection(viewModel, roi);

            public bool RemoveFromCollection(ImageViewerViewModel viewModel, RoiBase roi) => _inner.RemoveFromCollection(viewModel, roi);

            public RoiBase CreateRoi(RoiPersistenceData data) => _inner.CreateRoi(data);

            public void PopulatePersistenceData(RoiBase roi, RoiPersistenceData data) => _inner.PopulatePersistenceData(roi, data);

            public IReadOnlyList<string> BuildInfoLines(RoiBase roi, BitmapSource? bitmap, double pixelSize, string? physicalUnit)
                => _inner.BuildInfoLines(roi, bitmap, pixelSize, physicalUnit);

            public FrameworkElement? CreatePropertyEditor(RoiBase roi) => _inner.CreatePropertyEditor(roi);
        }

        private readonly struct BuiltInPluginFactory
        {
            private readonly IReadOnlyDictionary<Type, IRoiBehavior> _behaviors;
            private readonly IReadOnlyDictionary<Type, IRoiRenderer> _renderers;

            public BuiltInPluginFactory(IReadOnlyDictionary<Type, IRoiBehavior> behaviors, IReadOnlyDictionary<Type, IRoiRenderer> renderers)
            {
                _behaviors = behaviors ?? throw new ArgumentNullException(nameof(behaviors));
                _renderers = renderers ?? throw new ArgumentNullException(nameof(renderers));
            }

            public RoiPlugin<T> Create<T>(
                string typeKey,
                int hitTestOrder,
                IEnumerable<RoiToolDescriptor>? drawingTools,
                Func<RoiPersistenceData, T> createRoi,
                Action<T, RoiPersistenceData> populatePersistenceData,
                Func<T, BitmapSource?, double, string?, IEnumerable<string>>? buildInfoLines = null,
                Func<T, FrameworkElement?>? createPropertyEditor = null)
                where T : RoiBase
            {
                FrameworkElement? DefaultCreatePropertyEditor(T roi) => BuiltInRoiPropertyEditorFactory.CreateEditor(roi);

                Func<T, FrameworkElement?> resolvedPropertyEditor = createPropertyEditor ?? DefaultCreatePropertyEditor;

                return new RoiPlugin<T>(
                    typeKey,
                    hitTestOrder,
                    static vm => vm.GetRoiCollection<T>(),
                    drawingTools,
                    _behaviors[typeof(T)],
                    _renderers[typeof(T)],
                    createRoi,
                    populatePersistenceData,
                    buildInfoLines,
                    resolvedPropertyEditor);
            }
        }

        private interface IBuiltInPluginRegistration
        {
            void Register(RoiPluginRegistry registry, BuiltInPluginFactory pluginFactory);
        }

        private readonly struct RoiPersistenceDescriptor<T>
            where T : RoiBase
        {
            private readonly Func<RoiPersistenceData, T> _createRoi;
            private readonly Action<T, RoiPersistenceData> _populatePersistenceData;

            public RoiPersistenceDescriptor(Func<RoiPersistenceData, T> createRoi, Action<T, RoiPersistenceData> populatePersistenceData)
            {
                _createRoi = createRoi ?? throw new ArgumentNullException(nameof(createRoi));
                _populatePersistenceData = populatePersistenceData ?? throw new ArgumentNullException(nameof(populatePersistenceData));
            }

            public T CreateRoi(RoiPersistenceData data) => _createRoi(data);

            public void PopulatePersistenceData(T roi, RoiPersistenceData data) => _populatePersistenceData(roi, data);
        }

        private sealed class BuiltInPluginRegistration<T> : IBuiltInPluginRegistration
            where T : RoiBase
        {
            private readonly string _typeKey;
            private readonly int _hitTestOrder;
            private readonly IReadOnlyList<RoiToolDescriptor>? _drawingTools;
            private readonly RoiPersistenceDescriptor<T> _persistence;
            private readonly Func<T, BitmapSource?, double, string?, IEnumerable<string>>? _buildInfoLines;
            private readonly Func<T, FrameworkElement?>? _createPropertyEditor;

            public BuiltInPluginRegistration(
                string typeKey,
                int hitTestOrder,
                IReadOnlyList<RoiToolDescriptor>? drawingTools,
                RoiPersistenceDescriptor<T> persistence,
                Func<T, BitmapSource?, double, string?, IEnumerable<string>>? buildInfoLines = null,
                Func<T, FrameworkElement?>? createPropertyEditor = null)
            {
                _typeKey = typeKey;
                _hitTestOrder = hitTestOrder;
                _drawingTools = drawingTools;
                _persistence = persistence;
                _buildInfoLines = buildInfoLines;
                _createPropertyEditor = createPropertyEditor;
            }

            public void Register(RoiPluginRegistry registry, BuiltInPluginFactory pluginFactory)
            {
                var persistence = _persistence;

                registry.Register(pluginFactory.Create(
                    _typeKey,
                    _hitTestOrder,
                    _drawingTools,
                    persistence.CreateRoi,
                    persistence.PopulatePersistenceData,
                    _buildInfoLines,
                    _createPropertyEditor));
            }
        }

        private static RoiPersistenceDescriptor<T> CreatePersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Action<T, RoiPersistenceData> populatePersistenceData)
            where T : RoiBase
        {
            return new RoiPersistenceDescriptor<T>(createRoi, populatePersistenceData);
        }

        private static RoiPersistenceDescriptor<T> CreateCenterWidthHeightAnglePersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getCenter,
            Func<T, double> getWidth,
            Func<T, double> getHeight,
            Func<T, double> getAngle,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.Center = RoiPersistencePointExtensions.FromPoint(getCenter(roi));
                data.Geometry.Width = getWidth(roi);
                data.Geometry.Height = getHeight(roi);
                data.Geometry.Angle = getAngle(roi);
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreateCenterEllipsePersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getCenter,
            Func<T, double> getRadiusX,
            Func<T, double> getRadiusY,
            Func<T, double> getAngle,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.Center = RoiPersistencePointExtensions.FromPoint(getCenter(roi));
                data.Geometry.RadiusX = getRadiusX(roi);
                data.Geometry.RadiusY = getRadiusY(roi);
                data.Geometry.Angle = getAngle(roi);
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreateCenterRadiusPersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getCenter,
            Func<T, double> getRadius,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.Center = RoiPersistencePointExtensions.FromPoint(getCenter(roi));
                data.Geometry.Radius = getRadius(roi);
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreateCenterRadiusPairPersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getCenter,
            Func<T, double> getRadius,
            Func<T, double> getRadius2,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.Center = RoiPersistencePointExtensions.FromPoint(getCenter(roi));
                data.Geometry.Radius = getRadius(roi);
                data.Geometry.Radius2 = getRadius2(roi);
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreatePositionPersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getPosition,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.Position = RoiPersistencePointExtensions.FromPoint(getPosition(roi));
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreatePointPairPersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getP1,
            Func<T, Point> getP2,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.P1 = RoiPersistencePointExtensions.FromPoint(getP1(roi));
                data.Geometry.P2 = RoiPersistencePointExtensions.FromPoint(getP2(roi));
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreatePointTriplePersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getP1,
            Func<T, Point> getP2,
            Func<T, Point> getVertex,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.P1 = RoiPersistencePointExtensions.FromPoint(getP1(roi));
                data.Geometry.P2 = RoiPersistencePointExtensions.FromPoint(getP2(roi));
                data.Geometry.Vertex = RoiPersistencePointExtensions.FromPoint(getVertex(roi));
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreatePointQuadPersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getP1,
            Func<T, Point> getP2,
            Func<T, Point> getVertex,
            Func<T, Point> getP3,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.P1 = RoiPersistencePointExtensions.FromPoint(getP1(roi));
                data.Geometry.P2 = RoiPersistencePointExtensions.FromPoint(getP2(roi));
                data.Geometry.Vertex = RoiPersistencePointExtensions.FromPoint(getVertex(roi));
                data.Geometry.P3 = RoiPersistencePointExtensions.FromPoint(getP3(roi));
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreateLinePairPersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getLine1P1,
            Func<T, Point> getLine1P2,
            Func<T, Point> getLine2P1,
            Func<T, Point> getLine2P2,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePointQuadPersistence(
                createRoi,
                getLine1P1,
                getLine1P2,
                getLine2P1,
                getLine2P2,
                populateExtra);
        }

        private static RoiPersistenceDescriptor<T> CreatePointPairRadiusPairPersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, Point> getP1,
            Func<T, Point> getP2,
            Func<T, double> getRadius,
            Func<T, double> getRadius2,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePointPairPersistence(createRoi, getP1, getP2, (roi, data) =>
            {
                data.Radius = getRadius(roi);
                data.Radius2 = getRadius2(roi);
                populateExtra?.Invoke(roi, data);
            });
        }

        private static RoiPersistenceDescriptor<T> CreatePointsPersistence<T>(
            Func<RoiPersistenceData, T> createRoi,
            Func<T, IEnumerable<Point>> getPoints,
            Action<T, RoiPersistenceData>? populateExtra = null)
            where T : RoiBase
        {
            return CreatePersistence(createRoi, (roi, data) =>
            {
                data.Geometry.Points = getPoints(roi)
                    .Select(RoiPersistencePointExtensions.FromPoint)
                    .ToList();
                populateExtra?.Invoke(roi, data);
            });
        }

        private static void PopulateCaliperPersistenceData(
            RoiPersistenceData data,
            int caliperCount,
            int caliperSearchRange,
            int caliperSamplingHalfWidth,
            int minimumValidCalipers,
            double caliperMinimumGradient,
            double caliperOutlierThreshold,
            CaliperEdgePolarity caliperEdgePolarity)
        {
            data.Measurement.CaliperCount = caliperCount;
            data.Measurement.CaliperSearchRange = caliperSearchRange;
            data.Measurement.CaliperSamplingHalfWidth = caliperSamplingHalfWidth;
            data.Measurement.MinimumValidCalipers = minimumValidCalipers;
            data.Measurement.CaliperMinimumGradient = caliperMinimumGradient;
            data.Measurement.CaliperOutlierThreshold = caliperOutlierThreshold;
            data.Measurement.CaliperEdgePolarity = caliperEdgePolarity.ToString();
        }

        private static CaliperEdgePolarity ParseCaliperEdgePolarity(string? value)
        {
            return Enum.TryParse<CaliperEdgePolarity>(value, true, out var polarity)
                ? polarity
                : CaliperEdgePolarity.Any;
        }

        private static List<IBuiltInPluginRegistration> GetBuiltInRegistrations()
        {
            var registrations = new List<IBuiltInPluginRegistration>();

            registrations.AddRange(RegionCatalog.GetRegistrations());
            registrations.AddRange(AnnotationCatalog.GetRegistrations());
            registrations.AddRange(MeasurementCatalog.GetRegistrations());

            return registrations;
        }

        private static BuiltInPluginRegistration<T> CreateRegistration<T>(
            string typeKey,
            int hitTestOrder,
            IReadOnlyList<RoiToolDescriptor>? drawingTools,
            RoiPersistenceDescriptor<T> persistence,
            Func<T, BitmapSource?, double, string?, IEnumerable<string>>? buildInfoLines = null,
            Func<T, FrameworkElement?>? createPropertyEditor = null)
            where T : RoiBase
        {
            return new BuiltInPluginRegistration<T>(
                typeKey,
                hitTestOrder,
                drawingTools,
                persistence,
                buildInfoLines,
                createPropertyEditor);
        }

        public static RoiPluginRegistry CreateBuiltIn()
        {
            var registry = new RoiPluginRegistry();
            var behaviors = RoiInteractionService.CreateBuiltInBehaviorMap();
            var renderers = RoiRenderService.CreateBuiltInRendererMap();
            var pluginFactory = new BuiltInPluginFactory(behaviors, renderers);

            foreach (var registration in GetBuiltInRegistrations())
            {
                registration.Register(registry, pluginFactory);
            }

            return registry;
        }

        private static Rectangle CreateRectangleIcon() => new() { Width = 12, Height = 10, Stroke = Brushes.Black, StrokeThickness = 1 };

        private static Canvas CreateBlobIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Rectangle { Width = 10, Height = 10, Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(1) },
                new Ellipse { Width = 3, Height = 3, Fill = Brushes.Black, Margin = new Thickness(3) },
                new Ellipse { Width = 2, Height = 2, Fill = Brushes.Black, Margin = new Thickness(7, 6, 0, 0) }
            }
        };

        private static Ellipse CreateEllipseIcon() => new() { Width = 12, Height = 10, Stroke = Brushes.Black, StrokeThickness = 1 };

        private static Canvas CreateFittedEllipseIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Ellipse { Width = 10, Height = 8, Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(1, 2, 0, 0) },
                new Polyline { Points = new PointCollection { new(2, 9), new(4, 3), new(8, 2), new(10, 8) }, Stroke = Brushes.Black, StrokeThickness = 1 }
            }
        };

        private static Polyline CreatePolygonIcon() => new() { Points = new PointCollection { new(0, 10), new(5, 0), new(10, 10), new(0, 10) }, Stroke = Brushes.Black, StrokeThickness = 1 };

        private static Ellipse CreateCircleIcon() => new() { Width = 10, Height = 10, Stroke = Brushes.Black, StrokeThickness = 1 };

        private static Canvas CreateRingIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Ellipse { Width = 10, Height = 10, Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(1) },
                new Ellipse { Width = 4, Height = 4, Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(4) }
            }
        };

        private static Polyline CreatePolylineIcon() => new() { Points = new PointCollection { new(0, 10), new(4, 3), new(8, 8), new(12, 0) }, Stroke = Brushes.Black, StrokeThickness = 1 };

        private static Path CreateFreehandIcon() => new() { Data = Geometry.Parse("M 0,8 C 4,0 8,12 12,4"), Stroke = Brushes.Black, StrokeThickness = 1 };

        private static Ellipse CreatePointAnnotationIcon() => new() { Width = 4, Height = 4, Fill = Brushes.Black };

        private static TextBlock CreateTextIcon() => new() { Text = "T", FontWeight = FontWeights.Bold };

        private static Canvas CreateArrowAnnotationIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Line { X1 = 1, Y1 = 10, X2 = 10, Y2 = 3, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 10, Y1 = 3, X2 = 7, Y2 = 3, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 10, Y1 = 3, X2 = 9, Y2 = 6, Stroke = Brushes.Black, StrokeThickness = 1 }
            }
        };

        private static Canvas CreateLineMeasureIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Line { X1 = 1, Y1 = 10, X2 = 11, Y2 = 2, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 1, Y1 = 8, X2 = 1, Y2 = 11, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 11, Y1 = 1, X2 = 11, Y2 = 4, Stroke = Brushes.Black, StrokeThickness = 1 }
            }
        };

        private static Canvas CreateArcCaliperMeasureIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Path { Data = Geometry.Parse("M 1,9 A 5,5 0 0 1 11,9"), Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 3, Y1 = 8, X2 = 3, Y2 = 3, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 6, Y1 = 7, X2 = 6, Y2 = 2, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 9, Y1 = 8, X2 = 9, Y2 = 3, Stroke = Brushes.Black, StrokeThickness = 1 }
            }
        };

        private static Canvas CreateCircularCaliperMeasureIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Ellipse { Width = 8, Height = 8, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 6, Y1 = 0, X2 = 6, Y2 = 12, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 0, Y1 = 6, X2 = 12, Y2 = 6, Stroke = Brushes.Black, StrokeThickness = 1 }
            }
        };

        private static Canvas CreateCaliperMeasureIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Line { X1 = 2, Y1 = 10, X2 = 10, Y2 = 10, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 3, Y1 = 2, X2 = 3, Y2 = 10, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 6, Y1 = 4, X2 = 6, Y2 = 10, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 9, Y1 = 1, X2 = 9, Y2 = 10, Stroke = Brushes.Black, StrokeThickness = 1 }
            }
        };

        private static Canvas CreateLineCaliperMeasureIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Line { X1 = 1, Y1 = 9, X2 = 11, Y2 = 3, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 3, Y1 = 10, X2 = 1.5, Y2 = 7.5, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 4.5, Y1 = 9, X2 = 3, Y2 = 6.5, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 7.5, Y1 = 7, X2 = 6, Y2 = 4.5, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 10.5, Y1 = 5, X2 = 9, Y2 = 2.5, Stroke = Brushes.Black, StrokeThickness = 1 }
            }
        };

        private static Path CreateAngleMeasureIcon() => new() { Data = Geometry.Parse("M 0,10 L 10,10 L 10,0"), Stroke = Brushes.Black, StrokeThickness = 1 };

        private static Path CreateArcMeasureIcon() => new() { Data = Geometry.Parse("M 0,8 A 8,8 0 0 1 12,4"), Stroke = Brushes.Black, StrokeThickness = 1 };

        private static Canvas CreatePointToLineIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Line { X1 = 0, Y1 = 10, X2 = 12, Y2 = 10, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Ellipse { Width = 4, Height = 4, Fill = Brushes.Black, Margin = new Thickness(4, 2, 0, 0) },
                new Line { X1 = 6, Y1 = 4, X2 = 6, Y2 = 10, Stroke = Brushes.Black, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 1 } }
            }
        };

        private static Canvas CreatePointToCircleIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Ellipse { Width = 8, Height = 8, Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(2) },
                new Ellipse { Width = 3, Height = 3, Fill = Brushes.Black, Margin = new Thickness(1) },
                new Line { X1 = 2.5, Y1 = 2.5, X2 = 6, Y2 = 6, Stroke = Brushes.Black, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 1 } }
            }
        };

        private static Canvas CreateParallelismIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Line { X1 = 0, Y1 = 3, X2 = 12, Y2 = 3, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 0, Y1 = 9, X2 = 12, Y2 = 9, Stroke = Brushes.Black, StrokeThickness = 1 }
            }
        };

        private static Canvas CreatePerpendicularityIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Line { X1 = 0, Y1 = 10, X2 = 12, Y2 = 10, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Line { X1 = 6, Y1 = 0, X2 = 6, Y2 = 10, Stroke = Brushes.Black, StrokeThickness = 1 },
                new Rectangle { Width = 4, Height = 4, Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(6, 6, 0, 0) }
            }
        };

        private static Canvas CreateConcentricityIcon() => new()
        {
            Width = 12,
            Height = 12,
            Children =
            {
                new Ellipse { Width = 10, Height = 10, Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(1) },
                new Ellipse { Width = 6, Height = 6, Stroke = Brushes.Black, StrokeThickness = 1, Margin = new Thickness(3) },
                new Ellipse { Width = 2, Height = 2, Fill = Brushes.Black, Margin = new Thickness(5) }
            }
        };
    }

    internal static class RoiPersistencePointExtensions
    {
        public static RoiPersistencePoint FromPoint(Point point) => new() { X = point.X, Y = point.Y };

        public static Point ToPoint(this RoiPersistencePoint? point)
        {
            return point == null ? default : new Point(point.X, point.Y);
        }

        public static void ApplyCommonState(this RoiBase roi, RoiPersistenceData data)
        {
            roi.Label = data.Common.Label ?? string.Empty;
            roi.StrokeThickness = data.Common.StrokeThickness;
            roi.IsVisible = data.Common.IsVisible;
            roi.IsLocked = data.Common.IsLocked;

            if (!string.IsNullOrWhiteSpace(data.Common.StrokeColor))
            {
                roi.StrokeColor = (Color)ColorConverter.ConvertFromString(data.Common.StrokeColor);
            }
        }

        public static void PopulateCommonState(this RoiPersistenceData data, RoiBase roi, string typeKey)
        {
            data.Type = typeKey;
            data.Common.Label = roi.Label;
            data.Common.StrokeColor = roi.StrokeColor.ToString(CultureInfo.InvariantCulture);
            data.Common.StrokeThickness = roi.StrokeThickness;
            data.Common.IsVisible = roi.IsVisible;
            data.Common.IsLocked = roi.IsLocked;
        }
    }
}
