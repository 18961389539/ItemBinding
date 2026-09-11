using BenchmarkDotNet.Running;

// BenchmarkDotNet 入口点：以 Release 模式运行（dotnet run -c Release）
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
