using CommunityToolkit.Mvvm.ComponentModel;
using MainAPP.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MainAPP.Services
{
    /// <summary>
    /// 配方管理器
    /// </summary>
    public partial class RecipesManage : ObservableObject
    {
        private static readonly Lazy<RecipesManage> _instance = new(() => new RecipesManage());

        /// <summary>
        /// 单例实例
        /// </summary>
        public static RecipesManage Instance => _instance.Value;

        private string _recipesDirectory = string.Empty;

        /// <summary>
        /// 配方文件扩展名
        /// </summary>
        public const string RecipeFileExtension = ".recipe";

        /// <summary>
        /// L407c: 合并文件名与路径无效字符，用于清理配方名称（既用于文件名又用于路径片段）
        /// </summary>
        private static readonly char[] InvalidNameChars =
            Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct().ToArray();

        /// <summary>
        /// 所有配方列表（支持WPF绑定）
        /// </summary>
        public ObservableCollection<Recipe> Recipes { get; } = [];

        /// <summary>
        /// 当前选中的配方（由 [ObservableProperty] 生成，变更时触发 OnCurrentRecipeChanged）
        /// </summary>
        [ObservableProperty]
        private Recipe? _currentRecipe;

        /// <summary>
        /// CurrentRecipe 变更后触发的部分方法：通知订阅者并持久化到设置
        /// </summary>
        /// <param name="value">新的当前配方</param>
        partial void OnCurrentRecipeChanged(Recipe? value)
        {
            CurrentRecipeChanged?.Invoke(value);
            Settings.Instance.CurrentRecipeName = value?.Name ?? string.Empty;
            // M182: Save 失败时记录日志，避免异常中断 setter 流程
            try
            {
                Settings.Instance.Save();
            }
            catch (Exception ex)
            {
                LogService.Instance.Warning($"保存当前配方设置失败: {ex}");
            }
        }

        /// <summary>
        /// 配方目录路径
        /// </summary>
        public string RecipesDirectory => _recipesDirectory;

        /// <summary>
        /// 当前配方变更事件
        /// </summary>
        public event Action<Recipe?>? CurrentRecipeChanged;

        private RecipesManage() { }

        /// <summary>
        /// L436: ObservableCollection 修改统一调度到 UI 线程，防止 WPF CollectionView 跨线程异常
        /// (复制配方/刷新配方等操作若从后台线程触发集合变更会触发"该类型的 CollectionView 不支持从调度程序线程以外的线程..."错误)
        /// </summary>
        private static void SafeModify(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
#pragma warning disable VSTHRD001 // WPF 标准跨线程集合更新,Dispatcher.Invoke 为常规做法(与项目内 ImageViewer/LogService 一致)
                dispatcher.Invoke(action);
#pragma warning restore VSTHRD001
        }

        /// <summary>
        /// 初始化配方管理器
        /// </summary>
        /// <param name="recipesDirectory">配方存储目录</param>
        public void Initialize(string recipesDirectory)
        {
            _recipesDirectory = recipesDirectory;

            if (!Directory.Exists(_recipesDirectory))
            {
                Directory.CreateDirectory(_recipesDirectory);
            }

            LoadAllRecipes();

            // 从设置加载上次的当前配方
            if (!string.IsNullOrEmpty(Settings.Instance.CurrentRecipeName))
            {
                var savedRecipe = FindByName(Settings.Instance.CurrentRecipeName);
                if (savedRecipe != null)
                {
                    CurrentRecipe = savedRecipe;
                    LogService.Instance.Info($"已恢复上次当前配方: {savedRecipe.Name}");
                }
                else
                {
                    LogService.Instance.Warning($"设置中保存的当前配方不存在: {Settings.Instance.CurrentRecipeName}");
                    Settings.Instance.CurrentRecipeName = string.Empty;
                    // M241: Save 失败时记录日志，避免异常中断初始化流程
                    try
                    {
                        Settings.Instance.Save();
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Warning($"保存设置失败: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// 加载所有配方
        /// </summary>
        public void LoadAllRecipes()
        {
            // L436: 后台线程安全 — 先在调用线程同步加载Recipe到本地列表,再统一在 UI 线程更新 ObservableCollection
            var loaded = new List<Recipe>();
            if (!string.IsNullOrEmpty(_recipesDirectory) && Directory.Exists(_recipesDirectory))
            {
                var loadedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var subdirs = Directory.GetDirectories(_recipesDirectory);
                foreach (var dir in subdirs)
                {
                    var files = Directory.GetFiles(dir, $"*{RecipeFileExtension}");
                    foreach (var file in files)
                    {
                        var recipe = Recipe.LoadFromFile(file);
                        if (recipe != null && loadedNames.Add(recipe.Name))
                            loaded.Add(recipe);
                    }
                }
                var rootFiles = Directory.GetFiles(_recipesDirectory, $"*{RecipeFileExtension}");
                foreach (var file in rootFiles)
                {
                    var recipe = Recipe.LoadFromFile(file);
                    if (recipe != null && loadedNames.Add(recipe.Name))
                        loaded.Add(recipe);
                }
            }
            SafeModify(() =>
            {
                Recipes.Clear();
                foreach (var r in loaded) Recipes.Add(r);
            });
        }

        /// <summary>
        /// 添加新配方
        /// </summary>
        public void AddRecipe(Recipe recipe)
        {
            SaveRecipe(recipe);
            SafeModify(() => Recipes.Add(recipe));
        }

        /// <summary>
        /// 删除配方
        /// </summary>
        public bool RemoveRecipe(Recipe recipe)
        {
            var filePath = GetRecipeFilePath(recipe);
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);

                    // If the recipe folder is now empty, remove it
                    var folder = GetRecipeFolderPath(recipe);
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    {
                        Directory.Delete(folder);
                    }
                }
            }
            catch (Exception ex)
            {
                // H77: 文件删除失败时记录日志后返回，不从 Recipes 移除，保持数据一致性
                LogService.Instance.Warning($"删除配方文件失败: {ex}");
                return false;
            }

            bool result = false;
            SafeModify(() => { result = Recipes.Remove(recipe); });
            var wasCurrentRecipe = CurrentRecipe == recipe;

            if (wasCurrentRecipe)
            {
                // M242: CurrentRecipe setter 已处理 CurrentRecipeName 清空与持久化，无需重复保存
                CurrentRecipe = null;
            }

            return result;
        }

        /// <summary>
        /// 保存配方
        /// </summary>
        public void SaveRecipe(Recipe recipe)
        {
            var folder = GetRecipeFolderPath(recipe);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var filePath = GetRecipeFilePath(recipe);
            recipe.SaveToFile(filePath);
        }

        /// <summary>
        /// 根据名称查找配方
        /// </summary>
        public Recipe? FindByName(string name)
        {
            return Recipes.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 检查配方名称是否已存在
        /// </summary>
        public bool IsNameExists(string name)
        {
            return Recipes.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 设置并保存当前配方（用户主动设置时调用）
        /// </summary>
        /// <param name="recipe">要设置为当前的配方</param>
        public void SetAndSaveCurrentRecipe(Recipe recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));

            // L110: 直接使用 CurrentRecipe setter，其内部已处理属性通知、事件触发与持久化，避免重复保存逻辑
            CurrentRecipe = recipe;
        }

        /// <summary>
        /// 获取配方文件路径
        /// </summary>
        private string GetRecipeFilePath(Recipe recipe)
        {
            var folder = GetRecipeFolderPath(recipe);
            // L407c: 合并文件名与路径无效字符，确保清理更彻底
            var safeFileName = string.Join("_", recipe.Name.Split(InvalidNameChars)) + RecipeFileExtension;
            return Path.Combine(folder, safeFileName);
        }

        /// <summary>
        /// 获取配方所在文件夹路径（按配方名称分文件夹）
        /// </summary>
        private string GetRecipeFolderPath(Recipe recipe)
        {
            // L407c: 合并文件名与路径无效字符，确保清理更彻底
            var safeName = string.Join("_", recipe.Name.Split(InvalidNameChars));
            return Path.Combine(_recipesDirectory, safeName);
        }
    }
}
