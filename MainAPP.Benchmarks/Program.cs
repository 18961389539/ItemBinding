using BenchmarkDotNet.Running;

// BenchmarkDotNet 入口点：以 Release 模式运行（dotnet run -c Release）
// 默认运行所有 [Benchmark] 方法，也可通过命令行参数筛选
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
