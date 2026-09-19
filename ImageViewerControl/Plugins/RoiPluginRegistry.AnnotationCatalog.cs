using System.Collections.Generic;
using ImageViewer.Localization;
using ImageViewer.Models;

namespace ImageViewer.Plugins
{
    public sealed partial class RoiPluginRegistry
    {
        private static class AnnotationCatalog
        {
            public static IReadOnlyList<IBuiltInPluginRegistration> GetRegistrations()
            {
                return
                [
                    CreateRegistration<ArrowAnnotationRoi>(
                    typeKey: "arrow-annotation",
                    hitTestOrder: 29,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolArrowAnnotation"), viewer => viewer.StartArrowAnnotationMode(), 101, CreateArrowAnnotationIcon)
                    ],
                    persistence: CreatePointPairPersistence(
                        data => new ArrowAnnotationRoi
                        {
                            P1 = data.Geometry.P1.ToPoint(),
                            P2 = data.Geometry.P2.ToPoint(),
                            ArrowHeadLength = data.Geometry.Width > 0 ? data.Geometry.Width : 12
                        },
                        static roi => roi.P1,
                        static roi => roi.P2,
                        static (roi, data) => data.Geometry.Width = roi.ArrowHeadLength)),

                    CreateRegistration<PointAnnotationRoi>(
                    typeKey: "point-annotation",
                    hitTestOrder: 40,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolPointAnnotation"), viewer => viewer.StartPointAnnotationMode(), 100, CreatePointAnnotationIcon)
                    ],
                    persistence: CreatePositionPersistence(
                        data => new PointAnnotationRoi
                        {
                            Position = data.Geometry.Position.ToPoint()
                        },
                        static roi => roi.Position)),

                    // 2026-09-19: 抓取点十字准星——供配方页「示教」拖动设置抓取点。
                    // drawingTools 为空数组：不进入工具菜单，由宿主（RecipeViewModel）经
                    // ImageViewer.ViewerState.AddRoi 直接加/删。hitTestOrder 高于点标注，命中优先。
                    CreateRegistration<GrabPointRoi>(
                    typeKey: "grab-point",
                    hitTestOrder: 45,
                    drawingTools: [],
                    persistence: CreatePositionPersistence(
                        data => new GrabPointRoi
                        {
                            Position = data.Geometry.Position.ToPoint()
                        },
                        static roi => roi.Position)),

                    CreateRegistration<TextAnnotationRoi>(
                    typeKey: "text-annotation",
                    hitTestOrder: 30,
                    drawingTools:
                    [
                        new RoiToolDescriptor(UiText.Get("ToolTextAnnotation"), viewer => viewer.StartTextAnnotationMode(), 110, CreateTextIcon)
                    ],
                    persistence: CreatePositionPersistence(
                        data => new TextAnnotationRoi
                        {
                            Position = data.Geometry.Position.ToPoint()
                        },
                        static roi => roi.Position))
                ];
            }
        }
    }
}