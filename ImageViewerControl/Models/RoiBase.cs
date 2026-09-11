using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ImageViewer.Common;
using ImageViewer.Localization;

namespace ImageViewer.Models
{
    /// <summary>
    /// ROI 基类
    /// Chinese: 表示所有 ROI（感兴趣区域）对象的基类，包含共享属性如标签、边框颜色、粗细、可见性等。
    /// English: Base class for all ROI (Region of Interest) objects. Provides common properties such as Label,
    /// StrokeColor, StrokeThickness, visibility and locking.
    /// </summary>
    public abstract class RoiBase : BaseViewModel
    {
        private string _label = string.Empty;

        private RoiVisualState VisualState => RoiVisualStateStore.GetOrCreate(this);

        public string Label
        {
            get => _label;
            set
            {
                if (SetProperty(ref _label, value))
                {
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public virtual string RoiTypeName => GetType().Name;

        public string DisplayTypeName => RoiDisplayNameLocalizer.GetDisplayName(this);

        public string DisplayName => string.IsNullOrWhiteSpace(Label) ? DisplayTypeName : $"{DisplayTypeName}: {Label}";

        protected void ApplyCommonState(RoiBase source)
        {
            Label = source.Label;
            RoiVisualState.Capture(source).ApplyTo(this, includeSelection: false);
        }

        public Color StrokeColor
        {
            get => VisualState.StrokeColor;
            set => SetVisualStateValue(VisualState.StrokeColor, value, static (state, next) => state.StrokeColor = next);
        }

        public double StrokeThickness
        {
            get => VisualState.StrokeThickness;
            set => SetVisualStateValue(VisualState.StrokeThickness, value, static (state, next) => state.StrokeThickness = next);
        }

        public bool IsSelected
        {
            get => VisualState.IsSelected;
            set => SetVisualStateValue(VisualState.IsSelected, value, static (state, next) => state.IsSelected = next);
        }

        public bool IsVisible
        {
            get => VisualState.IsVisible;
            set => SetVisualStateValue(VisualState.IsVisible, value, static (state, next) => state.IsVisible = next);
        }

        public bool IsLocked
        {
            get => VisualState.IsLocked;
            set => SetVisualStateValue(VisualState.IsLocked, value, static (state, next) => state.IsLocked = next);
        }

        private void SetVisualStateValue<T>(T currentValue, T newValue, Action<RoiVisualState, T> assign, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
            {
                return;
            }

            assign(VisualState, newValue);
            OnPropertyChanged(propertyName);
        }

        public abstract RoiBase Clone();
        public abstract void ApplyFrom(RoiBase source);
        /// <summary>
        /// 克隆当前 ROI 的方法（深拷贝或值拷贝依具体实现而定）。
        /// Chinese: 返回一个表示当前对象状态的副本，供撤销/重做与状态保存使用。
        /// English: Creates and returns a copy of the ROI instance for undo/redo or state snapshots.
        /// </summary>
        /// <returns>返回 RoiBase 的副本 / A copy of the RoiBase instance.</returns>
    }
}
