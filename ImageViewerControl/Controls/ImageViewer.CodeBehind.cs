using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void InitializeEventHandlers()
        {
            rootGrid.MouseWheel += OnMouseWheel;
            rootGrid.MouseDown += OnMouseDown;
            rootGrid.MouseMove += OnMouseMove;
            rootGrid.MouseUp += OnMouseUp;
            rootGrid.MouseRightButtonDown += OnMouseRightButtonDown;
            rootGrid.LostMouseCapture += OnLostMouseCapture;
            rootGrid.DragOver += OnDragOver;
            rootGrid.Drop += OnDrop;
            KeyDown += OnKeyDown;
            rootGrid.SizeChanged += OnRootGridSizeChanged;
        }

        private void UnregisterEventHandlers()
        {
            rootGrid.MouseWheel -= OnMouseWheel;
            rootGrid.MouseDown -= OnMouseDown;
            rootGrid.MouseMove -= OnMouseMove;
            rootGrid.MouseUp -= OnMouseUp;
            rootGrid.MouseRightButtonDown -= OnMouseRightButtonDown;
            rootGrid.LostMouseCapture -= OnLostMouseCapture;
            rootGrid.DragOver -= OnDragOver;
            rootGrid.Drop -= OnDrop;
            rootGrid.SizeChanged -= OnRootGridSizeChanged;
            KeyDown -= OnKeyDown;
        }

        private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _imageViewStateController.HandleRootGridSizeChanged();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _externalImageSourceBindingController.Refresh();
            _imageSourceController.HandleLoaded();
        }

        private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is not MenuItem menuItem)
                {
                    continue;
                }

                yield return menuItem;

                foreach (var child in EnumerateMenuItems(menuItem))
                {
                    yield return child;
                }
            }
        }

        private void RefreshRoiDrawingMenuItems()
        {
            drawRoiMenuItem.Items.Clear();
            measureMenuItem.Items.Clear();
            roiOperationsMenuItem.Items.Remove(gradientDetectMenuItem);

            foreach (var tool in AvailableDrawingTools)
            {
                ImageViewerDynamicMenuItem menuDescriptor = ImageViewerDynamicMenuItem.FromRoiTool(tool);
                MenuItem menuItem = CreateDynamicMenuItem(menuDescriptor);

                menuItem.Click += OnRoiDrawingToolClick;
                if (menuDescriptor.Group == ImageViewerDynamicMenuGroup.Measurement)
                {
                    measureMenuItem.Items.Add(menuItem);
                }
                else
                {
                    drawRoiMenuItem.Items.Add(menuItem);
                }
            }

            measureMenuItem.Items.Add(gradientDetectMenuItem);

            drawRoiMenuItem.Visibility = drawRoiMenuItem.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            measureMenuItem.Visibility = measureMenuItem.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyMenuItemContentAlignment(drawRoiMenuItem);
            ApplyMenuItemContentAlignment(measureMenuItem);
        }

        private static MenuItem CreateDynamicMenuItem(ImageViewerDynamicMenuItem item)
        {
            var menuItem = new MenuItem
            {
                Header = item.Header,
                ToolTip = item.ToolTip,
                IsEnabled = item.IsEnabled,
                Tag = item.Tag
            };

            if (item.CreateIcon != null)
            {
                menuItem.Icon = item.CreateIcon();
            }

            return menuItem;
        }

        private static void ApplyMenuItemContentAlignment(ItemsControl itemsControl)
        {
            foreach (var menuItem in EnumerateMenuItems(itemsControl))
            {
                menuItem.HorizontalContentAlignment = HorizontalAlignment.Left;
                menuItem.VerticalContentAlignment = VerticalAlignment.Center;
            }
        }

        private void OnRoiDrawingToolClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: ImageViewerRoiToolMenuTag tool })
            {
                tool.Activate(this);
            }
        }

        private static bool TryGetTaggedCommand<TCommand>(object sender, out TCommand command)
            where TCommand : struct, Enum
        {
            if (sender is FrameworkElement { Tag: IImageViewerMenuCommandTag<TCommand> tag })
            {
                command = tag.Command;
                return true;
            }

            command = default;
            return false;
        }

        private void OnKeyDown(object sender, KeyEventArgs e) => _interactionController.HandleKeyDown(e);

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) => _interactionController.HandleMouseRightButtonDown(e);

        private void OnContextMenuOpened(object sender, RoutedEventArgs e) => _contextMenuController.HandleOpened();

        private void UpdateContextMenuState() => _contextMenuController.UpdateState();

        private void OnViewCommandMenuClick(object sender, RoutedEventArgs e)
        {
            if (TryGetTaggedCommand(sender, out ImageViewerViewCommand command))
            {
                _viewCommandController.Execute(command);
                UpdateContextMenuState();
            }
        }

        private void OnAnalysisCommandMenuClick(object sender, RoutedEventArgs e)
        {
            if (TryGetTaggedCommand(sender, out ImageViewerAnalysisCommand command))
            {
                _analysisCommandController.Execute(command);
                UpdateContextMenuState();
            }
        }

        private void OnRoiMenuCommandClick(object sender, RoutedEventArgs e)
        {
            if (TryGetTaggedCommand(sender, out ImageViewerRoiMenuCommand command))
            {
                _roiMenuCommandController.Execute(command);
            }
        }

        private async void OnFileMenuCommandClick(object sender, RoutedEventArgs e)
        {
            await HandleFileMenuCommandClickAsync(sender);
        }

        private async void OnToolbarFileCommandClick(object sender, RoutedEventArgs e)
        {
            await HandleFileMenuCommandClickAsync(sender);
        }

        private void OnToolbarViewCommandClick(object sender, RoutedEventArgs e)
        {
            if (TryGetTaggedCommand(sender, out ImageViewerViewCommand command))
            {
                _viewCommandController.Execute(command);
                UpdateContextMenuState();
            }
        }

        private void OnToolbarPanelToggleChanged(object sender, RoutedEventArgs e)
        {
            UpdateContextMenuState();
        }

        private async Task HandleFileMenuCommandClickAsync(object sender)
        {
            if (sender is MenuItem { Tag: ImageViewerRecentProjectMenuTag recentProject, IsEnabled: true })
            {
                await _fileMenuCommandController.OpenRecentProjectAsync(recentProject.ProjectPath);
                return;
            }

            if (TryGetTaggedCommand(sender, out ImageViewerFileMenuCommand command))
            {
                await _fileMenuCommandController.ExecuteAsync(command);
            }
        }

        private void OnDragOver(object sender, DragEventArgs e) => DroppedContentController.HandleDragOver(e);

        private async void OnDrop(object sender, DragEventArgs e)
        {
            await _droppedContentController.HandleDropAsync(e);
        }

        private async void OnRetryImageLoadClick(object sender, RoutedEventArgs e)
        {
            await RetryLastImageLoadAsync();
        }

        public Task ShowOpenImageDialogAsync() => _imageSourceController.OpenImageAsync();

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use ShowOpenImageDialogAsync() instead.", false)]
        public Task OpenImageAsync() => ShowOpenImageDialogAsync();

        private void OnMouseWheel(object sender, MouseWheelEventArgs e) => _interactionController.HandleMouseWheel(e);

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _interactionController.HandleMouseDown(e);
        }

        private void OnMouseMove(object sender, MouseEventArgs e) => _interactionController.HandleMouseMove(e);

        private void OnMouseUp(object sender, MouseButtonEventArgs e) => _interactionController.HandleMouseUp(e);
    }
}