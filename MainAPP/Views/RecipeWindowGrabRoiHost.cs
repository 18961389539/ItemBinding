using ImageViewer.Models;

namespace MainAPP.Views
{
    /// <summary>
    /// 示教十字 ROI ↔ ImageViewer 控件桥接（2026-09-19）。
    /// RecipeViewModel 不持有控件引用，通过本静态宿主把 GrabPointRoi 加入/移出当前配方窗口的
    /// ImageViewer（一次仅一个配方窗口打开）。窗口打开时 Register，关闭时 Unregister。
    /// </summary>
    internal static class RecipeWindowGrabRoiHost
    {
        private static ImageViewer.Controls.ImageViewer? s_viewer;

        /// <summary>配方窗口加载完成时注册 ImageViewer 实例。</summary>
        public static void Register(ImageViewer.Controls.ImageViewer viewer)
        {
            s_viewer = viewer;
        }

        /// <summary>配方窗口关闭/卸载时注销。</summary>
        public static void Unregister(ImageViewer.Controls.ImageViewer viewer)
        {
            if (ReferenceEquals(s_viewer, viewer))
            {
                s_viewer = null;
            }
        }

        public static void Attach(GrabPointRoi roi)
        {
            s_viewer?.ViewerState.AddRoi(roi);
        }

        public static void Detach(GrabPointRoi roi)
        {
            if (s_viewer is null)
            {
                return;
            }
            // 若十字正被选中，先清除选中态，避免 ROI 移除后 SelectedRoi 悬空
            if (ReferenceEquals(s_viewer.ViewerState.SelectedRoi, roi))
            {
                s_viewer.ViewerState.SelectedRoi = null;
            }
            s_viewer.ViewerState.RemoveRoi(roi);
        }
    }
}