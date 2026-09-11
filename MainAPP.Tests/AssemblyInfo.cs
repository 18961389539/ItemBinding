using Xunit;

// 多个测试共享单例（Settings, AuthService, BarcodeDataService, RecipesManage, LogService），
// 禁用测试并行化避免文件 I/O 竞态与状态污染。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
