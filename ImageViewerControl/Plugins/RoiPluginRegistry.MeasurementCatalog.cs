using System;
using System.Collections.Generic;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Plugins
{
    public sealed partial class RoiPluginRegistry
    {
        private static class MeasurementCatalog
        {
            public static IReadOnlyList<IBuiltInPluginRegistration> GetRegistrations()
            {
                return
                [
                    CreateRegistration<CircularCaliperMeasureRoi>(
                    typeKey: "circular-caliper-measure",
                    hitTestOrder: 78,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolCircularCaliper"), viewer => viewer.StartCircularCaliperMeasureMode(), 85, CreateCircularCaliperMeasureIcon, isMeasurement: true)
                    ],
                    persistence: CreateCenterRadiusPersistence(
                        data => new CircularCaliperMeasureRoi
                        {
                            Center = data.Geometry.Center.ToPoint(),
                            Radius = data.Geometry.Radius,
                            CaliperCount = data.Measurement.CaliperCount > 0 ? data.Measurement.CaliperCount : 16,
                            CaliperSearchRange = data.Measurement.CaliperSearchRange > 0 ? data.Measurement.CaliperSearchRange : 18,
                            CaliperSamplingHalfWidth = data.Measurement.CaliperSamplingHalfWidth,
                            MinimumValidCalipers = data.Measurement.MinimumValidCalipers > 0 ? data.Measurement.MinimumValidCalipers : 8,
                            CaliperMinimumGradient = data.Measurement.CaliperMinimumGradient > 0 ? data.Measurement.CaliperMinimumGradient : 8,
                            CaliperOutlierThreshold = data.Measurement.CaliperOutlierThreshold > 0 ? data.Measurement.CaliperOutlierThreshold : 2.5,
                            CaliperEdgePolarity = ParseCaliperEdgePolarity(data.Measurement.CaliperEdgePolarity)
                        },
                        static roi => roi.Center,
                        static roi => roi.Radius,
                        static (roi, data) => PopulateCaliperPersistenceData(
                            data,
                            roi.CaliperCount,
                            roi.CaliperSearchRange,
                            roi.CaliperSamplingHalfWidth,
                            roi.MinimumValidCalipers,
                            roi.CaliperMinimumGradient,
                            roi.CaliperOutlierThreshold,
                                roi.CaliperEdgePolarity))),

                            CreateRegistration<ArcCaliperMeasureRoi>(
                    typeKey: "arc-caliper-measure",
                    hitTestOrder: 77,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolArcCaliper"), viewer => viewer.StartArcCaliperMeasureMode(), 86, CreateArcCaliperMeasureIcon, isMeasurement: true)
                    ],
                    persistence: CreateCenterRadiusPersistence(
                        data => new ArcCaliperMeasureRoi
                        {
                            Center = data.Geometry.Center.ToPoint(),
                            Radius = data.Geometry.Radius,
                            StartAngle = data.Geometry.Angle,
                            SweepAngle = data.Geometry.Height == 0 ? 180 : data.Geometry.Height,
                            CaliperCount = data.Measurement.CaliperCount > 0 ? data.Measurement.CaliperCount : 16,
                            CaliperSearchRange = data.Measurement.CaliperSearchRange > 0 ? data.Measurement.CaliperSearchRange : 18,
                            CaliperSamplingHalfWidth = data.Measurement.CaliperSamplingHalfWidth,
                            MinimumValidCalipers = data.Measurement.MinimumValidCalipers > 0 ? data.Measurement.MinimumValidCalipers : 8,
                            CaliperMinimumGradient = data.Measurement.CaliperMinimumGradient > 0 ? data.Measurement.CaliperMinimumGradient : 8,
                            CaliperOutlierThreshold = data.Measurement.CaliperOutlierThreshold > 0 ? data.Measurement.CaliperOutlierThreshold : 2.5,
                            CaliperEdgePolarity = ParseCaliperEdgePolarity(data.Measurement.CaliperEdgePolarity)
                        },
                        static roi => roi.Center,
                        static roi => roi.Radius,
                        static (roi, data) =>
                        {
                            data.Geometry.Angle = roi.StartAngle;
                            data.Geometry.Height = roi.SweepAngle;
                            PopulateCaliperPersistenceData(
                                data,
                                roi.CaliperCount,
                                roi.CaliperSearchRange,
                                roi.CaliperSamplingHalfWidth,
                                roi.MinimumValidCalipers,
                                roi.CaliperMinimumGradient,
                                roi.CaliperOutlierThreshold,
                                roi.CaliperEdgePolarity);
                        })),

                    CreateRegistration<LineMeasureRoi>(
                    typeKey: "line-measure",
                    hitTestOrder: 20,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolLineMeasure"), viewer => viewer.StartLineMeasureMode(), 70, CreateLineMeasureIcon, isMeasurement: true)
                    ],
                    persistence: CreatePointPairPersistence(
                        data => new LineMeasureRoi
                        {
                            P1 = data.Geometry.P1.ToPoint(),
                            P2 = data.Geometry.P2.ToPoint()
                        },
                        static roi => roi.P1,
                        static roi => roi.P2)),

                    CreateRegistration<CaliperMeasureRoi>(
                    typeKey: "caliper-measure",
                    hitTestOrder: 25,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolCaliperMeasure"), viewer => viewer.StartCaliperMeasureMode(), 80, CreateCaliperMeasureIcon, isMeasurement: true)
                    ],
                    persistence: CreatePointPairPersistence(
                        data => new CaliperMeasureRoi
                        {
                            P1 = data.Geometry.P1.ToPoint(),
                            P2 = data.Geometry.P2.ToPoint(),
                            CaliperCenter = data.Geometry.Center.ToPoint(),
                            CaliperRegionLength = data.Geometry.Width,
                            CaliperSearchRange = data.Geometry.Height > 0 ? (int)Math.Round(data.Geometry.Height / 2) : 24,
                            CaliperAngleDegrees = data.Geometry.Angle,
                            HasExplicitCaliperRegion = data.Geometry.Center != null
                        },
                        static roi => roi.P1,
                        static roi => roi.P2,
                        static (roi, data) =>
                        {
                            data.Geometry.Center = RoiPersistencePointExtensions.FromPoint(roi.CaliperCenter);
                            data.Geometry.Width = roi.GetResolvedCaliperRegionLength();
                            data.Geometry.Height = roi.CaliperSearchRange * 2;
                            data.Geometry.Angle = roi.CaliperAngleDegrees;
                        }),
                    createPropertyEditor: static _ => null),

                    CreateRegistration<LineCaliperMeasureRoi>(
                    typeKey: "line-caliper-measure",
                    hitTestOrder: 24,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolLineCaliper"), viewer => viewer.StartLineCaliperMeasureMode(), 82, CreateLineCaliperMeasureIcon, isMeasurement: true)
                    ],
                    persistence: CreatePointPairPersistence(
                        data => new LineCaliperMeasureRoi
                        {
                            P1 = data.Geometry.P1.ToPoint(),
                            P2 = data.Geometry.P2.ToPoint(),
                            CaliperCount = data.Measurement.CaliperCount > 0 ? data.Measurement.CaliperCount : 16,
                            CaliperSearchRange = data.Measurement.CaliperSearchRange > 0 ? data.Measurement.CaliperSearchRange : 18,
                            CaliperSamplingHalfWidth = data.Measurement.CaliperSamplingHalfWidth,
                            MinimumValidCalipers = data.Measurement.MinimumValidCalipers > 0 ? data.Measurement.MinimumValidCalipers : 8,
                            CaliperMinimumGradient = data.Measurement.CaliperMinimumGradient > 0 ? data.Measurement.CaliperMinimumGradient : 8,
                            CaliperOutlierThreshold = data.Measurement.CaliperOutlierThreshold > 0 ? data.Measurement.CaliperOutlierThreshold : 2.5,
                            CaliperEdgePolarity = ParseCaliperEdgePolarity(data.Measurement.CaliperEdgePolarity)
                        },
                        static roi => roi.P1,
                        static roi => roi.P2,
                        static (roi, data) => PopulateCaliperPersistenceData(
                            data,
                            roi.CaliperCount,
                            roi.CaliperSearchRange,
                            roi.CaliperSamplingHalfWidth,
                            roi.MinimumValidCalipers,
                            roi.CaliperMinimumGradient,
                            roi.CaliperOutlierThreshold,
                            roi.CaliperEdgePolarity)),
                    createPropertyEditor: static _ => null),

                    CreateRegistration<AngleMeasureRoi>(
                    typeKey: "angle-measure",
                    hitTestOrder: 10,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolAngleMeasure"), viewer => viewer.StartAngleMeasureMode(), 90, CreateAngleMeasureIcon, isMeasurement: true)
                    ],
                    persistence: CreatePointTriplePersistence(
                        data => new AngleMeasureRoi
                        {
                            P1 = data.Geometry.P1.ToPoint(),
                            Vertex = data.Geometry.Vertex.ToPoint(),
                            P2 = data.Geometry.P2.ToPoint()
                        },
                        static roi => roi.P1,
                        static roi => roi.P2,
                        static roi => roi.Vertex)),

                    CreateRegistration<ArcMeasureRoi>(
                    typeKey: "arc-measure",
                    hitTestOrder: 8,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolArcMeasure"), viewer => viewer.StartArcMeasureMode(), 95, CreateArcMeasureIcon, isMeasurement: true)
                    ],
                    persistence: CreatePointTriplePersistence(
                        data => new ArcMeasureRoi
                        {
                            StartPoint = data.Geometry.P1.ToPoint(),
                            EndPoint = data.Geometry.P2.ToPoint(),
                            ArcPoint = data.Geometry.Vertex.ToPoint()
                        },
                        static roi => roi.StartPoint,
                        static roi => roi.EndPoint,
                        static roi => roi.ArcPoint)),

                    CreateRegistration<PointToLineDistanceRoi>(
                    typeKey: "point-to-line-distance",
                    hitTestOrder: 7,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolPointToLineDistance"), viewer => viewer.StartPointToLineMeasureMode(), 96, CreatePointToLineIcon, isMeasurement: true)
                    ],
                    persistence: CreatePointTriplePersistence(
                        data => new PointToLineDistanceRoi
                        {
                            Point = data.Geometry.P1.ToPoint(),
                            LineP1 = data.Geometry.P2.ToPoint(),
                            LineP2 = data.Geometry.Vertex.ToPoint()
                        },
                        static roi => roi.Point,
                        static roi => roi.LineP1,
                        static roi => roi.LineP2)),

                    CreateRegistration<PointToCircleDistanceRoi>(
                    typeKey: "point-to-circle-distance",
                    hitTestOrder: 6,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolPointToCircleDistance"), viewer => viewer.StartPointToCircleMeasureMode(), 97, CreatePointToCircleIcon, isMeasurement: true)
                    ],
                    persistence: CreatePointPairPersistence(
                        data => new PointToCircleDistanceRoi
                        {
                            Point = data.Geometry.P1.ToPoint(),
                            Center = data.Geometry.P2.ToPoint(),
                            Radius = data.Geometry.Radius
                        },
                        static roi => roi.Point,
                        static roi => roi.Center,
                        static (roi, data) => data.Geometry.Radius = roi.Radius)),

                    CreateRegistration<ParallelismMeasureRoi>(
                    typeKey: "parallelism-measure",
                    hitTestOrder: 5,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolParallelism"), viewer => viewer.StartParallelismMeasureMode(), 98, CreateParallelismIcon, isMeasurement: true)
                    ],
                    persistence: CreateLinePairPersistence(
                        data => new ParallelismMeasureRoi
                        {
                            Line1P1 = data.Geometry.P1.ToPoint(),
                            Line1P2 = data.Geometry.P2.ToPoint(),
                            Line2P1 = data.Geometry.Vertex.ToPoint(),
                            Line2P2 = data.Geometry.P3.ToPoint()
                        },
                        static roi => roi.Line1P1,
                        static roi => roi.Line1P2,
                        static roi => roi.Line2P1,
                        static roi => roi.Line2P2)),

                    CreateRegistration<PerpendicularityMeasureRoi>(
                    typeKey: "perpendicularity-measure",
                    hitTestOrder: 4,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolPerpendicularity"), viewer => viewer.StartPerpendicularityMeasureMode(), 99, CreatePerpendicularityIcon, isMeasurement: true)
                    ],
                    persistence: CreateLinePairPersistence(
                        data => new PerpendicularityMeasureRoi
                        {
                            Line1P1 = data.Geometry.P1.ToPoint(),
                            Line1P2 = data.Geometry.P2.ToPoint(),
                            Line2P1 = data.Geometry.Vertex.ToPoint(),
                            Line2P2 = data.Geometry.P3.ToPoint()
                        },
                        static roi => roi.Line1P1,
                        static roi => roi.Line1P2,
                        static roi => roi.Line2P1,
                        static roi => roi.Line2P2)),

                    CreateRegistration<ConcentricityMeasureRoi>(
                    typeKey: "concentricity-measure",
                    hitTestOrder: 3,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolConcentricity"), viewer => viewer.StartConcentricityMeasureMode(), 100, CreateConcentricityIcon, isMeasurement: true)
                    ],
                    persistence: CreatePointPairRadiusPairPersistence(
                        data => new ConcentricityMeasureRoi
                        {
                            Center1 = data.Geometry.P1.ToPoint(),
                            Radius1 = data.Geometry.Radius,
                            Center2 = data.Geometry.P2.ToPoint(),
                            Radius2 = data.Geometry.Radius2
                        },
                        static roi => roi.Center1,
                        static roi => roi.Center2,
                        static roi => roi.Radius1,
                        static roi => roi.Radius2))
                ];
            }
        }
    }
}