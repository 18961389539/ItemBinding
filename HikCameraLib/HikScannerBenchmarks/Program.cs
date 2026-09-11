using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using HikScannerBenchmarks;

// 切换当前目录到输出路径，避免找不到依赖 DLL
var config = ManualConfig
    .Create(DefaultConfig.Instance)
    .AddDiagnoser(MemoryDiagnoser.Default);

// 支持命令行参数选择特定 benchmark，无参数则运行全部
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
