using System;
using System.Runtime.CompilerServices;

namespace ImageViewer.Models
{
    internal static class RoiVisualStateStore
    {
        private static readonly ConditionalWeakTable<RoiBase, RoiVisualState> States = new();

        public static RoiVisualState GetOrCreate(RoiBase roi)
        {
            ArgumentNullException.ThrowIfNull(roi);
            return States.GetValue(roi, static _ => new RoiVisualState());
        }
    }
}