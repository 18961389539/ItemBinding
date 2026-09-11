using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Plugins
{
    public sealed partial class RoiPluginRegistry
    {
        private static class RegionCatalog
        {
            public static IReadOnlyList<IBuiltInPluginRegistration> GetRegistrations()
            {
                return
                [
                    CreateRegistration<RotatedRect>(
                    typeKey: "rotated-rect",
                    hitTestOrder: 60,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolRotatedRect"), viewer => viewer.StartRoiMode(), 10, CreateRectangleIcon)
                    ],
                    persistence: CreateCenterWidthHeightAnglePersistence(
                        data => new RotatedRect
                        {
                            Center = data.Geometry.Center.ToPoint(),
                            Width = data.Geometry.Width,
                            Height = data.Geometry.Height,
                            Angle = data.Geometry.Angle
                        },
                        static roi => roi.Center,
                        static roi => roi.Width,
                        static roi => roi.Height,
                        static roi => roi.Angle)),

                    CreateRegistration<BlobAnalysisRoi>(
                    typeKey: "blob-analysis",
                    hitTestOrder: 55,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolBlobAnalysis"), viewer => viewer.StartBlobAnalysisMode(), 15, CreateBlobIcon)
                    ],
                    persistence: CreateCenterWidthHeightAnglePersistence(
                        data => new BlobAnalysisRoi
                        {
                            Center = data.Geometry.Center.ToPoint(),
                            Width = data.Geometry.Width,
                            Height = data.Geometry.Height,
                            Angle = data.Geometry.Angle,
                            UseOtsu = data.Options.UseOtsu,
                            ManualThreshold = data.Options.ManualThreshold,
                            DetectDark = data.Options.DetectDark,
                            MinArea = data.Options.MinArea
                        },
                        static roi => roi.Center,
                        static roi => roi.Width,
                        static roi => roi.Height,
                        static roi => roi.Angle,
                        static (roi, data) =>
                        {
                            data.Options.UseOtsu = roi.UseOtsu;
                            data.Options.ManualThreshold = roi.ManualThreshold;
                            data.Options.DetectDark = roi.DetectDark;
                            data.Options.MinArea = roi.MinArea;
                        })),

                    CreateRegistration<EllipseRoi>(
                    typeKey: "ellipse",
                    hitTestOrder: 90,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolEllipse"), viewer => viewer.StartEllipseRoiMode(), 20, CreateEllipseIcon)
                    ],
                    persistence: CreateCenterEllipsePersistence(
                        data => new EllipseRoi
                        {
                            Center = data.Geometry.Center.ToPoint(),
                            RadiusX = data.Geometry.RadiusX,
                            RadiusY = data.Geometry.RadiusY,
                            Angle = data.Geometry.Angle
                        },
                        static roi => roi.Center,
                        static roi => roi.RadiusX,
                        static roi => roi.RadiusY,
                        static roi => roi.Angle)),

                    CreateRegistration<FittedEllipseRoi>(
                    typeKey: "fitted-ellipse",
                    hitTestOrder: 89,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolFittedEllipse"), viewer => viewer.StartFitEllipseMode(), 25, CreateFittedEllipseIcon, isMeasurement: true)
                    ],
                    persistence: CreateCenterEllipsePersistence(
                        data => new FittedEllipseRoi
                        {
                            Center = data.Geometry.Center.ToPoint(),
                            RadiusX = data.Geometry.RadiusX,
                            RadiusY = data.Geometry.RadiusY,
                            Angle = data.Geometry.Angle,
                            SourcePointCount = (int)Math.Round(data.Geometry.Width)
                        },
                        static roi => roi.Center,
                        static roi => roi.RadiusX,
                        static roi => roi.RadiusY,
                        static roi => roi.Angle,
                        static (roi, data) => data.Geometry.Width = roi.SourcePointCount)),

                    CreateRegistration<PolygonRoi>(
                    typeKey: "polygon",
                    hitTestOrder: 100,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolPolygon"), viewer => viewer.StartPolygonRoiMode(), 30, CreatePolygonIcon)
                    ],
                    persistence: CreatePointsPersistence(
                        data => new PolygonRoi
                        {
                            Points = new ObservableCollection<Point>((data.Geometry.Points ?? []).Select(point => point.ToPoint())),
                            IsClosed = data.Options.IsClosed
                        },
                        static roi => roi.Points,
                        static (roi, data) => data.Options.IsClosed = roi.IsClosed)),

                    CreateRegistration<RingRoi>(
                    typeKey: "ring",
                    hitTestOrder: 79,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolRing"), viewer => viewer.StartRingRoiMode(), 45, CreateRingIcon)
                    ],
                    persistence: CreateCenterRadiusPairPersistence(
                        data => new RingRoi
                        {
                            Center = data.Geometry.Center.ToPoint(),
                            OuterRadius = data.Geometry.Radius,
                            InnerRadius = data.Geometry.Radius2
                        },
                        static roi => roi.Center,
                        static roi => roi.OuterRadius,
                        static roi => roi.InnerRadius)),

                    CreateRegistration<CircleRoi>(
                    typeKey: "circle",
                    hitTestOrder: 80,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolCircle"), viewer => viewer.StartCircleRoiMode(), 40, CreateCircleIcon)
                    ],
                    persistence: CreateCenterRadiusPersistence(
                        data => new CircleRoi
                        {
                            Center = data.Geometry.Center.ToPoint(),
                            Radius = data.Geometry.Radius
                        },
                        static roi => roi.Center,
                        static roi => roi.Radius)),

                    CreateRegistration<PolylineRoi>(
                    typeKey: "polyline",
                    hitTestOrder: 50,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolPolyline"), viewer => viewer.StartPolylineRoiMode(false), 50, CreatePolylineIcon),
                        new RoiToolDescriptor(UiText.Get("ToolFreehand"), viewer => viewer.StartPolylineRoiMode(true), 60, CreateFreehandIcon)
                    ],
                    persistence: CreatePointsPersistence(
                        data => new PolylineRoi
                        {
                            Points = new ObservableCollection<Point>((data.Geometry.Points ?? []).Select(point => point.ToPoint())),
                            IsFreehand = data.Options.IsFreehand
                        },
                        static roi => roi.Points,
                        static (roi, data) => data.Options.IsFreehand = roi.IsFreehand))
                ];
            }
        }
    }
}