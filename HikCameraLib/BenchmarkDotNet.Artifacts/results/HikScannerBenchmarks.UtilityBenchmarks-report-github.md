```

BenchmarkDotNet v0.14.0, Windows 10 (10.0.19044.7417/21H2/November2021Update)
Intel Core Ultra 5 245K, 1 CPU, 14 logical and 14 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2
  Job-DSAPOT : .NET 8.0.14 (8.0.1425.11118), X64 RyuJIT AVX2

IterationCount=2  LaunchCount=1  WarmupCount=1  

```
| Method                         | TextLength | Mean           | Error          | StdDev     | Gen0   | Allocated |
|------------------------------- |----------- |---------------:|---------------:|-----------:|-------:|----------:|
| **&#39;IsTextUtf8 - 纯ASCII&#39;**          | **100**        |     **41.7529 ns** |     **11.7043 ns** |  **0.0260 ns** |      **-** |         **-** |
| &#39;IsTextUtf8 - 中文UTF8&#39;          | 100        |    146.0554 ns |    276.5617 ns |  0.6144 ns |      - |         - |
| &#39;IsTextUtf8 - 混合UTF8&#39;          | 100        |    164.0798 ns |    442.2999 ns |  0.9825 ns |      - |         - |
| &#39;IsTextUtf8 - 无效序列&#39;            | 100        |      0.2299 ns |      4.2348 ns |  0.0094 ns |      - |         - |
| &#39;GetCodeTypeName - QR码&#39;        | 100        |      0.2048 ns |      4.5967 ns |  0.0102 ns |      - |         - |
| &#39;GetCodeTypeName - Code128&#39;    | 100        |      0.4330 ns |      0.8288 ns |  0.0018 ns |      - |         - |
| &#39;GetCodeTypeName - 未知码&#39;        | 100        |      0.2099 ns |      6.6180 ns |  0.0147 ns |      - |         - |
| &#39;GetErrorDescription - 网络错误&#39;   | 100        |      0.6345 ns |      2.1353 ns |  0.0047 ns |      - |         - |
| &#39;GetErrorDescription - 未知错误码&#39;  | 100        |      0.6517 ns |      4.0172 ns |  0.0089 ns |      - |         - |
| &#39;DeviceInfo.ToString - GigE设备&#39; | 100        |     92.1938 ns |    349.7282 ns |  0.7769 ns | 0.0036 |     768 B |
| **&#39;IsTextUtf8 - 纯ASCII&#39;**          | **1000**       |    **379.4895 ns** |    **338.2003 ns** |  **0.7513 ns** |      **-** |         **-** |
| &#39;IsTextUtf8 - 中文UTF8&#39;          | 1000       |  1,416.2928 ns |  2,004.4919 ns |  4.4529 ns |      - |         - |
| &#39;IsTextUtf8 - 混合UTF8&#39;          | 1000       |  1,518.0553 ns |  4,601.5402 ns | 10.2221 ns |      - |         - |
| &#39;IsTextUtf8 - 无效序列&#39;            | 1000       |      0.2013 ns |      2.0070 ns |  0.0045 ns |      - |         - |
| &#39;GetCodeTypeName - QR码&#39;        | 1000       |      0.2183 ns |     11.6019 ns |  0.0258 ns |      - |         - |
| &#39;GetCodeTypeName - Code128&#39;    | 1000       |      0.4084 ns |      3.0190 ns |  0.0067 ns |      - |         - |
| &#39;GetCodeTypeName - 未知码&#39;        | 1000       |      0.3979 ns |      0.7374 ns |  0.0016 ns |      - |         - |
| &#39;GetErrorDescription - 网络错误&#39;   | 1000       |      0.6507 ns |      5.1803 ns |  0.0115 ns |      - |         - |
| &#39;GetErrorDescription - 未知错误码&#39;  | 1000       |      0.8380 ns |      6.5859 ns |  0.0146 ns |      - |         - |
| &#39;DeviceInfo.ToString - GigE设备&#39; | 1000       |     91.2118 ns |    925.1370 ns |  2.0551 ns | 0.0036 |     768 B |
| **&#39;IsTextUtf8 - 纯ASCII&#39;**          | **10000**      |  **3,660.9865 ns** |    **722.1174 ns** |  **1.6041 ns** |      **-** |         **-** |
| &#39;IsTextUtf8 - 中文UTF8&#39;          | 10000      | 13,336.5532 ns | 18,401.3038 ns | 40.8775 ns |      - |         - |
| &#39;IsTextUtf8 - 混合UTF8&#39;          | 10000      | 15,205.8487 ns | 14,341.8070 ns | 31.8595 ns |      - |         - |
| &#39;IsTextUtf8 - 无效序列&#39;            | 10000      |      0.2358 ns |      7.6328 ns |  0.0170 ns |      - |         - |
| &#39;GetCodeTypeName - QR码&#39;        | 10000      |      0.4490 ns |     11.5447 ns |  0.0256 ns |      - |         - |
| &#39;GetCodeTypeName - Code128&#39;    | 10000      |      0.6349 ns |      4.0354 ns |  0.0090 ns |      - |         - |
| &#39;GetCodeTypeName - 未知码&#39;        | 10000      |      0.4546 ns |     10.6416 ns |  0.0236 ns |      - |         - |
| &#39;GetErrorDescription - 网络错误&#39;   | 10000      |      0.6142 ns |      3.6607 ns |  0.0081 ns |      - |         - |
| &#39;GetErrorDescription - 未知错误码&#39;  | 10000      |      0.5925 ns |      6.1973 ns |  0.0138 ns |      - |         - |
| &#39;DeviceInfo.ToString - GigE设备&#39; | 10000      |     94.4601 ns |  2,804.1736 ns |  6.2293 ns | 0.0036 |     768 B |
