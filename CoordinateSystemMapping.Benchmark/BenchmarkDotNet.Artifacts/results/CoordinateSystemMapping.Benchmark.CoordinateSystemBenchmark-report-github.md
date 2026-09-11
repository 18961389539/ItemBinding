```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19044.7417/21H2/November2021Update)
Intel Core Ultra 5 245K, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2
  Job-MOXWNR : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3  

```
| Method              | Mean        | Error    | StdDev   | Gen0   | Allocated |
|-------------------- |------------:|---------:|---------:|-------:|----------:|
| GetCoordinateSystem |    137.6 ns |  0.69 ns |  0.46 ns | 0.0062 |      80 B |
| DrawOnMat           | 22,896.1 ns | 65.30 ns | 43.19 ns | 0.0305 |     512 B |
