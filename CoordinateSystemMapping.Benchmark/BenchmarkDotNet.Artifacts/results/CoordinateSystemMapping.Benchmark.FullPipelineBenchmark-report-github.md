```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19044.7417/21H2/November2021Update)
Intel Core Ultra 5 245K, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2
  Job-KNGSZD : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2

IterationCount=5  WarmupCount=2  

```
| Method       | Mean     | Error    | StdDev   | Allocated |
|------------- |---------:|---------:|---------:|----------:|
| FullPipeline | 79.95 ms | 2.384 ms | 0.369 ms |  135.8 KB |
