using MainAPP.Models;
using MainAPP.ViewModels;
using System.Windows;

namespace MainAPP.Services
{
    /// <summary>
    /// 对话框服务抽象。
    /// 将 ViewModel 中的 new Window 调用封装为接口方法，
    /// 让 VM 不再直接依赖 View，便于单元测试和未来替换为其他 UI 框架。
    /// </summary>
    public interface IDialogService
    {
        /// <summary>显示配方编辑对话框（模态）。返回 true 表示用户保存了修改。</summary>
        Task<bool> ShowRecipeEditorAsync(RecipeViewModel recipeVm);

        /// <summary>显示图表分析窗口（非模态）。</summary>
        void ShowCharts(IReadOnlyList<DbModel> items);

        /// <summary>显示文本输入对话框（模态）。返回用户输入的文本，取消则返回 null。</summary>
        Task<string?> ShowInputDialogAsync(string title, string message, string defaultValue = "");

        /// <summary>显示登录窗口（模态）。返回 true 表示登录成功。</summary>
        Task<bool> ShowLoginAsync();

        /// <summary>显示确认对话框（模态）。返回 true 表示用户点击了"是"。</summary>
        bool Confirm(string message, string caption = "", MessageBoxButton buttons = MessageBoxButton.YesNo, MessageBoxImage icon = MessageBoxImage.Question);
    }
}
