using MainAPP.Models;
using MainAPP.Services;
using MainAPP.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MainAPP.Views
{
    /// <summary>
    /// DatabaseView.xaml 的交互逻辑
    /// </summary>
    public partial class DatabaseView : UserControl
    {
        public DatabaseView()
        {
            InitializeComponent();

            Loaded += DatabaseView_Loaded;
        }

        private void DatabaseView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= DatabaseView_Loaded;
            try
            {
                if (DataContext is DatabaseViewModel vm)
                {
                    if (vm.LoadDataCommand.CanExecute(null))
                    {
                        vm.LoadDataCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Error($"DatabaseView 加载失败: {ex}");
            }
        }

        private void ResetFilters_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DatabaseViewModel vm)
            {
                vm.SearchBarcode = string.Empty;
                vm.StartDate = null;
                vm.EndDate = null;
                vm.MinEncode = null;
                vm.MaxEncode = null;
                vm.MinScore = null;
                vm.MaxScore = null;
            }
        }

        // 清除单个时间筛选，便于在 DateTimePicker 选错时快速重置
        private void ClearStartDate_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DatabaseViewModel vm)
            {
                vm.StartDate = null;
            }
        }

        private void ClearEndDate_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is DatabaseViewModel vm)
            {
                vm.EndDate = null;
            }
        }

        // DataGrid.SelectedItems 不是 DependencyProperty，无法直接绑定，在此同步到 ViewModel.SelectedItems
        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not DatabaseViewModel vm)
            {
                return;
            }

            vm.SelectedItems.Clear();
            foreach (var item in DataGrid.SelectedItems)
            {
                if (item is DbModel model)
                {
                    vm.SelectedItems.Add(model);
                }
            }
        }

        // D1: 列显隐切换。CheckBox.Tag 保存对应 DataGridColumn 的字段名，通过 switch 找到字段后切换 Visibility。
        // 注：DataGridColumn 不在可视树中，FindName 无法返回，故使用直接字段引用。
        private void ColumnToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not string columnName)
            {
                return;
            }
            DataGridColumn? column = columnName switch
            {
                "ColId" => ColId,
                "ColBarcode" => ColBarcode,
                "ColEncode" => ColEncode,
                "ColWorldX" => ColWorldX,
                "ColWorldY" => ColWorldY,
                "ColImageX" => ColImageX,
                "ColImageY" => ColImageY,
                "ColAngle" => ColAngle,
                "ColBrightMean" => ColBrightMean,
                "ColDarkMean" => ColDarkMean,
                "ColBrightnessDiff" => ColBrightnessDiff,
                "ColArea" => ColArea,
                "ColWidth" => ColWidth,
                "ColHeight" => ColHeight,
                "ColCostTime" => ColCostTime,
                "ColDetectTime" => ColDetectTime,
                "ColImageBarcodeX" => ColImageBarcodeX,
                "ColImageBarcodeY" => ColImageBarcodeY,
                "ColBarcodeScore" => ColBarcodeScore,
                "ColScore" => ColScore,
                "ColSpeed" => ColSpeed,
                "ColImage" => ColImage,
                _ => null
            };
            if (column == null)
            {
                return;
            }
            column.Visibility = cb.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
