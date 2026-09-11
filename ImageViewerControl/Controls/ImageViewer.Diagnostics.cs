using System;
using ImageViewer.Models;
using ImageViewer.ViewModels;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void LogNonCriticalError(string context, Exception ex)
        {
            Logger.LogError(context, ex);
        }

        private void ShowNonCriticalError(string title, string message, Exception ex)
        {
            LogNonCriticalError(title, ex);
            _dialogWorkflowService.ShowWarning(title, message);
        }

        private static RoiStateCommand CreateStateCommand(RoiBase roi, RoiBase oldState, RoiBase newState)
        {
            return new RoiStateCommand(roi, oldState, newState);
        }
    }
}