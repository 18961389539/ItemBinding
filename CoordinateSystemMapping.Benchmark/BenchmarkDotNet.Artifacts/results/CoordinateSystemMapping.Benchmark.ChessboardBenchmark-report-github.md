```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19044.7417/21H2/November2021Update)
Intel Core Ultra 5 245K, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2
  Job-KNGSZD : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2

IterationCount=5  WarmupCount=2  

```
| Method                    | Mean       | Error      | StdDev     | Allocated |
|-------------------------- |-----------:|-----------:|-----------:|----------:|
| GenerateForA4_10mm_300DPI |   4.144 ms |  0.1804 ms |  0.0468 ms |     659 B |
| GenerateForA4_15mm_300DPI |   3.873 ms |  0.2837 ms |  0.0737 ms |     658 B |
| GenerateForA4_10mm_600DPI |  17.690 ms |  2.2587 ms |  0.5866 ms |     668 B |
| Find_10mm                 |  20.838 ms |  1.4235 ms |  0.3697 ms |    3980 B |
| Find_20mm                 |  17.964 ms |  0.5239 ms |  0.1360 ms |    1004 B |
| GetThreePoints_10mm       |  56.307 ms |  4.9437 ms |  1.2839 ms |  134812 B |
| GetThreePoints_15mm       | 621.037 ms | 76.3134 ms | 19.8184 ms |  196464 B |
