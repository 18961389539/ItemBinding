```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19044.7417/21H2/November2021Update)
Intel Core Ultra 5 245K, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2
  Job-PZGFOA : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2

IterationCount=1  WarmupCount=1  

```
| Method        | MergeCount | Mean      | Error | Allocated |
|-------------- |----------- |----------:|------:|----------:|
| **&#39;Area - 计算面积&#39;** | **10**         | **0.8170 ns** |    **NA** |         **-** |
| **&#39;Area - 计算面积&#39;** | **100**        | **0.8266 ns** |    **NA** |         **-** |
| **&#39;Area - 计算面积&#39;** | **1000**       | **0.8314 ns** |    **NA** |         **-** |
