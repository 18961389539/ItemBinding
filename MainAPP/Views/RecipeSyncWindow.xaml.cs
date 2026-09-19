using MainAPP.Models;
using MainAPP.Services;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace MainAPP.Views
{
    /// <summary>
    /// 批量参数同步：把当前配方的若干参数组（图像采集 / 边缘模型 / 角度模型 / 标定补偿）
    /// 覆盖写入选中的目标配方并逐一保存，用于多条相似产线配方的统一刷参。
    /// </summary>
    public partial class RecipeSyncWindow : Window
    {
        private readonly Recipe _source;

        public RecipeSyncWindow(Recipe source)
        {
            InitializeComponent();
            _source = source ?? throw new System.ArgumentNullException(nameof(source));
            // 目标列表 = 全部配方去掉当前对象（同一配方被多个 VM 引用，直接引用相等排除即可）
            TargetList.ItemsSource = RecipesManage.Instance.Recipes
                .Where(r => !ReferenceEquals(r, source))
                .ToList();
        }

        private void SyncButton_Click(object sender, RoutedEventArgs e)
        {
            var targets = TargetList.SelectedItems.Cast<Recipe>().ToList();
            if (targets.Count == 0)
            {
                NotificationService.Warning("请先选择至少一个目标配方。");
                return;
            }

            var anyGroup = SyncImageTool.IsChecked == true || SyncEdgeTool.IsChecked == true
                || SyncAngleTool.IsChecked == true || SyncCalib.IsChecked == true;
            if (!anyGroup)
            {
                NotificationService.Warning("请至少勾选一组要同步的内容。");
                return;
            }

            foreach (var target in targets)
            {
                ApplyGroups(target);
                RecipesManage.Instance.SaveRecipe(target);
            }

            var names = string.Join("、", targets.Select(t => t.Name));
            AuditLogService.Instance.Record("批量同步", "配方", _source.Name, names);
            NotificationService.Info($"已同步 {targets.Count} 个配方：\n{names}");
            DialogResult = true;
        }

        /// <summary>把勾选的参数组从源配方覆盖到目标配方（JSON 深拷贝，避免共享引用）。</summary>
        private void ApplyGroups(Recipe target)
        {
            if (SyncImageTool.IsChecked == true && _source.ImageTool is { } img)
            {
                target.ImageTool = DeepCopy(img) ?? new ImageTool();
            }

            target.YoloTool ??= new YoloTools();
            if (SyncEdgeTool.IsChecked == true && _source.YoloTool?.EdgeDetection is { } edge)
            {
                target.YoloTool.EdgeDetection = DeepCopy(edge) ?? new YoloTool();
            }

            if (SyncAngleTool.IsChecked == true)
            {
                if (_source.YoloTool?.AngleDetection is { } angle)
                {
                    target.YoloTool.AngleDetection = DeepCopy(angle) ?? new YoloTool();
                }
                target.YoloTool.IsAngleDetectionEnabled = _source.YoloTool?.IsAngleDetectionEnabled ?? false;
                target.YoloTool.IsBrightnessDirectionEnabled = _source.YoloTool?.IsBrightnessDirectionEnabled;
            }

            if (SyncCalib.IsChecked == true)
            {
                target.OffsetAngle = _source.OffsetAngle;
                target.GrabOffsetLongMm = _source.GrabOffsetLongMm;
                target.GrabOffsetShortMm = _source.GrabOffsetShortMm;
            }
        }

        private static T? DeepCopy<T>(T source) where T : class =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source));

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}