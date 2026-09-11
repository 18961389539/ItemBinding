```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19044.7417/21H2/November2021Update)
Intel Core Ultra 5 245K, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2
  Job-MOXWNR : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2

IterationCount=10  WarmupCount=3  

```
| Method                         | Mean      | Error     | StdDev    | Gen0   | Allocated |
|------------------------------- |----------:|----------:|----------:|-------:|----------:|
| Initialize_Orthogonal          | 24.176 ns | 0.3513 ns | 0.2091 ns | 0.0076 |      96 B |
| Initialize_Skewed              | 24.326 ns | 0.3494 ns | 0.2311 ns | 0.0076 |      96 B |
| ImageToPhysical_Point2f        |  5.299 ns | 0.0142 ns | 0.0084 ns |      - |         - |
| ImageToPhysical_Point2d        | 24.133 ns | 0.1058 ns | 0.0700 ns |      - |         - |
| ImageToPhysical_Float_Tuple    | 24.479 ns | 0.0879 ns | 0.0581 ns |      - |         - |
| ImageToPhysical_Double_Tuple   | 23.317 ns | 0.1072 ns | 0.0709 ns |      - |         - |
| PhysicalToImage                | 17.779 ns | 0.0400 ns | 0.0238 ns |      - |         - |
| Roundtrip_Image_Physical_Image | 17.726 ns | 1.5595 ns | 1.0315 ns |      - |         - |
| ImageToPhysical_Skewed         |  4.582 ns | 0.0014 ns | 0.0007 ns |      - |         - |
