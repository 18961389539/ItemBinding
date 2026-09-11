using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal interface IImageViewerRoiPersistenceControllerHost
    {
        string? ShowSaveRoiDialog();

        string? ShowOpenRoiDialog();

        RoiPluginRegistry PluginRegistry { get; }

        IReadOnlyList<RoiBase> AllRois { get; }

        double PixelSize { get; set; }

        string PhysicalUnit { get; set; }

        void ReplaceAllRois(IReadOnlyList<RoiBase> rois);

        void RefreshAllCaliperDetections();

        void DrawRois();

        void RefreshSelectedRoiPropertyPanel();

        void ShowNonCriticalError(string title, string message, Exception ex);
    }

    internal sealed class ImageViewerRoiPersistenceController
    {
        private readonly IImageViewerRoiPersistenceControllerHost _host;

        public ImageViewerRoiPersistenceController(IImageViewerRoiPersistenceControllerHost host)
        {
            _host = host;
        }

        public async Task SaveRoisAsync()
        {
            string? filePath = _host.ShowSaveRoiDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                await RoiPersistenceService.SaveToFileAsync(filePath, _host.AllRois, _host.PixelSize, _host.PhysicalUnit, _host.PluginRegistry);
            }
            catch (Exception ex)
            {
                _host.ShowNonCriticalError(UiText.Get("ErrorSaveRoiTitle"), UiText.Get("ErrorSaveRoiMessage"), ex);
            }
        }

        public async Task LoadRoisAsync()
        {
            string? filePath = _host.ShowOpenRoiDialog();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            try
            {
                var result = await RoiPersistenceService.LoadFromFileAsync(filePath, _host.PluginRegistry);
                _host.ReplaceAllRois(result.Rois);
                _host.PixelSize = result.PixelSize;
                _host.PhysicalUnit = result.PhysicalUnit;
                _host.RefreshAllCaliperDetections();
                _host.DrawRois();
                _host.RefreshSelectedRoiPropertyPanel();
            }
            catch (Exception ex)
            {
                _host.ShowNonCriticalError(UiText.Get("ErrorLoadRoiTitle"), UiText.Get("ErrorLoadRoiMessage"), ex);
            }
        }
    }
}